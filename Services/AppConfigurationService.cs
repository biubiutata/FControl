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

    public void ResetToDefaults()
    {
        Current = AppConfigurationDefaults.CreateDefault();
        Save();
    }

    public void Save()
    {
        Current = Normalize(Current);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Current, _jsonOptions));
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

        normalized.Version = config.Version <= 0 ? 1 : config.Version;
        normalized.BrightnessStepPercent = config.BrightnessStepPercent <= 0
            ? normalized.BrightnessStepPercent
            : Math.Clamp(config.BrightnessStepPercent, 1, 25);
        normalized.VolumeStepPercent = config.VolumeStepPercent <= 0
            ? normalized.VolumeStepPercent
            : Math.Clamp(config.VolumeStepPercent, 1, 25);

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

        return normalized;
    }
}
