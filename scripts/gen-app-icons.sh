#!/usr/bin/env bash
# Regenerate the app icon family from the canonical Mubi art (assets/mubi/
# tokens.json — the same master data every host embeds). Never hand-edit the
# outputs; rerun this script after touching the character art.
#
#   scripts/gen-app-icons.sh
#
# Outputs: Assets/movebit.icns, Assets/icon.ico, Assets/icon.png
# Requires: jq, rsvg-convert, iconutil (macOS), python3 + Pillow (ico).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE}[0]")/.." && pwd)"
TOKENS="$ROOT/assets/mubi/tokens.json"
OUT="$ROOT/Assets"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

command -v jq >/dev/null && command -v rsvg-convert >/dev/null || {
  echo "need jq + rsvg-convert" >&2; exit 1; }

# ---- canonical geometry + colors (tokens.json is the single source) ----
BODY="$(jq -r '.geometry.body' "$TOKENS")"
FIN_L="$(jq -r '.geometry.finLeft' "$TOKENS")"
FIN_R="$(jq -r '.geometry.finRight' "$TOKENS")"
EYE_RX="$(jq -r '.geometry.eye.rx' "$TOKENS")"
EYE_RY="$(jq -r '.geometry.eye.ry' "$TOKENS")"
EYE_LX="$(jq -r '.geometry.eye.leftCx' "$TOKENS")"
EYE_RXC="$(jq -r '.geometry.eye.rightCx' "$TOKENS")"
EYE_CY="$(jq -r '.geometry.eye.cy' "$TOKENS")"
SH_CX="$(jq -r '.geometry.contactShadow.cx' "$TOKENS")"
SH_CY="$(jq -r '.geometry.contactShadow.cy' "$TOKENS")"
SH_RX="$(jq -r '.geometry.contactShadow.rx' "$TOKENS")"
SH_RY="$(jq -r '.geometry.contactShadow.ry' "$TOKENS")"
INK="$(jq -r '.colors["mubi.ink"]' "$TOKENS")"
FIN="$(jq -r '.colors["mubi.fin"]' "$TOKENS")"
BODY_LIGHT="$(jq -r '.colors["mubi.body.light"]' "$TOKENS")"
BODY_MID="$(jq -r '.colors["mubi.body"]' "$TOKENS")"
BODY_DEEP="$(jq -r '.colors["mubi.body.deep"]' "$TOKENS")"
SHADOW="$(jq -r '.colors["mubi.shadow"]' "$TOKENS")"
# The lower-form shade path is host-embedded master data (tokens.json does
# not carry it); same string as the Linux/Windows hosts embed.
LOWER_SHADE='M 16 96 C 24 118, 42 126, 60 126 C 80 126, 98 116, 105 94 C 100 116, 84 128, 60 128 C 38 128, 20 114, 16 96 Z'

# ---- compose the app icon: warm-dark squircle, Mubi front and center ----
# macOS grid: 824pt content on a 1024 canvas, ~184 corner radius; the
# character sits low (icons read heavy at the bottom) at 4.6x.
cat > "$TMP/icon.svg" <<SVG
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1024 1024" width="1024" height="1024">
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#3E3931"/>
      <stop offset="1" stop-color="#272319"/>
    </linearGradient>
    <linearGradient id="mubiBody" x1="0.18" y1="0.10" x2="0.78" y2="1.0">
      <stop offset="0" stop-color="$BODY_LIGHT"/>
      <stop offset="0.45" stop-color="$BODY_MID"/>
      <stop offset="1" stop-color="$BODY_DEEP"/>
    </linearGradient>
    <linearGradient id="lowerShade" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="$SHADOW" stop-opacity="0"/>
      <stop offset="1" stop-color="$SHADOW" stop-opacity="0.55"/>
    </linearGradient>
    <radialGradient id="sheen" cx="0.5" cy="0.5" r="0.5">
      <stop offset="0" stop-color="#FFFFFF" stop-opacity="0.09"/>
      <stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/>
    </radialGradient>
    <filter id="soft" x="-60%" y="-60%" width="220%" height="220%">
      <feGaussianBlur stdDeviation="2.2"/>
    </filter>
  </defs>
  <rect x="100" y="100" width="824" height="824" rx="184" fill="url(#bg)"/>
  <ellipse cx="512" cy="150" rx="560" ry="420" fill="url(#sheen)"/>
  <rect x="101" y="101" width="822" height="822" rx="183" fill="none"
        stroke="#FFFFFF" stroke-opacity="0.07" stroke-width="2"/>
  <g transform="translate(236 121) scale(4.6)">
    <ellipse cx="$SH_CX" cy="$SH_CY" rx="$SH_RX" ry="$SH_RY"
             fill="$SHADOW" opacity="0.5" filter="url(#soft)"/>
    <path d="$FIN_L" fill="$FIN"/>
    <path d="$FIN_R" fill="$FIN"/>
    <path d="$BODY" fill="url(#mubiBody)"/>
    <path d="$LOWER_SHADE" fill="url(#lowerShade)"/>
    <ellipse cx="$EYE_LX" cy="$EYE_CY" rx="$EYE_RX" ry="$EYE_RY" fill="$INK"/>
    <ellipse cx="$EYE_RXC" cy="$EYE_CY" rx="$EYE_RX" ry="$EYE_RY" fill="$INK"/>
  </g>
</svg>
SVG

# ---- icns (macOS) ----
mkdir -p "$TMP/icon.iconset"
for spec in "16 icon_16x16" "32 icon_16x16@2x" "32 icon_32x32" "64 icon_32x32@2x" \
            "128 icon_128x128" "256 icon_128x128@2x" "256 icon_256x256" \
            "512 icon_256x256@2x" "512 icon_512x512" "1024 icon_512x512@2x"; do
  set -- $spec
  rsvg-convert -w "$1" -h "$1" "$TMP/icon.svg" -o "$TMP/icon.iconset/$2.png"
done
iconutil -c icns "$TMP/icon.iconset" -o "$OUT/movebit.icns"

# ---- ico (Windows: app + tray resource) ----
python3 - "$TMP/icon.svg" "$OUT/icon.ico" <<'PY'
import sys, subprocess, tempfile, os
from PIL import Image
svg, out = sys.argv[1], sys.argv[2]
tmp = tempfile.mkdtemp()
sizes = [256, 128, 64, 48, 32, 16]
frames = []
for s in sizes:
    p = os.path.join(tmp, f"{s}.png")
    subprocess.run(["rsvg-convert", "-w", str(s), "-h", str(s), svg, "-o", p], check=True)
    frames.append(Image.open(p))
frames[0].save(out, format="ICO", sizes=[(s, s) for s in sizes])
PY

# ---- png (Linux: AppImage/desktop entry) ----
rsvg-convert -w 512 -h 512 "$TMP/icon.svg" -o "$OUT/icon.png"

echo "app icons regenerated:"
ls -la "$OUT/movebit.icns" "$OUT/icon.ico" "$OUT/icon.png"
