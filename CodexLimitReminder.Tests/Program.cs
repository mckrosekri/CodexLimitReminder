using CodexLimitReminder;

var tests = new (string Name, Action Run)[]
{
    ("parser reads the main Codex weekly window", ParserReadsMainCodexWeeklyWindow),
    ("parser uses a weekly secondary window when primary is short", ParserUsesWeeklySecondaryWindow),
    ("parser rejects responses without a weekly Codex limit", ParserRejectsMissingWeeklyWindow),
    ("usage calculates the remaining weekly percentage", UsageCalculatesRemainingPercentage),
    ("usage percentages are safely clamped", UsagePercentagesAreClamped),
    ("day 6 is due two mornings before the exact Codex reset", Day6IsDue),
    ("day 7 is due one morning before the exact Codex reset", Day7IsDue),
    ("a reminder is not repeated after its key is saved", ReminderDoesNotRepeat),
    ("a missed previous-day reminder is not backfilled", PreviousDayIsNotBackfilled),
    ("next reminders are returned in day 6 then day 7 order", NextReminderOrder),
    ("no future cycle is invented after both reminders", NoFutureCycleIsInvented),
    ("startup command is quoted and windowless", StartupCommandIsQuoted),
    ("startup-folder wrapper is hidden and quoted", StartupFolderWrapperIsHiddenAndQuoted)
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {name}\n      {exception.Message}");
    }
}

Console.WriteLine($"\n{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static AppSettings Settings(string reminderTime = "09:00", string lastKey = "") => new(
    TimeSpan.Parse(reminderTime),
    true,
    lastKey,
    0,
    null,
    null);

static WeeklyRateLimit Limit() => new(
    "codex",
    "Codex",
    7,
    10_080,
    new DateTimeOffset(2026, 8, 21, 17, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
    "pro");

static void ParserReadsMainCodexWeeklyWindow()
{
    const string json = """
        {"id":2,"result":{"rateLimits":{"limitId":"codex","planType":"pro","primary":{"usedPercent":12,"windowDurationMins":10080,"resetsAt":1787333400}},"rateLimitsByLimitId":{"codex":{"limitId":"codex","limitName":"Codex","planType":"pro","primary":{"usedPercent":7,"windowDurationMins":10080,"resetsAt":1787333400}},"codex_bengalfox":{"limitId":"codex_bengalfox","primary":{"usedPercent":99,"windowDurationMins":10080,"resetsAt":1787333500}}}}}
        """;

    WeeklyRateLimit result = CodexRateLimitParser.ParseResponse(json);
    Equal("codex", result.LimitId);
    Equal(7d, result.UsedPercent);
    Equal(10_080, result.WindowDurationMinutes);
    Equal(1787333400L, result.ResetsAtUnixSeconds);
    Equal("pro", result.PlanType);
}

static void ParserUsesWeeklySecondaryWindow()
{
    const string json = """
        {"id":2,"result":{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"usedPercent":10,"windowDurationMins":300,"resetsAt":1787000000},"secondary":{"usedPercent":25,"windowDurationMins":10080,"resetsAt":1787333400}}}}}
        """;

    WeeklyRateLimit result = CodexRateLimitParser.ParseResponse(json);
    Equal(25d, result.UsedPercent);
    Equal(10_080, result.WindowDurationMinutes);
}

static void ParserRejectsMissingWeeklyWindow()
{
    const string json = """
        {"id":2,"result":{"rateLimitsByLimitId":{"codex":{"limitId":"codex","primary":{"usedPercent":10,"windowDurationMins":300,"resetsAt":1787000000}}}}}
        """;

    Throws<InvalidOperationException>(() => CodexRateLimitParser.ParseResponse(json));
}

static void UsageCalculatesRemainingPercentage()
{
    WeeklyRateLimit limit = Limit();
    Equal(7d, limit.NormalizedUsedPercent);
    Equal(93d, limit.RemainingPercent);
}

static void UsagePercentagesAreClamped()
{
    WeeklyRateLimit overLimit = Limit() with { UsedPercent = 125 };
    WeeklyRateLimit belowZero = Limit() with { UsedPercent = -5 };
    WeeklyRateLimit invalid = Limit() with { UsedPercent = double.NaN };
    Equal(100d, overLimit.NormalizedUsedPercent);
    Equal(0d, overLimit.RemainingPercent);
    Equal(0d, belowZero.NormalizedUsedPercent);
    Equal(100d, belowZero.RemainingPercent);
    Equal(0d, invalid.NormalizedUsedPercent);
    Equal(100d, invalid.RemainingPercent);
}

static void Day6IsDue()
{
    DateTime now = new(2026, 8, 19, 9, 0, 0);
    ReminderOccurrence? due = ReminderScheduler.FindDue(Settings(), Limit(), now);
    NotNull(due);
    Equal(6, due!.CycleDay);
    Equal(2, due.DaysBeforeReset);
}

static void Day7IsDue()
{
    DateTime now = new(2026, 8, 20, 10, 0, 0);
    ReminderOccurrence? due = ReminderScheduler.FindDue(Settings(), Limit(), now);
    NotNull(due);
    Equal(7, due!.CycleDay);
    Equal(1, due.DaysBeforeReset);
}

static void ReminderDoesNotRepeat()
{
    DateTime now = new(2026, 8, 19, 9, 5, 0);
    ReminderOccurrence due = ReminderScheduler.FindDue(Settings(), Limit(), now)!;
    Equal(null, ReminderScheduler.FindDue(Settings(lastKey: due.Key), Limit(), now));
}

static void PreviousDayIsNotBackfilled()
{
    DateTime now = new(2026, 8, 20, 8, 30, 0);
    Equal(null, ReminderScheduler.FindDue(Settings(), Limit(), now));
}

static void NextReminderOrder()
{
    AppSettings settings = Settings();
    WeeklyRateLimit limit = Limit();
    DateTime now = new(2026, 8, 18, 12, 0, 0);
    ReminderOccurrence first = ReminderScheduler.FindNext(settings, limit, now)!;
    ReminderOccurrence second = ReminderScheduler.FindNext(settings, limit, first.DueLocal.AddSeconds(1))!;
    Equal(6, first.CycleDay);
    Equal(new DateTime(2026, 8, 19, 9, 0, 0), first.DueLocal);
    Equal(7, second.CycleDay);
    Equal(new DateTime(2026, 8, 20, 9, 0, 0), second.DueLocal);
}

static void NoFutureCycleIsInvented()
{
    DateTime now = new(2026, 8, 20, 9, 1, 0);
    Equal(null, ReminderScheduler.FindNext(Settings(), Limit(), now));
}

static void StartupCommandIsQuoted()
{
    string command = StartupRegistration.BuildCommand(@"C:\Program Files\CodexLimitReminder\CodexLimitReminder.exe");
    Equal("\"C:\\Program Files\\CodexLimitReminder\\CodexLimitReminder.exe\" --background", command);
}

static void StartupFolderWrapperIsHiddenAndQuoted()
{
    string script = StartupRegistration.BuildStartupScript(@"C:\Program Files\CodexLimitReminder\CodexLimitReminder.exe");
    Contains("shell.Run \"\"\"C:\\Program Files\\CodexLimitReminder\\CodexLimitReminder.exe\"\" --background\", 0, False", script);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void NotNull(object? value)
{
    if (value is null)
    {
        throw new InvalidOperationException("Expected a value, got null.");
    }
}

static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected to find '{expected}'.");
    }
}

static void Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
