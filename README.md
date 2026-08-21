# Codex Limit Reminder

A tiny, private Windows tray app that reminds you on the final two mornings of your weekly Codex limit cycle.

- Normally visible only as a system-tray icon; no background taskbar window.
- Full-screen, topmost reminder with a keyboard-accessible **Close reminder** button.
- Two alerts per cycle: day 6 (two calendar days before reset) and day 7 (one calendar day before reset).
- One blocked native message loop and one one-shot timer: no polling and no background network traffic.
- Single 1.23 MB x64 executable in the initial release.
- Stores settings only in the current user's Windows registry.

## Install

### From a release

1. Download the latest `CodexLimitReminder-win-x64.zip` from [Releases](https://github.com/mckrosekri/CodexLimitReminder/releases).
2. Extract the ZIP.
3. Right-click `install.ps1`, choose **Run with PowerShell**, and follow the first-run settings window.

The installer copies the app to `%LOCALAPPDATA%\Programs\CodexLimitReminder`, creates a current-user startup entry, and opens the settings window. The startup target is the GUI-subsystem executable itself, so Windows does not flash a console.

You can also run `CodexLimitReminder.exe` directly without installing it. Settings can enable or disable quiet startup with Windows.

## Configure the weekly reset

1. In Codex, open **Settings → Usage** and note the weekly reset day and local time shown for your account.
2. Open **Codex Limit Reminder settings** from the tray icon.
3. Enter that weekly reset day/time and your preferred morning alert time.
4. Select **Save**.

The app repeats the configured reset every seven days. If Codex changes your reset time, update it in the tray settings.

The schedule is date-based:

| Reminder | When it appears |
| --- | --- |
| Day 6 | Reminder time on the date two days before reset |
| Day 7 | Reminder time on the date one day before reset |

Each reminder is recorded before it appears, so restarting the app cannot duplicate that day's alert. If the PC wakes or the app starts later on the same reminder date, the alert still appears once. It does not backfill a reminder from a previous date.

## Tray controls

- Left-click the tray icon to open settings.
- Right-click for **Settings**, **Test day 6 reminder**, **Test day 7 reminder**, or **Exit**.
- The full-screen reminder closes through the button, `Enter`, `Escape`, or `Alt+F4`; the tray app keeps running.

## Privacy and security

Codex Limit Reminder does not sign in to Codex, read browser or Codex data, call an OpenAI API, collect telemetry, or use the network. It stores six small values under:

```text
HKEY_CURRENT_USER\Software\CodexLimitReminder
```

Quiet startup uses:

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run\CodexLimitReminder
```

## Uninstall

Run `uninstall.ps1`. Add `-KeepSettings` if you want to preserve the weekly schedule for a later reinstall.

## Build and test

Requirements: Windows 10/11 x64 and the .NET 10 SDK with NativeAOT prerequisites.

```powershell
.\scripts\build.ps1
```

The script builds the solution, runs all schedule/startup tests, and publishes the single native executable to `artifacts\win-x64`.

Manual test hooks:

```powershell
.\artifacts\win-x64\CodexLimitReminder.exe --show-settings
.\artifacts\win-x64\CodexLimitReminder.exe --test-day-6
.\artifacts\win-x64\CodexLimitReminder.exe --test-day-7
```

## Design notes

This is a direct Win32 C# application compiled with NativeAOT. That choice keeps the distribution self-contained and much smaller than a WinUI runtime bundle while retaining native controls, UI Automation names, high-contrast system colors, per-monitor DPI awareness, and a GUI-only startup path.

The test suite covers reset rollover, day-6/day-7 selection, duplicate suppression, missed-day behavior, reminder ordering, and safe startup-command quoting.

## License

[MIT](LICENSE)
