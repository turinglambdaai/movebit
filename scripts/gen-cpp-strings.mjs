#!/usr/bin/env node
// Generate linux/GeneratedStrings.hpp and windows/GeneratedStrings.h from the
// shared/i18n single source (zh.json / en.json). Run from the repo root:
//   node scripts/gen-cpp-strings.mjs
//
// Same flattening and key naming as gen-swift-strings.mjs: nested objects and
// arrays become dot-keyed entries, each segment PascalCased
//   "today.title"          -> "TodayTitle"
//   "copy.breakHintPool.3" -> "CopyBreakHintPool3"
// Only string leaves become entries. The generated header is shared verbatim
// by the GTK4 and WinUI hosts (plain C++20, no framework types).
import { readFileSync, writeFileSync } from "node:fs";

const zh = JSON.parse(readFileSync("shared/i18n/zh.json", "utf8"));
const en = JSON.parse(readFileSync("shared/i18n/en.json", "utf8"));

// The JSON carries XML-escaped ampersands over from the old Avalonia string
// table; undo the basic entities for plain display (parity with the Swift gen).
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

const cppKey = (dotted) =>
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
    .map(([k, v]) => `    {"${esc(cppKey(k))}", "${esc(v)}"},`)
    .join("\n");

const flatZh = {};
const flatEn = {};
flatten(zh, "", flatZh);
flatten(en, "", flatEn);

const zhKeys = Object.keys(flatZh).map(cppKey);
const enKeys = Object.keys(flatEn).map(cppKey);
if (new Set(zhKeys).size !== zhKeys.length || new Set(enKeys).size !== enKeys.length) {
  throw new Error("duplicate C++ key after flattening");
}
if (zhKeys.length !== enKeys.length || new Set(zhKeys).symmetricDifference(new Set(enKeys)).size > 0) {
  throw new Error("zh/en key sets diverge after flattening");
}

const header = (guard) => `// Generated from shared/i18n/{zh,en}.json — the single i18n source.
// Do not edit; run: node scripts/gen-cpp-strings.mjs
#ifndef ${guard}
#define ${guard}

#include <cstdint>
#include <map>
#include <random>
#include <string>
#include <vector>

namespace l10n {

// The active language ("zh" or "en"); hosts set it from the resolved config.
inline std::string g_language = "zh";

inline const std::map<std::string, std::string>& zh_table() {
  static const std::map<std::string, std::string> t = {
${table(flatZh)}
  };
  return t;
}

inline const std::map<std::string, std::string>& en_table() {
  static const std::map<std::string, std::string> t = {
${table(flatEn)}
  };
  return t;
}

// A format argument: plain text, or an epoch-ms timestamp so the
// "{0:yyyy-MM-dd}" / "{0:HH:mm}" patterns from the JSON can be resolved.
struct Arg {
  std::string text;
  std::int64_t epoch_ms = 0;
  bool is_time = false;
};

inline Arg A(std::string s) { Arg a; a.text = std::move(s); return a; }
inline Arg A(const char* s) { Arg a; a.text = s; return a; }
inline Arg A(std::int64_t n) { Arg a; a.text = std::to_string(n); return a; }
inline Arg T(std::int64_t epoch_ms) { Arg a; a.epoch_ms = epoch_ms; a.is_time = true; return a; }

inline std::string replace_first(std::string s, const std::string& from,
                                 const std::string& to) {
  auto pos = s.find(from);
  if (pos != std::string::npos) s.replace(pos, from.size(), to);
  return s;
}

inline std::string format_epoch(std::int64_t ms, const char* pattern) {
  std::time_t seconds = static_cast<std::time_t>(ms / 1000);
  std::tm local{};
#if defined(_WIN32)
  localtime_s(&local, &seconds);
#else
  localtime_r(&seconds, &local);
#endif
  char buffer[64];
  std::strftime(buffer, sizeof(buffer), pattern, &local);
  return buffer;
}

// Look up a key in the active language, falling back to zh then the key
// itself; substitutes "{i}" and "{i:pattern}" placeholders from args.
inline std::string t(const std::string& key, const std::vector<Arg>& args = {}) {
  const auto& table_ref = g_language == "en" ? en_table() : zh_table();
  auto it = table_ref.find(key);
  if (it == table_ref.end()) {
    it = zh_table().find(key);
    if (it == zh_table().end()) return key;
  }
  std::string s = it->second;
  for (std::size_t i = 0; i < args.size(); ++i) {
    const Arg& arg = args[i];
    if (arg.is_time) {
      s = replace_first(std::move(s), "{" + std::to_string(i) + ":yyyy-MM-dd}",
                        format_epoch(arg.epoch_ms, "%Y-%m-%d"));
      s = replace_first(std::move(s), "{" + std::to_string(i) + ":HH:mm}",
                        format_epoch(arg.epoch_ms, "%H:%M"));
      s = replace_first(std::move(s), "{" + std::to_string(i) + ":HH:mm:ss}",
                        format_epoch(arg.epoch_ms, "%H:%M:%S"));
    }
    s = replace_first(std::move(s), "{" + std::to_string(i) + "}",
                      arg.is_time ? format_epoch(arg.epoch_ms, "%Y-%m-%d")
                                  : arg.text);
  }
  return s;
}

// Pick a random line from a copy pool emitted as indexed keys
// ("Prefix0" ... "Prefix{count-1}").
inline std::string pick(const std::string& prefix, int count) {
  if (count <= 0) return prefix;
  static std::mt19937 generator{std::random_device{}()};
  std::uniform_int_distribution<int> dist(0, count - 1);
  return t(prefix + std::to_string(dist(generator)));
}

}  // namespace l10n

#endif  // ${guard}
`;

const content = header("MOVEBIT_GENERATED_STRINGS_H");
writeFileSync("linux/GeneratedStrings.hpp", content);
writeFileSync("windows/GeneratedStrings.h", content);
console.log(
  `GeneratedStrings: ${zhKeys.length} zh / ${enKeys.length} en keys -> linux/GeneratedStrings.hpp, windows/GeneratedStrings.h`,
);
