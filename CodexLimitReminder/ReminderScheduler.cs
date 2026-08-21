namespace CodexLimitReminder;

public static class ReminderScheduler
{
    public static ReminderOccurrence? FindDue(AppSettings settings, WeeklyRateLimit weeklyLimit, DateTime nowLocal)
    {
        DateTime resetLocal = weeklyLimit.ResetsAt.ToLocalTime().DateTime;

        foreach (int daysBefore in new[] { 2, 1 })
        {
            DateTime due = resetLocal.Date.AddDays(-daysBefore).Add(settings.ReminderTime);
            var occurrence = CreateOccurrence(due, resetLocal, weeklyLimit.ResetsAtUnixSeconds, daysBefore);

            if (due.Date == nowLocal.Date && due <= nowLocal && occurrence.Key != settings.LastReminderKey)
            {
                return occurrence;
            }
        }

        return null;
    }

    public static ReminderOccurrence? FindNext(AppSettings settings, WeeklyRateLimit weeklyLimit, DateTime nowLocal)
    {
        DateTime resetLocal = weeklyLimit.ResetsAt.ToLocalTime().DateTime;

        foreach (int daysBefore in new[] { 2, 1 })
        {
            DateTime due = resetLocal.Date.AddDays(-daysBefore).Add(settings.ReminderTime);
            if (due > nowLocal)
            {
                return CreateOccurrence(due, resetLocal, weeklyLimit.ResetsAtUnixSeconds, daysBefore);
            }
        }

        return null;
    }

    private static ReminderOccurrence CreateOccurrence(DateTime due, DateTime reset, long resetUnixSeconds, int daysBefore) =>
        new(due, reset, resetUnixSeconds, daysBefore == 2 ? 6 : 7, daysBefore);
}
