# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.3] - 2026-09-21

### Fixed

- Single instance enforcement: launching a second copy now exits immediately
  instead of stacking a second tray icon, a second scheduler (double reminders,
  double break overlays) and a second history writer that clobbers the first
  one's data. The second launch pokes the running instance over a loopback
  channel to surface its window — re-launching feels like "bring it up", not
  nothing. The break lock suppresses the poke: no window fights the overlay.

[0.2.3]: https://github.com/turinglambdaai/movebit/releases/tag/v0.2.3

## [0.2.2] - 2026-09-21

### Fixed

- Onboarding choice cards showed no selection state at all: the style selector
  used `Border.choice:selected` (pseudo-class syntax) instead of
  `Border.choice.selected` (class syntax), so the selected card never
  highlighted and the choices looked unclickable — clicks did register, the
  feedback just never rendered
- Choice cards now also show a subtle hover state for affordance

[0.2.2]: https://github.com/turinglambdaai/movebit/releases/tag/v0.2.2

## [0.2.1] - 2026-09-21

### Fixed

- Micro-break interval/duration number boxes were too narrow (95 DIP) to show
  two-digit values — widened to match the other settings rows
- Week card header: "本周活跃 …" total overlapped the "最近 7 天" label (missing
  `Grid.Column` assignment); now sits on the right as intended

[0.2.1]: https://github.com/turinglambdaai/movebit/releases/tag/v0.2.1

## [0.2.0] - 2026-09-21

### Added

- **Activity history**: daily stats persist to `history.json` (flushed every ~5 min,
  archived at midnight) and render as a last-7-days bar chart in the main window —
  the "monitor my work time" promise now survives restarts
- **Micro breaks**: third reminder layer — a screen-center "stand up, look far away"
  nudge every 30 min (default 20 s, no lock, no sound, Esc/click to dismiss);
  independent cycle that resets on away, never blocks the long-break cycle
- **Autostart on login**: one toggle in settings (Windows HKCU Run key / macOS
  LaunchAgent / Linux XDG autostart)
- **Light/dark theme** following the system: paper-warm palette for the day,
  warm-dark for night; all windows migrated to theme resources
- Onboarding gains micro-break defaults; strictness tiers unchanged

### Changed

- Scheduler: third micro cycle, `DayCompleted` archive event, refactored `Snooze`
- Main window: today card gains micro-break count, new micro-break settings block,
  autostart toggle; window grows to fit the week chart

[0.2.0]: https://github.com/turinglambdaai/movebit/releases/tag/v0.2.0

## [0.1.4] - 2026-09-21

### Added

- Real four-step onboarding replaces the welcome card: meet the droplet → pick a
  strictness pact (gentle toast-only / standard / strict-research-backed, the
  30-min tier is labeled as closest to the evidence) → water cadence and sound →
  summary of the pact with live values
- Choices write through to the config immediately; skipping applies clean defaults;
  closing the window counts as seen so onboarding never nags on every launch

[0.1.4]: https://github.com/turinglambdaai/movebit/releases/tag/v0.1.4

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
