namespace CodexLimitReminder;

public static class UsageWarningScheduler
{
    private static readonly int[] Thresholds = [80, 95];

    public static UsageWarningOccurrence? FindDue(AppSettings settings, WeeklyRateLimit weeklyLimit)
    {
        int crossedThreshold = Thresholds.LastOrDefault(threshold => weeklyLimit.NormalizedUsedPercent >= threshold);
        if (crossedThreshold == 0)
        {
            return null;
        }

        int lastThreshold = ReadLastThreshold(settings.LastUsageWarningKey, weeklyLimit.ResetsAtUnixSeconds);
        return crossedThreshold > lastThreshold
            ? new UsageWarningOccurrence(weeklyLimit.ResetsAtUnixSeconds, crossedThreshold)
            : null;
    }

    private static int ReadLastThreshold(string key, long resetUnixSeconds)
    {
        string prefix = resetUnixSeconds + ":";
        return key.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(key.AsSpan(prefix.Length), out int threshold)
            ? threshold
            : 0;
    }
}

public sealed record UsageWarningOccurrence(long ResetUnixSeconds, int UsedThreshold)
{
    public string Key => $"{ResetUnixSeconds}:{UsedThreshold}";
}
