# Changelog

## [Unreleased]

**Rollout note: the v1 reboot's visual identity landed — Mubi（沐比）is the
official companion (issue #56), and the repo is staged for the first real
v1.0.0 release.**

### Added

- **Mubi（沐比）design system (issue #56 D1).** Original project-owned
  character: warm-ivory water-bean with two leaf fins, frozen proportions
  (120×140 canvas, 100×108 body, fixed 27-unit eye spacing), design tokens
  (`assets/mubi/tokens.json`), a six-state vector master and contact sheet
  regenerated from one generator (`scripts/gen-mubi-art.mjs`), and a
  bilingual design-system doc with the host implementation contract.
- **Mubi delivers the cues on all three hosts (D2).** macOS renders the
  canonical Mubi free-floating (pointer-through panel); Windows renders it
  on a compact rounded card (WinUI surfaces are opaque — documented
  difference) that is fully pointer-through and never activates; Linux
  renders it on X11 and **honestly reports no character capability on
  Wayland**, where cues deliver as non-blocking notifications instead of a
  compositor-placed pet.
- **Companion settings in the v1 schema:** Mubi size S/M/L (body heights
  60/78/104), motion still/subtle/lively, dock bottom-right/left/top-right,
  label during-reminders/always — with tolerant defaults, live re-anchoring,
  dark-mode art direction and health intervals kept more prominent than
  character customization.
- **Tray reminder controls + milestone cheers.** Done (advice-labelled) /
  Later / Hide act on the newest open cue from the tray on every host, plus
  the Focus toggle; self-reported sit acknowledgments cheer at 3/5/8 — a
  cheer claims nothing clinical (#51).
- **i18n: flat single source.** `shared/i18n/{zh,en}.json` is now a flat
  dot-keyed table (the family convention rivet's release gate enforces);
  generated tables are sorted and byte-stable, and rivet's own
  `GeneratedI18n` artifacts are committed fresh. Host migration to rivet's
  i18n pipeline is tracked for a follow-up slice.

### Changed

- **The first real v1.0.0 is staged:** `VERSION`/`rivet.rktd` now say
  1.0.0. Publishing remains gated on the release checklist (see #55 R6);
  no tag exists yet.

**Previous slices (R1 cue engine, R2 macOS companion) below.**

**Rollout note: this is the R1 cue-engine slice of the v1 reboot
(owner decisions in issues #51/#54/#55) — the fullscreen forced-break
contract is gone and the canonical reminder state machine now emits
semantic health cues.** v0.x releases were experiments with no real users:
there is no migration path, and that is deliberate.

### Removed

- **The fullscreen forced-break product is retired from the codebase.** The
  Racket countdown engine (`countdown.rkt`), the orchestrator's break
  lifecycle, the `break-started` / `break-ended` events, the `break-active`
  state, the `skip-break` / `complete-break` RPCs, and the per-host fullscreen
  overlay windows (SwiftUI `BreakOverlayController`, WinUI `BreakOverlay`,
  the GTK4 multi-monitor overlay with countdown ring) are deleted. No
  reminder can ever cover a screen, grab focus or intercept input again.
- **Force-break settings are gone** from config and every settings UI:
  `ForceBreakEnabled`, `BreakDurationMinutes`, `SkipAfterSeconds` and the
  gentle/standard/strict onboarding pact. v1 has exactly one gentle,
  non-blocking reminder experience.

### Changed

- **The backend now speaks semantic health cues.** The scheduler/orchestrator
  model an explicit `due -> pending/displayed -> acknowledged | snoozed |
  expired` lifecycle and emit one merged `health-cue` per tick (kinds
  preserved, priority-ordered; the suggested action — stand-and-stretch /
  drink-water / rest-eyes — and the planned delivery travel with it). New
  RPCs: `acknowledge-cue` (Done is self-reported, never proof), `snooze-cue`
  (Later defers every merged kind), `dismiss-cue` (Hide claims nothing),
  `cue-displayed`, `set-quiet` (Focus/presentation silence queues cues and
  delivers ONE bounded summary on exit — never a pile-up), and
  `set-host-capability` (honest per-platform surface reporting; Linux starts
  at notification fallback). Unanswered cues expire after ten minutes; the
  next reminder then arrives on its normal schedule — silence never
  fabricates completion and never loses a reminder.
- **v1 config schema, clean-install only.** `config.json` now carries a
  `SchemaVersion` marker; a file without it (any v0.x document) is foreign
  and falls back to defaults. New `MotionEnabled` setting replaces the
  removed force-break knobs; the onboarding wizard is a compact
  health-reminder flow (water/sit cadence + sound) on all three hosts.
- **Transitional presentation, honestly labeled.** Each host currently
  renders cues through its non-blocking toast / eye-rest cards with
  Done / Later / Hide wired to the new RPCs; the per-platform character
  slices (R2 macOS, R3 Windows, R4 Linux) replace them. Milestone cheers
  return with the character.

### Tests

- The cue lifecycle is pinned by rewritten orchestrator, config, scheduler
  and real-RVT1 wire suites (369 checks): merge rule, pending/displayed
  transitions, quiet queuing + bounded summary, timeout expiry, capability
  fallback and the v1 schema gate.

**Previous slice (issue #49) notes follow.**

### Changed

- **Fresh profiles start gentle.** `default-config` ships
  `force-break-enabled = false`, and a config file with no recorded
  `ForceBreakEnabled` choice now also falls back to gentle — fullscreen
  breaks are an explicit opt-in through the onboarding cards or the settings
  switch. A persisted explicit choice is never silently overwritten (files
  written by any earlier version carry the key, so upgrades keep their
  preference). The first-run wizard starts on the gentle card, and
  "Skip, use defaults" now means the gentle defaults; the Standard/Strict
  cards say "Opt in" and explain the fullscreen contract plus the Esc exit.
- **Every fullscreen break has an immediate emergency exit.** All three
  hosts grew an always-visible "Emergency exit" button and an Escape key
  path that tear the cover down locally first, so usable windows are back
  on every display even when the backend never answers; the delayed
  ordinary Skip stays a separate control. The macOS overlay installs local
  and global key monitors (it never steals focus, so Esc is caught wherever
  focus lives), Windows binds an Escape keyboard accelerator on the overlay
  root, and the GTK overlay puts a key controller on every overlay window.
- **Leaving a break reconciles without instant re-locking.** An emergency
  exit that the backend missed (skip RPC failed or the engine was
  unreachable) is remembered as a pending skip and flushed on the next live
  tick: retried while the backend still holds the break, dropped once the
  break has expired on its own. Skip semantics remain "snooze the sit
  cycle", so nothing re-locks the moment the engine returns.

### Added

- **Reduced-motion and screen-reader handling on the macOS break cover.**
  The decorative halo pulse and the goodbye transition honor
  accessibilityDisplayShouldReduceMotion, and the decorative ring is hidden
  from assistive tech; both overlay buttons keep their ordinary accessible
  labels. The Windows and Linux covers carry no self-initiated motion.
- **User-guide contract for break screens.** The paired USER-GUIDE
  documents the gentle default, the opt-in strong modes, the always-on
  emergency exit, backend-failure behavior and reduced motion
  (USER-GUIDE.md / USER-GUIDE.zh-CN.md).
- The i18n single-source checker's drifted UI-key pin is re-pinned to the
  counted truth (181 UI keys, `brk.emergency` included); nothing invoked
  the checker in CI, so a 0.5.0-era key had landed unnoticed.

## [0.5.0] - 2026-10-22

**Rollout note: this is the release where in-app updates meet every install
shape on every platform.** Downloads can be cancelled (all three hosts), and
Windows joins the update flow with no dead ends: MSI installs (Program Files)
now take the feed's signed .msi through a passive msiexec upgrade, and
per-user installs gained backup + rollback around the file swap. The feed
also grows a real staged-rollout lever and every install keeps a JSONL
diagnostic log a bug report can point at.

### Added

- **Downloads can be cancelled.** The start-download RPC doubles as the
  cancel gesture (the family toggle contract — no schema change): a second
  call while a download runs breaks the worker and lands the phase back on
  idle with the offer intact, because a cancel is not a failure. All three
  hosts grew the Cancel button, the cancelled state (「更新已取消，当前版本
  保持不变」), and a one-click way back to 更新并重启.
- **Windows MSI installs self-update.** Until now an MSI install (Program
  Files, per-machine) could never pass the writability probe — the only
  Windows update path was the portable zip swap. The feed now carries the
  signed .msi alongside the zip; installs under the Program Files prefixes
  (the taskly IsMsiInstall signal) select it by install-kind and hand off
  to one passive `msiexec /passive /norestart /i` — one UAC consent, and a
  failure leaves the old install working.
- **Windows update swaps are backed up.** The zip handoff used to overlay
  the payload in place; it now moves the old install aside first, copies
  into a fresh directory, and restores the old install when the copy fails
  — the update can no longer leave a half-swapped install behind. Both
  failure paths (backup rename, robocopy) write an `update-failed.txt`
  marker that the next launch surfaces once and deletes.
- **Staged rollout is real.** The updater config carried a hardcoded bucket
  0 with a "ships to everyone today" comment; the bucket is now generated
  once per machine (`crypto-random-bytes` — racket/base's fixed-seed PRNG
  would put every fresh install in the same lockstep bucket) and persisted
  in bootstrap.json, so a manifest can narrow its rollout without any
  client change.
- **Diagnostics land in a file.** Rivet's diagnostic sink wrote to stderr,
  which on a Windows GUI process means a black console window and then the
  void. Diagnostics now append as JSONL to `<data-dir>/diagnostics.log` —
  a bug report starts from the copyable failure text and can attach real
  backend records.
- **Host-side download watchdog.** The backend's watchdog (stall 120 s /
  total 30 min) already bounded every download; each host now also holds a
  30-minute ceiling of its own, so even a wedged backend can never leave
  the UI resting on an endless progress bar.

### Changed

- **macOS: the menu bar shows the droplet icon** — a template image that
  follows light/dark — instead of the "MoveBit" text label the app's own
  copy never mentioned. Tray menu labels now swap in place on pause instead
  of rebuilding the status item.
- **The macOS DMG is the branded one.** The release leg used to hand-roll
  `hdiutil create`, leaving the declared `dmg-background` artwork unused;
  it now runs the same `raco rivet release` the other legs use, so the DMG
  ships the background artwork and icon layout. The release validator also
  refuses to start without the update signing secret — a missing key stops
  the pipeline with a readable reason, not a garbled DER failure three jobs
  later.

### Fixed

- **检查更新 died with `updater-config: arity mismatch`.** Rivet main added a
  trailing `component` discriminator to the updater structs; dev builds
  linked to rivet main constructed the config with the old field count, so
  every manual update check failed at the RPC. The config is now built for
  whichever struct shape the linked rivet provides, selecting the plain
  component-less artifact — the v0.11.0 release pin and rivet main both
  work, and the updater tests cover both shapes.
- **macOS: no more stray empty "MoveBit Settings" window.** A SwiftUI scene
  placeholder presented its own empty window at every launch (current macOS
  auto-presents Settings and Window scenes), so finishing the first-run
  wizard still left an untouchable-looking window behind. The host now boots
  as a pure AppKit tray app: first run shows only the wizard, afterwards
  launch shows no window at all — MoveBit lives in the menu bar. The
  standard menu bar (app/edit/window, localized) is rebuilt in AppKit to
  keep Cmd+Q, the selectable update-failure text (Cmd+C), and Cmd+W/M
  working.

## [0.4.0] - 2026-10-15

**Rollout note: in-app updates on macOS and Linux are a new capability
of 0.4.0.** Windows has been self-updating since 0.3.x; from this
release the macOS host auto-installs a verified update (staged bundle →
POSIX apply helper: backup → swap with rollback → relaunch) and the
Linux host follows the family install-shape flow (AppImage swaps in
place, deb/rpm get package-manager guidance, portable tarballs get the
verified archive). **If you run 0.3.x today, your build cannot update
itself yet — install 0.4.0 once by hand** (dmg / msi / deb / AppImage
from this release or the stable `releases/latest/download/<alias>`
links), and every later release then offers 检查更新 → 重启并安装
inside the app.

### Added

- The macOS and Linux hosts are wired to the updater's full state machine
  (the backend already checked silently at startup and every 6 hours, but
  no host ever called `check-updates` / `get-update-state`): a 检查更新 /
  Check for updates entry in the About card now walks 检查中 → 发现新版本
  (consent dialog) → download with live progress → 重启并安装, with
  up-to-date and retry states inline; failure text is selectable/copyable
  on both hosts.
- Linux install shapes after a download (parity with the family's taskly
  flow): AppImage copies get the sibling `.AppImage` release asset
  downloaded, sidecar-checksummed, and swapped over the running image —
  the dialog only offers the re-exec; deb/rpm copies get package-manager
  guidance and never download; portable tarball copies get the verified
  archive plus an extract-over hint and an Open Folder button.
- macOS auto-install: after the verified zip downloads, the backend stages
  the bundle and spawns a POSIX apply helper (wait for exit → backup →
  same-volume swap with rollback → relaunch via `open`), mirroring the
  Windows `.cmd` helper; `prepare-update-install` / `quit-and-install`
  spawn the right helper per platform.
- GitHub issue templates (bug report, feature request, contact links)
  pointing at the site manual and asking for the diagnostics line and the
  copyable update error text.

### Changed

- The update state snapshot keeps a stable key set in every phase
  (`downloadedPath`/`stagedPath` are `#f` instead of absent until used) —
  the hosts' poll contract no longer depends on which transitions ran.
- The About card on every host (settings & stats window) now carries the
  family About rows alongside version and the auto-update toggle: links
  to the website (<https://movebit.jrtx.site>) and the issue tracker
  (<https://github.com/turinglambdaai/movebit/issues>), plus the MIT
  license line.
- Windows update failures can leave the screen with the user: every
  error hint in the check/download flow (check failure, download start,
  progress error) now reveals a 复制错误信息 / Copy error button
  (`UpdCopyError`) that puts the shown failure text on the clipboard —
  a bug report starts from this string.
- `get-diagnostics` no longer reports a stale hand-written epoch string:
  the version line rides the repo single source (`effective-version`,
  i.e. rivet-app-info in packaged builds — staged from `rivet.rktd` =
  `VERSION` — and the update-version parameter in headless runs), and
  the server-level test asserts the semver shape.

## [0.3.1] - 2026-10-14

**Alignment release: the updater rides rivet, the host builds consume rivet as a package, and the site gains a bilingual manual.** No new features.

### Added

- A user manual at [docs/manual.html](https://movebit.jrtx.site/manual): a five-minute start plus one self-contained section each for work timing, forced breaks, water & micro breaks, tray behavior, and online updates, with an FAQ (data location, accounts, platforms, language/theme, uninstall, failed updates). Fully bilingual (Chinese/English) through the site i18n mechanism, with sticky table of contents and the real macOS screenshot embedded
- Stable direct-download aliases on every release (`movebit-macos-arm64.dmg`, `movebit-macos-x64.dmg`, `movebit-windows-x64.msi`, `movebit-linux-x64.deb`, `movebit-linux-arm64.deb`, `movebit-linux-x64.AppImage`, `movebit-linux-arm64.AppImage`) served forever at `releases/latest/download/<alias>`: the site's download cards and the hero call-to-action link these alias URLs directly, browser platform detection highlights the current OS card (and points the hero button at its alias), and the Releases page link becomes a secondary "all formats" entry

### Changed

- The updater download leg now rides rivet's `download-update` (redirect-following fetch, signed-size cap, `.partial` cleanup, SHA-256 + size verification, atomic rename) instead of a hand-rolled copy loop, and a watchdog bounds it: no byte progress for 120 seconds or 30 minutes total wall clock breaks the worker (`exn:break`), so the UI phase always reaches a terminal state instead of resting on "downloading" forever
- The rivet pin moved from the stale `3a4d931` git-sha to the released `v0.11.0` tag in CI and release workflows, and `setup-embed-racket` is pinned to `@v0.11.0` instead of `@main`
- The Linux host consumes the Rivet platform as a CMake package target (`add_subdirectory` + `rivet::platform`) instead of hand-listing 12 rivet translation units; the Windows host links `rivet-host.vcxproj` via ProjectReference with an explicit four-way `RivetRuntimeLibrary` CRT forward (static flavors for self-contained packaging, DLL flavors for development builds) instead of compiling rivet cpp files — completed by including `winui_window.hpp` where the rivet 0.11 split moved `NativeWindowHandle`; the macOS host consumes the root rivet package (`.package(path:)` with `package: "rivet"` products, including `RivetUpdate`) instead of the old platform-subdirectory form
- The site palette aligns with the family values (warm paper `#faf7f2`, terracotta accent `#c15f3c`), dark mode gained a manual toggle with `localStorage` persistence and an anti-flicker head script, i18n moved to the family-standard `data-i18n` + JS dictionary with Chinese as the default (`lang="zh-CN"`), and the download area is a `dl-card` structure listing the real assets of the latest release (no more hardcoded version in URLs); the hero shows the real app screenshot
- README gained release/platform/CI badges (both English and Chinese)


## [0.3.0] - 2026-10-11

**Linux rpm and portable zips close the packaging benchmark gap.** The Linux set now matches the family benchmark (taskly) format-for-format: deb + rpm + AppImage + tar.gz + zip on both architectures.

### Added

- RPM packages for both Linux architectures (`movebit-<version>-linux-x86_64.rpm`, `movebit-<version>-linux-aarch64.rpm`), hand-rolled with `rpmbuild` from a generated spec (`packaging/linux/build-rpm.sh`, the rpm twin of `build-deb.sh`: same `/opt/movebit` payload, `/usr/bin/movebit` symlink, desktop entry, pixmaps icon, and rivet's libtinfo exclusion for Fedora) — each ships with a `.sha256` sidecar
- Portable zips for both Linux architectures (`movebit-<version>-linux-x64.zip`, `movebit-<version>-linux-arm64.zip`), the tar.gz twin built from the same staged payload, with `.sha256` sidecars
- The download page lists the new rpm and zip assets

### Changed

- The rivet pin stays at `a5f6718`: newer pins generate the RivetTypes-namespaced Swift schema this host has not migrated to yet (the pin bump was attempted and reverted — the host migration is its own project). rpm and zip are therefore hand-rolled beside the existing hand-rolled deb and AppImage, which keep their in-job self-verification

## [0.2.0] - 2026-10-10

**Linux catches up to the family standard.** Where v0.1.0 shipped a single Linux portable archive, this release covers both Linux architectures and adds native installer formats, keeping the same embedded-Racket payload everywhere.

### Added

- Linux arm64 is a first-class release target: `movebit-<version>-linux-arm64.tar.gz` is built on native `ubuntu-24.04-arm` runners with the same source-built embeddable Racket CS recipe as x64 (per-arch build cache keyed on `runner.arch`)
- Debian packages for both architectures (`movebit-<version>-linux-x64.deb`, `movebit-<version>-linux-arm64.deb`): the self-contained payload installs under `/opt/movebit`, with a `/usr/bin/movebit` symlink, a desktop entry, and the hicolor icon; the Racket CS runtime is statically linked into the host, so the only runtime Depends are `libgtk-4-1` and `libtinfo6`
- An AppImage for Linux x64 (`movebit-<version>-linux-x64.AppImage`), assembled with the official `appimagetool` (AppImage/appimagetool 1.9.1, tag-pinned and SHA-256-checked per architecture); the arm64 AppImage builds best-effort with the aarch64 tool and warns instead of failing the release
- The signed update feed carries a Linux arm64 entry; the update payload stays the tar.gz on every Linux architecture — deb and AppImage are installer assets and never enter the feed
- `packaging/linux/build-deb.sh` and `packaging/linux/build-appimage.sh` self-verify their output (dpkg metadata round-trip, FHS contents, AppImage self-extraction) inside the release job, since Linux installers cannot be executed on the publishing host

### Changed

- The release Linux job is a two-architecture matrix (`linux-x64`, `linux-arm64` artifacts); `SHA256SUMS` covers every new asset through the same per-asset `.sha256` sidecars

## [0.1.0] - 2026-10-10

**Version-era reset.** The product's technical foundation is settled — one embedded-Racket domain core driving first-party native hosts on macOS, Windows, and Linux, with an Ed25519-signed update feed — so MoveBit enters a 0.x functional-validation phase. The version counter restarts at 0.1.0 and the pre-rebuild release line is retired.

Shipped in this build:

### Added

- The in-app updater consumes the signed update feed: it downloads the single `update-manifest.json` from `releases/latest/download`, verifies the Ed25519 signature over the exact payload bytes (BouncyCastle.Cryptography), and pins both the signature key-id (`movebit-2026-10`) and the embedded public key before trusting a manifest
- Artifact selection happens inside the signed manifest (platform + architecture), and downloads are verified against the manifest's SHA-256 and byte size — the old GitHub-API + `.sha256`-sidecar discovery path is gone
- `scripts/update-keys.sh` manages the signing keypair the family way (private key outside the repo, DER base64 for the `UPDATE_ED25519_PRIVATE_KEY_B64` secret, raw public key for embedding)
- The release matrix produces DMG and portable ZIP for macOS on both Apple silicon and Intel (`macos-arm64`, `macos-x64`)
- Portable ZIPs ship for Windows x64 and macOS next to the MSI/DMG installers — these are also the archives the in-app update feed serves
- Every release asset carries its own `.sha256` sidecar alongside the `SHA256SUMS` manifest

### Changed

- Release assets follow one lowercase naming scheme, `movebit-<version>-<os>-<arch>.<ext>` (e.g. `movebit-0.1.0-macos-arm64.dmg`, `movebit-0.1.0-windows-x64.msi`, `movebit-0.1.0-windows-x64.zip`, `movebit-0.1.0-linux-x64.tar.gz`); the site and the in-app updater match these names exactly
- The signed update feed manifest is published as `update-manifest.json` (family format), listing the portable archives for every platform
- The version lives in a single `VERSION` file; CI and the release pipeline verify `VERSION == rivet.rktd == MoveBit.csproj` before building

### Security

- Unsigned or wrongly-signed feeds are refused outright: bad schema, unknown algorithm, foreign key-id, tampered payload, non-https artifact URLs, and cross-application manifests all fail closed; no update ever falls back to an unsigned path
- The feed signing key was rotated when the signature verifier landed; no shipped client had verified signatures, so there was no trust to migrate

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
