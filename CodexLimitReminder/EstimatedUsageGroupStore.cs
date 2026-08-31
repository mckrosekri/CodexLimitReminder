using System.Globalization;
using Microsoft.Win32;

namespace CodexLimitReminder;

internal static class EstimatedUsageGroupStore
{
    private const string RootKeyPath = @"Software\CodexLimitReminder\EstimatedUsageGroups";

    internal static IReadOnlyList<EstimatedUsageGroup> Load()
    {
        var groups = new List<EstimatedUsageGroup>();
        using RegistryKey? root = Registry.CurrentUser.OpenSubKey(RootKeyPath);
        if (root is null)
        {
            return groups;
        }

        foreach (string name in root.GetSubKeyNames())
        {
            using RegistryKey? key = root.OpenSubKey(name);
            string? stateKey = key?.GetValue("LimitStateKey") as string;
            double amount = key is null ? 0 : ReadDouble(key, "EstimatedPercent");
            long observed = key?.GetValue("ObservedAtUnixSeconds") is long observedValue ? observedValue : 0;
            long release = key?.GetValue("EstimatedReleaseAtUnixSeconds") is long releaseValue ? releaseValue : 0;
            if (string.IsNullOrWhiteSpace(stateKey) || amount <= 0 || observed <= 0 || release <= 0)
            {
                continue;
            }

            groups.Add(new EstimatedUsageGroup(
                stateKey,
                amount,
                observed,
                release,
                key!.GetValue("IsBaseline") is int baseline && baseline != 0));
        }

        return groups
            .OrderBy(group => group.LimitStateKey, StringComparer.Ordinal)
            .ThenBy(group => group.EstimatedReleaseAtUnixSeconds)
            .ToArray();
    }

    internal static void Save(IReadOnlyList<EstimatedUsageGroup> groups)
    {
        using RegistryKey root = Registry.CurrentUser.CreateSubKey(RootKeyPath);
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < groups.Count; index++)
        {
            string name = $"Group{index:D4}";
            expected.Add(name);
            EstimatedUsageGroup group = groups[index];
            using RegistryKey key = root.CreateSubKey(name);
            key.SetValue("LimitStateKey", group.LimitStateKey, RegistryValueKind.String);
            key.SetValue("EstimatedPercent", group.EstimatedPercent.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String);
            key.SetValue("ObservedAtUnixSeconds", group.ObservedAtUnixSeconds, RegistryValueKind.QWord);
            key.SetValue("EstimatedReleaseAtUnixSeconds", group.EstimatedReleaseAtUnixSeconds, RegistryValueKind.QWord);
            key.SetValue("IsBaseline", group.IsBaseline ? 1 : 0, RegistryValueKind.DWord);
        }

        foreach (string stale in root.GetSubKeyNames().Where(name => !expected.Contains(name)))
        {
            root.DeleteSubKeyTree(stale, throwOnMissingSubKey: false);
        }
    }

    private static double ReadDouble(RegistryKey key, string name) =>
        key.GetValue(name) is string text &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : 0;
}
