# MoveBit

A tray-resident health companion that watches how long you **actually work** and forces you out of the chair — sit reminders lock **every monitor** with a countdown break screen, water reminders stay as toasts. Built with **Avalonia 11** / .NET 10. Cross-platform (Windows / macOS / Linux).
![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp&logoColor=white) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

**English** · [中文](README.zh-CN.md)

## Why

Sitting for hours is quietly wrecking you. The usual reminder tools pop a little bubble, you click it away without standing up, and the loop continues. MoveBit takes the commit-out-of-it approach seriously:

- **It measures active work time**, not wall-clock time. Leave your desk and the clock stops.
- **Sit reminders take over the whole screen** — every connected monitor gets a topmost break lock with a countdown. The "skip" button only appears after a delay (default 20 s), so skipping is a deliberate act, not a reflex.
- **You already took the break? It doesn't nag.** Idle beyond the away threshold (default 5 min) resets both cycles when you return.

## Features

- 🪟 **Tray-resident**, no window in your face at startup; left-click the tray icon for settings & stats
- ⏱️ **Work-time monitoring** via session-wide idle detection (keyboard/mouse anywhere, any app)
- 🚨 **Forced break**: full-screen, multi-monitor break lock with countdown ring; Alt+F4-proof; delayed skip button
- 👀 **Micro breaks**: a screen-center "stand up, look far away" nudge every 30 min (default), 20 s, no lock, no sound — the evidence-friendly layer between long breaks
- 💧 **Water reminders** as lightweight bottom-right toasts (no need to lock the screen for a sip)
- 📊 **Today stats + last-7-days chart**: active time, reminder counts, per-day bars; history persists across restarts
- 🌗 **Light/dark theme** following the system: paper-warm for the workday, warm-dark for night use
- 🔁 **Autostart on login** (Windows registry / macOS LaunchAgent / Linux XDG autostart), one toggle
- ⏸️ **Pause for 1 hour** from the tray (meetings, screen sharing)
- 🧙 **Droplet persona**: first-run onboarding picks a strictness pact (gentle / standard / strict-evidence-backed); rotating in-first-person copy; milestone cheers
- 🛠️ Everything configurable; settings persist to `config.json`

## Install

Grab a self-contained single-file build from [Releases](https://github.com/turinglambdaai/movebit/releases) — download your platform's zip, unpack, run. No .NET runtime required.

| Platform | Archive |
| --- | --- |
| Windows x64 | `MoveBit-windows-x64.zip` |
| macOS arm64 | `MoveBit-macos-arm64.zip` |
| Linux x64 | `MoveBit-linux-x64.zip` |

## How it works

Two independent cycles plus a light third layer advance on a 30-second tick:

- **Sit cycle** accumulates only while you're active. At the interval (default 45 min) it fires — a full-screen break lock (default 5 min) if forced breaks are on, otherwise a toast.
- **Water cycle** runs on its own interval (default 30 min) as a toast.
- **Micro break cycle** fires every 30 min as a screen-center nudge (default 20 s): stand, look far away. It doesn't reset the sit cycle — the evidence suggests many small breaks plus occasional longer ones.

Idle ≥ the away threshold (default 5 min) means you stepped away: all cycles freeze, and when you come back they restart from zero — the break already happened. Oversized clock jumps (system sleep) are clamped so you never get dogpiled by stale reminders after waking the machine. Daily stats are flushed to `history.json` every few minutes and archived at midnight.

## Configuration

Settings live in `%APPDATA%\movebit\config.json` (Windows) or `~/.config/movebit/config.json` (macOS/Linux), and everything is editable in the settings window:

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

- **Windows**: full functionality — idle detection via `GetLastInputInfo`, reminder sound via `MessageBeep`.
- **macOS / Linux**: fully usable; idle detection is not implemented yet, so reminders run on natural time (builds are produced, but not regularly tested on real hardware).

The break lock is an always-on-top window, not a keyboard/mouse hook — by design. Locking input globally is risky territory (a crashed hook leaves the machine unusable); a screen-covering lock plus a delayed skip button gets you off the chair without that risk.

## Build from source

```bash
git clone https://github.com/turinglambdaai/movebit.git
cd movebit
dotnet run        # run from source
dotnet test       # scheduler unit tests
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o publish
```

Requires the .NET 10 SDK.

## Roadmap

- [ ] macOS/Linux idle detection (CGEventSource / XScreenSaver)
- [ ] Weekly / monthly history views beyond the 7-day chart
- [ ] Work-hours pattern insights (sedentary streaks, longest session)

## License

[MIT](LICENSE)
