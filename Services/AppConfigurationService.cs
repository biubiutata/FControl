using System.Text.Json;
using System.Text.Json.Serialization;
using FControl.Models;

namespace FControl.Services;

public sealed class AppConfigurationService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppConfigurationService()
    {
        ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FControl",
            "settings.json");

        Current = LoadConfiguration();
        Save();
    }

    public event EventHandler? ConfigurationChanged;

    public string ConfigPath { get; }
    public AppConfiguration Current { get; private set; }

    public void SetKeyMappings(IEnumerable<KeyMappingConfig> mappings)
    {
        Current.KeyMappings = mappings.Select(static mapping => mapping.Clone()).ToList();
        Current = Normalize(Current);
        Save();
    }

    public void SetControlSteps(int brightnessStepPercent, int volumeStepPercent)
    {
        Current.BrightnessStepPercent = brightnessStepPercent;
        Current.VolumeStepPercent = volumeStepPercent;
        Current = Normalize(Current);
        Save();
    }

    public void SetOverlaySettings(double durationSeconds, int opacityPercent)
    {
        Current.OverlayDurationSeconds = durationSeconds;
        Current.OverlayOpacityPercent = opacityPercent;
        Current = Normalize(Current);
        Save();
    }

    public void SetAdvancedSettings(
        bool compatibilityModeEnabled,
        bool startupEnabled,
        bool coldStartEnabled,
        bool keepTrayIconEnabled,
        bool debugLogEnabled)
    {
        Current.CompatibilityModeEnabled = compatibilityModeEnabled;
        Current.StartupEnabled = startupEnabled;
        Current.ColdStartEnabled = coldStartEnabled;
        Current.KeepTrayIconEnabled = keepTrayIconEnabled;
        Current.DebugLogEnabled = debugLogEnabled;
        Current = Normalize(Current);
        Save();
    }

    public void ResetToDefaults()
    {
        Current = AppConfigurationDefaults.CreateDefault();
        Save();
    }

    public void Save()
    {
        Current = Normalize(Current);
        AppServices.Log.IsEnabled = Current.DebugLogEnabled;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, _jsonOptions));
        AppServices.Log.Info($"配置已保存：{ConfigPath}");
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    private AppConfiguration LoadConfiguration()
    {
        if (!File.Exists(ConfigPath))
        {
            return AppConfigurationDefaults.CreateDefault();
        }

        try
        {
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<AppConfiguration>(json, _jsonOptions);
            return Normalize(config);
        }
        catch
        {
            return AppConfigurationDefaults.CreateDefault();
        }
    }

    private static AppConfiguration Normalize(AppConfiguration? config)
    {
        var normalized = AppConfigurationDefaults.CreateDefault();
        if (config is null)
        {
            return normalized;
        }

        var storedVersion = config.Version <= 0 ? 1 : config.Version;
        normalized.Version = AppConfigurationDefaults.CurrentVersion;
        normalized.OverlayDurationSeconds = config.OverlayDurationSeconds <= 0
            ? normalized.OverlayDurationSeconds
            : Math.Clamp(Math.Round(config.OverlayDurationSeconds * 2, MidpointRounding.AwayFromZero) / 2, 1, 10);
        normalized.OverlayOpacityPercent = config.OverlayOpacityPercent <= 0
            ? normalized.OverlayOpacityPercent
            : Math.Clamp(config.OverlayOpacityPercent, 20, 100);
        normalized.BrightnessStepPercent = config.BrightnessStepPercent <= 0
            ? normalized.BrightnessStepPercent
            : Math.Clamp(config.BrightnessStepPercent, 1, 25);
        normalized.VolumeStepPercent = config.VolumeStepPercent <= 0
            ? normalized.VolumeStepPercent
            : Math.Clamp(config.VolumeStepPercent, 1, 25);
        normalized.CompatibilityModeEnabled = config.CompatibilityModeEnabled;
        normalized.StartupEnabled = config.StartupEnabled;
        normalized.ColdStartEnabled = config.ColdStartEnabled;
        normalized.KeepTrayIconEnabled = config.KeepTrayIconEnabled;
        normalized.DebugLogEnabled = config.DebugLogEnabled;

        foreach (var defaultMapping in normalized.KeyMappings)
        {
            var storedMapping = config.KeyMappings.FirstOrDefault(
                mapping => string.Equals(mapping.Key, defaultMapping.Key, StringComparison.OrdinalIgnoreCase));

            if (storedMapping is null)
            {
                continue;
            }

            defaultMapping.Action = Enum.IsDefined(storedMapping.Action)
                ? storedMapping.Action
                : HotKeyAction.Disabled;
            defaultMapping.SeekSeconds = Math.Clamp(storedMapping.SeekSeconds, 1, 60);
        }

        if (storedVersion < 2)
        {
            MigrateDefaultMediaKeys(normalized);
        }

        return normalized;
    }

    private static void MigrateDefaultMediaKeys(AppConfiguration config)
    {
        var f7 = config.KeyMappings.FirstOrDefault(static mapping => mapping.Key == "F7");
        if (f7 is { Action: HotKeyAction.MediaRewind, SeekSeconds: 2 })
        {
            f7.Action = HotKeyAction.MediaPrevious;
        }

        var f9 = config.KeyMappings.FirstOrDefault(static mapping => mapping.Key == "F9");
        if (f9 is { Action: HotKeyAction.MediaFastForward, SeekSeconds: 2 })
        {
            f9.Action = HotKeyAction.MediaNext;
        }
    }
}
