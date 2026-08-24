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
        object? usedPercentValue = key.GetValue("LastKnownUsedPercent");
        double? usedPercent = usedPercentValue is string text &&
                              double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
        return new AppSettings(
            TimeSpan.FromMinutes(ReadInt(key, "ReminderMinutes", (int)defaults.ReminderTime.TotalMinutes, 0, 1439)),
            setupVersion == CurrentSetupVersion && ReadInt(key, "Configured", 0, 0, 1) == 1,
            key.GetValue("LastReminderKey") as string ?? string.Empty,
            key.GetValue("LastUsageWarningKey") as string ?? string.Empty,
            ReadLong(key, "LastKnownResetUnixSeconds", 0),
            usedPercent,
            key.GetValue("LastKnownPlanType") as string);
    }

    public static void Save(AppSettings settings)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath);
        key.SetValue("SetupVersion", CurrentSetupVersion, RegistryValueKind.DWord);
        key.SetValue("ReminderMinutes", (int)settings.ReminderTime.TotalMinutes, RegistryValueKind.DWord);
        key.SetValue("Configured", settings.IsConfigured ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue("LastReminderKey", settings.LastReminderKey, RegistryValueKind.String);
        key.SetValue("LastUsageWarningKey", settings.LastUsageWarningKey, RegistryValueKind.String);
        key.SetValue("LastKnownResetUnixSeconds", settings.LastKnownResetUnixSeconds, RegistryValueKind.QWord);
        if (settings.LastKnownUsedPercent is double usedPercent)
        {
            key.SetValue("LastKnownUsedPercent", usedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue("LastKnownUsedPercent", throwOnMissingValue: false);
        }

        if (string.IsNullOrWhiteSpace(settings.LastKnownPlanType))
        {
            key.DeleteValue("LastKnownPlanType", throwOnMissingValue: false);
        }
        else
        {
            key.SetValue("LastKnownPlanType", settings.LastKnownPlanType, RegistryValueKind.String);
        }

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

    private static long ReadLong(RegistryKey key, string name, long fallback)
    {
        object? value = key.GetValue(name);
        return value is long integer ? integer : fallback;
    }
}
