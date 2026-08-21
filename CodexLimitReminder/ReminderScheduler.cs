namespace CodexLimitReminder;

public static class ReminderScheduler
{
    public static ReminderOccurrence? FindDue(AppSettings settings, DateTime nowLocal)
    {
        DateTime nextReset = GetNextReset(settings, nowLocal);

        foreach (int daysBefore in new[] { 2, 1 })
        {
            DateTime due = nextReset.Date.AddDays(-daysBefore).Add(settings.ReminderTime);
            var occurrence = CreateOccurrence(due, nextReset, daysBefore);

            if (due.Date == nowLocal.Date && due <= nowLocal && occurrence.Key != settings.LastReminderKey)
            {
                return occurrence;
            }
        }

        return null;
    }

    public static ReminderOccurrence FindNext(AppSettings settings, DateTime nowLocal)
    {
        DateTime reset = GetNextReset(settings, nowLocal);

        for (int week = 0; week < 2; week++)
        {
            DateTime weeklyReset = reset.AddDays(week * 7);

            foreach (int daysBefore in new[] { 2, 1 })
            {
                DateTime due = weeklyReset.Date.AddDays(-daysBefore).Add(settings.ReminderTime);
                if (due > nowLocal)
                {
                    return CreateOccurrence(due, weeklyReset, daysBefore);
                }
            }
        }

        throw new InvalidOperationException("Unable to calculate the next weekly reminder.");
    }

    public static DateTime GetNextReset(AppSettings settings, DateTime nowLocal)
    {
        int daysUntilReset = ((int)settings.ResetDay - (int)nowLocal.DayOfWeek + 7) % 7;
        DateTime reset = nowLocal.Date.AddDays(daysUntilReset).Add(settings.ResetTime);
        return reset > nowLocal ? reset : reset.AddDays(7);
    }

    private static ReminderOccurrence CreateOccurrence(DateTime due, DateTime reset, int daysBefore) =>
        new(due, reset, daysBefore == 2 ? 6 : 7, daysBefore);
}
