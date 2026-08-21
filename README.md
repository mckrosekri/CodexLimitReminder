# Codex Limit Reminder

A tiny, private Windows tray app that reminds you on the final two mornings of your weekly Codex limit cycle.

- Normally visible only as a system-tray icon; no background taskbar window.
- Full-screen, topmost reminder with a keyboard-accessible **Close reminder** button.
- Prominently shows the live weekly balance, for example **7% used · 93% left**.
- Two alerts per cycle: day 6 (two calendar days before reset) and day 7 (one calendar day before reset).
- Reads the exact weekly reset and current usage from your signed-in local Codex installation.
- Only one setup choice: the morning notification time.
- Refreshes hourly, retries automatically after connection problems, and keeps the last known reset offline.
- Native single-file x64 executable with no installer framework or runtime bundle.
- Stores only the notification time, reminder deduplication key, and last Codex status in the current user's registry.

## Install

### From a release

1. Download the latest `CodexLimitReminder-win-x64.zip` from [Releases](https://github.com/mckrosekri/CodexLimitReminder/releases).
2. Extract the ZIP.
3. Right-click `install.ps1`, choose **Run with PowerShell**, and follow the first-run settings window.

The installer copies the app to `%LOCALAPPDATA%\Programs\CodexLimitReminder`, creates a current-user startup entry, and opens the settings window. The startup target is the GUI-subsystem executable itself, so Windows does not flash a console.

You can also run `CodexLimitReminder.exe` directly without installing it. The installer enables quiet startup with Windows; saving from a portable copy does the same.

## One-time setup

1. Make sure the Codex CLI is installed and signed in.
2. Open **Codex Limit Reminder settings** from the tray icon.
3. Choose your preferred notification time and select **Save time**.

That is the entire setup. The app launches Codex's local App Server without a console window and reads `account/rateLimits/read`. It selects the main `codex` seven-day window, including the exact reset timestamp and current usage percentage. It never guesses a future reset by adding seven days; after a reset it waits for Codex to expose the next exact cycle.

The schedule is date-based:

| Reminder | When it appears |
| --- | --- |
| Day 6 | Reminder time on the date two days before reset |
| Day 7 | Reminder time on the date one day before reset |

Each reminder is recorded before it appears, so restarting the app cannot duplicate that day's alert. If the PC wakes or the app starts later on the same reminder date, the alert still appears once. It does not backfill a reminder from a previous date.

## Tray controls

- Left-click the tray icon to open settings.
- Right-click for **Settings**, **Refresh Codex status**, both test reminders, or **Exit**.
- The full-screen reminder closes through the button, `Enter`, `Escape`, or `Alt+F4`; the tray app keeps running.

## Privacy and security

Codex Limit Reminder does not ask for or store your password, session token, or API key. It does not automate a browser, collect telemetry, or run its own cloud service. It starts the installed `codex.exe app-server` locally over hidden standard input/output and makes the read-only `account/rateLimits/read` request; Codex itself uses your existing sign-in.

Local settings are stored under:

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

The test suite covers App Server response parsing, correct selection of the main Codex weekly bucket, day-6/day-7 selection from the exact reset timestamp, duplicate suppression, missed-day behavior, and safe startup-command quoting.

## License

[MIT](LICENSE)
