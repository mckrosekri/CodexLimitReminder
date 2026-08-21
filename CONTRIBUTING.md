# Contributing

Issues and pull requests are welcome.

1. Keep the app tray-first, offline, dependency-free, and x64 Windows compatible.
2. Add or update a schedule test for behavior changes.
3. Run `scripts\build.ps1` before opening a pull request.
4. For UI changes, manually verify settings, day 6, day 7, keyboard close, and the return to tray-only state.

Please keep new background work event-driven. Recurring polling, analytics, network calls, and console-subsystem startup launchers are out of scope.
