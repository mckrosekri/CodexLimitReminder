using CodexLimitReminder;

var tests = new (string Name, Action Run)[]
{
    ("parser reads every General and Spark clock", ParserReadsAllClocks),
    ("parser rejects responses without limits", ParserRejectsMissingLimits),
    ("usage calculates remaining percentage", UsageCalculatesRemainingPercentage),
    ("usage percentages are safely clamped", UsagePercentagesAreClamped),
    ("daily summary is due after configured time", DailySummaryIsDue),
    ("daily summary waits until configured time", DailySummaryWaitsUntilTime),
    ("daily summary is not repeated", DailySummaryDoesNotRepeat),
    ("next daily summary uses today then tomorrow", NextDailySummaryIsCorrect),
    ("weekly usage below 50 percent does not alert", UsageBelowFiftyDoesNotWarn),
    ("50 percent weekly usage triggers an alert", UsageWarningAtFiftyPercent),
    ("first observation at 96 percent selects 95", UsageWarningSelectsHighestCrossedThreshold),
    ("weekly warnings escalate from 50 to 75", UsageWarningEscalates),
    ("weekly warning is not repeated", UsageWarningDoesNotRepeat),
    ("five-hour clocks do not trigger weekly warnings", FiveHourClockDoesNotWarn),
    ("major rolling recovery is detected", MajorRecoveryIsDetected),
    ("minor usage movement is not called a recovery", MinorDropIsNotRecovery),
    ("reset advance with near-full recovery is detected", ResetAdvanceRecoveryIsDetected),
    ("warning thresholds restart after recovery", ThresholdRestartsAfterRecovery),
    ("multiple limit states remain independent", MultipleLimitStatesAreIndependent),
    ("collapsed widget remains compact", CollapsedWidgetRemainsCompact),
    ("expanded widget grows for live limits", ExpandedWidgetGrowsForLimits),
    ("widget placement stays inside the work area", WidgetPlacementStaysInsideWorkArea),
    ("widget resize keeps its bottom-right anchor", WidgetResizeKeepsBottomRightAnchor),
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

static AppSettings Settings(string reminderTime = "09:00", string lastDailyDate = "") => new(
    TimeSpan.Parse(reminderTime),
    true,
    lastDailyDate);

static CodexRateLimitWindow General(double used = 7, long? reset = null) => new(
    "codex",
    null,
    "primary",
    used,
    10_080,
    reset ?? new DateTimeOffset(2026, 9, 5, 21, 33, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
    "pro");

static CodexRateLimitWindow SparkWeekly(double used = 0, long? reset = null) => new(
    "codex_bengalfox",
    "GPT-5.3-Codex-Spark",
    "secondary",
    used,
    10_080,
    reset ?? new DateTimeOffset(2026, 9, 6, 12, 29, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
    "pro");

static CodexRateLimitWindow SparkFiveHour(double used = 0) => new(
    "codex_bengalfox",
    "GPT-5.3-Codex-Spark",
    "primary",
    used,
    300,
    new DateTimeOffset(2026, 8, 30, 17, 29, 0, TimeSpan.Zero).ToUnixTimeSeconds(),
    "pro");

static void CollapsedWidgetRemainsCompact()
{
    WidgetSize collapsed = WidgetLayout.GetLogicalSize(expanded: false, limitCount: 8);
    Equal(260, collapsed.Width);
    Equal(84, collapsed.Height);
}

static void ExpandedWidgetGrowsForLimits()
{
    WidgetSize collapsed = WidgetLayout.GetLogicalSize(expanded: false, limitCount: 3);
    WidgetSize expanded = WidgetLayout.GetLogicalSize(expanded: true, limitCount: 3);
    True(expanded.Width > collapsed.Width);
    True(expanded.Height > collapsed.Height);
}

static void WidgetPlacementStaysInsideWorkArea()
{
    var work = new WidgetRectangle(0, 0, 1920, 1040);
    WidgetRectangle placed = WidgetLayout.PlaceSaved(5000, -200, new WidgetSize(260, 84), work);
    Equal(1660, placed.Left);
    Equal(0, placed.Top);
    Equal(1920, placed.Right);
    Equal(84, placed.Bottom);
}

static void WidgetResizeKeepsBottomRightAnchor()
{
    var work = new WidgetRectangle(0, 0, 1920, 1040);
    var current = new WidgetRectangle(1644, 940, 1904, 1024);
    WidgetRectangle expanded = WidgetLayout.ResizeFromBottomRight(current, new WidgetSize(348, 216), work);
    Equal(1904, expanded.Right);
    Equal(1024, expanded.Bottom);
    Equal(1556, expanded.Left);
    Equal(808, expanded.Top);
}

static void ParserReadsAllClocks()
{
    const string json = """
        {"id":2,"result":{"rateLimitsByLimitId":{"codex":{"limitId":"codex","planType":"pro","primary":{"usedPercent":3,"windowDurationMins":10080,"resetsAt":1788643995}},"codex_bengalfox":{"limitId":"codex_bengalfox","limitName":"GPT-5.3-Codex-Spark","planType":"pro","primary":{"usedPercent":0,"windowDurationMins":300,"resetsAt":1788103925},"secondary":{"usedPercent":0,"windowDurationMins":10080,"resetsAt":1788705125}}}}}
        """;

    IReadOnlyList<CodexRateLimitWindow> limits = CodexRateLimitParser.ParseAllResponse(json);
    Equal(3, limits.Count);
    Equal("codex", limits[0].LimitId);
    Equal(10_080, limits[0].WindowDurationMinutes);
    Equal(300, limits[1].WindowDurationMinutes);
    Equal("GPT-5.3-Codex-Spark", limits[2].LimitName);
    Equal(10_080, limits[2].WindowDurationMinutes);
}

static void ParserRejectsMissingLimits()
{
    Throws<InvalidOperationException>(() => CodexRateLimitParser.ParseAllResponse("{\"id\":2,\"result\":{}}"));
}

static void UsageCalculatesRemainingPercentage()
{
    CodexRateLimitWindow limit = General();
    Equal(7d, limit.NormalizedUsedPercent);
    Equal(93d, limit.RemainingPercent);
    Equal(true, limit.IsWeekly);
    Equal("weekly", limit.WindowLabel);
}

static void UsagePercentagesAreClamped()
{
    Equal(100d, General(125).NormalizedUsedPercent);
    Equal(0d, General(125).RemainingPercent);
    Equal(0d, General(-5).NormalizedUsedPercent);
    Equal(100d, General(double.NaN).RemainingPercent);
}

static void DailySummaryIsDue()
{
    DailyReminderOccurrence? due = DailyReminderScheduler.FindDue(Settings(), new DateTime(2026, 8, 30, 9, 1, 0));
    NotNull(due);
    Equal("2026-08-30", due!.DateKey);
}

static void DailySummaryWaitsUntilTime()
{
    Equal(null, DailyReminderScheduler.FindDue(Settings(), new DateTime(2026, 8, 30, 8, 59, 59)));
}

static void DailySummaryDoesNotRepeat()
{
    Equal(null, DailyReminderScheduler.FindDue(Settings(lastDailyDate: "2026-08-30"), new DateTime(2026, 8, 30, 12, 0, 0)));
}

static void NextDailySummaryIsCorrect()
{
    Equal(new DateTime(2026, 8, 30, 9, 0, 0), DailyReminderScheduler.FindNext(Settings(), new DateTime(2026, 8, 30, 8, 0, 0)));
    Equal(new DateTime(2026, 8, 31, 9, 0, 0), DailyReminderScheduler.FindNext(Settings(), new DateTime(2026, 8, 30, 9, 1, 0)));
}

static void UsageBelowFiftyDoesNotWarn()
{
    LimitMonitorResult result = LimitMonitor.Evaluate([], [General(49.9)]);
    Equal(0, result.Events.Count);
}

static void UsageWarningAtFiftyPercent()
{
    LimitMonitorEvent item = Single(LimitMonitor.Evaluate([], [General(50)]).Events);
    Equal(LimitMonitorEventKind.Threshold, item.Kind);
    Equal(50, item.Threshold);
}

static void UsageWarningSelectsHighestCrossedThreshold()
{
    LimitMonitorEvent item = Single(LimitMonitor.Evaluate([], [General(96)]).Events);
    Equal(95, item.Threshold);
}

static void UsageWarningEscalates()
{
    LimitMonitorResult first = LimitMonitor.Evaluate([], [General(50)]);
    LimitMonitorEvent item = Single(LimitMonitor.Evaluate(first.States, [General(76)]).Events);
    Equal(75, item.Threshold);
}

static void UsageWarningDoesNotRepeat()
{
    LimitMonitorResult first = LimitMonitor.Evaluate([], [General(76)]);
    Equal(0, LimitMonitor.Evaluate(first.States, [General(77)]).Events.Count);
}

static void FiveHourClockDoesNotWarn()
{
    Equal(0, LimitMonitor.Evaluate([], [SparkFiveHour(99)]).Events.Count);
}

static void MajorRecoveryIsDetected()
{
    IReadOnlyList<MonitoredLimitState> previous = [new(General(43), 0)];
    LimitMonitorEvent item = Single(LimitMonitor.Evaluate(previous, [General(0, General().ResetsAtUnixSeconds + 2 * 24 * 60 * 60)]).Events);
    Equal(LimitMonitorEventKind.Recovery, item.Kind);
    Equal(43d, item.RecoveredPercent);
}

static void MinorDropIsNotRecovery()
{
    IReadOnlyList<MonitoredLimitState> previous = [new(General(43), 0)];
    Equal(0, LimitMonitor.Evaluate(previous, [General(39)]).Events.Count);
}

static void ResetAdvanceRecoveryIsDetected()
{
    IReadOnlyList<MonitoredLimitState> previous = [new(General(5), 0)];
    long later = General().ResetsAtUnixSeconds + 24 * 60 * 60;
    Equal(LimitMonitorEventKind.Recovery, Single(LimitMonitor.Evaluate(previous, [General(0, later)]).Events).Kind);
}

static void ThresholdRestartsAfterRecovery()
{
    LimitMonitorResult warned = LimitMonitor.Evaluate([], [General(95)]);
    long later = General().ResetsAtUnixSeconds + 2 * 24 * 60 * 60;
    LimitMonitorResult recovered = LimitMonitor.Evaluate(warned.States, [General(0, later)]);
    LimitMonitorEvent item = Single(LimitMonitor.Evaluate(recovered.States, [General(52, later)]).Events);
    Equal(50, item.Threshold);
}

static void MultipleLimitStatesAreIndependent()
{
    LimitMonitorResult result = LimitMonitor.Evaluate([], [General(51), SparkFiveHour(99), SparkWeekly(76)]);
    Equal(3, result.States.Count);
    Equal(2, result.Events.Count);
    Equal(50, result.Events[0].Threshold);
    Equal(75, result.Events[1].Threshold);
}

static void StartupCommandIsQuoted()
{
    Equal("\"C:\\Program Files\\CodexLimitReminder\\CodexLimitReminder.exe\" --background",
        StartupRegistration.BuildCommand(@"C:\Program Files\CodexLimitReminder\CodexLimitReminder.exe"));
}

static void StartupFolderWrapperIsHiddenAndQuoted()
{
    string script = StartupRegistration.BuildStartupScript(@"C:\Program Files\CodexLimitReminder\CodexLimitReminder.exe");
    Contains("shell.Run \"\"\"C:\\Program Files\\CodexLimitReminder\\CodexLimitReminder.exe\"\" --background\", 0, False", script);
}

static T Single<T>(IReadOnlyList<T> values)
{
    Equal(1, values.Count);
    return values[0];
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

static void True(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected condition to be true.");
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
