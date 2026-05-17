namespace FControl.Models;

public sealed class AppConfiguration
{
    public int Version { get; set; } = 1;
    public List<KeyMappingConfig> KeyMappings { get; set; } = AppConfigurationDefaults.CreateDefaultKeyMappings();
    public double OverlayDurationSeconds { get; set; } = 3;
    public int OverlayOpacityPercent { get; set; } = 80;
    public int BrightnessStepPercent { get; set; } = 5;
    public int VolumeStepPercent { get; set; } = 2;
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

public static class AppConfigurationDefaults
{
    public static IReadOnlyList<string> FunctionKeys { get; } =
        Enumerable.Range(1, 12).Select(static number => $"F{number}").ToArray();

    public static AppConfiguration CreateDefault()
    {
        return new AppConfiguration
        {
            Version = 1,
            KeyMappings = CreateDefaultKeyMappings(),
            OverlayDurationSeconds = 3,
            OverlayOpacityPercent = 80,
            BrightnessStepPercent = 5,
            VolumeStepPercent = 2
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
            CreateMapping("F7", HotKeyAction.MediaRewind, 2),
            CreateMapping("F8", HotKeyAction.MediaPlayPause),
            CreateMapping("F9", HotKeyAction.MediaFastForward, 2),
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
