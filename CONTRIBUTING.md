# Contributing

Issues and pull requests are welcome.

1. Keep the app tray-first, offline, dependency-free, and x64 Windows compatible.
2. Add or update a schedule test for behavior changes.
3. Run `scripts\build.ps1` before opening a pull request.
4. For UI changes, manually verify settings, the all-limit daily summary, keyboard close, and the return to tray-only state.

Please keep new background work event-driven. Polling beyond the existing local Codex limit check, analytics, unrelated network calls, and console-subsystem startup launchers are out of scope.
