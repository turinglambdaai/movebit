# MoveBit

> **Rivet rebuild (this branch):** `umovebit` is being rebuilt on [Rivet](https://github.com/turinglambdaai/rivet) — one Racket domain core driving first-party native hosts over typed RPC (see AGENTS.md). The stack described below is the archived `main` line, kept as the behavior/visual reference.

A tray-resident health companion that watches how long you **actually work** and gets you out of the chair — sit reminders can cover **every monitor** with a countdown break screen, while water reminders stay lightweight. Built with **Avalonia 11** / .NET 10 for Windows, macOS, and Linux.

![C#](https://img.shields.io/badge/C%23-512BD4?logo=csharp&logoColor=white) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

**English** · [中文](README.zh-CN.md)

## Why

Most reminder tools lose the moment the notification becomes easy to dismiss. MoveBit is designed around a stronger contract:

- **Track active work instead of blindly counting wall-clock time.** Session-wide idle detection is available on Windows, macOS, Linux/X11, and mainstream Wayland sessions. Wayland uses GNOME/Mutter IdleMonitor or the freedesktop ScreenSaver session-bus API when the desktop exposes one; otherwise MoveBit deliberately falls back to elapsed time instead of inventing idle data.
- **Make long breaks intentional.** Sit reminders can cover every connected display with a topmost countdown. The skip button appears only after a configurable delay (20 seconds by default).
- **Do not nag after a real break.** Being idle beyond the away threshold resets every reminder cycle when you return. Completing a forced break does the same, and the break itself is never counted as active work.
- **Show patterns, not just today's number.** The main window can switch between 7-day and 30-day activity history and tracks continuous-work streaks, recent longest sessions, active-day averages, and the busiest recent day.
- **Keep installed copies current where the installation is user-writable.** MoveBit's update feed serves portable archives from GitHub Releases with SHA-256 sidecars, and the C# line checks it in the background, verifying SHA-256, replacing the current portable/per-user installation, and restarting only after you explicitly choose **Update & Restart**.
- **Ship native installers and portable archives for every supported desktop.** Windows gets an MSI plus a portable ZIP, macOS gets a DMG plus a portable ZIP on both Apple silicon and Intel, and Linux gets a portable tar.gz.

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
- 📦 **Native + portable distribution** — Windows MSI, macOS DMG, portable ZIPs on Windows and macOS, Linux tar.gz

## Install

Grab your build from [Releases](https://github.com/turinglambdaai/movebit/releases/latest):

| Platform | Portable archive (update feed) | Installer |
| --- | --- | --- |
| macOS Apple silicon | `movebit-<version>-macos-arm64.zip` | `movebit-<version>-macos-arm64.dmg` |
| macOS Intel | `movebit-<version>-macos-x64.zip` | `movebit-<version>-macos-x64.dmg` |
| Windows x64 | `movebit-<version>-windows-x64.zip` | `movebit-<version>-windows-x64.msi` |
| Windows ARM64 | runs the x64 build via Windows on ARM's x64 emulation | — |
| Linux x64 | `movebit-<version>-linux-x64.tar.gz` | — |

Assets follow one lowercase scheme, `movebit-<version>-<os>-<arch>.<ext>` (for example `movebit-1.6.0-macos-arm64.dmg`). Every asset has a matching `.sha256` sidecar, and each release publishes a `SHA256SUMS` manifest plus the Ed25519-signed `update-manifest.json` that forms the update feed.

Platform notes:

- **Windows** — run the MSI for a normal installed app, or unpack the portable ZIP anywhere you like. On ARM64 Windows, use the x64 assets: Windows on ARM runs them through its x64 emulation, and no native ARM64 build is published.
- **macOS** — open the DMG and drag **MoveBit.app** to Applications (or another folder); the portable ZIP is the same app bundle without the disk image. Builds are ad-hoc signed, not notarized, so Gatekeeper may require an explicit first-launch approval (right-click → Open, or `xattr -cr /Applications/MoveBit.app`). Notarization is deferred until paid Apple developer credentials are available.
- **Linux** — unpack the tar.gz and run the host inside; GTK 4 and its system libraries are the only runtime dependencies, everything else is bundled.

> Release binaries carry no publisher code signature. Windows SmartScreen or macOS Gatekeeper may therefore show a warning on first launch. Paid code signing/notarization is the remaining distribution roadmap item.

## Online updates

The update feed is the release's portable archive for your platform plus its `.sha256` sidecar, discovered through GitHub's latest-release API and matched by the published asset naming. Downloads are always verified before anything is replaced.

For the C# line (≤ 1.4.x), the full in-app flow exists: MoveBit checks shortly after startup and roughly every six hours when **Automatically check for updates** is enabled, downloads the matching portable archive and its `.sha256` sidecar, verifies the checksum, stages the files, and — only after you explicitly choose **Update & Restart** — exits, lets a helper replace the application files with rollback on failure, and starts again. Configuration and history live in the user application-data directory, outside the application folder, so updates never touch user data. Installs in system-owned locations (such as `/Applications`) are read-only to the app; update those by installing the newer DMG.

The 1.5+ Rivet native hosts do not bundle the in-app updater yet; the feed and the signed `update-manifest.json` are published for that migration. Until then, upgrade those installs by running the newer MSI/DMG or replacing the portable/tar payload.

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

- **Windows**: session-wide idle detection via `GetLastInputInfo`; MSI installer plus portable ZIP (ARM64 runs the x64 build via emulation).
- **macOS**: session-wide idle detection via CoreGraphics; DMG plus portable ZIP on Apple silicon and Intel.
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
bash packaging/macos/build-native.sh 1.6.0

# Debian/Ubuntu runner
bash packaging/linux/build-deb.sh 1.6.0
```

## Release process

1. Bump the single-source `VERSION` file; `scripts/check-release-version.sh` enforces `VERSION == rivet.rktd == MoveBit.csproj <Version>`, and the release tag must equal it too (for example `1.6.0` ↔ `v1.6.0`).
2. Merge only with CI green (Racket tests plus the Windows/macOS/Linux host matrix; CI verifies the Windows host compile).
3. Push the version tag.
4. Release automation builds macOS DMG + portable ZIP (arm64 and x64), the Windows MSI + portable ZIP, the Linux tar.gz, per-asset `.sha256` sidecars, the `SHA256SUMS` manifest, and the signed `update-manifest.json`; one GitHub Release is created only after every required package succeeds, with the release notes taken from the matching CHANGELOG section.
5. The portable archives are the update feed: updatable installations discover the new release through GitHub's latest-release API and match assets by the published `movebit-<version>-<os>-<arch>.<ext>` naming.

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
