#!/usr/bin/env node
// Generate macos-host/Sources/RivetHost/GeneratedStrings.swift from the
// shared/i18n single source (zh.json / en.json). Run from the repo root:
//   node scripts/gen-swift-strings.mjs
//
// MoveBit's i18n JSON is nested (sections plus string-array copy pools), so
// unlike the flat brainfuel table this script flattens nested objects and
// arrays into dot-keyed entries and PascalCases each segment:
//   "today.title"           -> "TodayTitle"
//   "copy.breakHintPool.3"  -> "CopyBreakHintPool3"
// Only string leaves become entries; non-string leaves (e.g. copy.celebrateAt
// numbers) are skipped — the host hardcodes those the way the old app did.
import { readFileSync, writeFileSync } from "node:fs";

const zh = JSON.parse(readFileSync("shared/i18n/zh.json", "utf8"));
const en = JSON.parse(readFileSync("shared/i18n/en.json", "utf8"));

// The JSON carries XML-escaped ampersands over from the old Avalonia string
// table ("Settings &amp; stats"); undo the basic entities for plain display.
const unescapeXml = (s) =>
  s
    .replace(/&lt;/g, "<")
    .replace(/&gt;/g, ">")
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, "&");

const flatten = (value, prefix, out) => {
  for (const [k, v] of Object.entries(value)) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v !== null && typeof v === "object") {
      flatten(v, key, out);
    } else if (typeof v === "string") {
      out[key] = unescapeXml(v);
    }
  }
};

const swiftKey = (dotted) =>
  dotted
    .split(".")
    .map((segment) => segment.charAt(0).toUpperCase() + segment.slice(1))
    .join("");

const esc = (s) =>
  String(s)
    .replace(/\\/g, "\\\\")
    .replace(/"/g, '\\"')
    .replace(/\n/g, "\\n")
    .replace(/\r/g, "\\r")
    .replace(/\t/g, "\\t");

const table = (flat) =>
  Object.entries(flat)
    .map(([k, v]) => `    "${esc(swiftKey(k))}": "${esc(v)}",`)
    .join("\n");

const flatZh = {};
const flatEn = {};
flatten(zh, "", flatZh);
flatten(en, "", flatEn);

const zhKeys = Object.keys(flatZh).map(swiftKey);
const enKeys = Object.keys(flatEn).map(swiftKey);
if (new Set(zhKeys).size !== zhKeys.length || new Set(enKeys).size !== enKeys.length) {
  throw new Error("duplicate Swift key after flattening");
}
if (zhKeys.length !== enKeys.length || new Set(zhKeys).symmetricDifference(new Set(enKeys)).size > 0) {
  throw new Error("zh/en key sets diverge after flattening");
}

const out = `// Generated from shared/i18n/{zh,en}.json — the single i18n source.
// Do not edit; run: node scripts/gen-swift-strings.mjs

import Foundation

enum L10n {
    nonisolated(unsafe) static var language: String = "zh"

    private static let zh: [String: String] = [
${table(flatZh)}
    ]

    private static let en: [String: String] = [
${table(flatEn)}
    ]

    private static let formatPatterns = ["yyyy-MM-dd", "HH:mm:ss", "HH:mm"]

    /// Look up a key in the active language, falling back to zh then the key
    /// itself. "{0}"-style placeholders are substituted from the args; a
    /// "{0:HH:mm}"-style placeholder formats a Date arg with that pattern.
    static func t(_ key: String, _ args: Any...) -> String {
        let table = language == "en" ? en : zh
        var s = table[key] ?? zh[key] ?? key
        for (i, arg) in args.enumerated() {
            s = s.replacingOccurrences(of: "{\\(i)}", with: string(of: arg))
            s = replaceFormatted(s, index: i, arg: arg)
        }
        return s
    }

    /// Pick a random line from a copy pool emitted as indexed keys
    /// ("\\(prefix)0" ... "\\(prefix)\\(count - 1)").
    static func pick(_ prefix: String, _ count: Int) -> String {
        guard count > 0 else { return prefix }
        return t("\\(prefix)\\(Int.random(in: 0..<count))")
    }

    private static func string(of arg: Any) -> String {
        if let date = arg as? Date { return formatted(date, pattern: "yyyy-MM-dd") }
        return String(describing: arg)
    }

    private static func replaceFormatted(_ s: String, index: Int, arg: Any) -> String {
        guard let date = arg as? Date else { return s }
        var result = s
        for pattern in formatPatterns {
            let token = "{\\(index):\\(pattern)}"
            if result.contains(token) {
                result = result.replacingOccurrences(of: token, with: formatted(date, pattern: pattern))
            }
        }
        return result
    }

    private static func formatted(_ date: Date, pattern: String) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = pattern
        return formatter.string(from: date)
    }
}
`;

writeFileSync("macos-host/Sources/RivetHost/GeneratedStrings.swift", out);
console.log(`GeneratedStrings.swift: ${zhKeys.length} zh / ${enKeys.length} en keys`);
