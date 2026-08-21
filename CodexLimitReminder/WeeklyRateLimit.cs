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

    public double NormalizedUsedPercent => double.IsFinite(UsedPercent)
        ? Math.Clamp(UsedPercent, 0, 100)
        : 0;

    public double RemainingPercent => 100 - NormalizedUsedPercent;
}
