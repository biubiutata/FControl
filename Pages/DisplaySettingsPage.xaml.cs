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
        BrightnessStepNumberBox.Value = _configurationService.Current.BrightnessStepPercent;
        VolumeStepNumberBox.Value = _configurationService.Current.VolumeStepPercent;
        _isLoading = false;
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
}
