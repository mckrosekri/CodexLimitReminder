namespace CodexLimitReminder;

public sealed record AppSettings(
    DayOfWeek ResetDay,
    TimeSpan ResetTime,
    TimeSpan ReminderTime,
    bool StartWithWindows,
    bool IsConfigured,
    string LastReminderKey)
{
    public static AppSettings Default => new(
        DayOfWeek.Monday,
        TimeSpan.Zero,
        TimeSpan.FromHours(9),
        true,
        false,
        string.Empty);
}

public sealed record ReminderOccurrence(
    DateTime DueLocal,
    DateTime ResetLocal,
    int CycleDay,
    int DaysBeforeReset)
{
    public string Key => $"{ResetLocal:yyyyMMddHHmm}:{DaysBeforeReset}";
}
