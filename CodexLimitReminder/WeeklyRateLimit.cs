namespace CodexLimitReminder;

public sealed record WeeklyRateLimit(
    string LimitId,
    string? LimitName,
    double UsedPercent,
    int WindowDurationMinutes,
    long ResetsAtUnixSeconds,
    string? PlanType)
{
    public DateTimeOffset ResetsAt => DateTimeOffset.FromUnixTimeSeconds(ResetsAtUnixSeconds);
}
