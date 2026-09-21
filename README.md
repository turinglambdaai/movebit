# MoveBit

[![CI](https://github.com/turinglambdaai/movebit/actions/workflows/ci.yml/badge.svg)](https://github.com/turinglambdaai/movebit/actions/workflows/ci.yml)
[![Release](https://github.com/turinglambdaai/movebit/actions/workflows/release.yml/badge.svg)](https://github.com/turinglambdaai/movebit/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Move a bit.** A cross-platform tray app that watches how long you actually work and forces you out of the chair — because another passive toast notification is exactly the thing you've learned to ignore.

[中文说明](README.zh-CN.md)

## Why

Sitting for hours is quietly wrecking you. The usual reminder tools pop a little bubble, you click it away without standing up, and the loop continues. MoveBit takes the commit-out-of-it approach seriously:

- **It measures active work time**, not wall-clock time. Leave your desk and the clock stops.
- **Sit reminders take over the whole screen** — every connected monitor gets a topmost break lock with a countdown. The "skip" button only appears after a delay (default 20 s), so skipping is a deliberate act, not a reflex.
- **You already took the break? It doesn't nag.** Idle beyond the away threshold (default 5 min) resets both cycles when you return.

## Features

- 🪟 **Tray-resident**, no window in your face at startup; left-click the tray icon for settings & stats
- ⏱️ **Work-time monitoring** via session-wide idle detection (keyboard/mouse anywhere, any app)
- 🚨 **Forced break**: full-screen, multi-monitor break lock with countdown; Alt+F4-proof; delayed skip button
- 💧 **Water reminders** stay as lightweight bottom-right toasts (no need to lock the screen for a sip)
- 📊 **Today stats**: active time, sit/water reminder counts, current cycle progress in the tray tooltip
- ⏸️ **Pause for 1 hour** from the tray (meetings, screen sharing)
- 🛠️ Everything configurable; settings persist to `config.json`

## Install

Grab a self-contained single-file build from [Releases](https://github.com/turinglambdaai/movebit/releases) — download your platform's zip, unpack, run. No .NET runtime required.

| Platform | Archive |
| --- | --- |
| Windows x64 | `MoveBit-windows-x64.zip` |
| macOS arm64 | `MoveBit-macos-arm64.zip` |
| Linux x64 | `MoveBit-linux-x64.zip` |

## How it works

Two independent cycles advance on a 30-second tick:

- **Sit cycle** accumulates only while you're active. At the interval (default 45 min) it fires — a full-screen break lock (default 5 min) if forced breaks are on, otherwise a toast.
- **Water cycle** runs on its own interval (default 30 min) as a toast.

Idle ≥ the away threshold (default 5 min) means you stepped away: both cycles freeze, and when you come back they restart from zero — the break already happened. Oversized clock jumps (system sleep) are clamped so you never get dogpiled by stale reminders after waking the machine.

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
- [ ] Daily/weekly stats history
- [ ] Autostart on login
- [ ] Micro-break mode (20 s every 10 min) alongside the long break

## License

[MIT](LICENSE)
