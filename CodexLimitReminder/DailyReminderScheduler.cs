namespace CodexLimitReminder;

public static class DailyReminderScheduler
{
    public static DailyReminderOccurrence? FindDue(AppSettings settings, DateTime nowLocal)
    {
        if (!settings.IsConfigured)
        {
            return null;
        }

        DateTime due = nowLocal.Date.Add(settings.ReminderTime);
        string dateKey = DateKey(nowLocal.Date);
        return due <= nowLocal && settings.LastDailySummaryDate != dateKey
            ? new DailyReminderOccurrence(due, dateKey)
            : null;
    }

    public static DateTime FindNext(AppSettings settings, DateTime nowLocal)
    {
        DateTime today = nowLocal.Date.Add(settings.ReminderTime);
        return today > nowLocal ? today : today.AddDays(1);
    }

    public static string DateKey(DateTime localDate) => localDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record DailyReminderOccurrence(DateTime DueLocal, string DateKey);
