# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.3] - 2026-09-21

### Added

- First-run welcome: on a fresh install the main window opens with a greeting card
  from the droplet that sets expectations (screen-lock behavior, skip delay, away
  reset, close-to-tray) with the live settings values baked in — a tool that locks
  every screen in 45 minutes owes the user an explanation first

### Changed

- Welcome is seen-once: dismissing the card or closing the window marks it read;
  it never reappears on later launches

[0.1.3]: https://github.com/turinglambdaai/movebit/releases/tag/v0.1.3

## [0.1.2] - 2026-09-21

### Added

- Droplet persona: all copy rewritten in the water-drop's first-person voice
- Countdown ring on the break screen: warm arc drains from 12 o'clock, breathing halo
- Droplet goodbye animation when a break completes ("回去工作吧，我随叫随到")
- Milestone cheers on the 3rd / 5th / 8th completed sit break of the day

### Changed

- Forced break lock is now silent: the screen takeover is its own notification —
  a beep plus a full-screen lock is exactly the office embarrassment that gets
  health tools uninstalled; toast reminders still honor the sound toggle
- Removed the "send a test reminder" button — a user-facing product, not a test build
- Sound setting now labeled "提示音（办公室可关）"

[0.1.2]: https://github.com/turinglambdaai/movebit/releases/tag/v0.1.2

## [0.1.1] - 2026-09-21

### Added

- Fun copy pools: randomized break hints (rotated every 25 s during the break) and
  randomized water/sit toast lines — same-message fatigue is the enemy of attention
- Color-coded notification toasts: orange accent bar for sit, blue for water
- Sit cycle progress bar and live status dot in the main window

### Changed

- Visual redesign: warm paper palette for the main window (cream canvas, ink text,
  terracotta accents), night-sky gradient break screen with a breathing countdown
- Break screen typography: oversized countdown, calmer hint layout, quieter skip button

[0.1.1]: https://github.com/turinglambdaai/movebit/releases/tag/v0.1.1

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
