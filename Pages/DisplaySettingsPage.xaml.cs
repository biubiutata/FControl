using FControl.Services;
using Microsoft.UI.Xaml.Controls;

namespace FControl.Pages;

public sealed partial class DisplaySettingsPage : Page
{
    private readonly AppConfigurationService _configurationService = AppServices.Configuration;
    private bool _isLoading = true;

    public DisplaySettingsPage()
    {
        InitializeComponent();
        LoadSettings();
    }

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
