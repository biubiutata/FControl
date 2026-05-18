using Microsoft.Win32;

namespace FControl.Services;

internal static class StartupRegistrationService
{
    private const string BackgroundStartupArgument = "--background-startup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FControl";

    public static bool IsBackgroundStartupLaunch()
    {
        return Environment.GetCommandLineArgs()
            .Any(static argument => string.Equals(argument, BackgroundStartupArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static void SetStartupEnabled(bool enabled, bool backgroundStartupEnabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            var argument = backgroundStartupEnabled ? $" {BackgroundStartupArgument}" : string.Empty;
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"{argument}");
            return;
        }

        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
