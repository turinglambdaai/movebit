# MoveBit

A tray-resident health companion that watches how long you **actually work** and gets you out of the chair — sit reminders can cover **every monitor** with a countdown break screen, while water reminders stay lightweight. Built with **Avalonia 11** / .NET 10 for Windows, macOS, and Linux.

![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp&logoColor=white) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

**English** · [中文](README.zh-CN.md)

## Why

Most reminder tools lose the moment the notification becomes easy to dismiss. MoveBit is designed around a stronger contract:

- **Track active work instead of blindly counting wall-clock time.** Session-wide idle detection is available on Windows, macOS, Linux/X11, and mainstream Wayland sessions. Wayland uses GNOME/Mutter IdleMonitor or the freedesktop ScreenSaver session-bus API when the desktop exposes one; otherwise MoveBit deliberately falls back to elapsed time instead of inventing idle data.
- **Make long breaks intentional.** Sit reminders can cover every connected display with a topmost countdown. The skip button appears only after a configurable delay (20 seconds by default).
- **Do not nag after a real break.** Being idle beyond the away threshold resets every reminder cycle when you return. Completing a forced break does the same, and the break itself is never counted as active work.
- **Show patterns, not just today's number.** The main window can switch between 7-day and 30-day activity history and tracks continuous-work streaks, recent longest sessions, active-day averages, and the busiest recent day.
- **Keep installed copies current where the installation is user-writable.** MoveBit can check GitHub Releases in the background, verify SHA-256, replace the current portable/per-user installation, and restart only after you explicitly choose **Update & Restart**.
- **Feel native when you want it.** Windows gets a normal installer, macOS gets an `.app` and DMG, Debian/Ubuntu users get a `.deb`, and portable ZIPs remain available for every supported platform.

## v1 product guarantees

MoveBit v1 treats the following as product contracts rather than best-effort behavior:

- a second launch activates the running instance instead of starting duplicate schedulers;
- forced-break time never inflates active-work statistics;
- queued water/micro reminders never pop over an active forced break;
- completed forced breaks restart sit, water, and micro-break cycles;
- settings and history are persisted with write-then-replace semantics;
- history is bounded to 370 days and older history files remain readable as insight fields evolve;
- release tags must match the project version;
- release archives/installers ship with SHA-256 checksum files;
- online updates verify the downloaded archive against the published SHA-256 sidecar before replacing files;
- update installation is staged and applied by a helper process after MoveBit exits, with rollback of overwritten files if replacement fails.

## Features

- 🪟 **Tray-resident** — no settings window forced in front of you at startup
- ⏱️ **Active-work monitoring** — Windows `GetLastInputInfo`, macOS CoreGraphics, Linux/X11 XScreenSaver, Wayland session-bus backends where available
- 🚨 **Forced breaks** — full-screen, multi-monitor break cover with countdown; Alt+F4-resistant; delayed skip
- 👀 **Micro breaks** — short screen-center “stand up / look far away” nudges, no lock and no sound
- 💧 **Water reminders** — lightweight toasts on their own interval
- 📊 **7 / 30-day history** — switchable activity chart backed by up to 370 days of persisted history
- 🧠 **Work-pattern insights** — current continuous session, today's/recent longest session, active-day average, busiest recent day, sedentary-streak guidance
- 🌗 **System light/dark theme**
- 🔁 **Autostart on login** — Windows registry / macOS LaunchAgent / Linux XDG autostart
- ⏸️ **Pause for one hour** from the tray, with exact pause-expiry accounting
- 🧙 **First-run onboarding** — choose gentle / standard / strict behavior before forced breaks are enabled
- 🔒 **Single-instance activation** — relaunching surfaces the existing instance instead of duplicating it
- ⬆️ **Verified online updates** — automatic checks plus explicit check/update controls for writable installations
- 📦 **Native + portable distribution** — Windows Setup, macOS `.app`/DMG, Debian `.deb`, plus portable ZIPs

## Install

### Windows x64 — installer recommended

Download **`MoveBit-Setup-windows-x64.exe`** from [Releases](https://github.com/turinglambdaai/movebit/releases). The installer recommends `%LOCALAPPDATA%\Programs\MoveBit`, which requires no administrator permission, but the destination page remains available so you can choose another writable folder. Setup creates a Start-menu shortcut, registers a standard uninstall entry, and offers an optional desktop shortcut.

Once installed, launch MoveBit from the Start menu like a normal desktop app. A user-writable installation can use MoveBit's verified in-app updater.

### macOS arm64 — app / DMG

Download **`MoveBit-macos-arm64.dmg`**, open it, and drag **MoveBit.app** to Applications (or another folder). `MoveBit-macos-arm64.app.zip` is also provided when you want the native app bundle without a disk image.

The native macOS build is currently **unsigned and not notarized**, so Gatekeeper may require an explicit first-launch approval. Signing/notarization is intentionally deferred because it requires paid Apple developer credentials.

If MoveBit.app is placed in a system-owned/non-writable location such as `/Applications`, install future native-package updates by replacing the app with the newer DMG. The portable ZIP remains the best choice when you specifically want MoveBit's self-update replacement flow in a user-writable directory.

### Debian / Ubuntu x64 — `.deb`

Download **`MoveBit-linux-x64.deb`** and install it with your package manager, for example:

```bash
sudo apt install ./MoveBit-linux-x64.deb
```

The package installs the application under `/opt/movebit`, adds `/usr/bin/movebit`, a desktop-menu entry, and the MoveBit icon. Because `/opt` is package-manager owned, upgrade `.deb` installations by installing the newer `.deb`; MoveBit does not try to overwrite package-manager-owned files as a normal user.

### Portable builds

Portable archives remain available:

| Platform | Portable archive |
| --- | --- |
| Windows x64 | `MoveBit-windows-x64.zip` |
| macOS arm64 | `MoveBit-macos-arm64.zip` |
| Linux x64 | `MoveBit-linux-x64.zip` |

The packaged builds are self-contained; no separate .NET runtime is required. Every published package has a matching `.sha256` file.

> Release binaries are currently unsigned. Windows SmartScreen or macOS Gatekeeper may therefore show a warning on first launch. Paid code signing/notarization is the remaining distribution roadmap item.

## Online updates

MoveBit 1.0.1+ checks the latest GitHub Release shortly after startup and then roughly every six hours when **Automatically check for updates** is enabled. It never silently installs a release.

For a writable portable/per-user installation:

1. Settings and the tray change to show **Update & Restart**.
2. MoveBit downloads the matching platform ZIP and its `.sha256` sidecar.
3. The archive is SHA-256 verified before extraction.
4. Files are extracted into a temporary staging directory.
5. MoveBit saves configuration/history and exits.
6. A helper replaces the application files, restores overwritten files if replacement fails, and starts MoveBit again.

Configuration and history live in the user application-data directory, outside the application folder, so updates do not replace user data. Package-manager/system-owned native installs may be read-only to the app; use the newer DMG or `.deb` for those installs.

## How it works

Three independent cycles advance on a 30-second scheduler tick:

- **Sit cycle** accumulates active work only. At the configured interval (45 minutes by default), it starts a full-screen break when forced breaks are enabled, otherwise a toast.
- **Water cycle** runs independently (30 minutes by default).
- **Micro-break cycle** runs independently (30 minutes / 20 seconds by default) and never replaces the longer sit-break cycle.

Idle time at or above the away threshold (5 minutes by default) freezes accumulation. On return, all cycles restart from zero. Oversized clock jumps such as system sleep are clamped so stale reminders are not dumped after wake.

A forced break is explicitly excluded from active-work time. When the countdown completes, all three cycles and the continuous-work session restart. Choosing “remind me later” on a toast or skipping a forced break schedules the same reminder after the configurable snooze delay (10 active minutes by default).

Daily stats are flushed to `history.json` every few minutes, archived across day boundaries, and pruned to the most recent 370 days. Longest continuous-work duration is persisted alongside the existing daily counters while older four-field history files remain compatible.

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

Numeric time fields accept direct keyboard entry and save immediately. Common longer intervals use practical five-minute/five-second spinner steps, while short durations keep one-minute precision.

## Platform notes

- **Windows**: session-wide idle detection via `GetLastInputInfo`; recommended per-user installer plus portable ZIP.
- **macOS**: session-wide idle detection via CoreGraphics; native `.app`/DMG plus portable arm64 ZIP.
- **Linux/X11**: idle detection via the XScreenSaver extension (`XScreenSaverQueryInfo`).
- **Linux/Wayland**: MoveBit first queries GNOME/Mutter `org.gnome.Mutter.IdleMonitor`, then the freedesktop ScreenSaver session-bus idle API. Desktops exposing neither return unknown idle time, so MoveBit safely falls back to elapsed-time reminders rather than guessing.

The forced-break screen is an always-on-top window, not a global keyboard/mouse hook. That is deliberate: globally locking input can leave a machine unusable if the process crashes.

## Build from source

```bash
git clone https://github.com/turinglambdaai/movebit.git
cd movebit
dotnet restore MoveBit.slnx
dotnet build MoveBit.slnx -c Release --no-restore
dotnet test MoveBit.Tests/MoveBit.Tests.csproj -c Release --no-build
```

Requires the .NET 10 SDK. Native package scripts live under `packaging/`:

```bash
# macOS runner
bash packaging/macos/build-native.sh 1.1.0

# Debian/Ubuntu runner
bash packaging/linux/build-deb.sh 1.1.0
```

## Release process

1. Keep `<Version>` in `MoveBit.csproj` and the release tag identical (for example `1.1.0` ↔ `v1.1.0`).
2. Merge only with the Windows/macOS/Linux build-test matrix green; CI additionally smoke-tests Windows Setup, mounts/verifies the DMG, and installs/removes the Debian package.
3. Push the version tag.
4. Release automation builds portable ZIPs, Windows Setup, macOS `.app.zip` + DMG, Linux `.deb`, and SHA-256 sidecars; one GitHub Release is created only after every required package succeeds.
5. Writable MoveBit 1.0.1+ installations discover the new release through GitHub's latest-release API and can apply the matching verified portable archive in-app.

## Roadmap

Completed without requiring paid credentials:

- [x] Native/mainstream idle detection for Linux/Wayland sessions, with safe fallback where no reliable desktop API exists
- [x] Native macOS `.app` / DMG distribution and Debian/Ubuntu `.deb` distribution
- [x] Weekly / monthly activity-history views
- [x] Work-pattern insights (continuous-work streaks, longest session, recent averages and busiest day)

Deferred paid distribution work:

- [ ] Windows code signing and macOS signing/notarization

## License

[MIT](LICENSE)
