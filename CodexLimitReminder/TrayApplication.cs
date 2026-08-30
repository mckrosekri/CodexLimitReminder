using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexLimitReminder;

internal sealed class TrayApplication : IDisposable
{
    private const uint TrayIconId = 1;
    private const nuint SchedulerTimerId = 1;
    private const nuint RateRefreshTimerId = 2;

    private const int MenuSettings = 100;
    private const int MenuTestSummary = 101;
    private const int MenuExit = 103;
    private const int MenuRefresh = 104;

    private const int ConnectionStatusControl = 201;
    private const int ResetStatusControl = 202;
    private const int UsageStatusControl = 203;
    private const int ReminderTimeControl = 204;
    private const int StatusControl = 205;
    private const int SaveControl = 206;
    private const int RefreshControl = 207;
    private const int TestControl = 208;
    private const int HideControl = 209;
    private const int AlertCloseControl = 301;

    private static readonly NativeMethods.WindowProc MessageProcedure = MessageWindowProcedure;
    private static readonly NativeMethods.WindowProc SettingsProcedure = SettingsWindowProcedure;
    private static readonly NativeMethods.WindowProc AlertProcedure = AlertWindowProcedure;
    private static TrayApplication? Current;

    private readonly nint _instance;
    private readonly uint _taskbarCreatedMessage;
    private readonly object _refreshLock = new();
    private readonly CancellationTokenSource _shutdown = new();
    private AppSettings _settings;
    private IReadOnlyList<MonitoredLimitState> _limitStates;
    private IReadOnlyList<CodexRateLimitWindow> _limits;
    private RateLimitRefreshResult? _pendingRefresh;
    private nint _messageWindow;
    private nint _settingsWindow;
    private nint _alertWindow;
    private nint _bodyFont;
    private nint _titleFont;
    private nint _alertTitleFont;
    private nint _alertUsageFont;
    private nint _alertBodyFont;
    private bool _trayIconAdded;
    private bool _settingsVisible;
    private bool _alertVisible;
    private bool _exiting;
    private bool _refreshInProgress;
    private int _refreshFailureCount;
    private string _connectionStatus = "Connecting to Codex…";
    private string _alertEyebrow = "DAILY CODEX LIMIT SUMMARY";
    private string _alertTitle = "Your current Codex limits";
    private string _alertBody = "Live from your signed-in Codex account.";
    private string _alertLogLabel = "daily summary";
    private IReadOnlyList<CodexRateLimitWindow> _alertLimits = Array.Empty<CodexRateLimitWindow>();

    public TrayApplication(LaunchCommand initialCommand)
    {
        Current = this;
        _instance = NativeMethods.GetModuleHandle(null);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
        _settings = SettingsStore.Load();
        _limitStates = LimitStateStore.Load();
        _limits = _limitStates.Select(state => state.Limit).ToArray();
        ActivityLog.Write($"Loaded settings: configured={_settings.IsConfigured}, reminder={FormatTime(_settings.ReminderTime)}.");
        if (_limits.Count > 0)
        {
            _connectionStatus = $"Using {_limits.Count} saved Codex limits while refreshing…";
        }

        RegisterWindowClasses();
        _messageWindow = CreateRequiredWindow(
            0,
            NativeMethods.MessageWindowClass,
            null,
            0,
            0,
            0,
            0,
            0,
            0,
            0);

        AddTrayIcon();
        EvaluateDailySummary();

        if (!_settings.IsConfigured || initialCommand.Kind == LaunchCommandKind.ShowSettings)
        {
            ShowSettings();
        }
        else if (initialCommand.Kind == LaunchCommandKind.TestSummary)
        {
            ShowTestSummary();
        }

        BeginRateLimitRefresh();
    }

    public int Run()
    {
        while (NativeMethods.GetMessage(out NativeMethods.Message message, 0, 0, 0) > 0)
        {
            if (_alertVisible && message.Value == NativeMethods.WmKeyDown && message.WParam == NativeMethods.VkEscape)
            {
                HideAlert();
                continue;
            }

            if (_alertVisible && IsMessageForWindowOrChild(message.Window, _alertWindow) &&
                NativeMethods.IsDialogMessage(_alertWindow, ref message))
            {
                continue;
            }

            if (_settingsVisible && IsMessageForWindowOrChild(message.Window, _settingsWindow) &&
                NativeMethods.IsDialogMessage(_settingsWindow, ref message))
            {
                continue;
            }

            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }

        return 0;
    }

    public void Dispose()
    {
        RemoveTrayIcon();

        if (_messageWindow != 0)
        {
            NativeMethods.KillTimer(_messageWindow, SchedulerTimerId);
            NativeMethods.KillTimer(_messageWindow, RateRefreshTimerId);
        }

        _shutdown.Cancel();
        _shutdown.Dispose();

        Destroy(ref _alertWindow);
        Destroy(ref _settingsWindow);
        Destroy(ref _messageWindow);
        DeleteFont(ref _alertBodyFont);
        DeleteFont(ref _alertUsageFont);
        DeleteFont(ref _alertTitleFont);
        DeleteFont(ref _titleFont);
        DeleteFont(ref _bodyFont);
        Current = null;
    }

    private void RegisterWindowClasses()
    {
        RegisterWindowClass(NativeMethods.MessageWindowClass, MessageProcedure, 0);
        RegisterWindowClass(NativeMethods.SettingsWindowClass, SettingsProcedure, NativeMethods.GetSysColorBrush(NativeMethods.ColorButtonFace));
        RegisterWindowClass(
            NativeMethods.AlertWindowClass,
            AlertProcedure,
            NativeMethods.GetSysColorBrush(NativeMethods.ColorWindow),
            NativeMethods.CsHRedraw | NativeMethods.CsVRedraw);
    }

    private void RegisterWindowClass(string name, NativeMethods.WindowProc procedure, nint background, uint style = 0)
    {
        var windowClass = new NativeMethods.WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WindowClassEx>(),
            Style = style,
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(procedure),
            Instance = _instance,
            Icon = NativeMethods.LoadIcon(0, new nint(32516)),
            Cursor = NativeMethods.LoadCursor(0, new nint(32512)),
            BackgroundBrush = background,
            ClassName = name,
            SmallIcon = NativeMethods.LoadIcon(0, new nint(32516))
        };

        if (NativeMethods.RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not register window class {name}.");
        }
    }

    private nint CreateRequiredWindow(
        uint extendedStyle,
        string className,
        string? title,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu)
    {
        nint window = NativeMethods.CreateWindowEx(
            extendedStyle,
            className,
            title,
            style,
            x,
            y,
            width,
            height,
            parent,
            menu,
            _instance,
            0);

        if (window == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not create {className}.");
        }

        return window;
    }

    private void AddTrayIcon()
    {
        var data = CreateNotifyIconData();
        _trayIconAdded = NativeMethods.ShellNotifyIcon(NativeMethods.NimAdd, ref data);
        if (!_trayIconAdded)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not add the tray icon.");
        }

        data.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        NativeMethods.ShellNotifyIcon(NativeMethods.NimSetVersion, ref data);
    }

    private void RemoveTrayIcon()
    {
        if (!_trayIconAdded || _messageWindow == 0)
        {
            return;
        }

        var data = CreateNotifyIconData();
        NativeMethods.ShellNotifyIcon(NativeMethods.NimDelete, ref data);
        _trayIconAdded = false;
    }

    private NativeMethods.NotifyIconData CreateNotifyIconData() => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        Window = _messageWindow,
        Id = TrayIconId,
        Flags = NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip | NativeMethods.NifShowTip,
        CallbackMessage = NativeMethods.WmTrayIcon,
        Icon = NativeMethods.LoadIcon(0, new nint(32516)),
        Tip = "Codex Limit Reminder — right-click for options",
        Info = string.Empty,
        InfoTitle = string.Empty
    };

    private void ShowTrayMenu()
    {
        nint menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString | NativeMethods.MfDefault, MenuSettings, "Settings…");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, MenuRefresh, "Refresh Codex status");
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, MenuTestSummary, "Test daily limit summary");
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, MenuExit, "Exit");

            NativeMethods.GetCursorPos(out NativeMethods.Point point);
            NativeMethods.SetForegroundWindow(_messageWindow);
            uint selected = NativeMethods.TrackPopupMenu(
                menu,
                NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd | NativeMethods.TpmNoNotify,
                point.X,
                point.Y,
                0,
                _messageWindow,
                0);

            HandleMenuCommand((int)selected);
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void HandleMenuCommand(int command)
    {
        switch (command)
        {
            case MenuSettings:
                ShowSettings();
                break;
            case MenuTestSummary:
                ShowTestSummary();
                break;
            case MenuRefresh:
                BeginRateLimitRefresh();
                break;
            case MenuExit:
                ExitApplication();
                break;
        }
    }

    private void ShowSettings()
    {
        EnsureSettingsWindow();
        PopulateSettingsControls();
        UpdateCodexStatusControls();
        UpdateNextReminderText();
        _settingsVisible = true;
        NativeMethods.ShowWindow(_settingsWindow, NativeMethods.SwRestore);
        NativeMethods.SetForegroundWindow(_settingsWindow);
        NativeMethods.SetFocus(NativeMethods.GetDlgItem(_settingsWindow, ReminderTimeControl));
    }

    private void HideSettings()
    {
        if (_settingsWindow == 0)
        {
            return;
        }

        _settingsVisible = false;
        NativeMethods.ShowWindow(_settingsWindow, NativeMethods.SwHide);
    }

    private void EnsureSettingsWindow()
    {
        if (_settingsWindow != 0)
        {
            return;
        }

        uint dpi = NativeMethods.GetDpiForSystem();
        int width = Scale(600, dpi);
        int height = Scale(540, dpi);
        NativeMethods.GetCursorPos(out NativeMethods.Point cursor);
        NativeMethods.MonitorInfo monitor = GetMonitorInfo(cursor);
        int x = monitor.Work.Left + (monitor.Work.Width - width) / 2;
        int y = monitor.Work.Top + (monitor.Work.Height - height) / 2;

        _settingsWindow = CreateRequiredWindow(
            0,
            NativeMethods.SettingsWindowClass,
            "Codex Limit Reminder settings",
            NativeMethods.WsOverlapped | NativeMethods.WsCaption | NativeMethods.WsSysMenu | NativeMethods.WsMinimizeBox,
            x,
            y,
            width,
            height,
            0,
            0);
    }

    private void CreateSettingsControls(nint window)
    {
        uint dpi = Math.Max(96, NativeMethods.GetDpiForWindow(window));
        _bodyFont = CreateSegoeFont(10, 400, dpi);
        _titleFont = CreateSegoeFont(20, 600, dpi);

        nint title = CreateControl("STATIC", "Codex Limit Reminder", NativeMethods.SsLeft, 28, 24, 520, 38, window, 0, dpi);
        ApplyFont(title, _titleFont);
        CreateControl(
            "STATIC",
            "Reads every signed-in Codex limit automatically. Choose only when the daily summary appears.",
            NativeMethods.SsLeft,
            30,
            70,
            530,
            42,
            window,
            0,
            dpi);

        CreateControl("STATIC", "Codex connection", NativeMethods.SsLeft, 30, 125, 170, 24, window, 0, dpi);
        CreateControl("STATIC", string.Empty, NativeMethods.SsLeft, 215, 125, 330, 38, window, ConnectionStatusControl, dpi);

        CreateControl("STATIC", "Live limits", NativeMethods.SsLeft, 30, 180, 170, 24, window, 0, dpi);
        CreateControl("STATIC", string.Empty, NativeMethods.SsLeft, 215, 174, 350, 92, window, ResetStatusControl, dpi);

        CreateControl("STATIC", "Protection", NativeMethods.SsLeft, 30, 275, 170, 24, window, 0, dpi);
        CreateControl("STATIC", string.Empty, NativeMethods.SsLeft, 215, 270, 350, 46, window, UsageStatusControl, dpi);

        CreateControl("STATIC", "Daily summary time", NativeMethods.SsLeft, 30, 326, 170, 24, window, 0, dpi);
        CreateControl(
            "EDIT",
            string.Empty,
            NativeMethods.EsAutoHScroll | NativeMethods.WsTabStop | NativeMethods.WsBorder,
            215,
            320,
            120,
            30,
            window,
            ReminderTimeControl,
            dpi,
            NativeMethods.WsExClientEdge);
        CreateControl("STATIC", "24-hour HH:mm", NativeMethods.SsLeft, 350, 326, 190, 24, window, 0, dpi);

        CreateControl(
            "STATIC",
            "No API key or reset-day setup. Rolling recoveries and weekly thresholds are detected automatically.",
            NativeMethods.SsLeft,
            30,
            362,
            525,
            42,
            window,
            0,
            dpi);

        CreateControl("STATIC", string.Empty, NativeMethods.SsLeft, 30, 410, 525, 36, window, StatusControl, dpi);

        CreateControl(
            "BUTTON",
            "Save time",
            NativeMethods.BsDefPushButton | NativeMethods.WsTabStop,
            30,
            457,
            120,
            38,
            window,
            SaveControl,
            dpi);
        CreateControl(
            "BUTTON",
            "Refresh Codex",
            NativeMethods.BsPushButton | NativeMethods.WsTabStop,
            163,
            457,
            130,
            38,
            window,
            RefreshControl,
            dpi);
        CreateControl(
            "BUTTON",
            "Test full screen",
            NativeMethods.BsPushButton | NativeMethods.WsTabStop,
            306,
            457,
            145,
            38,
            window,
            TestControl,
            dpi);
        CreateControl(
            "BUTTON",
            "Hide",
            NativeMethods.BsPushButton | NativeMethods.WsTabStop,
            464,
            457,
            81,
            38,
            window,
            HideControl,
            dpi);
    }

    private nint CreateControl(
        string className,
        string text,
        uint controlStyle,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        int id,
        uint dpi,
        uint extendedStyle = 0)
    {
        nint control = CreateRequiredWindow(
            extendedStyle,
            className,
            text,
            NativeMethods.WsChild | NativeMethods.WsVisible | controlStyle,
            Scale(x, dpi),
            Scale(y, dpi),
            Scale(width, dpi),
            Scale(height, dpi),
            parent,
            new nint(id));
        ApplyFont(control, _bodyFont);
        return control;
    }

    private void PopulateSettingsControls()
    {
        NativeMethods.SetWindowText(NativeMethods.GetDlgItem(_settingsWindow, ReminderTimeControl), FormatTime(_settings.ReminderTime));
    }

    private void SaveSettings()
    {
        if (!TryReadTime(ReminderTimeControl, out TimeSpan reminderTime))
        {
            NativeMethods.MessageBox(
                _settingsWindow,
                "Enter the notification time as a 24-hour HH:mm value, for example 09:00.",
                "Check the reminder settings",
                NativeMethods.MbIconError);
            return;
        }

        AppSettings updated = _settings with
        {
            ReminderTime = reminderTime,
            IsConfigured = true
        };

        try
        {
            StartupRegistration.Apply(true, Environment.ProcessPath!);
            SettingsStore.Save(updated);
        }
        catch (Exception exception)
        {
            NativeMethods.MessageBox(
                _settingsWindow,
                $"The settings could not be saved.\n\n{exception.Message}",
                "Codex Limit Reminder",
                NativeMethods.MbIconError);
            return;
        }

        _settings = updated;
        EvaluateDailySummary();
        HideSettings();
    }

    private bool TryReadTime(int controlId, out TimeSpan value)
    {
        nint control = NativeMethods.GetDlgItem(_settingsWindow, controlId);
        var text = new StringBuilder(Math.Max(16, NativeMethods.GetWindowTextLength(control) + 1));
        NativeMethods.GetWindowText(control, text, text.Capacity);
        return TimeSpan.TryParseExact(
            text.ToString().Trim(),
            new[] { "h\\:mm", "hh\\:mm" },
            CultureInfo.InvariantCulture,
            out value) && value >= TimeSpan.Zero && value < TimeSpan.FromDays(1);
    }

    private void UpdateCodexStatusControls()
    {
        if (_settingsWindow == 0)
        {
            return;
        }

        NativeMethods.SetWindowText(
            NativeMethods.GetDlgItem(_settingsWindow, ConnectionStatusControl),
            _connectionStatus);

        if (_limits.Count == 0)
        {
            NativeMethods.SetWindowText(NativeMethods.GetDlgItem(_settingsWindow, ResetStatusControl), "Waiting for Codex…");
            NativeMethods.SetWindowText(NativeMethods.GetDlgItem(_settingsWindow, UsageStatusControl), "Waiting for Codex…");
            return;
        }

        string liveLimits = string.Join("\r\n", _limits.Select(FormatCompactLimit));
        NativeMethods.SetWindowText(
            NativeMethods.GetDlgItem(_settingsWindow, ResetStatusControl),
            liveLimits);
        NativeMethods.SetWindowText(
            NativeMethods.GetDlgItem(_settingsWindow, UsageStatusControl),
            "Daily summary · weekly alerts at 50/75/90/95% · rolling recovery alerts");
    }

    private void UpdateNextReminderText()
    {
        if (_settingsWindow == 0)
        {
            return;
        }

        if (!_settings.IsConfigured)
        {
            NativeMethods.SetWindowText(
                NativeMethods.GetDlgItem(_settingsWindow, StatusControl),
                "Save the notification time once to activate reminders.");
            return;
        }

        DateTime now = DateTime.Now;
        DateTime next = DailyReminderScheduler.FindNext(_settings, now);
        NativeMethods.SetWindowText(
            NativeMethods.GetDlgItem(_settingsWindow, StatusControl),
            $"Next daily summary: {next:ddd, d MMM 'at' HH:mm}. Threshold and recovery alerts are immediate.");
    }

    private void EvaluateDailySummary()
    {
        if (_messageWindow == 0)
        {
            return;
        }

        NativeMethods.KillTimer(_messageWindow, SchedulerTimerId);
        if (!_settings.IsConfigured)
        {
            UpdateNextReminderText();
            return;
        }

        DateTime now = DateTime.Now;
        DailyReminderOccurrence? due = DailyReminderScheduler.FindDue(_settings, now);
        if (due is not null && _limits.Count > 0)
        {
            _settings = _settings with { LastDailySummaryDate = due.DateKey };
            SettingsStore.Save(_settings);
            ActivityLog.Write($"Showing daily summary with {_limits.Count} live limit windows.");
            ShowLimitAlert(
                "DAILY CODEX LIMIT SUMMARY",
                "Your current Codex limits",
                "Live from your signed-in Codex account. Weekly limits are rolling and may recover in stages.",
                "daily summary");
        }

        DateTime next = DailyReminderScheduler.FindNext(_settings, now);
        long milliseconds = (long)Math.Ceiling((next - now).TotalMilliseconds);
        uint timerDelay = (uint)Math.Clamp(milliseconds, 1000L, (long)uint.MaxValue - 1);
        NativeMethods.SetTimer(_messageWindow, SchedulerTimerId, timerDelay, 0);
        ActivityLog.Write($"Next daily summary scheduled for {next:yyyy-MM-dd HH:mm}.");

        UpdateNextReminderText();
    }

    private void MarkTodaySummarySatisfiedIfDue()
    {
        DailyReminderOccurrence? due = DailyReminderScheduler.FindDue(_settings, DateTime.Now);
        if (due is null)
        {
            return;
        }

        _settings = _settings with { LastDailySummaryDate = due.DateKey };
        SettingsStore.Save(_settings);
    }

    private void ShowTestSummary()
    {
        IReadOnlyList<CodexRateLimitWindow> actualLimits = _limits;
        if (_limits.Count == 0)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            _limits =
            [
                new("codex", null, "primary", 42, 10_080, now.AddDays(4).ToUnixTimeSeconds(), "pro"),
                new("codex_bengalfox", "GPT-5.3-Codex-Spark", "primary", 15, 300, now.AddHours(3).ToUnixTimeSeconds(), "pro"),
                new("codex_bengalfox", "GPT-5.3-Codex-Spark", "secondary", 27, 10_080, now.AddDays(5).ToUnixTimeSeconds(), "pro")
            ];
        }

        ShowLimitAlert(
            "TEST · DAILY CODEX LIMIT SUMMARY",
            "Your current Codex limits",
            "This is the real full-screen reminder layout. Close it to keep the tray app running.",
            "test summary");
        _limits = actualLimits;
    }

    private void BeginRateLimitRefresh()
    {
        if (_exiting || _messageWindow == 0 || _refreshInProgress)
        {
            return;
        }

        _refreshInProgress = true;
        NativeMethods.KillTimer(_messageWindow, RateRefreshTimerId);
        _connectionStatus = "Connecting to Codex automatically…";
        UpdateCodexStatusControls();

        _ = Task.Run(async () =>
        {
            RateLimitRefreshResult result;
            try
            {
                IReadOnlyList<CodexRateLimitWindow> limits = await CodexAppServerClient.ReadRateLimitsAsync(
                    TimeSpan.FromSeconds(20),
                    _shutdown.Token).ConfigureAwait(false);
                result = new RateLimitRefreshResult(limits, null);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                result = new RateLimitRefreshResult(null, FriendlyConnectionError(exception));
            }

            lock (_refreshLock)
            {
                _pendingRefresh = result;
            }

            if (!_exiting && _messageWindow != 0)
            {
                NativeMethods.PostMessage(_messageWindow, NativeMethods.WmRateLimitRefreshComplete, 0, 0);
            }
        });
    }

    private void CompleteRateLimitRefresh()
    {
        RateLimitRefreshResult? result;
        lock (_refreshLock)
        {
            result = _pendingRefresh;
            _pendingRefresh = null;
        }

        _refreshInProgress = false;
        if (result?.Limits is { Count: > 0 })
        {
            LimitMonitorResult monitor = LimitMonitor.Evaluate(_limitStates, result.Limits);
            _limitStates = monitor.States;
            _limits = monitor.States.Select(state => state.Limit).ToArray();
            LimitStateStore.Save(_limitStates);
            _refreshFailureCount = 0;
            _connectionStatus = $"Connected automatically to Codex · {_limits.Count} live clocks";
            ActivityLog.Write($"Codex refresh succeeded: {string.Join(" | ", _limits.Select(FormatLogLimit))}.");
            ScheduleRateLimitRefresh(TimeSpan.FromMinutes(15));

            foreach (LimitMonitorEvent item in monitor.Events)
            {
                ActivityLog.Write(item.Kind == LimitMonitorEventKind.Threshold
                    ? $"Detected {item.Threshold}% threshold for {item.Limit.DisplayName} {item.Limit.WindowLabel}."
                    : $"Detected {FormatPercentage(item.RecoveredPercent)}-point recovery for {item.Limit.DisplayName} {item.Limit.WindowLabel}.");
            }

            LimitMonitorEvent? alertEvent = monitor.Events
                .OrderByDescending(item => item.Kind == LimitMonitorEventKind.Threshold ? 1 : 0)
                .ThenByDescending(item => item.Threshold)
                .ThenByDescending(item => item.RecoveredPercent)
                .FirstOrDefault();
            if (alertEvent is not null)
            {
                MarkTodaySummarySatisfiedIfDue();
                if (alertEvent.Kind == LimitMonitorEventKind.Threshold)
                {
                    ShowLimitAlert(
                        $"WEEKLY SAFETY ALERT · {alertEvent.Threshold}% USED",
                        $"Only {FormatPercentage(alertEvent.Limit.RemainingPercent)}% of {alertEvent.Limit.DisplayName} remains",
                        $"The {alertEvent.Limit.WindowLabel} allowance crossed {alertEvent.Threshold}% used. Prioritize your remaining work.",
                        $"{alertEvent.Threshold}% threshold alert");
                }
                else
                {
                    ShowLimitAlert(
                        "ROLLING LIMIT RECOVERY DETECTED",
                        $"{alertEvent.Limit.DisplayName} recovered {FormatPercentage(alertEvent.RecoveredPercent)}%",
                        "Older usage aged out of the rolling window. The reset shown by Codex may move again as other usage ages out.",
                        "rolling recovery alert");
                }
            }

            EvaluateDailySummary();
        }
        else
        {
            _refreshFailureCount++;
            _connectionStatus = $"Could not read Codex: {result?.Error ?? "unknown error"} Retrying automatically.";
            TimeSpan retry = _refreshFailureCount switch
            {
                1 => TimeSpan.FromMinutes(5),
                2 => TimeSpan.FromMinutes(15),
                _ => TimeSpan.FromHours(1)
            };
            ScheduleRateLimitRefresh(retry);
            ActivityLog.Write($"Codex refresh failed: {result?.Error ?? "unknown error"}; retry in {retry.TotalMinutes:0} minutes.");
        }

        UpdateCodexStatusControls();
        UpdateNextReminderText();
    }

    private void ScheduleRateLimitRefresh(TimeSpan delay)
    {
        if (_messageWindow == 0 || _exiting)
        {
            return;
        }

        uint milliseconds = (uint)Math.Clamp((long)delay.TotalMilliseconds, 1000L, (long)uint.MaxValue - 1);
        NativeMethods.SetTimer(_messageWindow, RateRefreshTimerId, milliseconds, 0);
    }

    private static string FriendlyConnectionError(Exception exception) => exception switch
    {
        FileNotFoundException => "Codex is not installed.",
        TimeoutException => "the connection timed out.",
        Win32Exception => "Windows blocked the detected Codex executable.",
        _ => exception.Message.TrimEnd('.') + "."
    };

    private void ShowLimitAlert(string eyebrow, string title, string body, string logLabel)
    {
        _alertEyebrow = eyebrow;
        _alertTitle = title;
        _alertBody = body;
        _alertLogLabel = logLabel;
        _alertLimits = _limits.ToArray();
        EnsureAlertWindow();
        HideSettings();

        NativeMethods.SetWindowText(_alertWindow, $"Codex Limit Reminder — {title}");

        NativeMethods.GetCursorPos(out NativeMethods.Point cursor);
        NativeMethods.MonitorInfo monitor = GetMonitorInfo(cursor);
        NativeMethods.SetWindowPos(
            _alertWindow,
            NativeMethods.HwndTopmost,
            monitor.Monitor.Left,
            monitor.Monitor.Top,
            monitor.Monitor.Width,
            monitor.Monitor.Height,
            NativeMethods.SwpShowWindow);
        _alertVisible = true;
        NativeMethods.InvalidateRect(_alertWindow, 0, true);
        NativeMethods.SetForegroundWindow(_alertWindow);
        NativeMethods.SetFocus(NativeMethods.GetDlgItem(_alertWindow, AlertCloseControl));
    }

    private void EnsureAlertWindow()
    {
        if (_alertWindow != 0)
        {
            return;
        }

        _alertWindow = CreateRequiredWindow(
            NativeMethods.WsExTopmost,
            NativeMethods.AlertWindowClass,
            "Codex Limit Reminder",
            NativeMethods.WsPopup,
            0,
            0,
            1,
            1,
            0,
            0);
    }

    private void CreateAlertControls(nint window)
    {
        uint dpi = Math.Max(96, NativeMethods.GetDpiForWindow(window));
        _alertTitleFont = CreateSegoeFont(38, 600, dpi);
        _alertUsageFont = CreateSegoeFont(30, 600, dpi);
        _alertBodyFont = CreateSegoeFont(18, 400, dpi);
        nint close = CreateControl(
            "BUTTON",
            "Close reminder",
            NativeMethods.BsDefPushButton | NativeMethods.WsTabStop,
            0,
            0,
            230,
            54,
            window,
            AlertCloseControl,
            dpi);
        ApplyFont(close, _alertBodyFont);
    }

    private void LayoutAlertButton(nint window, int clientWidth, int clientHeight)
    {
        uint dpi = Math.Max(96, NativeMethods.GetDpiForWindow(window));
        int width = Scale(230, dpi);
        int height = Scale(54, dpi);
        int x = (clientWidth - width) / 2;
        int y = Math.Max(0, (clientHeight * 5 / 6) - height / 2);
        NativeMethods.SetWindowPos(
            NativeMethods.GetDlgItem(window, AlertCloseControl),
            0,
            x,
            y,
            width,
            height,
            NativeMethods.SwpNoActivate);
    }

    private void PaintAlert(nint window)
    {
        nint deviceContext = NativeMethods.BeginPaint(window, out NativeMethods.PaintStruct paint);
        try
        {
            NativeMethods.GetClientRect(window, out NativeMethods.Rect client);
            NativeMethods.FillRect(deviceContext, ref client, NativeMethods.GetSysColorBrush(NativeMethods.ColorWindow));
            NativeMethods.SetBkMode(deviceContext, NativeMethods.Transparent);
            NativeMethods.SetTextColor(deviceContext, NativeMethods.GetSysColor(NativeMethods.ColorWindowText));

            NativeMethods.Rect eyebrow = new()
            {
                Left = client.Left + client.Width / 10,
                Top = client.Top + client.Height / 12,
                Right = client.Right - client.Width / 10,
                Bottom = client.Top + client.Height / 6
            };
            nint previous = NativeMethods.SelectObject(deviceContext, _alertBodyFont);
            NativeMethods.DrawText(
                deviceContext,
                _alertEyebrow,
                -1,
                ref eyebrow,
                NativeMethods.DtCenter | NativeMethods.DtVCenter | NativeMethods.DtSingleLine);

            NativeMethods.Rect title = new()
            {
                Left = client.Left + client.Width / 12,
                Top = client.Top + client.Height / 6,
                Right = client.Right - client.Width / 12,
                Bottom = client.Top + client.Height * 3 / 10
            };
            NativeMethods.SelectObject(deviceContext, _alertTitleFont);
            NativeMethods.DrawText(
                deviceContext,
                _alertTitle,
                -1,
                ref title,
                NativeMethods.DtCenter | NativeMethods.DtVCenter | NativeMethods.DtWordBreak);

            int tableTop = client.Top + client.Height * 3 / 10;
            int tableBottom = client.Top + client.Height * 2 / 3;
            if (_alertLimits.Count == 0)
            {
                var unavailable = new NativeMethods.Rect
                {
                    Left = client.Left + client.Width / 10,
                    Top = tableTop,
                    Right = client.Right - client.Width / 10,
                    Bottom = tableBottom
                };
                NativeMethods.SelectObject(deviceContext, _alertUsageFont);
                NativeMethods.DrawText(deviceContext, "USAGE DATA UNAVAILABLE", -1, ref unavailable,
                    NativeMethods.DtCenter | NativeMethods.DtVCenter | NativeMethods.DtSingleLine);
            }
            else
            {
                int rowHeight = (tableBottom - tableTop) / _alertLimits.Count;
                for (int index = 0; index < _alertLimits.Count; index++)
                {
                    CodexRateLimitWindow limit = _alertLimits[index];
                    int rowTop = tableTop + index * rowHeight;
                    var label = new NativeMethods.Rect
                    {
                        Left = client.Left + client.Width / 12,
                        Top = rowTop,
                        Right = client.Right - client.Width / 12,
                        Bottom = rowTop + rowHeight * 2 / 5
                    };
                    NativeMethods.SelectObject(deviceContext, _alertBodyFont);
                    NativeMethods.DrawText(
                        deviceContext,
                        $"{limit.DisplayName.ToUpperInvariant()} · {limit.WindowLabel.ToUpperInvariant()}",
                        -1,
                        ref label,
                        NativeMethods.DtCenter | NativeMethods.DtVCenter | NativeMethods.DtSingleLine);

                    var value = new NativeMethods.Rect
                    {
                        Left = client.Left + client.Width / 12,
                        Top = rowTop + rowHeight * 2 / 5,
                        Right = client.Right - client.Width / 12,
                        Bottom = rowTop + rowHeight
                    };
                    DateTime reset = limit.ResetsAt.ToLocalTime().DateTime;
                    NativeMethods.SelectObject(deviceContext, _alertUsageFont);
                    NativeMethods.DrawText(
                        deviceContext,
                        $"{FormatPercentage(limit.NormalizedUsedPercent)}% USED   ·   {FormatPercentage(limit.RemainingPercent)}% LEFT   ·   RESETS {reset:ddd d MMM HH:mm}",
                        -1,
                        ref value,
                        NativeMethods.DtCenter | NativeMethods.DtVCenter | NativeMethods.DtSingleLine);
                }
            }

            NativeMethods.Rect body = new()
            {
                Left = client.Left + client.Width / 8,
                Top = client.Top + client.Height * 2 / 3,
                Right = client.Right - client.Width / 8,
                Bottom = client.Top + client.Height * 4 / 5
            };
            NativeMethods.SelectObject(deviceContext, _alertBodyFont);
            NativeMethods.DrawText(
                deviceContext,
                _alertBody,
                -1,
                ref body,
                NativeMethods.DtCenter | NativeMethods.DtVCenter | NativeMethods.DtWordBreak);
            NativeMethods.SelectObject(deviceContext, previous);
        }
        finally
        {
            NativeMethods.EndPaint(window, ref paint);
        }
    }

    private void HideAlert()
    {
        if (_alertWindow == 0)
        {
            return;
        }

        if (_alertVisible)
        {
            ActivityLog.Write($"Closed {_alertLogLabel}.");
        }

        _alertVisible = false;
        NativeMethods.ShowWindow(_alertWindow, NativeMethods.SwHide);
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        RemoveTrayIcon();
        NativeMethods.PostQuitMessage(0);
    }

    private void HandleExternalCommand(LaunchCommandKind command)
    {
        switch (command)
        {
            case LaunchCommandKind.Background:
                break;
            case LaunchCommandKind.TestSummary:
                ShowTestSummary();
                break;
            default:
                ShowSettings();
                break;
        }
    }

    private static nint MessageWindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        TrayApplication? app = Current;
        if (app is null)
        {
            return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }

        if (message == app._taskbarCreatedMessage)
        {
            app._trayIconAdded = false;
            app.AddTrayIcon();
            return 0;
        }

        switch (message)
        {
            case NativeMethods.WmTrayIcon:
                uint eventCode = (uint)lParam.ToInt64() & 0xFFFF;
                if (eventCode is NativeMethods.WmContextMenu)
                {
                    app.ShowTrayMenu();
                }
                else if (eventCode is NativeMethods.NinSelect or NativeMethods.WmLButtonUp)
                {
                    app.ShowSettings();
                }

                return 0;
            case NativeMethods.WmTimer:
                if (wParam == SchedulerTimerId)
                {
                    app.EvaluateDailySummary();
                }
                else if (wParam == RateRefreshTimerId)
                {
                    NativeMethods.KillTimer(app._messageWindow, RateRefreshTimerId);
                    app.BeginRateLimitRefresh();
                }

                return 0;
            case NativeMethods.WmTimeChange:
                app.EvaluateDailySummary();
                app.BeginRateLimitRefresh();
                return 0;
            case NativeMethods.WmExternalCommand:
                app.HandleExternalCommand((LaunchCommandKind)(int)wParam);
                return 0;
            case NativeMethods.WmRateLimitRefreshComplete:
                app.CompleteRateLimitRefresh();
                return 0;
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    private static nint SettingsWindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        TrayApplication? app = Current;
        if (app is null)
        {
            return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }

        switch (message)
        {
            case 0x0001: // WM_CREATE
                app._settingsWindow = window;
                app.CreateSettingsControls(window);
                return 0;
            case NativeMethods.WmCommand:
                int command = (int)(wParam & 0xFFFF);
                if (command == SaveControl)
                {
                    app.SaveSettings();
                    return 0;
                }

                if (command == TestControl)
                {
                    app.ShowTestSummary();
                    return 0;
                }

                if (command == RefreshControl)
                {
                    app.BeginRateLimitRefresh();
                    return 0;
                }

                if (command == HideControl)
                {
                    app.HideSettings();
                    return 0;
                }

                break;
            case NativeMethods.WmClose:
                app.HideSettings();
                return 0;
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    private static nint AlertWindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        TrayApplication? app = Current;
        if (app is null)
        {
            return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }

        switch (message)
        {
            case 0x0001: // WM_CREATE
                app._alertWindow = window;
                app.CreateAlertControls(window);
                return 0;
            case NativeMethods.WmSize:
                int width = (int)(lParam.ToInt64() & 0xFFFF);
                int height = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                app.LayoutAlertButton(window, width, height);
                return 0;
            case NativeMethods.WmPaint:
                app.PaintAlert(window);
                return 0;
            case NativeMethods.WmCommand:
                if ((int)(wParam & 0xFFFF) == AlertCloseControl)
                {
                    app.HideAlert();
                    return 0;
                }

                break;
            case NativeMethods.WmClose:
                app.HideAlert();
                return 0;
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    private static string FormatTime(TimeSpan value) => $"{(int)value.TotalHours:00}:{value.Minutes:00}";

    private static string FormatPercentage(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string FormatCompactLimit(CodexRateLimitWindow limit)
    {
        string name = limit.LimitId.Equals("codex", StringComparison.OrdinalIgnoreCase)
            ? "General"
            : limit.DisplayName.Replace("GPT-5.3-Codex-", string.Empty, StringComparison.OrdinalIgnoreCase);
        DateTime reset = limit.ResetsAt.ToLocalTime().DateTime;
        return $"{name} {limit.WindowLabel}: {FormatPercentage(limit.NormalizedUsedPercent)}% used · " +
               $"{FormatPercentage(limit.RemainingPercent)}% left · {reset:d MMM HH:mm}";
    }

    private static string FormatLogLimit(CodexRateLimitWindow limit) =>
        $"{limit.DisplayName} {limit.WindowLabel}: {FormatPercentage(limit.NormalizedUsedPercent)}% used, " +
        $"{FormatPercentage(limit.RemainingPercent)}% left, reset {limit.ResetsAt.ToLocalTime():yyyy-MM-dd HH:mm}";

    private static int Scale(int value, uint dpi) => NativeMethods.MulDiv(value, (int)dpi, 96);

    private static nint CreateSegoeFont(int points, int weight, uint dpi) => NativeMethods.CreateFont(
        -NativeMethods.MulDiv(points, (int)dpi, 72),
        0,
        0,
        0,
        weight,
        0,
        0,
        0,
        1,
        0,
        0,
        5,
        0,
        "Segoe UI");

    private static void ApplyFont(nint window, nint font) =>
        NativeMethods.SendMessage(window, NativeMethods.WmSetFont, (nuint)font, new nint(1));

    private static bool IsMessageForWindowOrChild(nint messageWindow, nint parent) =>
        messageWindow == parent || NativeMethods.IsChild(parent, messageWindow);

    private static NativeMethods.MonitorInfo GetMonitorInfo(NativeMethods.Point point)
    {
        nint monitorHandle = NativeMethods.MonitorFromPoint(point, NativeMethods.MonitorDefaultToNearest);
        var monitor = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitor))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read monitor dimensions.");
        }

        return monitor;
    }

    private static void DeleteFont(ref nint font)
    {
        if (font == 0)
        {
            return;
        }

        NativeMethods.DeleteObject(font);
        font = 0;
    }

    private static void Destroy(ref nint window)
    {
        if (window == 0)
        {
            return;
        }

        NativeMethods.DestroyWindow(window);
        window = 0;
    }

    private sealed record RateLimitRefreshResult(IReadOnlyList<CodexRateLimitWindow>? Limits, string? Error);
}
