namespace CodexLimitReminder;

internal static class EstimatedUsageTracker
{
    private const double MinimumMeaningfulChange = 0.05;
    private const int MaximumGroupsPerLimit = 64;

    internal static IReadOnlyList<EstimatedUsageGroup> Reconcile(
        IReadOnlyList<EstimatedUsageGroup> existing,
        IReadOnlyList<CodexRateLimitWindow> previous,
        IReadOnlyList<CodexRateLimitWindow> current,
        DateTimeOffset observedAt)
    {
        Dictionary<string, CodexRateLimitWindow> previousByKey = previous
            .ToDictionary(limit => limit.StateKey, StringComparer.Ordinal);
        long now = observedAt.ToUnixTimeSeconds();
        var result = new List<EstimatedUsageGroup>();

        foreach (CodexRateLimitWindow limit in current)
        {
            List<EstimatedUsageGroup> groups = existing
                .Where(group => group.LimitStateKey.Equals(limit.StateKey, StringComparison.Ordinal) &&
                                group.EstimatedReleaseAtUnixSeconds > now &&
                                group.EstimatedPercent >= MinimumMeaningfulChange)
                .Select(group => CapAtServerReset(group, limit))
                .OrderBy(group => group.EstimatedReleaseAtUnixSeconds)
                .ThenBy(group => group.ObservedAtUnixSeconds)
                .ToList();

            double currentUsed = limit.NormalizedUsedPercent;
            previousByKey.TryGetValue(limit.StateKey, out CodexRateLimitWindow? prior);
            if (groups.Count == 0 && currentUsed >= MinimumMeaningfulChange)
            {
                groups.Add(CreateBaseline(limit, currentUsed, now));
            }
            else if (prior is not null)
            {
                double change = currentUsed - prior.NormalizedUsedPercent;
                if (change >= MinimumMeaningfulChange)
                {
                    groups.Add(CreateObserved(limit, change, observedAt));
                }
                else if (change <= -MinimumMeaningfulChange)
                {
                    ReduceOldest(groups, -change);
                }
            }

            double tracked = groups.Sum(group => group.EstimatedPercent);
            if (currentUsed - tracked >= MinimumMeaningfulChange)
            {
                groups.Add(CreateBaseline(limit, currentUsed - tracked, now));
            }
            else if (tracked - currentUsed >= MinimumMeaningfulChange)
            {
                ReduceOldest(groups, tracked - currentUsed);
            }

            result.AddRange(Compact(groups));
        }

        return result
            .OrderBy(group => group.LimitStateKey, StringComparer.Ordinal)
            .ThenBy(group => group.EstimatedReleaseAtUnixSeconds)
            .ThenBy(group => group.ObservedAtUnixSeconds)
            .ToArray();
    }

    private static EstimatedUsageGroup CreateBaseline(CodexRateLimitWindow limit, double amount, long observedAt) =>
        new(limit.StateKey, amount, observedAt, limit.ResetsAtUnixSeconds, IsBaseline: true);

    private static EstimatedUsageGroup CreateObserved(
        CodexRateLimitWindow limit,
        double amount,
        DateTimeOffset observedAt)
    {
        long estimatedByDuration = observedAt.AddMinutes(limit.WindowDurationMinutes).ToUnixTimeSeconds();
        long estimatedRelease = limit.ResetsAtUnixSeconds > observedAt.ToUnixTimeSeconds()
            ? Math.Min(estimatedByDuration, limit.ResetsAtUnixSeconds)
            : estimatedByDuration;
        return new EstimatedUsageGroup(
            limit.StateKey,
            amount,
            observedAt.ToUnixTimeSeconds(),
            estimatedRelease,
            IsBaseline: false);
    }

    private static EstimatedUsageGroup CapAtServerReset(EstimatedUsageGroup group, CodexRateLimitWindow limit) =>
        limit.ResetsAtUnixSeconds > 0 && limit.ResetsAtUnixSeconds < group.EstimatedReleaseAtUnixSeconds
            ? group with { EstimatedReleaseAtUnixSeconds = limit.ResetsAtUnixSeconds }
            : group;

    private static void ReduceOldest(List<EstimatedUsageGroup> groups, double amount)
    {
        for (int index = 0; index < groups.Count && amount >= MinimumMeaningfulChange;)
        {
            EstimatedUsageGroup group = groups[index];
            if (group.EstimatedPercent <= amount + MinimumMeaningfulChange)
            {
                amount -= group.EstimatedPercent;
                groups.RemoveAt(index);
                continue;
            }

            groups[index] = group with { EstimatedPercent = group.EstimatedPercent - amount };
            amount = 0;
        }
    }

    private static IReadOnlyList<EstimatedUsageGroup> Compact(List<EstimatedUsageGroup> groups)
    {
        groups.RemoveAll(group => group.EstimatedPercent < MinimumMeaningfulChange);
        if (groups.Count <= MaximumGroupsPerLimit)
        {
            return groups;
        }

        int mergeCount = groups.Count - MaximumGroupsPerLimit + 1;
        EstimatedUsageGroup[] oldest = groups.Take(mergeCount).ToArray();
        var merged = new EstimatedUsageGroup(
            oldest[0].LimitStateKey,
            oldest.Sum(group => group.EstimatedPercent),
            oldest.Min(group => group.ObservedAtUnixSeconds),
            oldest.Min(group => group.EstimatedReleaseAtUnixSeconds),
            IsBaseline: true);
        return [merged, .. groups.Skip(mergeCount)];
    }
}
