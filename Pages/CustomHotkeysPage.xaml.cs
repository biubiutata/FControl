using System.Collections.ObjectModel;
using System.ComponentModel;
using FControl.Models;
using FControl.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FControl.Pages;

public sealed partial class CustomHotkeysPage : Page
{
    private readonly AppConfigurationService _configurationService = AppServices.Configuration;
    private bool _isApplyingToggle;

    public CustomHotkeysPage()
    {
        InitializeComponent();
        LoadHotkeys();
        Loaded += CustomHotkeysPage_Loaded;
        Unloaded += CustomHotkeysPage_Unloaded;
    }

    public ObservableCollection<CustomHotkeyItem> Hotkeys { get; } = [];
    public ObservableCollection<RecentScriptRunItem> RecentRuns { get; } = [];

    private void LoadHotkeys()
    {
        Hotkeys.Clear();
        foreach (var hotkey in _configurationService.Current.CustomHotkeys)
        {
            Hotkeys.Add(CustomHotkeyItem.FromConfig(hotkey));
        }

        LoadRecentRuns();
    }

    private void LoadRecentRuns()
    {
        RecentRuns.Clear();
        foreach (var result in _configurationService.Current.RecentScriptRuns)
        {
            RecentRuns.Add(RecentScriptRunItem.FromResult(result));
        }
    }

    private void CustomHotkeysPage_Loaded(object sender, RoutedEventArgs e)
    {
        _configurationService.ConfigurationChanged += ConfigurationService_ConfigurationChanged;
    }

    private void CustomHotkeysPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _configurationService.ConfigurationChanged -= ConfigurationService_ConfigurationChanged;
    }

    private void ConfigurationService_ConfigurationChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(LoadRecentRuns);
    }

    private async void AddHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        var item = CustomHotkeyItem.CreateDefault();
        if (await ShowEditorAsync(item, "新增快捷键"))
        {
            Hotkeys.Add(item);
            if (!await SaveHotkeysAsync(showSuccess: true))
            {
                Hotkeys.Remove(item);
            }
        }
    }

    private async void EditHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || FindItem(id) is not { } item)
        {
            return;
        }

        var edit = item.Clone();
        if (await ShowEditorAsync(edit, "编辑快捷键"))
        {
            var before = item.Clone();
            item.CopyFrom(edit);
            if (!await SaveHotkeysAsync(showSuccess: true))
            {
                item.CopyFrom(before);
            }
        }
    }

    private async void CopyHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || FindItem(id) is not { } item)
        {
            return;
        }

        var copy = item.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name += " - 副本";
        copy.Enabled = false;
        Hotkeys.Add(copy);
        await SaveHotkeysAsync(showSuccess: true);
    }

    private async void TestHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || FindItem(id) is not { } item)
        {
            return;
        }

        ShowStatus("正在测试脚本...", InfoBarSeverity.Informational);
        var result = AppServices.Actions is null
            ? await new ScriptExecutionService(_configurationService).TestAsync(item.ToConfig())
            : await AppServices.Actions.TestScriptAsync(item.ToConfig());
        ShowStatus(result.Succeeded ? $"测试成功：{result.Message}" : $"测试失败：{result.Message}", result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private async void DeleteHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string id } || FindItem(id) is not { } item)
        {
            return;
        }

        Hotkeys.Remove(item);
        await SaveHotkeysAsync(showSuccess: true);
    }

    private void ClearRecentRunsButton_Click(object sender, RoutedEventArgs e)
    {
        _configurationService.ClearScriptRunResults();
        LoadRecentRuns();
        ShowStatus("最近执行记录已清空。", InfoBarSeverity.Success);
    }

    private async void HotkeyEnabledSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isApplyingToggle || sender is not ToggleSwitch toggle || toggle.Tag is not string id || FindItem(id) is not { } item)
        {
            return;
        }

        var requestedEnabled = toggle.IsOn;
        var previousEnabled = item.Enabled;
        if (item.Enabled != requestedEnabled)
        {
            item.Enabled = requestedEnabled;
        }

        var persisted = _configurationService.Current.CustomHotkeys.FirstOrDefault(config => config.Id == id);
        if (persisted is not null && persisted.Enabled == requestedEnabled)
        {
            return;
        }

        if (await SaveHotkeysAsync(showSuccess: true))
        {
            return;
        }

        _isApplyingToggle = true;
        item.Enabled = previousEnabled;
        toggle.IsOn = previousEnabled;
        _isApplyingToggle = false;
    }

    private async Task<bool> SaveHotkeysAsync(bool showSuccess)
    {
        var configs = Hotkeys.Select(static item => item.ToConfig()).ToList();
        var conflicts = AppServices.HotKeys?.ValidateCustomHotkeys(configs) ?? [];
        if (conflicts.Count > 0)
        {
            ShowStatus(string.Join(Environment.NewLine, conflicts.Select(static conflict => $"{conflict.Hotkey}：{conflict.Message}")), InfoBarSeverity.Error);
            return false;
        }

        if (configs.Any(static config => config.Enabled) && !_configurationService.Current.ScriptSafetyWarningAccepted)
        {
            var dialog = new ContentDialog
            {
                Title = "确认启用自定义脚本",
                Content = "脚本会以当前 Windows 用户权限运行。请仅配置和执行可信脚本。",
                PrimaryButtonText = "我已了解",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                ShowStatus("已取消保存。", InfoBarSeverity.Warning);
                return false;
            }

            _configurationService.AcceptScriptSafetyWarning();
        }

        _configurationService.SetCustomHotkeys(configs);
        LoadRecentRuns();
        if (showSuccess)
        {
            ShowStatus("快捷键已保存。", InfoBarSeverity.Success);
        }

        return true;
    }

    private async Task<bool> ShowEditorAsync(CustomHotkeyItem item, string title)
    {
        var editor = new CustomHotkeyEditorWindow(XamlRoot, item, title, candidate => ValidateCandidate(item.Id, candidate));
        return await editor.ShowEditorAsync();
    }

    private IReadOnlyList<HotkeyConflict> ValidateCandidate(string editingId, CustomHotkeyConfig candidate)
    {
        var configs = Hotkeys
            .Where(item => item.Id != editingId)
            .Select(static item => item.ToConfig())
            .Append(candidate)
            .ToList();
        return AppServices.HotKeys?.ValidateCustomHotkeys(configs) ?? [];
    }

    private CustomHotkeyItem? FindItem(string id)
    {
        return Hotkeys.FirstOrDefault(item => item.Id == id);
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }
}

public sealed class CustomHotkeyItem : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _enabled;
    private string _hotkey = string.Empty;
    private string _scriptTypeName = ScriptTypeMetadata.GetDisplayName(ScriptType.WindowsShell);
    private string _scriptModeName = ScriptModeMetadata.GetDisplayName(ScriptMode.Inline);
    private string _scriptPath = string.Empty;
    private string _inlineCode = string.Empty;
    private string _arguments = string.Empty;
    private string _workingDirectory = string.Empty;
    private string _runWindowModeName = RunWindowModeMetadata.GetDisplayName(RunWindowMode.Hidden);
    private int _timeoutSeconds = 30;
    private string _concurrencyPolicyName = ConcurrencyPolicyMetadata.GetDisplayName(ConcurrencyPolicy.IgnoreIfRunning);
    private bool _showOverlay = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public string Name
    {
        get => _name;
        set => SetField(ref _name, value);
    }

    public string Hotkey
    {
        get => _hotkey;
        set => SetField(ref _hotkey, value);
    }

    public string ScriptTypeName
    {
        get => _scriptTypeName;
        set => SetField(ref _scriptTypeName, value);
    }

    public string ScriptModeName
    {
        get => _scriptModeName;
        set
        {
            if (SetField(ref _scriptModeName, value))
            {
                OnPropertyChanged(nameof(TargetSummary));
            }
        }
    }

    public string ScriptPath
    {
        get => _scriptPath;
        set
        {
            if (SetField(ref _scriptPath, value))
            {
                OnPropertyChanged(nameof(TargetSummary));
            }
        }
    }

    public string InlineCode
    {
        get => _inlineCode;
        set
        {
            if (SetField(ref _inlineCode, value))
            {
                OnPropertyChanged(nameof(TargetSummary));
            }
        }
    }

    public string Arguments
    {
        get => _arguments;
        set => SetField(ref _arguments, value);
    }

    public string WorkingDirectory
    {
        get => _workingDirectory;
        set => SetField(ref _workingDirectory, value);
    }

    public string RunWindowModeName
    {
        get => _runWindowModeName;
        set => SetField(ref _runWindowModeName, value);
    }

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        set => SetField(ref _timeoutSeconds, value);
    }

    public string ConcurrencyPolicyName
    {
        get => _concurrencyPolicyName;
        set => SetField(ref _concurrencyPolicyName, value);
    }

    public bool ShowOverlay
    {
        get => _showOverlay;
        set => SetField(ref _showOverlay, value);
    }

    public string TargetSummary => ScriptModeMetadata.FromDisplayName(ScriptModeName) == ScriptMode.File
        ? ScriptPath
        : InlineCode.ReplaceLineEndings(" ");

    public static CustomHotkeyItem CreateDefault()
    {
        return new CustomHotkeyItem
        {
            Name = "运行自定义脚本",
            Hotkey = "Ctrl+Shift+K",
            Enabled = false
        };
    }

    public static CustomHotkeyItem FromConfig(CustomHotkeyConfig config)
    {
        return new CustomHotkeyItem
        {
            Id = config.Id,
            Enabled = config.Enabled,
            Name = config.Name,
            Hotkey = config.Hotkey,
            ScriptTypeName = ScriptTypeMetadata.GetDisplayName(config.ScriptType),
            ScriptModeName = ScriptModeMetadata.GetDisplayName(config.ScriptMode),
            ScriptPath = config.ScriptPath,
            InlineCode = config.InlineCode,
            Arguments = config.Arguments,
            WorkingDirectory = config.WorkingDirectory,
            RunWindowModeName = RunWindowModeMetadata.GetDisplayName(config.RunWindowMode),
            TimeoutSeconds = config.TimeoutSeconds,
            ConcurrencyPolicyName = ConcurrencyPolicyMetadata.GetDisplayName(config.ConcurrencyPolicy),
            ShowOverlay = config.ShowOverlay
        };
    }

    public CustomHotkeyConfig ToConfig()
    {
        return new CustomHotkeyConfig
        {
            Id = Id,
            Enabled = Enabled,
            Name = Name,
            Hotkey = Hotkey,
            ScriptType = ScriptTypeMetadata.FromDisplayName(ScriptTypeName),
            ScriptMode = ScriptModeMetadata.FromDisplayName(ScriptModeName),
            ScriptPath = ScriptPath,
            InlineCode = InlineCode,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            RunWindowMode = RunWindowModeMetadata.FromDisplayName(RunWindowModeName),
            TimeoutSeconds = TimeoutSeconds,
            ConcurrencyPolicy = ConcurrencyPolicyMetadata.FromDisplayName(ConcurrencyPolicyName),
            ShowOverlay = ShowOverlay
        };
    }

    public CustomHotkeyItem Clone()
    {
        return FromConfig(ToConfig());
    }

    public void CopyFrom(CustomHotkeyItem item)
    {
        Enabled = item.Enabled;
        Name = item.Name;
        Hotkey = item.Hotkey;
        ScriptTypeName = item.ScriptTypeName;
        ScriptModeName = item.ScriptModeName;
        ScriptPath = item.ScriptPath;
        InlineCode = item.InlineCode;
        Arguments = item.Arguments;
        WorkingDirectory = item.WorkingDirectory;
        RunWindowModeName = item.RunWindowModeName;
        TimeoutSeconds = item.TimeoutSeconds;
        ConcurrencyPolicyName = item.ConcurrencyPolicyName;
        ShowOverlay = item.ShowOverlay;
    }

    private bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class RecentScriptRunItem
{
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;

    public static RecentScriptRunItem FromResult(ScriptRunResult result)
    {
        var title = $"{result.HotkeyName} · {result.Hotkey} · {result.Summary}";
        var detailParts = new[] { result.Message, result.OutputSummary, result.ErrorSummary }.Where(static part => !string.IsNullOrWhiteSpace(part));
        return new RecentScriptRunItem { Title = title, Details = string.Join(" | ", detailParts) };
    }
}

