using Microsoft.Win32;

namespace CodexLimitReminder;

internal static class SettingsStore
{
    private const string SettingsKeyPath = @"Software\CodexLimitReminder";

    public static AppSettings Load()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
        if (key is null)
        {
            return AppSettings.Default;
        }

        AppSettings defaults = AppSettings.Default;
        return new AppSettings(
            ReadDay(key, "ResetDay", defaults.ResetDay),
            TimeSpan.FromMinutes(ReadInt(key, "ResetMinutes", (int)defaults.ResetTime.TotalMinutes, 0, 1439)),
            TimeSpan.FromMinutes(ReadInt(key, "ReminderMinutes", (int)defaults.ReminderTime.TotalMinutes, 0, 1439)),
            ReadInt(key, "StartWithWindows", 1, 0, 1) == 1,
            ReadInt(key, "Configured", 0, 0, 1) == 1,
            key.GetValue("LastReminderKey") as string ?? string.Empty);
    }

    public static void Save(AppSettings settings)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath);
        key.SetValue("ResetDay", (int)settings.ResetDay, RegistryValueKind.DWord);
        key.SetValue("ResetMinutes", (int)settings.ResetTime.TotalMinutes, RegistryValueKind.DWord);
        key.SetValue("ReminderMinutes", (int)settings.ReminderTime.TotalMinutes, RegistryValueKind.DWord);
        key.SetValue("StartWithWindows", settings.StartWithWindows ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("Configured", settings.IsConfigured ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("LastReminderKey", settings.LastReminderKey, RegistryValueKind.String);
    }

    private static DayOfWeek ReadDay(RegistryKey key, string name, DayOfWeek fallback)
    {
        int value = ReadInt(key, name, (int)fallback, 0, 6);
        return (DayOfWeek)value;
    }

    private static int ReadInt(RegistryKey key, string name, int fallback, int min, int max)
    {
        object? value = key.GetValue(name);
        int parsed = value is int integer ? integer : fallback;
        return Math.Clamp(parsed, min, max);
    }
}
