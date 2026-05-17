using System.Collections.ObjectModel;
using FControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FControl.Pages;

public sealed partial class AdvancedSettingsPage : Page
{
    private readonly AppConfigurationService _configurationService = AppServices.Configuration;
    private readonly AppLogService _logService = AppServices.Log;
    private bool _isLoading = true;

    public ObservableCollection<string> LogLines { get; } = [];

    public AdvancedSettingsPage()
    {
        InitializeComponent();

        foreach (var line in _logService.GetSnapshot())
        {
            LogLines.Add(line);
        }

        LoadSettings();
        Loaded += AdvancedSettingsPage_Loaded;
        Unloaded += AdvancedSettingsPage_Unloaded;
    }

    private void LoadSettings()
    {
        _isLoading = true;
        CompatibilityModeSwitch.IsOn = _configurationService.Current.CompatibilityModeEnabled;
        StartupSwitch.IsOn = _configurationService.Current.StartupEnabled;
        ColdStartSwitch.IsOn = _configurationService.Current.ColdStartEnabled;
        KeepTrayIconSwitch.IsOn = _configurationService.Current.KeepTrayIconEnabled;
        _isLoading = false;
    }

    private void AdvancedSettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        _logService.LineAdded += LogService_LineAdded;
    }

    private void AdvancedSettingsPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _logService.LineAdded -= LogService_LineAdded;
    }

    private void LogService_LineAdded(object? sender, string line)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LogLines.Add(line);
            LogListView.ScrollIntoView(line);
        });
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        _logService.Clear();
        LogLines.Clear();
        ShowInfo("日志已清空。");
    }

    private void ExportLogButton_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"FControl-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        _logService.Export(path);
        ShowInfo($"日志已导出：{path}");
    }

    private void AdvancedSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _configurationService.SetAdvancedSettings(
            CompatibilityModeSwitch.IsOn,
            StartupSwitch.IsOn,
            ColdStartSwitch.IsOn,
            KeepTrayIconSwitch.IsOn);

        StartupRegistrationService.SetStartupEnabled(StartupSwitch.IsOn);
    }

    private void ShowInfo(string message)
    {
        ExportInfoBar.Title = message;
        ExportInfoBar.IsOpen = true;
    }
}
