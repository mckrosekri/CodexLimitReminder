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
    private const nuint ResetCountdownTimerId = 3;

    private const int MenuSettings = 100;
    private const int MenuTestSummary = 101;
    private const int MenuExit = 103;
    private const int MenuRefresh = 104;
    private const int MenuToggleWidget = 105;

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
    private const int WidgetToggleControl = 401;

    private static readonly NativeMethods.WindowProc MessageProcedure = MessageWindowProcedure;
    private static readonly NativeMethods.WindowProc SettingsProcedure = SettingsWindowProcedure;
    private static readonly NativeMethods.WindowProc AlertProcedure = AlertWindowProcedure;
    private static readonly NativeMethods.WindowProc WidgetProcedure = WidgetWindowProcedure;
    private static readonly uint WidgetBackgroundColor = Rgb(9, 18, 26);
    private static readonly uint WidgetRaisedColor = Rgb(17, 34, 45);
    private static readonly uint WidgetTextColor = Rgb(234, 251, 255);
    private static readonly uint WidgetMutedColor = Rgb(156, 181, 191);
    private static readonly uint WidgetAccentColor = Rgb(61, 230, 255);
    private static readonly uint WidgetDangerColor = Rgb(255, 77, 141);
    private static readonly uint WidgetTrackColor = Rgb(38, 57, 67);
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
    private nint _widgetWindow;
    private nint _bodyFont;
    private nint _titleFont;
    private nint _alertTitleFont;
    private nint _alertUsageFont;
    private nint _alertBodyFont;
    private nint _widgetTitleFont;
    private nint _widgetMetricFont;
    private nint _widgetBodyFont;
    private bool _trayIconAdded;
    private bool _settingsVisible;
    private bool _alertVisible;
    private bool _widgetVisible;
    private bool _widgetExpanded;
    private bool _exiting;
    private bool _refreshInProgress;
    private int _refreshFailureCount;
    private DateTime? _lastSuccessfulRefresh;
    private DateTime _lastCountdownRefreshRequest = DateTime.MinValue;
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
        EnsureWidgetWindow();
        ShowWidget();
        NativeMethods.SetTimer(_messageWindow, ResetCountdownTimerId, 1000, 0);
        EvaluateDailySummary();

        if (!_settings.IsConfigured || initialCommand.Kind == LaunchCommandKind.ShowSettings)
        {
            ShowSettings();
        }
        else if (initialCommand.Kind == LaunchCommandKind.TestSummary)
        {
            ShowTestSummary();
        }
        else if (initialCommand.Kind == LaunchCommandKind.ShowWidgetExpanded)
        {
            SetWidgetExpanded(true);
        }
        else if (initialCommand.Kind == LaunchCommandKind.ShowWidgetCollapsed)
        {
            SetWidgetExpanded(false);
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
            NativeMethods.KillTimer(_messageWindow, ResetCountdownTimerId);
        }

        _shutdown.Cancel();
        _shutdown.Dispose();

        Destroy(ref _alertWindow);
        Destroy(ref _settingsWindow);
        Destroy(ref _widgetWindow);
        Destroy(ref _messageWindow);
        DeleteFont(ref _widgetBodyFont);
        DeleteFont(ref _widgetMetricFont);
        DeleteFont(ref _widgetTitleFont);
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
        RegisterWindowClass(
            NativeMethods.WidgetWindowClass,
            WidgetProcedure,
            0,
            NativeMethods.CsHRedraw | NativeMethods.CsVRedraw | NativeMethods.CsDropShadow);
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
        Tip = "Codex limits — left-click to show or hide",
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
            NativeMethods.AppendMenu(
                menu,
                NativeMethods.MfString | NativeMethods.MfDefault,
                MenuToggleWidget,
                _widgetVisible ? "Hide floating limits" : "Show floating limits");
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, MenuSettings, "Settings…");
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
            case MenuToggleWidget:
                ToggleWidgetVisibility();
                break;
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

    private void EnsureWidgetWindow()
    {
        if (_widgetWindow != 0)
        {
            return;
        }

        uint dpi = Math.Max(96, NativeMethods.GetDpiForSystem());
        WidgetSize logicalSize = WidgetLayout.GetLogicalSize(_widgetExpanded, _limits.Count);
        var size = new WidgetSize(Scale(logicalSize.Width, dpi), Scale(logicalSize.Height, dpi));
        WidgetPlacement? saved = WidgetPlacementStore.Load();
        NativeMethods.Point anchor;
        if (saved is WidgetPlacement placement)
        {
            anchor = new NativeMethods.Point { X = placement.X, Y = placement.Y };
        }
        else
        {
            NativeMethods.GetCursorPos(out anchor);
        }

        WidgetRectangle work = ToWidgetRectangle(GetMonitorInfo(anchor).Work);
        WidgetRectangle bounds = saved is WidgetPlacement savedPlacement
            ? WidgetLayout.PlaceSaved(savedPlacement.X, savedPlacement.Y, size, work)
            : WidgetLayout.PlaceAtBottomRight(size, work);

        _widgetWindow = CreateRequiredWindow(
            NativeMethods.WsExToolWindow | NativeMethods.WsExTopmost | NativeMethods.WsExLayered,
            NativeMethods.WidgetWindowClass,
            "Codex live limits",
            NativeMethods.WsPopup,
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            0,
            0);

        NativeMethods.SetLayeredWindowAttributes(_widgetWindow, 0, 238, NativeMethods.LwaAlpha);
        ApplyWidgetShape();
    }

    private void CreateWidgetControls(nint window)
    {
        uint dpi = Math.Max(96, NativeMethods.GetDpiForWindow(window));
        RecreateWidgetFonts();

        nint toggle = CreateControl(
            "BUTTON",
            _widgetExpanded ? "Collapse limits" : "Expand limits",
            NativeMethods.BsOwnerDraw | NativeMethods.WsTabStop,
            0,
            0,
            30,
            30,
            window,
            WidgetToggleControl,
            dpi);
        ApplyFont(toggle, _widgetBodyFont);
        LayoutWidgetButton();
    }

    private void RecreateWidgetFonts()
    {
        if (_widgetWindow == 0)
        {
            return;
        }

        DeleteFont(ref _widgetBodyFont);
        DeleteFont(ref _widgetMetricFont);
        DeleteFont(ref _widgetTitleFont);
        uint dpi = Math.Max(96, NativeMethods.GetDpiForWindow(_widgetWindow));
        _widgetTitleFont = CreateUiFont(9, 600, dpi, "Bahnschrift");
        _widgetMetricFont = CreateUiFont(20, 600, dpi, "Bahnschrift");
        _widgetBodyFont = CreateUiFont(8, 400, dpi, "Bahnschrift");
        nint toggle = NativeMethods.GetDlgItem(_widgetWindow, WidgetToggleControl);
        if (toggle != 0)
        {
            ApplyFont(toggle, _widgetBodyFont);
        }
    }

    private void ShowWidget()
    {
        EnsureWidgetWindow();
        _widgetVisible = true;
        ResizeWidget();
    }

    private void HideWidget()
    {
        if (_widgetWindow == 0)
        {
            return;
        }

        _widgetVisible = false;
        NativeMethods.ShowWindow(_widgetWindow, NativeMethods.SwHide);
        ActivityLog.Write("Floating limit widget hidden; tray monitoring remains active.");
    }

    private void ToggleWidgetVisibility()
    {
        if (_widgetVisible)
        {
            HideWidget();
        }
        else
        {
            ShowWidget();
        }
    }

    private void ToggleWidgetExpanded()
    {
        SetWidgetExpanded(!_widgetExpanded);
    }

    private void SetWidgetExpanded(bool expanded)
    {
        _widgetExpanded = expanded;
        NativeMethods.SetWindowText(
            NativeMethods.GetDlgItem(_widgetWindow, WidgetToggleControl),
            _widgetExpanded ? "Collapse limits" : "Expand limits");
        NativeMethods.InvalidateRect(NativeMethods.GetDlgItem(_widgetWindow, WidgetToggleControl), 0, true);
        ResizeWidget();
        ActivityLog.Write($"Floating limit widget {(_widgetExpanded ? "expanded" : "collapsed")}.");
    }

    private void ResizeWidget()
    {
        if (_widgetWindow == 0)
        {
            return;
        }

        uint dpi = Math.Max(96, NativeMethods.GetDpiForWindow(_widgetWindow));
        WidgetSize logicalSize = WidgetLayout.GetLogicalSize(_widgetExpanded, _limits.Count);
        var desired = new WidgetSize(Scale(logicalSize.Width, dpi), Scale(logicalSize.Height, dpi));
        if (!NativeMethods.GetWindowRect(_widgetWindow, out NativeMethods.Rect currentRect))
        {
            return;
        }

        var anchor = new NativeMethods.Point { X = currentRect.Left, Y = currentRect.Top };
        WidgetRectangle work = ToWidgetRectangle(GetMonitorInfo(anchor).Work);
        WidgetRectangle target = WidgetLayout.ResizeFromBottomRight(ToWidgetRectangle(currentRect), desired, work);
        uint flags = NativeMethods.SwpNoActivate | (_widgetVisible ? NativeMethods.SwpShowWindow : 0);
        NativeMethods.SetWindowPos(
            _widgetWindow,
            NativeMethods.HwndTopmost,
            target.Left,
            target.Top,
            target.Width,
            target.Height,
            flags);
        ApplyWidgetShape();
        LayoutWidgetButton();
        NativeMethods.InvalidateRect(_widgetWindow, 0, true);
    }

    private void LayoutWidgetButton()
    {
        if (_widgetWindow == 0 || !NativeMethods.GetClientRect(_widgetWindow, out NativeMethods.Rect client))
        {
            return;
        }

        uint dpi = Math.Max(96, NativeMethods.GetDpiForWindow(_widgetWindow));
        int width = Scale(30, dpi);
        int height = Scale(30, dpi);
        NativeMethods.SetWindowPos(
            NativeMethods.GetDlgItem(_widgetWindow, WidgetToggleControl),
            0,
            client.Right - width - Scale(8, dpi),
            Scale(8, dpi),
            width,
            height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);
    }

    private void PaintWidgetToggle(nint drawItemPointer)
    {
        if (drawItemPointer == 0)
        {
            return;
        }

        NativeMethods.DrawItemStruct item = Marshal.PtrToStructure<NativeMethods.DrawItemStruct>(drawItemPointer);
        if (item.ControlId != WidgetToggleControl)
        {
            return;
        }

        bool pressed = (item.ItemState & NativeMethods.OdsSelected) != 0;
        bool focused = (item.ItemState & NativeMethods.OdsFocus) != 0;
        bool hovered = (item.ItemState & NativeMethods.OdsHotLight) != 0;
        bool disabled = (item.ItemState & NativeMethods.OdsDisabled) != 0;
        uint fillColor = pressed ? WidgetAccentColor : hovered ? WidgetTrackColor : WidgetRaisedColor;
        uint borderColor = disabled ? WidgetMutedColor : focused ? WidgetTextColor : WidgetAccentColor;
        uint textColor = disabled ? WidgetMutedColor : pressed ? WidgetBackgroundColor : WidgetAccentColor;

        nint brush = NativeMethods.CreateSolidBrush(fillColor);
        nint pen = NativeMethods.CreatePen(NativeMethods.PsSolid, 1, borderColor);
        nint previousBrush = NativeMethods.SelectObject(item.DeviceContext, brush);
        nint previousPen = NativeMethods.SelectObject(item.DeviceContext, pen);
        NativeMethods.RoundRect(
            item.DeviceContext,
            item.ItemRectangle.Left,
            item.ItemRectangle.Top,
            item.ItemRectangle.Right,
            item.ItemRectangle.Bottom,
            8,
            8);
        nint previousFont = NativeMethods.SelectObject(item.DeviceContext, _widgetTitleFont);
        NativeMethods.SetBkMode(item.DeviceContext, NativeMethods.Transparent);
        NativeMethods.SetTextColor(item.DeviceContext, textColor);
        NativeMethods.Rect glyph = item.ItemRectangle;
        NativeMethods.DrawText(
            item.DeviceContext,
            _widgetExpanded ? "−" : "+",
            -1,
            ref glyph,
            NativeMethods.DtCenter | NativeMethods.DtVCenter | NativeMethods.DtSingleLine | NativeMethods.DtNoPrefix);
        NativeMethods.SelectObject(item.DeviceContext, previousFont);
        NativeMethods.SelectObject(item.DeviceContext, previousPen);
        NativeMethods.SelectObject(item.DeviceContext, previousBrush);
        NativeMethods.DeleteObject(pen);
        NativeMethods.DeleteObject(brush);
    }

    private void ApplyWidgetShape()
    {
        if (_widgetWindow == 0 || !NativeMethods.GetClientRect(_widgetWindow, out NativeMethods.Rect client))
        {
            return;
        }

        uint dpi = Math.Max(96, NativeMethods.GetDpiForWindow(_widgetWindow));
        int radius = Scale(10, dpi);
        nint region = NativeMethods.CreateRoundRectRgn(0, 0, client.Width + 1, client.Height + 1, radius, radius);
        if (region != 0 && NativeMethods.SetWindowRgn(_widgetWindow, region, true) == 0)
        {
            NativeMethods.DeleteObject(region);
        }
    }

    private void PaintWidget(nint window)
    {
        nint deviceContext = NativeMethods.BeginPaint(window, out NativeMethods.PaintStruct paint);
        try
        {
            NativeMethods.GetClientRect(window, out NativeMethods.Rect client);
            nint background = NativeMethods.CreateSolidBrush(WidgetBackgroundColor);
            NativeMethods.FillRect(deviceContext, ref client, background);
            NativeMethods.DeleteObject(background);
            NativeMethods.SetBkMode(deviceContext, NativeMethods.Transparent);

            uint dpi = Math.Max(96, NativeMethods.GetDpiForWindow(window));
            int margin = Scale(12, dpi);
            int buttonReserve = Scale(48, dpi);
            nint previous = NativeMethods.SelectObject(deviceContext, _widgetBodyFont);

            if (_widgetExpanded)
            {
                PaintExpandedWidget(deviceContext, client, margin, buttonReserve, dpi);
            }
            else
            {
                PaintCollapsedWidget(deviceContext, client, margin, buttonReserve, dpi);
            }

            NativeMethods.SelectObject(deviceContext, previous);
        }
        finally
        {
            NativeMethods.EndPaint(window, ref paint);
        }
    }

    private void PaintCollapsedWidget(nint deviceContext, NativeMethods.Rect client, int margin, int buttonReserve, uint dpi)
    {
        CodexRateLimitWindow? primary = SelectPrimaryLimit();
        var label = new NativeMethods.Rect
        {
            Left = margin,
            Top = Scale(4, dpi),
            Right = client.Right - buttonReserve,
            Bottom = Scale(25, dpi)
        };
        NativeMethods.SelectObject(deviceContext, _widgetTitleFont);
        NativeMethods.SetTextColor(deviceContext, WidgetMutedColor);
        NativeMethods.DrawText(
            deviceContext,
            primary is null
                ? "Codex limits"
                : $"{ShortWidgetName(primary)} / {primary.WindowLabel} · {FormatWidgetReset(primary)}",
            -1,
            ref label,
            NativeMethods.DtSingleLine | NativeMethods.DtVCenter | NativeMethods.DtEndEllipsis | NativeMethods.DtNoPrefix);

        var metric = new NativeMethods.Rect
        {
            Left = margin,
            Top = Scale(23, dpi),
            Right = client.Right - margin,
            Bottom = Scale(55, dpi)
        };
        NativeMethods.SelectObject(deviceContext, _widgetMetricFont);
        NativeMethods.SetTextColor(deviceContext, WidgetTextColor);
        NativeMethods.DrawText(
            deviceContext,
            primary is null ? "Connecting…" : $"{FormatPercentage(primary.RemainingPercent)}% left",
            -1,
            ref metric,
            NativeMethods.DtSingleLine | NativeMethods.DtVCenter | NativeMethods.DtNoPrefix);

        var detail = new NativeMethods.Rect
        {
            Left = margin,
            Top = Scale(54, dpi),
            Right = client.Right - margin,
            Bottom = Scale(76, dpi)
        };
        NativeMethods.SelectObject(deviceContext, _widgetBodyFont);
        NativeMethods.SetTextColor(deviceContext, WidgetMutedColor);
        NativeMethods.DrawText(
            deviceContext,
            primary is null
                ? "Waiting for the signed-in Codex session"
                : $"{FormatPercentage(primary.NormalizedUsedPercent)}% used · resets in {FormatResetCountdown(primary)}",
            -1,
            ref detail,
            NativeMethods.DtSingleLine | NativeMethods.DtVCenter | NativeMethods.DtEndEllipsis | NativeMethods.DtNoPrefix);

        PaintProgressBar(deviceContext, client, margin, client.Bottom - Scale(4, dpi), primary?.RemainingPercent ?? 0, dpi);
    }

    private void PaintExpandedWidget(nint deviceContext, NativeMethods.Rect client, int margin, int buttonReserve, uint dpi)
    {
        var heading = new NativeMethods.Rect
        {
            Left = margin,
            Top = Scale(5, dpi),
            Right = client.Right - buttonReserve,
            Bottom = Scale(33, dpi)
        };
        NativeMethods.SelectObject(deviceContext, _widgetTitleFont);
        NativeMethods.SetTextColor(deviceContext, WidgetTextColor);
        NativeMethods.DrawText(
            deviceContext,
            "Codex limits",
            -1,
            ref heading,
            NativeMethods.DtSingleLine | NativeMethods.DtVCenter | NativeMethods.DtNoPrefix);

        int rowsTop = Scale(38, dpi);
        int footerHeight = Scale(22, dpi);
        int available = Math.Max(1, client.Bottom - footerHeight - rowsTop);
        int rowCount = Math.Max(1, _limits.Count);
        int rowHeight = available / rowCount;

        if (_limits.Count == 0)
        {
            var waiting = new NativeMethods.Rect
            {
                Left = margin,
                Top = rowsTop,
                Right = client.Right - margin,
                Bottom = client.Bottom - footerHeight
            };
            NativeMethods.SelectObject(deviceContext, _widgetBodyFont);
            NativeMethods.SetTextColor(deviceContext, WidgetMutedColor);
            NativeMethods.DrawText(
                deviceContext,
                "Connecting to the signed-in Codex session…",
                -1,
                ref waiting,
                NativeMethods.DtVCenter | NativeMethods.DtWordBreak | NativeMethods.DtNoPrefix);
        }
        else
        {
            for (int index = 0; index < _limits.Count; index++)
            {
                CodexRateLimitWindow limit = _limits[index];
                int top = rowsTop + index * rowHeight;
                var name = new NativeMethods.Rect
                {
                    Left = margin,
                    Top = top,
                    Right = client.Right - margin,
                    Bottom = top + Scale(18, dpi)
                };
                NativeMethods.SelectObject(deviceContext, _widgetTitleFont);
                NativeMethods.SetTextColor(deviceContext, WidgetTextColor);
                NativeMethods.DrawText(
                    deviceContext,
                    $"{ShortWidgetName(limit)} / {limit.WindowLabel} · {FormatWidgetReset(limit)}",
                    -1,
                    ref name,
                    NativeMethods.DtSingleLine | NativeMethods.DtVCenter | NativeMethods.DtEndEllipsis | NativeMethods.DtNoPrefix);

                var values = new NativeMethods.Rect
                {
                    Left = margin,
                    Top = top + Scale(17, dpi),
                    Right = client.Right - margin,
                    Bottom = top + Scale(37, dpi)
                };
                NativeMethods.SelectObject(deviceContext, _widgetBodyFont);
                NativeMethods.SetTextColor(deviceContext, WidgetMutedColor);
                NativeMethods.DrawText(
                    deviceContext,
                    $"{FormatPercentage(limit.NormalizedUsedPercent)}% used · {FormatPercentage(limit.RemainingPercent)}% left · resets in {FormatResetCountdown(limit)}",
                    -1,
                    ref values,
                    NativeMethods.DtSingleLine | NativeMethods.DtVCenter | NativeMethods.DtEndEllipsis | NativeMethods.DtNoPrefix);

                PaintProgressBar(
                    deviceContext,
                    client,
                    margin,
                    Math.Min(top + Scale(42, dpi), client.Bottom - footerHeight - Scale(4, dpi)),
                    limit.RemainingPercent,
                    dpi);
            }
        }

        var footer = new NativeMethods.Rect
        {
            Left = margin,
            Top = client.Bottom - footerHeight,
            Right = client.Right - margin,
            Bottom = client.Bottom
        };
        string footerText = _lastSuccessfulRefresh is DateTime refreshed
            ? $"Live {refreshed:HH:mm} · auto 15 min"
            : "Refreshing automatically…";
        NativeMethods.SelectObject(deviceContext, _widgetBodyFont);
        NativeMethods.SetTextColor(deviceContext, WidgetAccentColor);
        NativeMethods.DrawText(
            deviceContext,
            footerText,
            -1,
            ref footer,
            NativeMethods.DtSingleLine | NativeMethods.DtVCenter | NativeMethods.DtEndEllipsis | NativeMethods.DtNoPrefix);
    }

    private static void PaintProgressBar(
        nint deviceContext,
        NativeMethods.Rect client,
        int margin,
        int top,
        double remainingPercent,
        uint dpi)
    {
        int height = Math.Max(Scale(3, dpi), 2);
        var track = new NativeMethods.Rect
        {
            Left = margin,
            Top = top,
            Right = client.Right - margin,
            Bottom = top + height
        };
        nint trackBrush = NativeMethods.CreateSolidBrush(WidgetTrackColor);
        NativeMethods.FillRect(deviceContext, ref track, trackBrush);
        NativeMethods.DeleteObject(trackBrush);
        int fillWidth = (int)Math.Round(track.Width * Math.Clamp(remainingPercent, 0, 100) / 100.0);
        if (fillWidth <= 0)
        {
            return;
        }

        var fill = track;
        fill.Right = fill.Left + fillWidth;
        uint accent = remainingPercent <= 25 ? WidgetDangerColor : WidgetAccentColor;
        nint fillBrush = NativeMethods.CreateSolidBrush(accent);
        NativeMethods.FillRect(deviceContext, ref fill, fillBrush);
        NativeMethods.DeleteObject(fillBrush);
    }

    private CodexRateLimitWindow? SelectPrimaryLimit() =>
        _limits.FirstOrDefault(limit => limit.LimitId.Equals("codex", StringComparison.OrdinalIgnoreCase) && limit.IsWeekly)
        ?? _limits.FirstOrDefault(limit => limit.IsWeekly)
        ?? _limits.FirstOrDefault();

    private static string ShortWidgetName(CodexRateLimitWindow limit) =>
        limit.LimitId.Equals("codex", StringComparison.OrdinalIgnoreCase)
            ? "General"
            : limit.DisplayName.Replace("GPT-5.3-Codex-", string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string FormatWidgetReset(CodexRateLimitWindow limit)
    {
        DateTime reset = limit.ResetsAt.ToLocalTime().DateTime;
        DateTime today = DateTime.Today;
        return reset.Date == today
            ? $"today {reset:HH:mm}"
            : reset.Date == today.AddDays(1)
                ? $"tomorrow {reset:HH:mm}"
                : $"{reset:d MMM HH:mm}";
    }

    private static string FormatResetCountdown(CodexRateLimitWindow limit) =>
        ResetCountdownFormatter.Format(limit.ResetsAt, DateTimeOffset.Now);

    private void TickResetCountdowns()
    {
        if (_widgetVisible && _widgetWindow != 0)
        {
            NativeMethods.InvalidateRect(_widgetWindow, 0, false);
        }

        if (_alertVisible && _alertWindow != 0)
        {
            NativeMethods.InvalidateRect(_alertWindow, 0, false);
        }

        DateTime now = DateTime.Now;
        if (_limits.Any(limit => limit.ResetsAt <= DateTimeOffset.Now) &&
            now - _lastCountdownRefreshRequest >= TimeSpan.FromMinutes(5))
        {
            _lastCountdownRefreshRequest = now;
            BeginRateLimitRefresh();
        }
    }

    private void SaveWidgetPlacement()
    {
        if (_widgetWindow != 0 && NativeMethods.GetWindowRect(_widgetWindow, out NativeMethods.Rect rectangle))
        {
            WidgetPlacementStore.Save(rectangle.Left, rectangle.Top);
        }
    }

    private static WidgetRectangle ToWidgetRectangle(NativeMethods.Rect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

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
            "No API key or reset-day setup. Allowance recoveries and weekly thresholds are detected automatically.",
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
            "Daily summary · weekly alerts at 50/75/90/95% · allowance recovery alerts");
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
                "Live from your signed-in Codex account. Each countdown uses Codex's authoritative next-reset time.",
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
            _lastSuccessfulRefresh = DateTime.Now;
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
                        "LIMIT RECOVERY DETECTED",
                        $"{alertEvent.Limit.DisplayName} recovered {FormatPercentage(alertEvent.RecoveredPercent)}%",
                        "Codex reports more capacity in this allowance. The next-reset time below is the authoritative server value.",
                        "allowance recovery alert");
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
        ResizeWidget();
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
                        $"{FormatPercentage(limit.NormalizedUsedPercent)}% USED   ·   {FormatPercentage(limit.RemainingPercent)}% LEFT   ·   NEXT RESET {reset:ddd d MMM HH:mm}   ·   IN {FormatResetCountdown(limit).ToUpperInvariant()}",
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
            case LaunchCommandKind.ShowWidgetExpanded:
                ShowWidget();
                SetWidgetExpanded(true);
                break;
            case LaunchCommandKind.ShowWidgetCollapsed:
                ShowWidget();
                SetWidgetExpanded(false);
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
                    app.ToggleWidgetVisibility();
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
                else if (wParam == ResetCountdownTimerId)
                {
                    app.TickResetCountdowns();
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

    private static nint WidgetWindowProcedure(nint window, uint message, nuint wParam, nint lParam)
    {
        TrayApplication? app = Current;
        if (app is null)
        {
            return NativeMethods.DefWindowProc(window, message, wParam, lParam);
        }

        switch (message)
        {
            case NativeMethods.WmCreate:
                app._widgetWindow = window;
                app.CreateWidgetControls(window);
                return 0;
            case NativeMethods.WmSize:
                app.ApplyWidgetShape();
                app.LayoutWidgetButton();
                NativeMethods.InvalidateRect(window, 0, true);
                return 0;
            case NativeMethods.WmPaint:
                app.PaintWidget(window);
                return 0;
            case NativeMethods.WmDrawItem:
                app.PaintWidgetToggle(lParam);
                return 1;
            case NativeMethods.WmEraseBackground:
                return 1;
            case NativeMethods.WmCommand:
                if ((int)(wParam & 0xFFFF) == WidgetToggleControl)
                {
                    app.ToggleWidgetExpanded();
                    return 0;
                }

                break;
            case NativeMethods.WmNcHitTest:
                nint hit = NativeMethods.DefWindowProc(window, message, wParam, lParam);
                return hit == NativeMethods.HtClient ? NativeMethods.HtCaption : hit;
            case NativeMethods.WmExitSizeMove:
                app.SaveWidgetPlacement();
                return 0;
            case NativeMethods.WmDisplayChange:
                app.ResizeWidget();
                return 0;
            case NativeMethods.WmDpiChanged:
                app.RecreateWidgetFonts();
                app.ResizeWidget();
                return 0;
            case NativeMethods.WmContextMenu:
                app.ShowTrayMenu();
                return 0;
            case NativeMethods.WmClose:
                app.HideWidget();
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
               $"{FormatPercentage(limit.RemainingPercent)}% left · next reset {reset:d MMM HH:mm} · " +
               $"in {FormatResetCountdown(limit)}";
    }

    private static string FormatLogLimit(CodexRateLimitWindow limit) =>
        $"{limit.DisplayName} {limit.WindowLabel}: {FormatPercentage(limit.NormalizedUsedPercent)}% used, " +
        $"{FormatPercentage(limit.RemainingPercent)}% left, reset {limit.ResetsAt.ToLocalTime():yyyy-MM-dd HH:mm}";

    private static int Scale(int value, uint dpi) => NativeMethods.MulDiv(value, (int)dpi, 96);

    private static nint CreateSegoeFont(int points, int weight, uint dpi) =>
        CreateUiFont(points, weight, dpi, "Segoe UI");

    private static nint CreateUiFont(int points, int weight, uint dpi, string faceName) => NativeMethods.CreateFont(
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
        faceName);

    private static uint Rgb(byte red, byte green, byte blue) =>
        red | ((uint)green << 8) | ((uint)blue << 16);

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
