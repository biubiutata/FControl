namespace FControl.Services;

internal static class AppServices
{
    public static AppLogService Log { get; } = new();
    public static AppConfigurationService Configuration { get; } = new();

    public static GlobalHotKeyService? HotKeys { get; set; }
    public static HotKeyActionService? Actions { get; set; }
    public static Action? RequestExit { get; set; }
}
