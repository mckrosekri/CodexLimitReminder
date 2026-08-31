namespace CodexLimitReminder;

internal sealed record EstimatedUsageGroup(
    string LimitStateKey,
    double EstimatedPercent,
    long ObservedAtUnixSeconds,
    long EstimatedReleaseAtUnixSeconds,
    bool IsBaseline)
{
    internal DateTimeOffset ObservedAt => DateTimeOffset.FromUnixTimeSeconds(ObservedAtUnixSeconds);

    internal DateTimeOffset EstimatedReleaseAt => DateTimeOffset.FromUnixTimeSeconds(EstimatedReleaseAtUnixSeconds);
}
