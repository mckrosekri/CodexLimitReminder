namespace CodexLimitReminder;

internal static class ResetCountdownFormatter
{
    internal static string Format(DateTimeOffset resetsAt, DateTimeOffset now)
    {
        TimeSpan remaining = resetsAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            return "due now";
        }

        int days = (int)remaining.TotalDays;
        if (days > 0)
        {
            return $"{days}d {remaining.Hours}h {remaining.Minutes}m";
        }

        if (remaining.Hours > 0)
        {
            return $"{remaining.Hours}h {remaining.Minutes}m {remaining.Seconds}s";
        }

        return remaining.Minutes > 0
            ? $"{remaining.Minutes}m {remaining.Seconds}s"
            : $"{Math.Max(1, remaining.Seconds)}s";
    }
}
