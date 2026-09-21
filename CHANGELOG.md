# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-09-21

### Added

- Active work-time monitoring via session-wide idle detection (Windows: `GetLastInputInfo`)
- Sit reminder cycle (default 45 min) with full-screen multi-monitor forced break lock:
  countdown, Alt+F4-proof, delayed skip button (default 20 s)
- Water reminder cycle (default 30 min) as lightweight toasts
- Away detection: idle ≥ 5 min freezes both cycles and resets them on return
- Today stats (active time, reminder counts) in the settings window and tray tooltip
- Tray menu: settings, pause 1 hour, exit; close-to-tray behavior
- JSON-persisted settings with clamping and corrupt-file recovery
- Scheduler unit tests (8 cases: cycles, away reset, pause, snooze, day rollover, sleep clamp)
- CI (build + test on Windows/macOS/Linux) and self-contained release workflow

[0.1.0]: https://github.com/turinglambdaai/movebit/releases/tag/v0.1.0
