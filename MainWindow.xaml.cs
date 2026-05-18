using System.Runtime.InteropServices;
using FControl.Models;
using FControl.Pages;
using FControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.UI;

namespace FControl;

public sealed partial class MainWindow : Window
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int SwRestore = 9;

    private readonly nint _hwnd;
    private TrayIconService? _trayIcon;
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
        ApplyTitleBarColors();
        ResizeMainWindowToWorkArea();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Closing += AppWindow_Closing;
        _hotKeys = new GlobalHotKeyService(_hwnd, AppServices.Configuration);
        _actions = new HotKeyActionService(AppServices.Configuration);
        _overlayWindow = new ActionOverlayWindow();
        ApplyTrayIconPreference();
        _hotKeys.HotKeyTriggered += HotKeys_HotKeyTriggered;
        _hotKeys.CustomHotkeyTriggered += HotKeys_CustomHotkeyTriggered;
        _actions.ActionExecuted += Actions_ActionExecuted;
        _actions.ScriptActionExecuted += Actions_ScriptActionExecuted;
        AppServices.HotKeys = _hotKeys;
        AppServices.Actions = _actions;
        AppServices.Configuration.ConfigurationChanged += Configuration_ConfigurationChanged;
        RootGrid.ActualThemeChanged += RootGrid_ActualThemeChanged;

        NavigateTo(typeof(KeyMappingPage));
    }

    private void Configuration_ConfigurationChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _hotKeys.Refresh();
            ApplyTrayIconPreference();
        });
    }

    private void HotKeys_HotKeyTriggered(object? sender, HotKeyTriggeredEventArgs e)
    {
        _actions.Execute(e.Mapping);
    }

    private void HotKeys_CustomHotkeyTriggered(object? sender, CustomHotkeyTriggeredEventArgs e)
    {
        _actions.Execute(e.Hotkey);
    }
    private void Actions_ActionExecuted(object? sender, ActionExecutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => ShowActionStatus(e));
    }

    private void Actions_ScriptActionExecuted(object? sender, ScriptActionExecutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => _overlayWindow.Show(e));
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
            case "customHotkeys":
                NavigateTo(typeof(CustomHotkeysPage));
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
        if (e.Mapping.Action is HotKeyAction.BrightnessDown or HotKeyAction.BrightnessUp)
        {
            _overlayWindow.Show(e);
        }
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
    }

    private void RootGrid_ActualThemeChanged(FrameworkElement sender, object args)
    {
        ApplyTitleBarColors();
    }

    private void ApplyTitleBarColors()
    {
        var titleBar = AppWindow.TitleBar;
        if (RootGrid.ActualTheme == ElementTheme.Dark)
        {
            titleBar.ForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 255, 255, 255);
            titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 51, 51, 51);
            titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 64, 64, 64);
            titleBar.BackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.InactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.InactiveForegroundColor = Color.FromArgb(255, 180, 180, 180);
            titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 180, 180, 180);
            return;
        }

        titleBar.ForegroundColor = Color.FromArgb(255, 0, 0, 0);
        titleBar.ButtonForegroundColor = Color.FromArgb(255, 0, 0, 0);
        titleBar.ButtonHoverForegroundColor = Color.FromArgb(255, 0, 0, 0);
        titleBar.ButtonPressedForegroundColor = Color.FromArgb(255, 0, 0, 0);
        titleBar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 230, 230, 230);
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(255, 215, 215, 215);
        titleBar.BackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.InactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.InactiveForegroundColor = Color.FromArgb(255, 90, 90, 90);
        titleBar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 90, 90, 90);
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExitRequested)
        {
            AppServices.Configuration.ConfigurationChanged -= Configuration_ConfigurationChanged;
            RootGrid.ActualThemeChanged -= RootGrid_ActualThemeChanged;
            _hotKeys.HotKeyTriggered -= HotKeys_HotKeyTriggered;
            _hotKeys.CustomHotkeyTriggered -= HotKeys_CustomHotkeyTriggered;
            _actions.ActionExecuted -= Actions_ActionExecuted;
            _actions.ScriptActionExecuted -= Actions_ScriptActionExecuted;
            AppServices.HotKeys = null;
            AppServices.Actions = null;
            _hotKeys.Dispose();
            _overlayWindow.Dispose();
            _trayIcon?.Dispose();
            _trayIcon = null;
            return;
        }

        args.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        if (AppServices.Configuration.Current.KeepTrayIconEnabled)
        {
            ShowWindow(_hwnd, SwHide);
            return;
        }

        ExitFromTray();
    }

    private void ShowSettingsWindow()
    {
        ShowWindow(_hwnd, SwShow);
        ShowWindow(_hwnd, SwRestore);
        Activate();
    }

    private void ShowAdvancedSettingsWindow()
    {
        ShowSettingsWindow();
        NavView.SelectedItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(static item => (string?)item.Tag == "advancedSettings");
        NavigateTo(typeof(AdvancedSettingsPage));
    }

    private void ExitFromTray()
    {
        _isExitRequested = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        Close();
        Application.Current.Exit();
    }

    private void ApplyTrayIconPreference()
    {
        if (AppServices.Configuration.Current.KeepTrayIconEnabled)
        {
            _trayIcon ??= new TrayIconService(_hwnd, DispatcherQueue, ShowSettingsWindow, ShowAdvancedSettingsWindow, ExitFromTray);
            return;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}


