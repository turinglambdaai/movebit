# MoveBit

A tray-resident health companion that watches how long you **actually work** and gets you out of the chair — sit reminders can cover **every monitor** with a countdown break screen, while water reminders stay lightweight. Built with **Avalonia 11** / .NET 10 for Windows, macOS, and Linux.

![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp&logoColor=white) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

**English** · [中文](README.zh-CN.md)

## Why

Most reminder tools lose the moment the notification becomes easy to dismiss. MoveBit is designed around a stronger contract:

- **Track active work instead of blindly counting wall-clock time.** Session-wide idle detection is available on Windows, macOS, and Linux/X11. Linux/Wayland currently falls back to elapsed time because there is no compositor-neutral global-idle API available to this app yet.
- **Make long breaks intentional.** Sit reminders can cover every connected display with a topmost countdown. The skip button appears only after a configurable delay (20 seconds by default).
- **Do not nag after a real break.** Being idle beyond the away threshold resets every reminder cycle when you return. Completing a forced break does the same, and the break itself is never counted as active work.
- **Keep installed copies current.** MoveBit can check GitHub Releases in the background, tell you when an update exists, then download, verify, replace, and restart only after you explicitly choose **Update & Restart**.
- **Feel like an installed desktop app when you want one.** Windows users can use a per-user installer with Start-menu launch and normal uninstall support, while portable ZIPs remain available.

## v1 product guarantees

MoveBit v1 treats the following as product contracts rather than best-effort behavior:

- a second launch activates the running instance instead of starting duplicate schedulers;
- forced-break time never inflates active-work statistics;
- queued water/micro reminders never pop over an active forced break;
- completed forced breaks restart sit, water, and micro-break cycles;
- settings and history are persisted with write-then-replace semantics;
- history is bounded to 370 days;
- release dependencies are pinned and release tags must match the project version;
- release archives and the Windows installer ship with SHA-256 checksum files;
- online updates verify the downloaded archive against the published SHA-256 sidecar before replacing files;
- update installation is staged and applied by a helper process after MoveBit exits, with rollback of overwritten files if replacement fails.

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
- ⬆️ **Online updates** — automatic checks plus explicit check/update controls in Settings and the tray; Windows x64, macOS arm64, and Linux x64 use the same verified GitHub Release assets
- 📦 **Installer + portable distribution** — Windows gets a normal per-user installer; portable ZIPs remain available for every supported platform

## Install

### Windows x64 — installer recommended

Download **`MoveBit-Setup-windows-x64.exe`** from [Releases](https://github.com/turinglambdaai/movebit/releases). The installer recommends `%LOCALAPPDATA%\Programs\MoveBit`, which requires no administrator permission, but the destination page remains available so you can choose another writable folder. Setup creates a Start-menu shortcut, registers a standard uninstall entry, and offers an optional desktop shortcut.

Once installed, launch MoveBit from the Start menu like a normal desktop app. Future versions continue to use MoveBit's own verified in-app updater; you do not need to download a new installer for each release.

### Portable builds

Portable archives remain available for users who do not want an installation:

| Platform | Portable archive |
| --- | --- |
| Windows x64 | `MoveBit-windows-x64.zip` |
| macOS arm64 | `MoveBit-macos-arm64.zip` |
| Linux x64 | `MoveBit-linux-x64.zip` |

The packaged builds are self-contained; no separate .NET runtime is required. Each installer/archive has a matching `.sha256` file in the GitHub Release.

> GitHub release binaries are currently unsigned. Windows SmartScreen or macOS Gatekeeper may therefore show a warning on first launch. Code signing/notarization remains the next major distribution improvement.

## Online updates

MoveBit 1.0.1+ checks the latest GitHub Release shortly after startup and then roughly every six hours when **Automatically check for updates** is enabled. It never silently installs a release.

When a newer version is available:

1. Settings and the tray change to show **Update & Restart**.
2. MoveBit downloads the matching platform ZIP and its `.sha256` sidecar.
3. The archive is SHA-256 verified before extraction.
4. Files are extracted into a temporary staging directory.
5. MoveBit saves configuration/history and exits.
6. A small platform helper replaces the application files, restores overwritten files if replacement fails, and starts MoveBit again.

The Windows installer and Windows portable build deliberately use the same updater after first launch. The installer establishes the selected user-writable location, Start-menu shortcut and uninstall registration; it does not introduce a second update framework. After an in-app update, installed Windows copies also refresh their Apps & Features display version on the next start.

Configuration and history live in the user application-data directory, outside the application folder, so an application update does not replace user data.

Automatic apply currently supports the same architectures shipped by the release workflow: **Windows x64, macOS arm64, and Linux x64**. If a portable application folder is not writable, MoveBit leaves the current version untouched and reports that the directory must be moved to a writable location. The recommended Windows installer location is per-user and writable by design; custom installer locations should likewise be writable by the current user so in-app updates can replace application files.

## How it works

Three independent cycles advance on a 30-second scheduler tick:

- **Sit cycle** accumulates active work only. At the configured interval (45 minutes by default), it starts a full-screen break when forced breaks are enabled, otherwise a toast.
- **Water cycle** runs independently (30 minutes by default).
- **Micro-break cycle** runs independently (30 minutes / 20 seconds by default) and never replaces the longer sit-break cycle.

Idle time at or above the away threshold (5 minutes by default) freezes accumulation. On return, all cycles restart from zero. Oversized clock jumps such as system sleep are clamped so stale reminders are not dumped after wake.

A forced break is explicitly excluded from active-work time. When the countdown completes, all three cycles restart. If several reminders become due on the same scheduler tick, the forced sit break takes priority so water and micro UI cannot appear over it.

Choosing “remind me later” on a toast or skipping a forced break schedules the same reminder after the configurable snooze delay (10 active minutes by default).

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
| `SnoozeMinutes` | 10 | 5–60 |
| `MicroBreakEnabled` | true | — |
| `MicroBreakIntervalMinutes` | 30 | 10–60 |
| `MicroBreakDurationSeconds` | 20 | 10–60 |
| `SoundEnabled` | true | — |
| `AutoCheckUpdates` | true | — |

Numeric time fields accept direct keyboard entry and save immediately. Common longer intervals use practical five-minute/five-second spinner steps, while short durations keep one-minute precision. The settings card shows explicit saved/failed persistence feedback after each change.

## Platform notes

- **Windows**: session-wide idle detection via `GetLastInputInfo`; reminder sound via `MessageBeep`; recommended per-user installer plus portable ZIP; online update uses `MoveBit-windows-x64.zip` for both distribution modes.
- **macOS**: session-wide idle detection via CoreGraphics (`CGEventSourceSecondsSinceLastEventType`); online update uses the arm64 release archive.
- **Linux/X11**: idle detection via the XScreenSaver extension (`XScreenSaverQueryInfo`); online update supports Linux x64.
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

The Windows setup is defined in `installer/windows/MoveBit.iss` and is compiled with Inno Setup by CI/release automation.

## Release process

1. Keep `<Version>` in `MoveBit.csproj` and the release tag identical (for example `1.0.4` ↔ `v1.0.4`).
2. Merge only with the three-platform CI matrix green; Windows CI additionally compiles, silently installs, and silently uninstalls the setup package.
3. Push the version tag.
4. The release workflow runs tests, builds all supported portable archives and their SHA-256 files, builds the Windows per-user installer, smoke-tests the final installer, creates its checksum, then creates one GitHub Release after every package succeeds.
5. Existing MoveBit 1.0.1+ copies discover the new release through GitHub's latest-release API and can apply the matching verified archive in-app.

## Roadmap

- [ ] Native idle detection for Linux/Wayland sessions
- [ ] Windows code signing and macOS signing/notarization
- [ ] Native macOS `.app` / DMG distribution and optional Linux installer formats
- [ ] Weekly / monthly views beyond the last-7-days chart
- [ ] Work-pattern insights (sedentary streaks, longest session)

## License

[MIT](LICENSE)
