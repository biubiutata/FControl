namespace FControl.Models;

public sealed class AppConfiguration
{
    public int Version { get; set; } = AppConfigurationDefaults.CurrentVersion;
    public List<KeyMappingConfig> KeyMappings { get; set; } = AppConfigurationDefaults.CreateDefaultKeyMappings();
    public List<CustomHotkeyConfig> CustomHotkeys { get; set; } = [];
    public RuntimePathConfig RuntimePaths { get; set; } = new();
    public List<ScriptRunResult> RecentScriptRuns { get; set; } = [];
    public bool ScriptSafetyWarningAccepted { get; set; }
    public double OverlayDurationSeconds { get; set; } = 3;
    public int OverlayOpacityPercent { get; set; } = 80;
    public int BrightnessStepPercent { get; set; } = 5;
    public int VolumeStepPercent { get; set; } = 2;
    public bool CompatibilityModeEnabled { get; set; }
    public bool StartupEnabled { get; set; }
    public bool ColdStartEnabled { get; set; }
    public bool KeepTrayIconEnabled { get; set; } = true;
    public bool DebugLogEnabled { get; set; }
}

public sealed class KeyMappingConfig
{
    public string Key { get; set; } = string.Empty;
    public HotKeyAction Action { get; set; } = HotKeyAction.Disabled;
    public int SeekSeconds { get; set; } = 2;

    public KeyMappingConfig Clone()
    {
        return new KeyMappingConfig
        {
            Key = Key,
            Action = Action,
            SeekSeconds = SeekSeconds
        };
    }
}

public sealed class CustomHotkeyConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Hotkey { get; set; } = string.Empty;
    public ScriptType ScriptType { get; set; } = ScriptType.WindowsShell;
    public ScriptMode ScriptMode { get; set; } = ScriptMode.Inline;
    public string ScriptPath { get; set; } = string.Empty;
    public string InlineCode { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public RunWindowMode RunWindowMode { get; set; } = RunWindowMode.Hidden;
    public int TimeoutSeconds { get; set; } = 30;
    public ConcurrencyPolicy ConcurrencyPolicy { get; set; } = ConcurrencyPolicy.IgnoreIfRunning;
    public bool ShowOverlay { get; set; } = true;

    public CustomHotkeyConfig Clone()
    {
        return new CustomHotkeyConfig
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            Enabled = Enabled,
            Name = Name,
            Hotkey = Hotkey,
            ScriptType = ScriptType,
            ScriptMode = ScriptMode,
            ScriptPath = ScriptPath,
            InlineCode = InlineCode,
            Arguments = Arguments,
            WorkingDirectory = WorkingDirectory,
            RunWindowMode = RunWindowMode,
            TimeoutSeconds = TimeoutSeconds,
            ConcurrencyPolicy = ConcurrencyPolicy,
            ShowOverlay = ShowOverlay
        };
    }
}

public enum ScriptType
{
    WindowsShell,
    PowerShell,
    Bash,
    Python,
    NodeJs,
    ExternalProgram
}

public enum ScriptMode
{
    File,
    Inline
}

public enum RunWindowMode
{
    Hidden,
    Normal,
    Minimized
}

public enum ConcurrencyPolicy
{
    IgnoreIfRunning,
    AllowConcurrent,
    RestartPrevious
}

public sealed class RuntimePathConfig
{
    public string PythonPath { get; set; } = string.Empty;
    public string NodePath { get; set; } = string.Empty;
    public string BashPath { get; set; } = string.Empty;
    public string PowerShellPath { get; set; } = string.Empty;

    public RuntimePathConfig Clone()
    {
        return new RuntimePathConfig
        {
            PythonPath = PythonPath,
            NodePath = NodePath,
            BashPath = BashPath,
            PowerShellPath = PowerShellPath
        };
    }
}

public sealed class ScriptRunResult
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string HotkeyId { get; set; } = string.Empty;
    public string HotkeyName { get; set; } = string.Empty;
    public string Hotkey { get; set; } = string.Empty;
    public ScriptType ScriptType { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public int DurationMilliseconds { get; set; }
    public bool Succeeded { get; set; }
    public bool TimedOut { get; set; }
    public int? ExitCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public string OutputSummary { get; set; } = string.Empty;
    public string ErrorSummary { get; set; } = string.Empty;

    public string Summary
    {
        get
        {
            var status = Succeeded ? "成功" : TimedOut ? "超时" : "失败";
            var exit = ExitCode is null ? string.Empty : $"，退出码 {ExitCode}";
            return $"{StartedAt:HH:mm:ss} {status}{exit}，{DurationMilliseconds} ms";
        }
    }

    public ScriptRunResult Clone()
    {
        return new ScriptRunResult
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            HotkeyId = HotkeyId,
            HotkeyName = HotkeyName,
            Hotkey = Hotkey,
            ScriptType = ScriptType,
            StartedAt = StartedAt,
            DurationMilliseconds = DurationMilliseconds,
            Succeeded = Succeeded,
            TimedOut = TimedOut,
            ExitCode = ExitCode,
            Message = Message,
            OutputSummary = OutputSummary,
            ErrorSummary = ErrorSummary
        };
    }
}

public static class ScriptTypeMetadata
{
    private static readonly IReadOnlyDictionary<ScriptType, string> Names = new Dictionary<ScriptType, string>
    {
        [ScriptType.WindowsShell] = "Windows Shell",
        [ScriptType.PowerShell] = "PowerShell",
        [ScriptType.Bash] = "Bash",
        [ScriptType.Python] = "Python",
        [ScriptType.NodeJs] = "Node.js",
        [ScriptType.ExternalProgram] = "外部程序"
    };

    public static IReadOnlyList<string> AvailableNames { get; } = Names.Values.ToArray();

    public static string GetDisplayName(ScriptType type)
    {
        return Names.TryGetValue(type, out var name) ? name : Names[ScriptType.WindowsShell];
    }

    public static ScriptType FromDisplayName(string? displayName)
    {
        foreach (var pair in Names)
        {
            if (pair.Value == displayName)
            {
                return pair.Key;
            }
        }

        return ScriptType.WindowsShell;
    }
}

public static class ScriptModeMetadata
{
    public static IReadOnlyList<string> AvailableNames { get; } = ["脚本文件", "内联代码/命令"];

    public static string GetDisplayName(ScriptMode mode)
    {
        return mode == ScriptMode.File ? "脚本文件" : "内联代码/命令";
    }

    public static ScriptMode FromDisplayName(string? displayName)
    {
        return displayName == "脚本文件" ? ScriptMode.File : ScriptMode.Inline;
    }
}

public static class RunWindowModeMetadata
{
    public static IReadOnlyList<string> AvailableNames { get; } = ["隐藏窗口", "普通窗口", "最小化窗口"];

    public static string GetDisplayName(RunWindowMode mode)
    {
        return mode switch
        {
            RunWindowMode.Normal => "普通窗口",
            RunWindowMode.Minimized => "最小化窗口",
            _ => "隐藏窗口"
        };
    }

    public static RunWindowMode FromDisplayName(string? displayName)
    {
        return displayName switch
        {
            "普通窗口" => RunWindowMode.Normal,
            "最小化窗口" => RunWindowMode.Minimized,
            _ => RunWindowMode.Hidden
        };
    }
}

public static class ConcurrencyPolicyMetadata
{
    public static IReadOnlyList<string> AvailableNames { get; } = ["运行中则忽略", "允许并发运行", "终止上次后重跑"];

    public static string GetDisplayName(ConcurrencyPolicy policy)
    {
        return policy switch
        {
            ConcurrencyPolicy.AllowConcurrent => "允许并发运行",
            ConcurrencyPolicy.RestartPrevious => "终止上次后重跑",
            _ => "运行中则忽略"
        };
    }

    public static ConcurrencyPolicy FromDisplayName(string? displayName)
    {
        return displayName switch
        {
            "允许并发运行" => ConcurrencyPolicy.AllowConcurrent,
            "终止上次后重跑" => ConcurrencyPolicy.RestartPrevious,
            _ => ConcurrencyPolicy.IgnoreIfRunning
        };
    }
}

public static class AppConfigurationDefaults
{
    public const int CurrentVersion = 3;

    public static IReadOnlyList<string> FunctionKeys { get; } =
        Enumerable.Range(1, 12).Select(static number => $"F{number}").ToArray();

    public static AppConfiguration CreateDefault()
    {
        return new AppConfiguration
        {
            Version = CurrentVersion,
            KeyMappings = CreateDefaultKeyMappings(),
            CustomHotkeys = [],
            RuntimePaths = new RuntimePathConfig(),
            RecentScriptRuns = [],
            ScriptSafetyWarningAccepted = false,
            OverlayDurationSeconds = 3,
            OverlayOpacityPercent = 80,
            BrightnessStepPercent = 5,
            VolumeStepPercent = 2,
            CompatibilityModeEnabled = false,
            StartupEnabled = false,
            ColdStartEnabled = false,
            KeepTrayIconEnabled = true,
            DebugLogEnabled = false
        };
    }

    public static List<KeyMappingConfig> CreateDefaultKeyMappings()
    {
        return
        [
            CreateMapping("F1", HotKeyAction.BrightnessDown),
            CreateMapping("F2", HotKeyAction.BrightnessUp),
            CreateMapping("F3", HotKeyAction.Disabled),
            CreateMapping("F4", HotKeyAction.Disabled),
            CreateMapping("F5", HotKeyAction.Disabled),
            CreateMapping("F6", HotKeyAction.Disabled),
            CreateMapping("F7", HotKeyAction.MediaPrevious),
            CreateMapping("F8", HotKeyAction.MediaPlayPause),
            CreateMapping("F9", HotKeyAction.MediaNext),
            CreateMapping("F10", HotKeyAction.MuteToggle),
            CreateMapping("F11", HotKeyAction.VolumeDown),
            CreateMapping("F12", HotKeyAction.VolumeUp)
        ];
    }

    private static KeyMappingConfig CreateMapping(string key, HotKeyAction action, int seekSeconds = 2)
    {
        return new KeyMappingConfig
        {
            Key = key,
            Action = action,
            SeekSeconds = seekSeconds
        };
    }
}
