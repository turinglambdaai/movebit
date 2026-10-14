# Changelog

## [Unreleased]

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
