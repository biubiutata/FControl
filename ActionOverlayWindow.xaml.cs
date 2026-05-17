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

    private readonly nint _hwnd;
    private readonly DispatcherTimer _timer = new();
    private readonly SystemVolumeService _volumeService = new();
    private readonly MonitorBrightnessService _brightnessService = new();
    private Storyboard? _storyboard;
    private HotKeyAction _currentAction = HotKeyAction.Disabled;
    private bool _isUpdatingSlider;

    public ActionOverlayWindow()
    {
        InitializeComponent();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow.Resize(new SizeInt32(360, 148));
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

        var extendedStyle = GetWindowLong(_hwnd, GwlExStyle);
        _ = SetWindowLong(_hwnd, GwlExStyle, extendedStyle | WsExToolWindow);
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

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= Timer_Tick;
        _storyboard?.Stop();
        Close();
    }

    private void UpdateContent(ActionExecutedEventArgs e)
    {
        OverlayTitleText.Text = $"{e.Mapping.Key} · {HotKeyActionMetadata.GetDisplayName(e.Mapping.Action)}";
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
            AppWindow.Resize(new SizeInt32(360, 148));
        }
        else
        {
            OverlayLevelPanel.Visibility = Visibility.Collapsed;
            _isUpdatingSlider = true;
            OverlayLevelSlider.Value = 0;
            _isUpdatingSlider = false;
            OverlayPercentText.Text = string.Empty;
            AppWindow.Resize(new SizeInt32(360, 104));
        }
    }

    private void ShowOverlay()
    {
        _storyboard?.Stop();
        _timer.Stop();

        OverlayCard.Opacity = 0;
        OverlayCard.Background = new SolidColorBrush(Color.FromArgb(GetConfiguredAlpha(), 250, 250, 250));
        _ = ShowWindow(_hwnd, SwShow);

        StartAnimation(0, 1, TimeSpan.FromMilliseconds(200), null);
        _timer.Interval = TimeSpan.FromSeconds(AppServices.Configuration.Current.OverlayDurationSeconds);
        _timer.Start();
    }

    private void Timer_Tick(object? sender, object e)
    {
        _timer.Stop();
        StartAnimation(OverlayCard.Opacity, 0, TimeSpan.FromMilliseconds(500), (_, _) =>
        {
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
        var currentLevel = (int)Math.Clamp(Math.Round(e.OldValue), 0, 100);
        var delta = requestedLevel - currentLevel;
        if (delta == 0)
        {
            return;
        }

        switch (_currentAction)
        {
            case HotKeyAction.VolumeDown:
            case HotKeyAction.VolumeUp:
            case HotKeyAction.MuteToggle:
                _volumeService.ChangeVolumeByPercent(delta);
                OverlayPercentText.Text = $"{requestedLevel}%";
                OverlayMessageText.Text = $"音量 {requestedLevel}%";
                break;
            case HotKeyAction.BrightnessDown:
            case HotKeyAction.BrightnessUp:
                _brightnessService.ChangeBrightnessByPercent(delta);
                OverlayPercentText.Text = $"{requestedLevel}%";
                OverlayMessageText.Text = $"亮度 {requestedLevel}%";
                break;
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
            HotKeyAction.MediaPlayPause => "\uE768",
            HotKeyAction.MediaRewind => "\uEB9E",
            HotKeyAction.MediaFastForward => "\uEB9D",
            HotKeyAction.MediaPrevious => "\uE892",
            HotKeyAction.MediaNext => "\uE893",
            HotKeyAction.MediaStop => "\uE71A",
            _ => "\uE946"
        };
    }

    private static byte GetConfiguredAlpha()
    {
        return (byte)Math.Round(Math.Clamp(AppServices.Configuration.Current.OverlayOpacityPercent, 20, 100) * 255 / 100.0);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);
}
