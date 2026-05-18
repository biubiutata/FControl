using FControl.Models;
using FControl.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.UI;

namespace FControl;

public sealed partial class ActionOverlayWindow : Window
{
    private const int SwHide = 0;
    private const int SwShow = 5;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int LwaAlpha = 0x00000002;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const uint DwmwaColorNone = 0xFFFFFFFE;
    private static readonly SizeInt32 CompactOverlaySize = new(400, 184);
    private static readonly SizeInt32 LevelOverlaySize = new(400, 204);

    private readonly nint _hwnd;
    private readonly DispatcherTimer _timer = new();
    private readonly MonitorBrightnessService _brightnessService = new();
    private Storyboard? _storyboard;
    private HotKeyAction _currentAction = HotKeyAction.Disabled;
    private bool _isUpdatingSlider;
    private bool _isVisible;
    private int _hideGeneration;

    public ActionOverlayWindow()
    {
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Move(new PointInt32(24, 24));
        AppWindow.IsShownInSwitchers = false;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        AppWindow.Resize(CompactOverlaySize);

        var extendedStyle = GetWindowLong(_hwnd, GwlExStyle);
        _ = SetWindowLong(_hwnd, GwlExStyle, extendedStyle | WsExToolWindow | WsExLayered | WsExNoActivate);
        ApplyChromeSettings();
        _ = ShowWindow(_hwnd, SwHide);

        _timer.Tick += Timer_Tick;
    }

    public void Show(ActionExecutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateContent(e);
            ShowOverlay();
        });
    }

    public void Show(ScriptActionExecutedEventArgs e)
    {
        if (!e.Hotkey.ShowOverlay)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateScriptContent(e);
            ShowOverlay();
        });
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _storyboard?.Stop();
        Close();
    }

    private void UpdateContent(ActionExecutedEventArgs e)
    {
        OverlayTitleText.Text = $"{e.Mapping.Key} · {GetOverlayTitle(e.Mapping.Action, e.Result)}";
        OverlayMessageText.Text = e.Result.Message;
        OverlayIcon.Glyph = GetGlyph(e.Mapping.Action, e.Result);
        _currentAction = e.Mapping.Action;

        if (e.Result.Succeeded && e.Result.LevelPercent is { } levelPercent)
        {
            var clampedLevel = Math.Clamp(levelPercent, 0, 100);
            OverlayLevelPanel.Visibility = Visibility.Visible;
            _isUpdatingSlider = true;
            OverlayLevelSlider.Value = clampedLevel;
            _isUpdatingSlider = false;
            OverlayPercentText.Text = $"{clampedLevel}%";
            OverlayContentGrid.MinHeight = 112;
            AppWindow.Resize(LevelOverlaySize);
        }
        else
        {
            OverlayLevelPanel.Visibility = Visibility.Collapsed;
            _isUpdatingSlider = true;
            OverlayLevelSlider.Value = 0;
            _isUpdatingSlider = false;
            OverlayPercentText.Text = string.Empty;
            OverlayContentGrid.MinHeight = 76;
            AppWindow.Resize(CompactOverlaySize);
        }
    }

    private void UpdateScriptContent(ScriptActionExecutedEventArgs e)
    {
        OverlayTitleText.Text = $"{e.Hotkey.Hotkey} · {e.Hotkey.Name}";
        OverlayMessageText.Text = e.Result.Message;
        OverlayIcon.Glyph = e.Result.Succeeded ? "\uE756" : "\uE7BA";
        _currentAction = HotKeyAction.Disabled;
        OverlayLevelPanel.Visibility = Visibility.Collapsed;
        _isUpdatingSlider = true;
        OverlayLevelSlider.Value = 0;
        _isUpdatingSlider = false;
        OverlayPercentText.Text = string.Empty;
        OverlayContentGrid.MinHeight = 76;
        AppWindow.Resize(CompactOverlaySize);
    }

    private void ShowOverlay()
    {
        _storyboard?.Stop();
        _timer.Stop();
        _hideGeneration++;

        OverlayRoot.Opacity = 1;
        ApplyWindowOpacity();
        ApplyThemeColors();
        _ = ShowWindow(_hwnd, SwShow);

        if (_isVisible)
        {
            OverlayCard.Opacity = 1;
        }
        else
        {
            OverlayCard.Opacity = 0;
            _isVisible = true;
            StartAnimation(0, 1, TimeSpan.FromMilliseconds(200), null);
        }

        _timer.Interval = TimeSpan.FromSeconds(AppServices.Configuration.Current.OverlayDurationSeconds);
        _timer.Start();
    }

    private void Timer_Tick(object? sender, object e)
    {
        _timer.Stop();
        var hideGeneration = ++_hideGeneration;
        StartAnimation(OverlayCard.Opacity, 0, TimeSpan.FromMilliseconds(500), (_, _) =>
        {
            if (hideGeneration != _hideGeneration)
            {
                return;
            }

            _isVisible = false;
            _ = ShowWindow(_hwnd, SwHide);
        });
    }

    private void OverlayLevelSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isUpdatingSlider)
        {
            return;
        }

        var requestedLevel = (int)Math.Clamp(Math.Round(e.NewValue), 0, 100);
        OverlayPercentText.Text = $"{requestedLevel}%";
        var currentLevel = (int)Math.Clamp(Math.Round(e.OldValue), 0, 100);
        var delta = requestedLevel - currentLevel;
        if (delta == 0)
        {
            return;
        }

        switch (_currentAction)
        {
            case HotKeyAction.BrightnessDown:
            case HotKeyAction.BrightnessUp:
                _brightnessService.ChangeBrightnessByPercent(delta);
                OverlayMessageText.Text = $"亮度 {requestedLevel}%";
                break;
            default:
                return;
        }

        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(AppServices.Configuration.Current.OverlayDurationSeconds);
        _timer.Start();
    }

    private void StartAnimation(
        double fromOpacity,
        double toOpacity,
        TimeSpan duration,
        EventHandler<object>? completed)
    {
        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateDoubleAnimation(OverlayCard, nameof(UIElement.Opacity), fromOpacity, toOpacity, duration));

        if (completed is not null)
        {
            storyboard.Completed += completed;
        }

        _storyboard = storyboard;
        storyboard.Begin();
    }

    private static DoubleAnimation CreateDoubleAnimation(
        DependencyObject target,
        string propertyPath,
        double from,
        double to,
        TimeSpan duration)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, propertyPath);
        return animation;
    }

    private static string GetGlyph(HotKeyAction action, ControlActionResult result)
    {
        if (!result.Succeeded)
        {
            return "\uE7BA";
        }

        return action switch
        {
            HotKeyAction.BrightnessDown or HotKeyAction.BrightnessUp => "\uE706",
            HotKeyAction.VolumeDown or HotKeyAction.VolumeUp => "\uE767",
            HotKeyAction.MuteToggle when result.IsMuted == true => "\uE74F",
            HotKeyAction.MuteToggle => "\uE767",
            HotKeyAction.MediaPlayPause when result.PlaybackToggleState == MediaPlaybackToggleState.Paused => "\uE768",
            HotKeyAction.MediaPlayPause => "\uE769",
            HotKeyAction.MediaRewind => "\uEB9E",
            HotKeyAction.MediaFastForward => "\uEB9D",
            HotKeyAction.MediaPrevious => "\uE892",
            HotKeyAction.MediaNext => "\uE893",
            HotKeyAction.MediaStop => "\uE71A",
            _ => "\uE946"
        };
    }

    private static string GetOverlayTitle(HotKeyAction action, ControlActionResult result)
    {
        return action switch
        {
            HotKeyAction.MediaPlayPause when result.PlaybackToggleState == MediaPlaybackToggleState.Playing => "媒体播放",
            HotKeyAction.MediaPlayPause when result.PlaybackToggleState == MediaPlaybackToggleState.Paused => "媒体暂停",
            HotKeyAction.MediaPlayPause => "媒体播放/暂停",
            _ => HotKeyActionMetadata.GetDisplayName(action)
        };
    }

    private void ApplyChromeSettings()
    {
        var cornerPreference = DwmwcpRound;
        _ = DwmSetWindowAttribute(_hwnd, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

        var noneColor = unchecked((int)DwmwaColorNone);
        _ = DwmSetWindowAttribute(_hwnd, DwmwaBorderColor, ref noneColor, sizeof(int));
        _ = DwmSetWindowAttribute(_hwnd, DwmwaCaptionColor, ref noneColor, sizeof(int));
    }

    private void ApplyThemeColors()
    {
        if (OverlayRoot.ActualTheme == ElementTheme.Dark)
        {
            OverlayCard.Background = new SolidColorBrush(Color.FromArgb(255, 32, 32, 32));
            OverlayTitleText.Foreground = new SolidColorBrush(Color.FromArgb(255, 245, 245, 245));
            OverlayMessageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220));
            OverlayPercentText.Foreground = new SolidColorBrush(Color.FromArgb(255, 220, 220, 220));
            return;
        }

        OverlayCard.Background = new SolidColorBrush(Color.FromArgb(255, 250, 250, 250));
        OverlayTitleText.Foreground = new SolidColorBrush(Color.FromArgb(255, 24, 24, 24));
        OverlayMessageText.Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80));
        OverlayPercentText.Foreground = new SolidColorBrush(Color.FromArgb(255, 80, 80, 80));
    }

    private static double GetConfiguredOpacity()
    {
        return Math.Clamp(AppServices.Configuration.Current.OverlayOpacityPercent, 20, 100) / 100.0;
    }

    private void ApplyWindowOpacity()
    {
        var alpha = (byte)Math.Clamp(Math.Round(GetConfiguredOpacity() * 255), 0, 255);
        _ = SetLayeredWindowAttributes(_hwnd, 0, alpha, LwaAlpha);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}

