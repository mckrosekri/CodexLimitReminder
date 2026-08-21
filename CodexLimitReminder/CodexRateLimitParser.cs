using System.Text.Json;

namespace CodexLimitReminder;

public static class CodexRateLimitParser
{
    private const int MinimumWeeklyWindowMinutes = 6 * 24 * 60;

    public static WeeklyRateLimit ParseResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("error", out JsonElement error))
        {
            string message = error.TryGetProperty("message", out JsonElement messageValue)
                ? messageValue.GetString() ?? "Codex returned an unknown error."
                : "Codex returned an unknown error.";
            throw new InvalidOperationException(message);
        }

        if (!root.TryGetProperty("result", out JsonElement result))
        {
            throw new InvalidOperationException("Codex did not return rate-limit data.");
        }

        if (result.TryGetProperty("rateLimitsByLimitId", out JsonElement byLimitId) &&
            byLimitId.ValueKind == JsonValueKind.Object &&
            byLimitId.TryGetProperty("codex", out JsonElement codexBucket) &&
            TryReadWeeklyWindow(codexBucket, out WeeklyRateLimit? mappedLimit))
        {
            return mappedLimit!;
        }

        if (result.TryGetProperty("rateLimits", out JsonElement legacyBucket) &&
            legacyBucket.ValueKind == JsonValueKind.Object &&
            TryReadWeeklyWindow(legacyBucket, out WeeklyRateLimit? legacyLimit))
        {
            return legacyLimit!;
        }

        throw new InvalidOperationException("Codex did not expose a weekly limit for the main codex bucket.");
    }

    private static bool TryReadWeeklyWindow(JsonElement bucket, out WeeklyRateLimit? limit)
    {
        limit = null;
        string limitId = ReadString(bucket, "limitId") ?? "codex";
        string? limitName = ReadString(bucket, "limitName");
        string? planType = ReadString(bucket, "planType");
        var candidates = new List<WeeklyRateLimit>(2);

        AddWindow(bucket, "primary", limitId, limitName, planType, candidates);
        AddWindow(bucket, "secondary", limitId, limitName, planType, candidates);

        limit = candidates
            .Where(candidate => candidate.WindowDurationMinutes >= MinimumWeeklyWindowMinutes)
            .OrderBy(candidate => Math.Abs(candidate.WindowDurationMinutes - 7 * 24 * 60))
            .ThenByDescending(candidate => candidate.WindowDurationMinutes)
            .FirstOrDefault();
        return limit is not null;
    }

    private static void AddWindow(
        JsonElement bucket,
        string propertyName,
        string limitId,
        string? limitName,
        string? planType,
        List<WeeklyRateLimit> candidates)
    {
        if (!bucket.TryGetProperty(propertyName, out JsonElement window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("windowDurationMins", out JsonElement durationElement) || !durationElement.TryGetInt32(out int duration) ||
            !window.TryGetProperty("resetsAt", out JsonElement resetsElement) || !resetsElement.TryGetInt64(out long resetsAt))
        {
            return;
        }

        double usedPercent = window.TryGetProperty("usedPercent", out JsonElement usedElement) && usedElement.TryGetDouble(out double used)
            ? used
            : 0;
        candidates.Add(new WeeklyRateLimit(limitId, limitName, usedPercent, duration, resetsAt, planType));
    }

    private static string? ReadString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
