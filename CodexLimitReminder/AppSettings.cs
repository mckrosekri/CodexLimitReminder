namespace CodexLimitReminder;

public sealed record AppSettings(
    TimeSpan ReminderTime,
    bool IsConfigured,
    string LastDailySummaryDate)
{
    public static AppSettings Default => new(
        TimeSpan.FromHours(9),
        false,
        string.Empty);
}
