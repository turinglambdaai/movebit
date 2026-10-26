# MoveBit

> **Product reboot in progress (2026-10-26):** All existing v0.x builds were experiments with no real users. MoveBit is being rebuilt as a **water / stand-up / eye-rest reminder app**, using an expressive, non-obstructive desktop character as its delivery interface. The **new v1 is not released yet**; existing installation and full-screen descriptions below document the old experimental v0.5.0 behavior, not the new product direction. The v1 plan explicitly removes forced full-screen breaks, old strict-mode onboarding and legacy migration requirements. See [v1 implementation #55](https://github.com/turinglambdaai/movebit/issues/55) and [interaction contract](docs/COMPANION-RFC.md).

A desktop health companion that watches how long you **actually work** and gets you out of the chair — sit reminders can cover **every monitor** with a countdown break screen, while water reminders stay lightweight. One Racket domain core drives first-party native hosts on Windows, macOS, and Linux over [Rivet](https://github.com/turinglambdaai/rivet)'s typed RPC.

[![release](https://img.shields.io/github/v/release/turinglambdaai/movebit)](https://github.com/turinglambdaai/movebit/releases/latest) ![platform](https://img.shields.io/badge/platform-macOS%20%7C%20Windows%20%7C%20Linux-lightgrey) ![built with](https://img.shields.io/badge/built%20with-Rivet-c15f3c) [![CI](https://github.com/turinglambdaai/movebit/actions/workflows/ci.yml/badge.svg)](https://github.com/turinglambdaai/movebit/actions/workflows/ci.yml) [![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

**English** · [中文](README.zh-CN.md)

## Start here

Free community application under MIT; no commercial activation or subscription. Desktop previews for macOS, Windows and Linux. Idle detection depends on the desktop session; unsupported Wayland sessions fall back to elapsed time.

[Installation, first use, privacy, backup and recovery](docs/USER-GUIDE.md) · [简体中文](docs/USER-GUIDE.zh-CN.md)

## Why

Most reminder tools lose the moment the notification becomes easy to dismiss. MoveBit is designed around a stronger contract:

- **Track active work instead of blindly counting wall-clock time.** Session-wide idle detection is available on Windows, macOS, Linux/X11, and mainstream Wayland sessions. Wayland uses GNOME/Mutter IdleMonitor or the freedesktop ScreenSaver session-bus API when the desktop exposes one; otherwise MoveBit deliberately falls back to elapsed time instead of inventing idle data.
- **Make long breaks intentional, and never inescapable.** Fullscreen breaks are an explicit opt-in — fresh profiles start with gentle toasts — and every break cover carries an immediately available emergency exit (<kbd>Esc</kbd> or a visible button) that restores all displays even if the reminder engine is unreachable. The ordinary skip stays delayed (20 seconds by default).
- **Do not nag after a real break.** Being idle beyond the away threshold resets every reminder cycle when you return. Completing a forced break does the same, and the break itself is never counted as active work.
- **Show patterns, not just today's number.** The main window can switch between 7-day and 30-day activity history and tracks continuous-work streaks, recent longest sessions, active-day averages, and the busiest recent day.
- **Ship native installers and portable archives for every supported desktop.** Windows gets an MSI plus a portable ZIP, macOS gets a DMG plus a portable ZIP on both Apple silicon and Intel, and Linux gets a portable tar.gz and zip plus a deb, an rpm, and an AppImage.

## Architecture

MoveBit is rebuilt on [Rivet](https://github.com/turinglambdaai/rivet): all policy lives in one Racket domain core (`app/backend.rkt` + `racket/`), and every platform host is a thin native shell that renders state and forwards user actions over typed RPC (RVT1):

| Host | Stack | Directory |
| --- | --- | --- |
| Windows | WinUI 3 (C++/WinRT), embedded Racket CS | `windows/` |
| macOS | SwiftUI | `macos-host/` |
| Linux | GTK 4, embedded Racket CS | `linux/` |

The Racket runtime is statically linked into each host — no external interpreter, no .NET, no Electron. Config and history files, paths, and formats are identical across hosts (`%APPDATA%\movebit` on Windows, the platform application-data directory elsewhere), so an install upgrades in place.

## Features

- ⏱️ **Active-work monitoring** — Windows `GetLastInputInfo`, macOS CoreGraphics, Linux/X11 XScreenSaver, Wayland session-bus backends where available
- 🚨 **Forced breaks** — opt-in full-screen, multi-monitor break cover with countdown; delayed skip plus an immediate <kbd>Esc</kbd> emergency exit
- 👀 **Micro breaks** — short screen-center "stand up / look far away" nudges, no lock and no sound
- 💧 **Water reminders** — lightweight toasts on their own interval
- 📊 **7 / 30-day history** — switchable activity chart backed by up to 370 days of persisted history
- 🧠 **Work-pattern insights** — current continuous session, today's/recent longest session, active-day average, busiest recent day
- 🌓 **中文 / English** — switched in app or resolved from the system UI language
- 🧙 **First-run onboarding** — gentle by default; Standard / Strict cards are the explicit opt-in to fullscreen breaks
- 🔁 **Autostart on login** — Windows registry / macOS LaunchAgent / Linux XDG autostart, owned by the backend
- ⏸️ **Pause for one hour** — with exact pause-expiry accounting
- 🔒 **Single-instance activation** — relaunching surfaces the existing instance instead of duplicating it
- 📦 **Native + portable distribution** — Windows MSI, macOS DMG, portable ZIPs, Linux tar.gz/zip/deb/rpm/AppImage

## Install

Grab your build from [Releases](https://github.com/turinglambdaai/movebit/releases/latest):

| Platform | Portable archive | Installer |
| --- | --- | --- |
| macOS Apple silicon | `movebit-<version>-macos-arm64.zip` | `movebit-<version>-macos-arm64.dmg` |
| macOS Intel | `movebit-<version>-macos-x64.zip` | `movebit-<version>-macos-x64.dmg` |
| Windows x64 | `movebit-<version>-windows-x64.zip` | `movebit-<version>-windows-x64.msi` |
| Windows ARM64 | runs the x64 build via Windows on ARM's x64 emulation | — |
| Linux x64 | `movebit-<version>-linux-x64.tar.gz` · `.zip` | `movebit-<version>-linux-x64.deb` · `-x86_64.rpm` · `.AppImage` |
| Linux arm64 | `movebit-<version>-linux-arm64.tar.gz` · `.zip` | `movebit-<version>-linux-arm64.deb` · `-aarch64.rpm` |

Assets follow one lowercase scheme, `movebit-<version>-<os>-<arch>.<ext>` (for example `movebit-0.5.0-macos-arm64.dmg`). Every asset has a matching `.sha256` sidecar, and each release publishes a `SHA256SUMS` manifest plus the Ed25519-signed `update-manifest.json`.

Platform notes:

- **Windows** — run the MSI for a normal installed app, or unpack the portable ZIP anywhere you like. On ARM64 Windows, use the x64 assets: Windows on ARM runs them through its x64 emulation, and no native ARM64 build is published.
- **macOS** — open the DMG and drag the app to Applications (or another folder); the portable ZIP is the same app bundle without the disk image. Builds are ad-hoc signed, not notarized, so Gatekeeper may require an explicit first-launch approval (right-click → Open, or `xattr -cr /Applications/MoveBit.app`). Notarization is deferred until paid Apple developer credentials are available.
- **Linux** — install the deb (`sudo apt install ./movebit-<version>-linux-<arch>.deb`), install the rpm, run the AppImage, or unpack the tar.gz and run the host inside; GTK 4 and its system libraries are the only runtime dependencies, everything else is bundled. The deb installs to `/opt/movebit` with a `movebit` command and a desktop entry.

> Release binaries carry no publisher code signature. Windows SmartScreen or macOS Gatekeeper may therefore show a warning on first launch. Paid code signing/notarization is the remaining distribution roadmap item.

## How it works

Three independent cycles advance on a 30-second scheduler tick:

- **Sit cycle** accumulates active work only. At the configured interval (45 minutes by default), it starts a full-screen break when forced breaks are enabled, otherwise a toast.
- **Water cycle** runs independently (30 minutes by default).
- **Micro-break cycle** runs independently (30 minutes / 20 seconds by default) and never replaces the longer sit-break cycle.

Idle time at or above the away threshold (5 minutes by default) freezes accumulation. On return, all cycles restart from zero. Oversized clock jumps such as system sleep are clamped so stale reminders are not dumped after wake.

A forced break is explicitly excluded from active-work time. When the countdown completes, all three cycles and the continuous-work session restart. Choosing "remind me later" on a toast or skipping a forced break schedules the same reminder after the configurable snooze delay (10 active minutes by default).

Daily stats are flushed to `history.json` every few minutes, archived across day boundaries, and pruned to the most recent 370 days.

## Configuration

Settings live in `%APPDATA%\movebit\config.json` on Windows or the platform application-data directory on macOS/Linux. All user-facing settings can be changed from the settings window; the backend clamps values and re-announces changes to every host.

| Setting | Default | Range |
| --- | --- | --- |
| Sit reminder interval | 45 | 10–240 min |
| Water reminder interval | 30 | 5–180 min |
| Away reset | 5 | 1–60 min |
| Force break | off (opt-in) | — |
| Break duration | 5 | 1–30 min |
| Skip delay | 20 | 0–120 s |
| Snooze | 10 | 5–60 min |
| Micro breaks | on | 10–60 min / 10–60 s |
| Sound | on | — |
| Auto update checks | on | — |
| Language | auto | auto / zh / en |

## Build from source

Prerequisites: [Racket CS](https://racket-lang.org) (9.x), the [rivet](https://github.com/turinglambdaai/rivet) CLI linked into it, and per-host toolchains (Visual Studio with WinUI for Windows, GTK 4 dev packages for Linux, Xcode for macOS).

```bash
git clone https://github.com/turinglambdaai/rivet.git ../rivet
raco pkg install --auto --no-docs --link ../rivet/rivet ../rivet/rivet-cli

raco rivet doctor        # toolchain self-check
raco test racket/        # domain core tests
raco rivet build         # generate the client, compile the backend, build this platform's host
```

Linux packaging scripts (deb / rpm / AppImage) live under `packaging/linux/` and operate on rivet's staged `dist/` payload.

## Release process

1. Bump the single-source `VERSION` file; `scripts/check-release-version.sh` enforces `VERSION == rivet.rktd`, and the release tag must equal it too (for example `0.3.1` ↔ `v0.3.1`).
2. Add a `CHANGELOG.md` section for the version — the release validate job greps for it.
3. Merge only with CI green (Racket tests plus the Windows/macOS/Linux host matrix).
4. Push the version tag. Release automation builds macOS DMG + portable ZIP (arm64 and x64), the Windows MSI + portable ZIP, the Linux tar.gz + zip (x64 + arm64) plus deb, rpm, and AppImage installers, per-asset `.sha256` sidecars, the `SHA256SUMS` manifest, and the signed `update-manifest.json`; one GitHub Release is created only after every required package succeeds, with the release notes taken from the matching CHANGELOG section.

## Roadmap

- [x] Tray icon with quick actions (open / pause / exit) on Windows
- [x] In-app signed-manifest updates shipped on all three hosts ([0.5.0](https://github.com/turinglambdaai/movebit/releases/tag/v0.5.0)): cancellable downloads, Windows MSI self-update through a passive msiexec handoff, and backup + rollback around the zip swap.
- [x] Cancellation, signature-rejection and rollback evidence at the updater test level (`racket/tests/update-test.rkt`, runs on CI).
- [ ] Live N→N+1 update evidence recorded for every supported install shape (the updater tests prove the mechanics; a recorded real update per shape is still to do).
- [ ] Windows code signing and macOS signing/notarization
- **Not planned: mobile (iOS / iPadOS / Android) and watchOS.** MoveBit guards against long uninterrupted sitting at a desk — a problem that only exists where people work at a computer for hours. On phones and tablets people stand, walk, or recline, so there is nothing to guard against. The product is desktop cross-platform by design: macOS, Windows, and Linux.

## License

[MIT](LICENSE)
