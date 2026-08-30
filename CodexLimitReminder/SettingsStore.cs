using Microsoft.Win32;

namespace CodexLimitReminder;

internal static class SettingsStore
{
    private const string SettingsKeyPath = @"Software\CodexLimitReminder";
    private const int CurrentSetupVersion = 2;

    public static AppSettings Load()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
        if (key is null)
        {
            return AppSettings.Default;
        }

        AppSettings defaults = AppSettings.Default;
        int setupVersion = ReadInt(key, "SetupVersion", 0, 0, CurrentSetupVersion);
        return new AppSettings(
            TimeSpan.FromMinutes(ReadInt(key, "ReminderMinutes", (int)defaults.ReminderTime.TotalMinutes, 0, 1439)),
            setupVersion == CurrentSetupVersion && ReadInt(key, "Configured", 0, 0, 1) == 1,
            key.GetValue("LastDailySummaryDate") as string ?? string.Empty);
    }

    public static void Save(AppSettings settings)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath);
        key.SetValue("SetupVersion", CurrentSetupVersion, RegistryValueKind.DWord);
        key.SetValue("ReminderMinutes", (int)settings.ReminderTime.TotalMinutes, RegistryValueKind.DWord);
        key.SetValue("Configured", settings.IsConfigured ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("LastDailySummaryDate", settings.LastDailySummaryDate, RegistryValueKind.String);

        key.DeleteValue("LastReminderKey", throwOnMissingValue: false);
        key.DeleteValue("LastUsageWarningKey", throwOnMissingValue: false);
        key.DeleteValue("LastKnownResetUnixSeconds", throwOnMissingValue: false);
        key.DeleteValue("LastKnownUsedPercent", throwOnMissingValue: false);
        key.DeleteValue("LastKnownPlanType", throwOnMissingValue: false);
        key.DeleteValue("ResetDay", throwOnMissingValue: false);
        key.DeleteValue("ResetMinutes", throwOnMissingValue: false);
        key.DeleteValue("StartWithWindows", throwOnMissingValue: false);
    }

    private static int ReadInt(RegistryKey key, string name, int fallback, int min, int max)
    {
        object? value = key.GetValue(name);
        int parsed = value is int integer ? integer : fallback;
        return Math.Clamp(parsed, min, max);
    }

}
