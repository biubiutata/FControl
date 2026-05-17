using System.Runtime.InteropServices;
using FControl.Pages;
using FControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private readonly ActionOverlayWindow _overlayWindow;
    private bool _isExitRequested;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ResizeMainWindowToWorkArea();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Closing += AppWindow_Closing;
        _trayIcon = new TrayIconService(_hwnd, DispatcherQueue, ShowSettingsWindow, ExitFromTray);
        _hotKeys = new GlobalHotKeyService(_hwnd, AppServices.Configuration);
        _actions = new HotKeyActionService(AppServices.Configuration);
        _overlayWindow = new ActionOverlayWindow();
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
            case "advancedSettings":
                NavigateTo(typeof(AdvancedSettingsPage));
                break;
            default:
                throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
        }

        UpdatePaneResizeGrip();
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
        _overlayWindow.Show(e);
    }

    private void PaneResizeGrip_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
    {
        NavView.OpenPaneLength = Math.Clamp(NavView.OpenPaneLength + e.Delta.Translation.X, 180, 420);
        UpdatePaneResizeGrip();
    }

    private void UpdatePaneResizeGrip()
    {
        PaneResizeGrip.Margin = new Thickness(NavView.OpenPaneLength - PaneResizeGrip.Width / 2, 0, 0, 0);
    }

    private void ResizeMainWindowToWorkArea()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = displayArea.WorkArea;
        var width = Math.Clamp((int)Math.Round(workArea.Width * 0.82), 980, 1500);
        var height = Math.Clamp((int)Math.Round(workArea.Height * 0.78), 680, 1000);
        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Y + (workArea.Height - height) / 2;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        UpdatePaneResizeGrip();
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
            _overlayWindow.Dispose();
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
