using System.Collections.ObjectModel;
using FControl.Services;
using Microsoft.UI.Xaml.Controls;

namespace FControl.Pages;

public sealed partial class DisplaySettingsPage : Page
{
    private readonly AppConfigurationService _configurationService = AppServices.Configuration;
    private readonly MonitorBrightnessService _monitorService = new();
    private bool _isLoading = true;

    public DisplaySettingsPage()
    {
        InitializeComponent();
        LoadSettings();
        _ = DetectMonitorsAsync();
    }

    public ObservableCollection<MonitorDetectionItem> Monitors { get; } = [];

    private void LoadSettings()
    {
        _isLoading = true;
        OverlayDurationSlider.Value = _configurationService.Current.OverlayDurationSeconds;
        OverlayOpacitySlider.Value = _configurationService.Current.OverlayOpacityPercent;
        BrightnessStepNumberBox.Value = _configurationService.Current.BrightnessStepPercent;
        VolumeStepNumberBox.Value = _configurationService.Current.VolumeStepPercent;
        _isLoading = false;
    }

    private void OverlaySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _configurationService.SetOverlaySettings(
            ClampOverlayDuration(OverlayDurationSlider.Value, _configurationService.Current.OverlayDurationSeconds),
            ClampOverlayOpacity(OverlayOpacitySlider.Value, _configurationService.Current.OverlayOpacityPercent));
    }

    private void ControlStepNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_isLoading)
        {
            return;
        }

        _configurationService.SetControlSteps(
            ClampPercentStep(BrightnessStepNumberBox.Value, _configurationService.Current.BrightnessStepPercent),
            ClampPercentStep(VolumeStepNumberBox.Value, _configurationService.Current.VolumeStepPercent));
    }

    private async void DetectMonitorsButton_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        await DetectMonitorsAsync();
    }

    private async Task DetectMonitorsAsync()
    {
        MonitorInfoBar.IsOpen = true;
        MonitorInfoBar.Severity = InfoBarSeverity.Informational;
        MonitorInfoBar.Message = "正在检测显示器 DDC/CI 状态...";
        Monitors.Clear();

        try
        {
            var monitors = await Task.Run(_monitorService.EnumerateMonitors);
            foreach (var monitor in monitors)
            {
                Monitors.Add(MonitorDetectionItem.FromInfo(monitor));
            }

            MonitorInfoBar.Severity = monitors.Any(static monitor => monitor.IsBrightnessSupported)
                ? InfoBarSeverity.Success
                : InfoBarSeverity.Warning;
            MonitorInfoBar.Message = monitors.Count == 0
                ? "未发现物理显示器。请检查连接方式、显卡驱动或显示器是否支持 DDC/CI。"
                : "检测完成。若不可用，请检查显示器是否支持 DDC/CI 功能，并确认 OSD 菜单中已开启 DDC/CI。";
        }
        catch (Exception ex)
        {
            MonitorInfoBar.Severity = InfoBarSeverity.Error;
            MonitorInfoBar.Message = $"DDC/CI 检测失败：{ex.Message}";
        }
    }

    private static int ClampPercentStep(double value, int fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return (int)Math.Clamp(Math.Round(value), 1, 25);
    }

    private static double ClampOverlayDuration(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return Math.Clamp(Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2, 1, 10);
    }

    private static int ClampOverlayOpacity(double value, int fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return fallback;
        }

        return (int)Math.Clamp(Math.Round(value), 20, 100);
    }
}

public sealed class MonitorDetectionItem
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;

    public static MonitorDetectionItem FromInfo(MonitorBrightnessInfo info)
    {
        var detail = info.IsBrightnessSupported
            ? $"支持亮度读取/写入，范围 {info.MinimumBrightness}-{info.MaximumBrightness}，当前 {info.BrightnessPercent ?? 0}%"
            : $"{info.ErrorMessage} 转接线、扩展坞、KVM 或显卡驱动也可能导致不可用。";
        return new MonitorDetectionItem { Title = info.Description, Detail = detail };
    }
}
