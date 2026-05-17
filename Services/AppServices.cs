namespace FControl.Services;

internal static class AppServices
{
    public static AppConfigurationService Configuration { get; } = new();

    public static GlobalHotKeyService? HotKeys { get; set; }
    public static HotKeyActionService? Actions { get; set; }
}
