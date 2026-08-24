namespace CodexLimitReminder;

public sealed record AppSettings(
    TimeSpan ReminderTime,
    bool IsConfigured,
    string LastReminderKey,
    string LastUsageWarningKey,
    long LastKnownResetUnixSeconds,
    double? LastKnownUsedPercent,
    string? LastKnownPlanType)
{
    public static AppSettings Default => new(
        TimeSpan.FromHours(9),
        false,
        string.Empty,
        string.Empty,
        0,
        null,
        null);
}

public sealed record ReminderOccurrence(
    DateTime DueLocal,
    DateTime ResetLocal,
    long ResetUnixSeconds,
    int CycleDay,
    int DaysBeforeReset)
{
    public string Key => $"{ResetUnixSeconds}:{DaysBeforeReset}";
}
