namespace CodexLimitReminder;

public sealed record CodexRateLimitWindow(
    string LimitId,
    string? LimitName,
    string WindowName,
    double UsedPercent,
    int WindowDurationMinutes,
    long ResetsAtUnixSeconds,
    string? PlanType)
{
    public const int MinimumWeeklyWindowMinutes = 6 * 24 * 60;

    public DateTimeOffset ResetsAt => DateTimeOffset.FromUnixTimeSeconds(ResetsAtUnixSeconds);

    public double NormalizedUsedPercent => double.IsFinite(UsedPercent)
        ? Math.Clamp(UsedPercent, 0, 100)
        : 0;

    public double RemainingPercent => 100 - NormalizedUsedPercent;

    public bool IsWeekly => WindowDurationMinutes >= MinimumWeeklyWindowMinutes;

    public string StateKey => $"{LimitId}:{WindowName}:{WindowDurationMinutes}";

    public string DisplayName => string.IsNullOrWhiteSpace(LimitName)
        ? LimitId.Equals("codex", StringComparison.OrdinalIgnoreCase) ? "General Codex" : LimitId
        : LimitName!;

    public string WindowLabel => WindowDurationMinutes switch
    {
        300 => "5-hour",
        10_080 => "weekly",
        _ when WindowDurationMinutes % (24 * 60) == 0 => $"{WindowDurationMinutes / (24 * 60)}-day",
        _ when WindowDurationMinutes % 60 == 0 => $"{WindowDurationMinutes / 60}-hour",
        _ => $"{WindowDurationMinutes}-minute"
    };
}
