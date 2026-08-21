using CodexLimitReminder;

var tests = new (string Name, Action Run)[]
{
    ("next reset stays in the current week before reset time", NextResetBeforeResetTime),
    ("next reset advances one week after reset time", NextResetAfterResetTime),
    ("day 6 is due two mornings before reset", Day6IsDue),
    ("day 7 is due one morning before reset", Day7IsDue),
    ("a reminder is not repeated after its key is saved", ReminderDoesNotRepeat),
    ("a missed previous-day reminder is not backfilled", PreviousDayIsNotBackfilled),
    ("next reminders are returned in day 6 then day 7 order", NextReminderOrder),
    ("startup command is quoted and windowless", StartupCommandIsQuoted)
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

static AppSettings Settings(
    DayOfWeek resetDay = DayOfWeek.Friday,
    string resetTime = "17:30",
    string reminderTime = "09:00",
    string lastKey = "") => new(
        resetDay,
        TimeSpan.Parse(resetTime),
        TimeSpan.Parse(reminderTime),
        true,
        true,
        lastKey);

static void NextResetBeforeResetTime()
{
    DateTime now = new(2026, 8, 21, 12, 0, 0); // Friday
    Equal(new DateTime(2026, 8, 21, 17, 30, 0), ReminderScheduler.GetNextReset(Settings(), now));
}

static void NextResetAfterResetTime()
{
    DateTime now = new(2026, 8, 21, 18, 0, 0); // Friday
    Equal(new DateTime(2026, 8, 28, 17, 30, 0), ReminderScheduler.GetNextReset(Settings(), now));
}

static void Day6IsDue()
{
    DateTime now = new(2026, 8, 19, 9, 0, 0); // Wednesday
    ReminderOccurrence? due = ReminderScheduler.FindDue(Settings(), now);
    NotNull(due);
    Equal(6, due!.CycleDay);
    Equal(2, due.DaysBeforeReset);
}

static void Day7IsDue()
{
    DateTime now = new(2026, 8, 20, 10, 0, 0); // Thursday
    ReminderOccurrence? due = ReminderScheduler.FindDue(Settings(), now);
    NotNull(due);
    Equal(7, due!.CycleDay);
    Equal(1, due.DaysBeforeReset);
}

static void ReminderDoesNotRepeat()
{
    DateTime now = new(2026, 8, 19, 9, 5, 0);
    ReminderOccurrence due = ReminderScheduler.FindDue(Settings(), now)!;
    Equal(null, ReminderScheduler.FindDue(Settings(lastKey: due.Key), now));
}

static void PreviousDayIsNotBackfilled()
{
    DateTime now = new(2026, 8, 20, 8, 30, 0);
    Equal(null, ReminderScheduler.FindDue(Settings(), now));
}

static void NextReminderOrder()
{
    AppSettings settings = Settings();
    DateTime now = new(2026, 8, 18, 12, 0, 0); // Tuesday
    ReminderOccurrence first = ReminderScheduler.FindNext(settings, now);
    ReminderOccurrence second = ReminderScheduler.FindNext(settings, first.DueLocal.AddSeconds(1));
    Equal(6, first.CycleDay);
    Equal(new DateTime(2026, 8, 19, 9, 0, 0), first.DueLocal);
    Equal(7, second.CycleDay);
    Equal(new DateTime(2026, 8, 20, 9, 0, 0), second.DueLocal);
}

static void StartupCommandIsQuoted()
{
    string command = StartupRegistration.BuildCommand(@"C:\Program Files\CodexLimitReminder\CodexLimitReminder.exe");
    Equal("\"C:\\Program Files\\CodexLimitReminder\\CodexLimitReminder.exe\" --background", command);
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
