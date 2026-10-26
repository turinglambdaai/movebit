#!/usr/bin/env node
// Generate the Mubi（沐比）artwork from one source of truth (issue #56 D1).
//
//   node scripts/gen-mubi-art.mjs
//
// Emits:
//   assets/mubi/source/mubi-master.svg      six-state reference master
//   .rivet/build/mubi/<state>.svg           per-state files for rendering
//
// Colors come from assets/mubi/tokens.json; geometry constants below are the
// canonical path data (mirrored into tokens.json "geometry" for host
// verification). Hosts embed these strings — never redraw or approximate.

import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const tokens = JSON.parse(readFileSync(join(root, "assets/mubi/tokens.json"), "utf8"));
const C = tokens.colors;

// ---- canonical geometry (120x140 canvas) -----------------------------------

const BODY = "M 58 16 C 82 18, 106 46, 109 82 C 111 104, 92 124, 60 124 C 33 124, 12.5 106, 11 84 C 9.5 52, 34 14, 58 16 Z";
const FIN_LEFT = "M 52 24 C 47 13, 40 7, 32 6 C 30 14, 36 23, 45 27 C 48 28.2, 51 26.5, 52 24 Z";
const FIN_RIGHT = "M 74 23 C 71 14, 66.5 8, 60.5 6 C 58.5 13, 62.5 21.5, 70 25 C 72.3 25.9, 73.8 25, 74 23 Z";
const FIN_SHADE = "M 48 22 C 44.5 15.5, 39.5 10.5, 34.5 8.5 C 33.5 13.5, 38 20.5, 45 24.5 Z";
const LOWER_SHADE = "M 16 96 C 24 118, 42 126, 60 126 C 80 126, 98 116, 105 94 C 100 116, 84 128, 60 128 C 38 128, 20 114, 16 96 Z";
const EYES = { left: 46.5, right: 73.5, cy: 65, rx: 3, ry: 3.6 };
const EYE_CLOSED_L = "M 43 65 C 45 67.6, 48 67.6, 50 65";
const EYE_CLOSED_R = "M 70 65 C 72 67.6, 75 67.6, 77 65";
const MOUTH_ACK = "M 57.5 74 C 59 75.4, 61 75.4, 62.5 74";

// ---- shared pieces ----------------------------------------------------------

const defs = `
  <defs>
    <linearGradient id="bodyGrad" x1="0.18" y1="0.10" x2="0.78" y2="1.0">
      <stop offset="0" stop-color="${C["mubi.body.light"]}"/>
      <stop offset="0.45" stop-color="${C["mubi.body"]}"/>
      <stop offset="1" stop-color="${C["mubi.body.deep"]}"/>
    </linearGradient>
    <linearGradient id="lowerShade" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="${C["mubi.shadow"]}" stop-opacity="0"/>
      <stop offset="1" stop-color="${C["mubi.shadow"]}" stop-opacity="0.55"/>
    </linearGradient>
    <radialGradient id="highlight" cx="0.5" cy="0.5" r="0.5">
      <stop offset="0" stop-color="#FFFFFF" stop-opacity="0.45"/>
      <stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/>
    </radialGradient>
    <filter id="soft2" x="-40%" y="-40%" width="180%" height="180%">
      <feGaussianBlur stdDeviation="2"/>
    </filter>
    <radialGradient id="focusTint" cx="0.5" cy="0.5" r="0.5">
      <stop offset="0.55" stop-color="${C["cue.focus"]}" stop-opacity="0"/>
      <stop offset="1" stop-color="${C["cue.focus"]}" stop-opacity="0.22"/>
    </radialGradient>
  </defs>`;

const lowerShade = `<path d="${LOWER_SHADE}" fill="url(#lowerShade)" filter="url(#soft2)"/>`;
const rimLight = `<ellipse cx="38" cy="46" rx="17" ry="24" fill="url(#highlight)" transform="rotate(-18 38 46)"/>`;
const openEyes = (glintY) => `
    <ellipse cx="${EYES.left}" cy="${EYES.cy}" rx="${EYES.rx}" ry="${EYES.ry}" fill="${C["mubi.ink"]}"/>
    <ellipse cx="${EYES.right}" cy="${EYES.cy}" rx="${EYES.rx}" ry="${EYES.ry}" fill="${C["mubi.ink"]}"/>
    <circle cx="${EYES.left + 1.1}" cy="${glintY}" r="1" fill="#FFFFFF" opacity="0.85"/>
    <circle cx="${EYES.right + 1.1}" cy="${glintY}" r="1" fill="#FFFFFF" opacity="0.85"/>`;
const fins = (fill = C["mubi.fin"]) => `
    <path d="${FIN_LEFT}" fill="${fill}"/>
    <path d="${FIN_RIGHT}" fill="${fill}"/>`;
const finShade = `<path d="${FIN_SHADE}" fill="${C["mubi.shadow"]}" opacity="0.35"/>`;
const body = `<path d="${BODY}" fill="url(#bodyGrad)"/>`;
const closedEyes = `
    <path d="${EYE_CLOSED_L}" stroke="${C["mubi.ink"]}" stroke-width="2.2" stroke-linecap="round" fill="none"/>
    <path d="${EYE_CLOSED_R}" stroke="${C["mubi.ink"]}" stroke-width="2.2" stroke-linecap="round" fill="none"/>`;

// ---- states ------------------------------------------------------------------

const states = {
  idle: `
    ${fins()}${finShade}${body}${lowerShade}${rimLight}
    ${openEyes(63.8)}`,
  water: `
    ${fins()}${body}${lowerShade}${rimLight}
    ${openEyes(63.2)}
    <g transform="translate(97 57) rotate(-10)">
      <path d="M -7 -9 L 7 -9 L 5.4 9 C 5.2 10.6, 4 11.5, 2.4 11.5 L -2.4 11.5 C -4 11.5, -5.2 10.6, -5.4 9 Z" fill="${C["cue.water"]}"/>
      <rect x="-8.4" y="-11.5" width="16.8" height="3.4" rx="1.7" fill="${C["cue.water.deep"]}"/>
      <path d="M -4.6 -6.5 L 4.4 -6.5 L 3.6 1.5 L -4 1.5 Z" fill="${C["cue.water.soft"]}" opacity="0.85"/>
    </g>`,
  stand: `
    ${fins()}${body}${lowerShade}${rimLight}
    ${openEyes(63.8)}
    <path d="M 42 11 C 47 4, 53 2, 58 4" stroke="${C["cue.move"]}" stroke-width="3" stroke-linecap="round" fill="none"/>
    <path d="M 78 11 C 73 4, 67 2, 62 4" stroke="${C["cue.move"]}" stroke-width="3" stroke-linecap="round" fill="none" opacity="0.65"/>`,
  eyes: `
    ${fins()}${body}${lowerShade}${rimLight}
    ${closedEyes}
    <path d="M 92 38 C 96.5 40.5, 99 45, 98 50" stroke="${C["cue.eyes"]}" stroke-width="3" stroke-linecap="round" fill="none"/>`,
  ack: `
    ${fins()}${body}${lowerShade}${rimLight}
    ${openEyes(63.8)}
    <path d="${MOUTH_ACK}" stroke="${C["mubi.ink"]}" stroke-width="1.6" stroke-linecap="round" fill="none"/>
    <path d="M 84 56 L 86 60.5 L 90.5 62 L 86 63.5 L 84 68 L 82 63.5 L 77.5 62 L 82 60.5 Z" fill="${C["cue.done"]}"/>`,
  focus: `
    ${fins("#EDE4D2")}${body}
    <circle cx="60" cy="72" r="52" fill="url(#focusTint)"/>
    ${lowerShade}${rimLight}
    ${closedEyes}
    <path d="M 84 32 C 90 31.5, 95.5 36, 96.5 42 C 97.5 48.5, 93.5 54.5, 87.5 56.5 C 91 52, 92 46, 90.5 41 C 89.2 36.8, 86.8 33.8, 84 32 Z" fill="${C["cue.focus"]}"/>`,
};

const stateSvg = (id, inner, width = 120, height = 140) =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${width} ${height}" width="${width * 4}" height="${height * 4}">${defs}
  <g id="state-${id}">${inner}
  </g>
</svg>
`;

// ---- emit --------------------------------------------------------------------

const buildDir = join(root, ".rivet/build/mubi");
mkdirSync(buildDir, { recursive: true });
for (const [id, inner] of Object.entries(states)) {
  writeFileSync(join(buildDir, `${id}.svg`), stateSvg(id, inner));
}

const names = Object.keys(states);
const masterBody = names
  .map((id, i) => `  <!-- ${id} -->
  <g transform="translate(${i * 130} 0)">${states[id]}
  </g>`)
  .join("\n");
const master = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${names.length * 130} 140" width="${names.length * 130 * 3}" height="420">
<!-- Mubi（沐比）canonical master — GENERATED by scripts/gen-mubi-art.mjs from
     assets/mubi/tokens.json. Regenerate; do not hand-edit. Provenance:
     original project-owned artwork for #56, MIT-compatible. -->
${defs}
${masterBody}
</svg>
`;
mkdirSync(join(root, "assets/mubi/source"), { recursive: true });
writeFileSync(join(root, "assets/mubi/source/mubi-master.svg"), master);
console.log(`mubi art: ${names.length} states -> master + per-state svg`);
