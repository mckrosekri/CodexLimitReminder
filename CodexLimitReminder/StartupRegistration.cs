using Microsoft.Win32;

namespace CodexLimitReminder;

internal static class StartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "CodexLimitReminder";

    public static string BuildCommand(string executablePath) => $"\"{Path.GetFullPath(executablePath)}\" --background";

    public static void Apply(bool enabled, string executablePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
