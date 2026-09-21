# MoveBit

A tray-resident health companion that watches how long you **actually work** and gets you out of the chair — sit reminders can cover **every monitor** with a countdown break screen, while water reminders stay lightweight. Built with **Avalonia 11** / .NET 10 for Windows, macOS, and Linux.

![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp&logoColor=white) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

**English** · [中文](README.zh-CN.md)

## Why

Most reminder tools lose the moment the notification becomes easy to dismiss. MoveBit is designed around a stronger contract:

- **Track active work instead of blindly counting wall-clock time.** Session-wide idle detection is available on Windows, macOS, and Linux/X11. Linux/Wayland currently falls back to elapsed time because there is no compositor-neutral global-idle API available to this app yet.
- **Make long breaks intentional.** Sit reminders can cover every connected display with a topmost countdown. The skip button appears only after a configurable delay (20 seconds by default).
- **Do not nag after a real break.** Being idle beyond the away threshold resets every reminder cycle when you return. Completing a forced break does the same, and the break itself is never counted as active work.

## v1.0 guarantees

MoveBit v1.0 treats the following as product contracts rather than best-effort behavior:

- a second launch activates the running instance instead of starting duplicate schedulers;
- forced-break time never inflates active-work statistics;
- queued water/micro reminders never pop over an active forced break;
- completed forced breaks restart sit, water, and micro-break cycles;
- settings and history are persisted with write-then-replace semantics;
- history is bounded to 370 days;
- release dependencies are pinned and release tags must match the project version;
- release archives ship with SHA-256 checksum files.

## Features

- 🪟 **Tray-resident** — no settings window forced in front of you at startup
- ⏱️ **Active-work monitoring** — Windows `GetLastInputInfo`, macOS CoreGraphics, Linux/X11 XScreenSaver
- 🚨 **Forced breaks** — full-screen, multi-monitor break cover with countdown; Alt+F4-resistant; delayed skip
- 👀 **Micro breaks** — short screen-center “stand up / look far away” nudges, no lock and no sound
- 💧 **Water reminders** — lightweight toasts on their own interval
- 📊 **Today + recent history** — live active time and reminder counts, a last-7-days chart, up to 370 days persisted
- 🌗 **System light/dark theme**
- 🔁 **Autostart on login** — Windows registry / macOS LaunchAgent / Linux XDG autostart
- ⏸️ **Pause for one hour** from the tray, with exact pause-expiry accounting
- 🧙 **First-run onboarding** — choose gentle / standard / strict behavior before forced breaks are enabled
- 🔒 **Single-instance activation** — relaunching surfaces the existing instance instead of duplicating it

## Install

Download the archive for your platform from [Releases](https://github.com/turinglambdaai/movebit/releases), verify the accompanying `.sha256` file if desired, unpack, and run. The builds are self-contained; no separate .NET runtime is required.

| Platform | Archive |
| --- | --- |
| Windows x64 | `MoveBit-windows-x64.zip` |
| macOS arm64 | `MoveBit-macos-arm64.zip` |
| Linux x64 | `MoveBit-linux-x64.zip` |

> GitHub release binaries are currently unsigned. Windows SmartScreen or macOS Gatekeeper may therefore show a warning on first launch. Code signing/notarization is a distribution improvement, not a runtime requirement.

## How it works

Three independent cycles advance on a 30-second scheduler tick:

- **Sit cycle** accumulates active work only. At the configured interval (45 minutes by default), it starts a full-screen break when forced breaks are enabled, otherwise a toast.
- **Water cycle** runs independently (30 minutes by default).
- **Micro-break cycle** runs independently (30 minutes / 20 seconds by default) and never replaces the longer sit-break cycle.

Idle time at or above the away threshold (5 minutes by default) freezes accumulation. On return, all cycles restart from zero. Oversized clock jumps such as system sleep are clamped so stale reminders are not dumped after wake.

A forced break is explicitly excluded from active-work time. When the countdown completes, all three cycles restart. If several reminders become due on the same scheduler tick, the forced sit break takes priority so water and micro UI cannot appear over it.

Daily stats are flushed to `history.json` every few minutes, archived across day boundaries, and pruned to the most recent 370 days.

## Configuration

Settings live in `%APPDATA%\movebit\config.json` on Windows or the platform application-data directory on macOS/Linux. All user-facing settings can be changed from the settings window:

| Setting | Default | Range |
| --- | --- | --- |
| `SitReminderMinutes` | 45 | 10–240 |
| `WaterReminderMinutes` | 30 | 5–180 |
| `AwayResetMinutes` | 5 | 1–60 |
| `ForceBreakEnabled` | true | — |
| `BreakDurationMinutes` | 5 | 1–30 |
| `SkipAfterSeconds` | 20 | 0–120 |
| `MicroBreakEnabled` | true | — |
| `MicroBreakIntervalMinutes` | 30 | 10–60 |
| `MicroBreakDurationSeconds` | 20 | 10–60 |
| `SoundEnabled` | true | — |

## Platform notes

- **Windows**: session-wide idle detection via `GetLastInputInfo`; reminder sound via `MessageBeep`.
- **macOS**: session-wide idle detection via CoreGraphics (`CGEventSourceSecondsSinceLastEventType`).
- **Linux/X11**: idle detection via the XScreenSaver extension (`XScreenSaverQueryInfo`).
- **Linux/Wayland**: MoveBit remains usable, but idle detection currently degrades to elapsed-time reminders. Native Wayland idle support remains on the roadmap.

The forced-break screen is an always-on-top window, not a global keyboard/mouse hook. That is deliberate: globally locking input can leave a machine unusable if the process crashes. MoveBit makes dismissal deliberate without taking control of input devices.

## Build from source

```bash
git clone https://github.com/turinglambdaai/movebit.git
cd movebit
dotnet restore MoveBit.slnx
dotnet build MoveBit.slnx -c Release --no-restore
dotnet test MoveBit.Tests/MoveBit.Tests.csproj -c Release --no-build
```

Requires the .NET 10 SDK.

To publish a Windows x64 build locally:

```bash
dotnet publish MoveBit.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -o publish
```

## Release process

1. Keep `<Version>` in `MoveBit.csproj` and the release tag identical (for example `1.0.0` ↔ `v1.0.0`).
2. Merge only with the three-platform CI matrix green.
3. Push the version tag.
4. The release workflow runs tests, builds all supported archives, generates SHA-256 files, then creates a single GitHub Release after every package succeeds.

## Roadmap

- [ ] Native idle detection for Linux/Wayland sessions
- [ ] Signed/notarized distribution packages and native installers
- [ ] Weekly / monthly views beyond the last-7-days chart
- [ ] Work-pattern insights (sedentary streaks, longest session)

## License

[MIT](LICENSE)
