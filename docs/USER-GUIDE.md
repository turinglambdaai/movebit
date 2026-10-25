# MoveBit user guide

**English** · [简体中文](USER-GUIDE.zh-CN.md)

Configure work-break and water reminders with local activity history.

## Availability

Free community application under MIT; no commercial activation or subscription.

## Platforms and installation

Desktop previews for macOS, Windows and Linux. Idle detection depends on the desktop session; unsupported Wayland sessions fall back to elapsed time.

Use [official Releases](https://github.com/turinglambdaai/movebit/releases), select an explicitly listed OS/architecture asset, read its release notes and check supplied checksums. An absent asset/platform is not a delivery promise. Preview builds may lack publisher signing or notarization; do not disable system-wide security protections to install them.

## Your first successful session

1. New profiles start **gentle**: toast reminders only, never a locked screen. A fullscreen break only ever starts after you explicitly opt in — picking Standard or Strict during onboarding, or the "Forced breaks" switch in settings.
2. Test pause, snooze and the break-screen controls before enabling stronger full-screen breaks.
3. Enable login startup only if you want background reminders. Review 7/30-day history as usage estimates, not health measurements.

## Break screens and how to leave them

- A fullscreen break covers **every connected display** with a countdown and unlocks automatically when it ends.
- The **emergency exit is always available**: press <kbd>Esc</kbd> or use the visible "Emergency exit" button. It restores usable windows on all displays immediately and does not depend on the reminder engine being reachable.
- The delayed ordinary **Skip** appears after the configured delay (20 seconds by default) and reschedules the sit reminder.
- Leaving a break never counts against you: an emergency exit or skip postpones the reminder instead of instantly re-locking. If the reminder engine is unreachable at the moment you exit, MoveBit still restores your screens first and finishes the cancellation quietly once the engine responds again.
- Completed breaks restart every reminder cycle, and the break itself is never counted as active work.
- Reduced Motion on macOS suppresses the decorative halo pulse; break screens expose their controls to screen readers through ordinary button labels.

## Data, backup and recovery

Quit MoveBit and back up config.json and history.json: ~/Library/Application Support/movebit on macOS, ~/.config/movebit on Linux, or %APPDATA%/movebit on Windows. History retains up to 370 days. Restore into a copy first.

## Network, keys and privacy

Reminder/accounting work is local. Update checks and release downloads contact external services. Activity history may reveal working habits; review it before sharing diagnostics.

## Known limits

Per-installation update recovery remains tracked in #48. The emergency exit ships on all three platforms; native runtime qualification of the full-screen controls on Windows and Linux follows their packaging legs. This tool provides reminders, not medical advice or a guaranteed way to prevent injury.

## Updates and removal

Back up data and read the destination release notes first. An in-app update check does not necessarily install an update for your channel; follow the manual for portable, installer and source paths. Never bypass a failed signature check or replace the program with an untrusted artifact. On failure, preserve data and the error, then recover with a trusted release. Removing the program does not necessarily erase its separate data directory; verify your backup before deleting personal data.

## Get help

Report problems in [GitHub Issues](https://github.com/turinglambdaai/movebit/issues), in English or Chinese. Include the version, OS/architecture, installation channel, steps, expected result and actual result. Reproduce with synthetic input first; do not attach keys, credentials, databases, private source documents or complete logs. Public support has no guaranteed response time.

## Further reading


- [Project overview and development](../README.md)
- [Current licence](../LICENSE)
