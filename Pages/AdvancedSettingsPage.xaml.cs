using System.Collections.ObjectModel;
using FControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FControl.Pages;

public sealed partial class AdvancedSettingsPage : Page
{
    private readonly AppLogService _logService = AppServices.Log;

    public ObservableCollection<string> LogLines { get; } = [];

    public AdvancedSettingsPage()
    {
        InitializeComponent();

        foreach (var line in _logService.GetSnapshot())
        {
            LogLines.Add(line);
        }

        Loaded += AdvancedSettingsPage_Loaded;
        Unloaded += AdvancedSettingsPage_Unloaded;
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

    private void ShowInfo(string message)
    {
        ExportInfoBar.Title = message;
        ExportInfoBar.IsOpen = true;
    }
}
