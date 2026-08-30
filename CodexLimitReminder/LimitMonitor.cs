namespace CodexLimitReminder;

public static class LimitMonitor
{
    private static readonly int[] Thresholds = [50, 75, 90, 95];
    private const double MajorRecoveryPoints = 10;
    private const long MeaningfulResetAdvanceSeconds = 6 * 60 * 60;

    public static LimitMonitorResult Evaluate(
        IReadOnlyList<MonitoredLimitState> previous,
        IReadOnlyList<CodexRateLimitWindow> current)
    {
        Dictionary<string, MonitoredLimitState> previousByKey = previous.ToDictionary(state => state.Limit.StateKey, StringComparer.Ordinal);
        var next = new List<MonitoredLimitState>(current.Count);
        var events = new List<LimitMonitorEvent>();

        foreach (CodexRateLimitWindow limit in current)
        {
            previousByKey.TryGetValue(limit.StateKey, out MonitoredLimitState? prior);
            int lastThreshold = prior?.LastNotifiedThreshold ?? 0;

            if (limit.IsWeekly && prior is not null)
            {
                double recovered = prior.Limit.NormalizedUsedPercent - limit.NormalizedUsedPercent;
                bool resetAdvanced = limit.ResetsAtUnixSeconds > prior.Limit.ResetsAtUnixSeconds + MeaningfulResetAdvanceSeconds;
                bool majorRecovery = recovered >= MajorRecoveryPoints ||
                                     (resetAdvanced && prior.Limit.NormalizedUsedPercent >= 5 && limit.NormalizedUsedPercent <= 1);

                if (majorRecovery)
                {
                    events.Add(LimitMonitorEvent.CreateRecovery(limit, recovered));
                    lastThreshold = HighestCrossedThreshold(limit.NormalizedUsedPercent);
                }
                else if (resetAdvanced && recovered > 0)
                {
                    lastThreshold = HighestCrossedThreshold(limit.NormalizedUsedPercent);
                }
            }

            if (limit.IsWeekly && !events.Any(item => item.Kind == LimitMonitorEventKind.Recovery && item.Limit.StateKey == limit.StateKey))
            {
                int crossed = HighestCrossedThreshold(limit.NormalizedUsedPercent);
                if (crossed > lastThreshold)
                {
                    events.Add(LimitMonitorEvent.CreateThreshold(limit, crossed));
                    lastThreshold = crossed;
                }
            }

            next.Add(new MonitoredLimitState(limit, lastThreshold));
        }

        return new LimitMonitorResult(next, events);
    }

    private static int HighestCrossedThreshold(double usedPercent) =>
        Thresholds.LastOrDefault(threshold => usedPercent >= threshold);
}

public sealed record MonitoredLimitState(CodexRateLimitWindow Limit, int LastNotifiedThreshold);

public enum LimitMonitorEventKind
{
    Threshold,
    Recovery
}

public sealed record LimitMonitorEvent(
    LimitMonitorEventKind Kind,
    CodexRateLimitWindow Limit,
    int Threshold,
    double RecoveredPercent)
{
    public static LimitMonitorEvent CreateThreshold(CodexRateLimitWindow limit, int threshold) =>
        new(LimitMonitorEventKind.Threshold, limit, threshold, 0);

    public static LimitMonitorEvent CreateRecovery(CodexRateLimitWindow limit, double recoveredPercent) =>
        new(LimitMonitorEventKind.Recovery, limit, 0, Math.Max(0, recoveredPercent));
}

public sealed record LimitMonitorResult(
    IReadOnlyList<MonitoredLimitState> States,
    IReadOnlyList<LimitMonitorEvent> Events);
