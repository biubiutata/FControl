using System.Collections.ObjectModel;
using FControl.Models;
using FControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FControl.Pages;

public sealed partial class AdvancedSettingsPage : Page
{
    private readonly AppConfigurationService _configurationService = AppServices.Configuration;
    private readonly AppLogService _logService = AppServices.Log;
    private readonly RuntimeEnvironmentService _runtimeService = new(AppServices.Configuration);
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
        _ = DetectRuntimesAsync(updatePathBoxes: true);
    }

    private void LoadSettings()
    {
        _isLoading = true;
        CompatibilityModeSwitch.IsOn = _configurationService.Current.CompatibilityModeEnabled;
        StartupSwitch.IsOn = _configurationService.Current.StartupEnabled;
        ColdStartSwitch.IsOn = _configurationService.Current.ColdStartEnabled;
        KeepTrayIconSwitch.IsOn = _configurationService.Current.KeepTrayIconEnabled;
        BackgroundPerformanceModeSwitch.IsOn = _configurationService.Current.BackgroundPerformanceModeEnabled;
        DebugLogSwitch.IsOn = _configurationService.Current.DebugLogEnabled;
        AutoUpdateSwitch.IsOn = _configurationService.Current.AutoUpdateEnabled;
        PythonPathBox.Text = _configurationService.Current.RuntimePaths.PythonPath;
        NodePathBox.Text = _configurationService.Current.RuntimePaths.NodePath;
        UpdateStartupOptionState();
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

        if (!StartupSwitch.IsOn)
        {
            ColdStartSwitch.IsOn = false;
        }

        if (ColdStartSwitch.IsOn)
        {
            KeepTrayIconSwitch.IsOn = true;
        }

        UpdateStartupOptionState();

        _configurationService.SetAdvancedSettings(
            CompatibilityModeSwitch.IsOn,
            StartupSwitch.IsOn,
            ColdStartSwitch.IsOn,
            KeepTrayIconSwitch.IsOn,
            BackgroundPerformanceModeSwitch.IsOn,
            DebugLogSwitch.IsOn,
            AutoUpdateSwitch.IsOn);

        StartupRegistrationService.SetStartupEnabled(StartupSwitch.IsOn, ColdStartSwitch.IsOn);
    }

    private void RuntimePathBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        SaveRuntimePaths();
    }

    private async void DetectRuntimesButton_Click(object sender, RoutedEventArgs e)
    {
        await DetectRuntimesAsync(updatePathBoxes: true, forceAutoDetection: true);
    }

    private async void TestPythonButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await _runtimeService.DetectPythonAsync();
        ApplyRuntimeResult(PythonStatusText, result);
        if (result.IsAvailable)
        {
            SetRuntimePathBox(PythonPathBox, result.Path);
            SaveRuntimePaths();
        }

        ShowRuntimeInfo(result);
    }

    private async void TestNodeButton_Click(object sender, RoutedEventArgs e)
    {
        var result = await _runtimeService.DetectNodeAsync();
        ApplyRuntimeResult(NodeStatusText, result);
        if (result.IsAvailable)
        {
            SetRuntimePathBox(NodePathBox, result.Path);
            SaveRuntimePaths();
        }

        ShowRuntimeInfo(result);
    }

    private void ClearPythonPathButton_Click(object sender, RoutedEventArgs e)
    {
        SetRuntimePathBox(PythonPathBox, string.Empty);
        SaveRuntimePaths();
        PythonStatusText.Text = "已清空，将使用自动检测。";
    }

    private void ClearNodePathButton_Click(object sender, RoutedEventArgs e)
    {
        SetRuntimePathBox(NodePathBox, string.Empty);
        SaveRuntimePaths();
        NodeStatusText.Text = "已清空，将使用自动检测。";
    }

    private async Task DetectRuntimesAsync(bool updatePathBoxes, bool forceAutoDetection = false)
    {
        RuntimeInfoBar.IsOpen = true;
        RuntimeInfoBar.Severity = InfoBarSeverity.Informational;
        RuntimeInfoBar.Message = "正在检测 Python 和 Node.js...";
        var pythonTask = forceAutoDetection ? _runtimeService.DetectPythonAutoAsync() : _runtimeService.DetectPythonAsync();
        var nodeTask = forceAutoDetection ? _runtimeService.DetectNodeAutoAsync() : _runtimeService.DetectNodeAsync();
        var results = await Task.WhenAll(pythonTask, nodeTask);
        ApplyRuntimeResult(PythonStatusText, results[0]);
        ApplyRuntimeResult(NodeStatusText, results[1]);

        if (updatePathBoxes)
        {
            var pathChanged = false;
            if (results[0].IsAvailable)
            {
                pathChanged |= SetRuntimePathBox(PythonPathBox, results[0].Path);
            }

            if (results[1].IsAvailable)
            {
                pathChanged |= SetRuntimePathBox(NodePathBox, results[1].Path);
            }

            if (pathChanged)
            {
                SaveRuntimePaths();
            }
        }

        RuntimeInfoBar.Severity = results.All(static result => result.IsAvailable) ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        RuntimeInfoBar.Message = "检测完成。检测成功的路径已显示在自定义路径输入框中。";
    }

    private static void ApplyRuntimeResult(TextBlock target, RuntimeDetectionResult result)
    {
        target.Text = result.IsAvailable
            ? $"{result.Version}"
            : result.Message;
    }

    private void ShowRuntimeInfo(RuntimeDetectionResult result)
    {
        RuntimeInfoBar.IsOpen = true;
        RuntimeInfoBar.Severity = result.IsAvailable ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        RuntimeInfoBar.Message = result.IsAvailable ? $"{result.Name} 可用：{result.Version}" : result.Message;
    }

    private bool SetRuntimePathBox(TextBox box, string path)
    {
        if (string.Equals(box.Text, path, StringComparison.Ordinal))
        {
            return false;
        }

        _isLoading = true;
        box.Text = path;
        _isLoading = false;
        return true;
    }

    private void SaveRuntimePaths()
    {
        var paths = _configurationService.Current.RuntimePaths.Clone();
        paths.PythonPath = PythonPathBox.Text.Trim();
        paths.NodePath = NodePathBox.Text.Trim();
        _configurationService.SetRuntimePaths(paths);
    }

    private void UpdateStartupOptionState()
    {
        ColdStartSwitch.IsEnabled = StartupSwitch.IsOn;
        KeepTrayIconSwitch.IsEnabled = !ColdStartSwitch.IsOn;
    }

    private void ShowInfo(string message)
    {
        ExportInfoBar.Title = message;
        ExportInfoBar.IsOpen = true;
    }
}
