# Codex Limit Reminder

A tiny, private floating Windows app that monitors every usage-limit clock exposed by your signed-in Codex installation.

- Ultra-compact 260×84 dark telemetry card with restrained neon-cyan status accents.
- Semi-transparent and always on top, with no taskbar window or focus stealing.
- Collapsed view shows the main weekly allowance, remaining percentage, exact reset time, and a live countdown.
- Select **+** to expand every General and model-specific clock; select **−** to return to the compact view.
- Expanded limits indent locally observed usage groups beneath their matching clock, including the observed time and estimated release countdown.
- Drag the card anywhere on the desktop. Its position is remembered across restarts.
- Full-screen, topmost alerts with a keyboard-accessible **Close reminder** button.
- One daily summary at your chosen time, showing every live General and model-specific limit.
- Immediate weekly safety alerts at 50%, 75%, 90%, and 95% used.
- Automatic alerts when a weekly allowance materially recovers.
- Tracks General Codex and separate model clocks such as GPT-5.3-Codex-Spark's five-hour and weekly limits, with an independent countdown for every exposed bucket.
- Checks Codex every 15 minutes, retries automatically after connection problems, and keeps the last live state offline.
- Starts quietly at Windows sign-in through two independent per-user triggers and repairs both whenever it launches.
- Native single-file x64 executable; no installer framework, bundled browser, cloud service, or separate runtime.

## What Codex actually exposes

Codex exposes one current usage percentage, window duration, and authoritative next-reset timestamp for each quota bucket. It does not expose a separate expiry for every message or token cohort. Codex also exposes daily token-activity totals, but those totals cannot be mapped safely to a particular quota bucket or release time.

The app always displays the live countdown to each server-reported reset. It also records each increase observed during its 15-minute checks as a local estimated group. These indented rows use `≈`, show when the increase was first seen, and estimate when it should be released from the bucket's reported window. Usage that predates tracking is shown as a baseline due by the server reset. These estimates never replace or override the authoritative reset.

## Alerts

| Alert | When it appears |
| --- | --- |
| Daily summary | Once each local day at the configured time; if Windows starts late, it appears once later that day |
| Weekly threshold | As soon as a 15-minute check first observes 50%, 75%, 90%, or 95% used |
| Allowance recovery | As soon as a check observes a major usage drop, or a reset advances with a near-full recovery |

Every full-screen alert lists all current clocks with used percentage, remaining percentage, exact reset, and live time remaining. Five-hour clocks appear in the daily summary but do not trigger the weekly percentage warnings.

## Install

### From a release

1. Download the latest `CodexLimitReminder-win-x64.zip` from [Releases](https://github.com/mckrosekri/CodexLimitReminder/releases).
2. Extract the ZIP.
3. Right-click `install.ps1`, choose **Run with PowerShell**, and follow the first-run settings window.

The installer copies the app to `%LOCALAPPDATA%\Programs\CodexLimitReminder`, creates current-user startup triggers, and opens settings. The app repairs both startup triggers on every primary launch: an HKCU Run value and a Startup-folder `wscript.exe` wrapper. Both start the GUI-subsystem executable without a console flash.

You can also run `CodexLimitReminder.exe` directly without installing it. Launching a portable copy once enables quiet Windows startup automatically.

## One-time setup

1. Make sure the Codex CLI or desktop app is installed and signed in.
2. Open **Codex Limit Reminder settings** from the tray icon.
3. Choose the daily summary time and select **Save time**.

That is the only setup. The app launches Codex's local App Server without a console window and makes the read-only `account/rateLimits/read` request. It discovers every returned bucket and window automatically.

## Floating widget and tray controls

- Select **+** or **−** on the floating card to change its size.
- Drag the card background to move it; right-click it for the tray menu.
- Left-click the tray icon to show or hide the floating card.
- Right-click for **Show/Hide floating limits**, **Settings**, **Refresh Codex status**, **Test daily limit summary**, or **Exit**.
- The full-screen alert closes through the button, `Enter`, `Escape`, or `Alt+F4`; the tray app keeps running.

## Privacy and local state

Codex Limit Reminder does not ask for or store your password, session token, or API key. It does not automate a browser, collect telemetry, or run its own cloud service. Codex itself uses your existing local sign-in.

Settings and deduplication state are stored under:

```text
HKEY_CURRENT_USER\Software\CodexLimitReminder
HKEY_CURRENT_USER\Software\CodexLimitReminder\LimitWindows
HKEY_CURRENT_USER\Software\CodexLimitReminder\EstimatedUsageGroups
```

Quiet startup uses:

```text
HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run\CodexLimitReminder
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\CodexLimitReminder.vbs
```

The small rotating diagnostic log is `%LOCALAPPDATA%\CodexLimitReminder\activity.log`. It records startup repair, successful or failed Codex checks, live limit values, scheduled daily summaries, and alert display/close events. It contains no tokens or message content.

## Uninstall

Run `uninstall.ps1`. Add `-KeepSettings` if you want to preserve the schedule and monitoring state for a later reinstall.

## Build and test

Requirements: Windows 10/11 x64 and the .NET 10 SDK with NativeAOT prerequisites.

```powershell
.\scripts\build.ps1
```

The script builds the solution, runs all parser/scheduler/limit-state/startup tests, and publishes the native executable to `artifacts\win-x64`.

Manual test hooks:

```powershell
.\artifacts\win-x64\CodexLimitReminder.exe --show-settings
.\artifacts\win-x64\CodexLimitReminder.exe --show-widget-expanded
.\artifacts\win-x64\CodexLimitReminder.exe --show-widget-collapsed
.\artifacts\win-x64\CodexLimitReminder.exe --test-summary
```

## Design notes

This is a direct Win32 C# application compiled with NativeAOT. That keeps the distribution self-contained and small while retaining native controls, UI Automation names, high-contrast system colors, per-monitor DPI awareness, semi-transparent layered rendering, and a GUI-only startup path.

## License

[MIT](LICENSE)
