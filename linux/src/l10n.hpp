// Host-side i18n loader over Rivet's GeneratedI18n table (issue #66).
// Same call surface the GTK host has always used — t(key, args) and
// pick(prefix, count) — but keyed by the dotted keys from the project's
// shared/i18n/{zh,en}.json single source, which `raco rivet i18n`
// verifies and embeds; the hand-grown PascalCase tables are gone.
#pragma once

#include <map>
#include <random>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

#include "GeneratedI18n.hpp"

namespace l10n {

inline std::string g_language = "zh";

struct Arg {
  std::string text;
  std::int64_t epoch_ms = 0;
  bool is_time = false;
};

inline Arg A(std::string s) { Arg a; a.text = std::move(s); return a; }
inline Arg A(const char* s) { Arg a; a.text = s; return a; }
inline Arg A(std::int64_t n) { Arg a; a.text = std::to_string(n); return a; }
inline Arg T(std::int64_t epoch_ms) { Arg a; a.epoch_ms = epoch_ms; a.is_time = true; return a; }

// One-shot lookup index over the generated table; kI18nTable is constexpr
// so the map is built once on first use and shared by every call site.
inline const std::map<std::string, std::pair<std::string_view, std::string_view>>& table_index() {
  static const std::map<std::string, std::pair<std::string_view, std::string_view>> index = [] {
    std::map<std::string, std::pair<std::string_view, std::string_view>> m;
    for (const auto& entry : rivet_app::kI18nTable) {
      m.emplace(std::string(entry.key),
                std::make_pair(entry.zh, entry.en));
    }
    return m;
  }();
  return index;
}

inline std::string replace_first(std::string s, const std::string& from,
                                 const std::string& to) {
  if (!from.empty()) {
    const auto pos = s.find(from);
    if (pos != std::string::npos) s.replace(pos, from.size(), to);
  }
  return s;
}

inline std::string format_epoch(std::int64_t ms, const char* pattern) {
  std::time_t seconds = static_cast<std::time_t>(ms / 1000);
  std::tm local{};
  localtime_r(&seconds, &local);
  char buffer[64];
  std::strftime(buffer, sizeof(buffer), pattern, &local);
  return buffer;
}

// Active language first, the other language as fallback, then the key —
// mirroring Rivet's own loader contract byte for byte.
inline std::string t(const std::string& key, const std::vector<Arg>& args = {}) {
  const auto& index = table_index();
  const auto it = index.find(key);
  if (it == index.end()) return key;
  const bool en = g_language == "en";
  std::string_view value = en ? it->second.second : it->second.first;
  if (value.empty()) {
    value = en ? it->second.first : it->second.second;
    if (value.empty()) return key;
  }
  std::string s{value};
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

// Picks one variation from a dotted pool prefix: pick("copy.microPool", 9)
// resolves e.g. "copy.microPool.3". Same uniform draw as before.
inline std::string pick(const std::string& prefix, int count) {
  if (count <= 0) return t(prefix);
  static std::mt19937 generator{std::random_device{}()};
  std::uniform_int_distribution<int> dist(0, count - 1);
  return t(prefix + "." + std::to_string(dist(generator)));
}

}  // namespace l10n
