# Changelog

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
