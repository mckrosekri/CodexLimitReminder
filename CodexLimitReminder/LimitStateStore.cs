using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace CodexLimitReminder;

internal static class LimitStateStore
{
    private const string RootKeyPath = @"Software\CodexLimitReminder\LimitWindows";

    public static IReadOnlyList<MonitoredLimitState> Load()
    {
        var states = new List<MonitoredLimitState>();
        using RegistryKey? root = Registry.CurrentUser.OpenSubKey(RootKeyPath);
        if (root is null)
        {
            return states;
        }

        foreach (string name in root.GetSubKeyNames())
        {
            using RegistryKey? key = root.OpenSubKey(name);
            if (key is null)
            {
                continue;
            }

            string? limitId = key.GetValue("LimitId") as string;
            string? windowName = key.GetValue("WindowName") as string;
            int duration = ReadInt(key, "DurationMinutes", 0);
            long reset = ReadLong(key, "ResetsAtUnixSeconds", 0);
            if (string.IsNullOrWhiteSpace(limitId) || string.IsNullOrWhiteSpace(windowName) || duration <= 0 || reset <= 0)
            {
                continue;
            }

            double used = ReadDouble(key, "UsedPercent", 0);
            var limit = new CodexRateLimitWindow(
                limitId,
                key.GetValue("LimitName") as string,
                windowName,
                used,
                duration,
                reset,
                key.GetValue("PlanType") as string);
            states.Add(new MonitoredLimitState(limit, ReadInt(key, "LastNotifiedThreshold", 0)));
        }

        return states.OrderBy(state => SortKey(state.Limit), StringComparer.Ordinal).ToArray();
    }

    public static void Save(IReadOnlyList<MonitoredLimitState> states)
    {
        using RegistryKey root = Registry.CurrentUser.CreateSubKey(RootKeyPath);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (MonitoredLimitState state in states)
        {
            string subKeyName = EncodeKey(state.Limit.StateKey);
            expected.Add(subKeyName);
            using RegistryKey key = root.CreateSubKey(subKeyName);
            key.SetValue("LimitId", state.Limit.LimitId, RegistryValueKind.String);
            SetOptionalString(key, "LimitName", state.Limit.LimitName);
            key.SetValue("WindowName", state.Limit.WindowName, RegistryValueKind.String);
            key.SetValue("UsedPercent", state.Limit.UsedPercent.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
            key.SetValue("DurationMinutes", state.Limit.WindowDurationMinutes, RegistryValueKind.DWord);
            key.SetValue("ResetsAtUnixSeconds", state.Limit.ResetsAtUnixSeconds, RegistryValueKind.QWord);
            SetOptionalString(key, "PlanType", state.Limit.PlanType);
            key.SetValue("LastNotifiedThreshold", state.LastNotifiedThreshold, RegistryValueKind.DWord);
        }

        foreach (string stale in root.GetSubKeyNames().Where(name => !expected.Contains(name)))
        {
            root.DeleteSubKeyTree(stale, throwOnMissingSubKey: false);
        }
    }

    private static string EncodeKey(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    private static string SortKey(CodexRateLimitWindow limit) =>
        $"{(limit.LimitId.Equals("codex", StringComparison.OrdinalIgnoreCase) ? 0 : 1)}:{limit.DisplayName}:{limit.WindowDurationMinutes:D8}";

    private static int ReadInt(RegistryKey key, string name, int fallback) =>
        key.GetValue(name) is int value ? value : fallback;

    private static long ReadLong(RegistryKey key, string name, long fallback) =>
        key.GetValue(name) is long value ? value : fallback;

    private static double ReadDouble(RegistryKey key, string name, double fallback) =>
        key.GetValue(name) is string text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    private static void SetOptionalString(RegistryKey key, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key.DeleteValue(name, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(name, value, RegistryValueKind.String);
        }
    }
}
