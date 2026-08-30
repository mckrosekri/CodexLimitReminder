using System.Text.Json;

namespace CodexLimitReminder;

public static class CodexRateLimitParser
{
    public static IReadOnlyList<CodexRateLimitWindow> ParseAllResponse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement result = ReadResult(document.RootElement);
        var limits = new List<CodexRateLimitWindow>();

        if (result.TryGetProperty("rateLimitsByLimitId", out JsonElement byLimitId) && byLimitId.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in byLimitId.EnumerateObject())
            {
                AddBucket(property.Value, property.Name, limits);
            }
        }
        else if (result.TryGetProperty("rateLimits", out JsonElement legacyBucket) && legacyBucket.ValueKind == JsonValueKind.Object)
        {
            AddBucket(legacyBucket, "codex", limits);
        }

        if (limits.Count == 0)
        {
            throw new InvalidOperationException("Codex did not expose any rate-limit windows.");
        }

        return limits
            .OrderBy(limit => limit.LimitId.Equals("codex", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(limit => limit.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(limit => limit.WindowDurationMinutes)
            .ToArray();
    }

    private static JsonElement ReadResult(JsonElement root)
    {
        if (root.TryGetProperty("error", out JsonElement error))
        {
            string message = error.TryGetProperty("message", out JsonElement messageValue)
                ? messageValue.GetString() ?? "Codex returned an unknown error."
                : "Codex returned an unknown error.";
            throw new InvalidOperationException(message);
        }

        return root.TryGetProperty("result", out JsonElement result)
            ? result
            : throw new InvalidOperationException("Codex did not return rate-limit data.");
    }

    private static void AddBucket(JsonElement bucket, string fallbackLimitId, List<CodexRateLimitWindow> limits)
    {
        string limitId = ReadString(bucket, "limitId") ?? fallbackLimitId;
        string? limitName = ReadString(bucket, "limitName");
        string? planType = ReadString(bucket, "planType");
        AddWindow(bucket, "primary", limitId, limitName, planType, limits);
        AddWindow(bucket, "secondary", limitId, limitName, planType, limits);
    }

    private static void AddWindow(
        JsonElement bucket,
        string propertyName,
        string limitId,
        string? limitName,
        string? planType,
        List<CodexRateLimitWindow> limits)
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
        limits.Add(new CodexRateLimitWindow(limitId, limitName, propertyName, usedPercent, duration, resetsAt, planType));
    }

    private static string? ReadString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
