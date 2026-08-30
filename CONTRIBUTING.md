# Contributing

Issues and pull requests are welcome.

1. Keep the app lightweight, offline, dependency-free, and x64 Windows compatible.
2. Add or update a schedule test for behavior changes.
3. Run `scripts\build.ps1` before opening a pull request.
4. For UI changes, manually verify the collapsed and expanded floating widget, dragging, tray show/hide, settings, the all-limit daily summary, and keyboard close.

Please keep new background work event-driven. Polling beyond the existing local Codex limit check, analytics, unrelated network calls, and console-subsystem startup launchers are out of scope.
