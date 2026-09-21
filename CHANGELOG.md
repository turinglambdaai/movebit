# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.3] - 2026-09-22

### Added

- Production Windows code-signing release gate using Azure Artifact Signing and GitHub OIDC; no private signing key or client secret is stored in the repository
- Authenticode verification for both the published `MoveBit.exe` payload and the final Windows Setup executable before checksums or release publication
- Unit coverage for forced-break countdown display rounding, progress clamping, and shared ring/countdown calculations

### Changed

- Bumped MoveBit to 1.0.3
- Every connected display now renders the complete forced-break experience instead of hiding the numeric countdown and guidance on secondary monitors
- The break progress indicator now uses Avalonia's native `Arc.SweepAngle` driven by the exact same remaining-time value as the numeric countdown
- Break completion and skip availability are presented consistently on every display
- Displayed countdown seconds round up until the break actually reaches zero, preventing a premature `0:00`
- Windows public releases now fail closed when production signing configuration is missing or any Authenticode signature is invalid

### Fixed

- Secondary monitors could show only the circular progress indicator while the primary monitor showed the actual countdown
- The old `StrokeDashArray`-based ring could visually reach the end before the numeric countdown finished
- Break completion visuals could differ between primary and secondary displays

[1.0.3]: https://github.com/turinglambdaai/movebit/releases/tag/v1.0.3

## [1.0.2] - 2026-09-21

### Added

- Windows x64 per-user installer (`MoveBit-Setup-windows-x64.exe`) with Start-menu launch, standard uninstall registration, and an optional desktop shortcut
- Installer SHA-256 sidecar published next to the portable release archives
- Windows CI installer smoke test that compiles the setup, performs a silent install into a temporary directory, verifies `MoveBit.exe`, then performs a silent uninstall
- Installed Windows copies now refresh their Apps & Features display version after MoveBit's own in-app updater replaces the executable
- Explicit settings persistence feedback (`✓ 已保存 · HH:mm:ss`) and clear failure feedback when a value is active in memory but cannot be written to disk

### Changed

- Bumped MoveBit to 1.0.2
- Windows distribution is now installer-first for normal users while `MoveBit-windows-x64.zip` remains available as a portable build
- The installer uses `%LOCALAPPDATA%\Programs\MoveBit`, avoiding administrator elevation and keeping the directory writable by MoveBit's existing in-app updater
- Windows installer and portable copies intentionally share the same verified ZIP-based updater rather than introducing a second Windows-only update framework
- Reminder/water/micro-break interval spinners now use practical 5-minute/5-second increments where appropriate; short durations retain 1-minute precision
- Settings UI explicitly tells users that numeric values can be typed directly and are saved immediately
- Release automation now waits for both the portable matrix and Windows installer before publishing a GitHub Release

[1.0.2]: https://github.com/turinglambdaai/movebit/releases/tag/v1.0.2

## [1.0.1] - 2026-09-21

### Added

- Cross-platform in-app online update checks for Windows x64, macOS arm64, and Linux x64
- Settings/About update panel with current version, update status, manual check, and explicit **Update & Restart** action
- Tray-menu update entry that changes into an install action when a newer release is available
- Automatic update checks after startup and roughly every six hours, enabled by default and configurable by the user
- SHA-256 verification of downloaded release archives before any files are replaced
- Staging + helper-process update application so the running executable never overwrites itself directly
- Backup/rollback of replaced application files if the update apply step fails
- Unit tests for release-version comparison and checksum tamper detection

### Changed

- Bumped MoveBit to 1.0.1
- Online updates reuse the same GitHub Release ZIP and `.sha256` artifacts already produced by the release pipeline, avoiding a second distribution channel
- Updates are intentionally user-approved: MoveBit may discover a release automatically, but it never silently installs one in the background

[1.0.1]: https://github.com/turinglambdaai/movebit/releases/tag/v1.0.1

## [1.0.0] - 2026-09-21

### Added

- Native session-wide idle detection on macOS through CoreGraphics
- Native idle detection on Linux/X11 through the XScreenSaver extension, with a safe elapsed-time fallback for Wayland/unsupported sessions
- SHA-256 checksum files for every release archive
- Config-store unit tests covering roundtrip persistence, clamping, and corrupt JSON recovery
- Scheduler coverage for precise pause expiry and forced-break time semantics

### Changed

- Promoted the application version to 1.0.0 and pinned Avalonia/test dependencies for reproducible restores
- Forced breaks are now first-class scheduler breaks: time under the overlay is excluded from active-work statistics and a completed break restarts sit, water, and micro cycles
- Release automation now verifies tag/version consistency, runs tests first, builds artifacts independently, and creates the GitHub Release exactly once after all packages succeed
- CI now has explicit restore/build/test stages, read-only permissions, and stale-run cancellation
- Autostart generation now escapes macOS plist and Linux desktop-entry executable paths and handles permission failures without crashing the UI
- Configuration writes now use same-directory write-then-replace persistence

### Fixed

- Water or micro-break UI could appear over a forced break when multiple cycles became due on the same scheduler tick
- Skipping a forced break incorrectly played the "break completed" goodbye animation
- Forced-break duration could be counted as active work after the next scheduler tick
- Pause expiry between timer ticks could count paused time as active work
- History claimed a 370-day retention policy but never actually pruned old records
- `ReminderConfig.Clone()` omitted micro-break and onboarding state
- Release matrix jobs could race while updating the same GitHub Release body

[1.0.0]: https://github.com/turinglambdaai/movebit/releases/tag/v1.0.0

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
