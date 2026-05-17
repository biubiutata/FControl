using System.Runtime.InteropServices;
using FControl.Models;
using FControl.Pages;
using FControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace FControl;

public sealed partial class MainWindow : Window
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    private readonly nint _hwnd;
    private readonly TrayIconService _trayIcon;
    private readonly GlobalHotKeyService _hotKeys;
    private readonly HotKeyActionService _actions;
    private InfoBar? _actionStatusInfoBar;
    private bool _isExitRequested;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new SizeInt32(980, 680));

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Closing += AppWindow_Closing;
        _trayIcon = new TrayIconService(_hwnd, DispatcherQueue, ShowSettingsWindow, ExitFromTray);
        _hotKeys = new GlobalHotKeyService(_hwnd, AppServices.Configuration);
        _actions = new HotKeyActionService(AppServices.Configuration);
        _hotKeys.HotKeyTriggered += HotKeys_HotKeyTriggered;
        _actions.ActionExecuted += Actions_ActionExecuted;
        AppServices.HotKeys = _hotKeys;
        AppServices.Actions = _actions;
        AppServices.Configuration.ConfigurationChanged += Configuration_ConfigurationChanged;

        NavigateTo(typeof(KeyMappingPage));
    }

    private void Configuration_ConfigurationChanged(object? sender, EventArgs e)
    {
        _hotKeys.Refresh();
    }

    private void HotKeys_HotKeyTriggered(object? sender, HotKeyTriggeredEventArgs e)
    {
        _actions.Execute(e.Mapping);
    }

    private void Actions_ActionExecuted(object? sender, ActionExecutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => ShowActionStatus(e));
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
        {
            NavFrame.GoBack();
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item)
        {
            return;
        }

        switch (item.Tag)
        {
            case "keyMapping":
                NavigateTo(typeof(KeyMappingPage));
                break;
            case "displaySettings":
                NavigateTo(typeof(DisplaySettingsPage));
                break;
            case "about":
                NavigateTo(typeof(AboutPage));
                break;
            default:
                throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
        }
    }

    private void NavigateTo(Type pageType)
    {
        if (NavFrame.CurrentSourcePageType != pageType)
        {
            NavFrame.Navigate(pageType);
        }
    }

    private void ShowActionStatus(ActionExecutedEventArgs e)
    {
        _actionStatusInfoBar ??= new InfoBar
        {
            IsClosable = true,
            Margin = new Thickness(16),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        if (_actionStatusInfoBar.Parent is null)
        {
            Grid.SetRowSpan(_actionStatusInfoBar, 2);
            RootGrid.Children.Add(_actionStatusInfoBar);
        }

        _actionStatusInfoBar.Severity = e.Result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        _actionStatusInfoBar.Title = $"{e.Mapping.Key} · {HotKeyActionMetadata.GetDisplayName(e.Mapping.Action)}";
        _actionStatusInfoBar.Message = e.Result.Message;
        _actionStatusInfoBar.IsOpen = true;
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExitRequested)
        {
            AppServices.Configuration.ConfigurationChanged -= Configuration_ConfigurationChanged;
            _hotKeys.HotKeyTriggered -= HotKeys_HotKeyTriggered;
            _actions.ActionExecuted -= Actions_ActionExecuted;
            AppServices.HotKeys = null;
            AppServices.Actions = null;
            _hotKeys.Dispose();
            _trayIcon.Dispose();
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        ShowWindow(_hwnd, SwHide);
    }

    private void ShowSettingsWindow()
    {
        ShowWindow(_hwnd, SwShow);
        ShowWindow(_hwnd, SwRestore);
        Activate();
    }

    private void ExitFromTray()
    {
        _isExitRequested = true;
        _trayIcon.Dispose();
        Close();
        Application.Current.Exit();
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
