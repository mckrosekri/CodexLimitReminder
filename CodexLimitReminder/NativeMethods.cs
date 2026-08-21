using System.Runtime.InteropServices;
using System.Text;

namespace CodexLimitReminder;

internal static class NativeMethods
{
    internal const string MessageWindowClass = "CodexLimitReminder.MessageWindow";
    internal const string SettingsWindowClass = "CodexLimitReminder.SettingsWindow";
    internal const string AlertWindowClass = "CodexLimitReminder.AlertWindow";

    internal static readonly nint HwndMessage = new(-3);
    internal static readonly nint HwndTopmost = new(-1);

    internal const uint WmDestroy = 0x0002;
    internal const uint WmClose = 0x0010;
    internal const uint WmPaint = 0x000F;
    internal const uint WmCommand = 0x0111;
    internal const uint WmTimer = 0x0113;
    internal const uint WmTimeChange = 0x001E;
    internal const uint WmSize = 0x0005;
    internal const uint WmKeyDown = 0x0100;
    internal const uint WmSetFont = 0x0030;
    internal const uint WmContextMenu = 0x007B;
    internal const uint WmLButtonUp = 0x0202;
    internal const uint WmApp = 0x8000;
    internal const uint WmTrayIcon = WmApp + 1;
    internal const uint WmExternalCommand = WmApp + 2;

    internal const uint NinSelect = 0x0400;
    internal const uint VkEscape = 0x1B;

    internal const uint WsOverlapped = 0x00000000;
    internal const uint WsPopup = 0x80000000;
    internal const uint WsChild = 0x40000000;
    internal const uint WsVisible = 0x10000000;
    internal const uint WsCaption = 0x00C00000;
    internal const uint WsSysMenu = 0x00080000;
    internal const uint WsMinimizeBox = 0x00020000;
    internal const uint WsTabStop = 0x00010000;
    internal const uint WsVScroll = 0x00200000;
    internal const uint WsBorder = 0x00800000;

    internal const uint WsExToolWindow = 0x00000080;
    internal const uint WsExTopmost = 0x00000008;
    internal const uint WsExClientEdge = 0x00000200;

    internal const uint CsHRedraw = 0x0002;
    internal const uint CsVRedraw = 0x0001;

    internal const int SwHide = 0;
    internal const int SwShow = 5;
    internal const int SwRestore = 9;

    internal const uint SwpShowWindow = 0x0040;
    internal const uint SwpNoActivate = 0x0010;

    internal const uint NifMessage = 0x00000001;
    internal const uint NifIcon = 0x00000002;
    internal const uint NifTip = 0x00000004;
    internal const uint NifShowTip = 0x00000080;
    internal const uint NimAdd = 0x00000000;
    internal const uint NimDelete = 0x00000002;
    internal const uint NimSetVersion = 0x00000004;
    internal const uint NotifyIconVersion4 = 4;

    internal const uint MfString = 0x00000000;
    internal const uint MfSeparator = 0x00000800;
    internal const uint MfDefault = 0x00001000;
    internal const uint TpmRightButton = 0x0002;
    internal const uint TpmReturnCmd = 0x0100;
    internal const uint TpmNoNotify = 0x0080;

    internal const uint BsPushButton = 0x00000000;
    internal const uint BsDefPushButton = 0x00000001;
    internal const uint BsAutoCheckBox = 0x00000003;
    internal const uint EsAutoHScroll = 0x0080;
    internal const uint CbsDropDownList = 0x0003;
    internal const uint SsLeft = 0x00000000;

    internal const uint CbAddString = 0x0143;
    internal const uint CbSetCurSel = 0x014E;
    internal const uint CbGetCurSel = 0x0147;
    internal const uint BmGetCheck = 0x00F0;
    internal const uint BmSetCheck = 0x00F1;
    internal const nuint BstChecked = 1;

    internal const uint MbIconError = 0x00000010;
    internal const uint MbIconInformation = 0x00000040;

    internal const int ColorWindow = 5;
    internal const int ColorWindowText = 8;
    internal const int ColorButtonFace = 15;
    internal const int Transparent = 1;

    internal const uint DtCenter = 0x00000001;
    internal const uint DtVCenter = 0x00000004;
    internal const uint DtSingleLine = 0x00000020;
    internal const uint DtWordBreak = 0x00000010;

    internal const int MonitorDefaultToNearest = 2;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClassEx
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProcedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint BackgroundBrush;
        internal string? MenuName;
        internal string ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal int Width => Right - Left;
        internal int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        internal nint Window;
        internal uint Value;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal Point Cursor;
        internal uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct PaintStruct
    {
        internal nint DeviceContext;
        internal int Erase;
        internal Rect Paint;
        internal int Restore;
        internal int IncUpdate;
        internal fixed byte Reserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        internal uint Size;
        internal nint Window;
        internal uint Id;
        internal uint Flags;
        internal uint CallbackMessage;
        internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string Tip;
        internal uint State;
        internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        internal string Info;
        internal uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid GuidItem;
        internal nint BalloonIcon;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    internal static extern int MulDiv(int number, int numerator, int denominator);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern ushort RegisterClassEx(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string? windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    internal static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern int GetMessage(out Message message, nint window, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(ref Message message);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    internal static extern nint DispatchMessage(ref Message message);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int exitCode);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint RegisterWindowMessage(string value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern nint SetFocus(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowText(nint window, string value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, StringBuilder value, int maxCount);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint window, out Rect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsChild(nint parent, nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsDialogMessage(nint dialog, ref Message message);

    [DllImport("user32.dll")]
    internal static extern nint GetDlgItem(nint parent, int id);

    [DllImport("user32.dll")]
    internal static extern nuint SetTimer(nint window, nuint id, uint milliseconds, nint timerProcedure);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool KillTimer(nint window, nuint id);

    [DllImport("user32.dll")]
    internal static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("user32.dll")]
    internal static extern nint LoadCursor(nint instance, nint cursorName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);

    [DllImport("user32.dll")]
    internal static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint window, nint rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    internal static extern nint SendMessageString(nint window, uint message, nuint wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(nint window, string text, string caption, uint type);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool InvalidateRect(nint window, nint rectangle, bool erase);

    [DllImport("user32.dll")]
    internal static extern nint BeginPaint(nint window, out PaintStruct paint);

    [DllImport("user32.dll")]
    internal static extern int FillRect(nint deviceContext, ref Rect rectangle, nint brush);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EndPaint(nint window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    internal static extern nint GetSysColorBrush(int index);

    [DllImport("user32.dll")]
    internal static extern uint GetSysColor(int index);

    [DllImport("gdi32.dll")]
    internal static extern uint SetTextColor(nint deviceContext, uint color);

    [DllImport("gdi32.dll")]
    internal static extern int SetBkMode(nint deviceContext, int mode);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint deviceContext, nint value);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint value);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint CreateFont(
        int height,
        int width,
        int escapement,
        int orientation,
        int weight,
        uint italic,
        uint underline,
        uint strikeOut,
        uint charSet,
        uint outputPrecision,
        uint clipPrecision,
        uint quality,
        uint pitchAndFamily,
        string faceName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int DrawText(nint deviceContext, string text, int count, ref Rect rectangle, uint format);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForSystem();
}
