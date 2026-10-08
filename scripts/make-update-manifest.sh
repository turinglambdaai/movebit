#!/usr/bin/env bash
# Build the MoveBit update manifest (rivet format) and merge platform
# artifacts from a release dist directory.
#
# Usage: scripts/make-update-manifest.sh <tag> <dist-dir> <key-der-path>
#   <tag>          release tag, e.g. v1.5.0 (must match rivet.rktd version)
#   <dist-dir>     directory containing the packaged installers:
#                    MoveBit-<tag>-macos.dmg
#                    movebit-<version>-windows-x64.msi
#                    movebit-<version>-linux-x64.tar.gz
#   <key-der-path> Ed25519 private key in DER (OneAsymmetricKey) form; the
#                  CI secret stores it base64-encoded.
#
# Emits <dist-dir>/update-stable.json — a single signed channel manifest
# carrying every platform artifact. Env overrides: RELEASE_ASSET_BASE_URL,
# RIVET_UPDATE_KEY_ID.

set -euo pipefail

TAG="${1:?usage: make-update-manifest.sh <tag> <dist-dir> <key-der-path>}"
DIST="${2:?usage: make-update-manifest.sh <tag> <dist-dir> <key-der-path>}"
KEY_PATH="${3:?usage: make-update-manifest.sh <tag> <dist-dir> <key-der-path>}"
VERSION="${TAG#v}"
KEY_ID="${RIVET_UPDATE_KEY_ID:-movebit-2026-10}"
BASE_URL="${RELEASE_ASSET_BASE_URL:-https://github.com/turinglambdaai/movebit/releases/download/$TAG}"

# ---- verify tag/version alignment -------------------------------------------
RKTD_VERSION="$(racket -e '(require racket/file) (displayln (hash-ref (file->value "rivet.rktd") (quote version)))' | tr -d '"')"
if [ "$VERSION" != "$RKTD_VERSION" ]; then
  echo "error: tag $VERSION != rivet.rktd version $RKTD_VERSION" >&2
  exit 1
fi

MAC_DMG="$DIST/MoveBit-$TAG-macos.dmg"
WIN_MSI="$DIST/movebit-$VERSION-windows-x64.msi"
LINUX_TARGZ="$DIST/movebit-$VERSION-linux-x64.tar.gz"

sha256_of() { shasum -a 256 "$1" | awk '{print $1}'; }
size_of() {
  if stat -f%z "$1" >/dev/null 2>&1; then stat -f%z "$1"; else stat -c%s "$1"; fi
}

# ---- build + sign the merged manifest with rivet's own signer ---------------
MANIFEST="$DIST/update-stable.json"

SCRIPT="$(mktemp /tmp/movebit-manifest-XXXXXX.rkt)"
trap 'rm -f "$SCRIPT"' EXIT

cat > "$SCRIPT" <<RKT
#lang racket/base
(require rivet/distribution
         racket/file
         racket/format
         racket/list
         racket/string)
(define tag "$TAG")
(define version "$VERSION")
(define base-url "$BASE_URL")
(define key-id "$KEY_ID")
(define dist (path->complete-path "$DIST"))
(define key-path (path->complete-path "$KEY_PATH"))
(define build (hash-ref (file->value "rivet.rktd") 'build))

(define (artifact platform architecture file installer)
  (define path (build-path dist file))
  (unless (file-exists? path)
    (error 'make-update-manifest "missing installer: ~a" path))
  (update-artifact platform architecture
                   (string-append base-url "/" file)
                   (sha256-file/hex path)
                   (file-size path)
                   installer
                   '()))

(define artifacts
  (filter-map
   (lambda (entry) entry)
   (list (let ([file (format "MoveBit-~a-macos.dmg" tag)])
           (and (file-exists? (build-path dist file))
                (artifact 'macos 'arm64 file 'dmg)))
         (let ([file (format "movebit-~a-windows-x64.msi" version)])
           (and (file-exists? (build-path dist file))
                (artifact 'windows 'x64 file 'msi)))
         (let ([file (format "movebit-~a-linux-x64.tar.gz" version)])
           (and (file-exists? (build-path dist file))
                (artifact 'linux 'x64 file 'tar-gz))))))

(when (null? artifacts)
  (error 'make-update-manifest "no installers found in ~a" dist))

(define manifest
  (update-manifest "site.jrtx.movebit"
                   version
                   build
                   'stable
                   ;; published-at: RFC 3339, second precision
                   (let ([d (seconds->date (current-seconds) #f)])
                     (format "~a-~a-~aT~a:~a:~aZ"
                             (date-year d)
                             (~r (date-month d) #:min-width 2 #:pad-string "0")
                             (~r (date-day d) #:min-width 2 #:pad-string "0")
                             (~r (date-hour d) #:min-width 2 #:pad-string "0")
                             (~r (date-minute d) #:min-width 2 #:pad-string "0")
                             (~r (date-second d) #:min-width 2 #:pad-string "0")))
                   "0.0.0"
                   #f
                   #t
                   100
                   artifacts))

(call-with-output-file (build-path dist "update-stable.json")
  #:exists 'truncate/replace
  (lambda (out)
    (write-signed-manifest manifest
                           (read-ed25519-private-key key-path)
                           key-id
                           out)
    (newline out)))
(printf "manifest: ~a (~a artifacts)\\n"
        (build-path dist "update-stable.json") (length artifacts))
RKT

racket "$SCRIPT"
