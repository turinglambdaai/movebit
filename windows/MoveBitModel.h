#pragma once

// Shared host-side model for the WinUI MoveBit host: copy pools, duration
// formatting, and the client-side history/insight slicing. Mirrors the Swift
// MoveBitModel/CopyPool and the GTK host helpers — all policy still lives in
// the backend; this only formats what the backend publishes.

#include "GeneratedBackend.hpp"
#include "GeneratedStrings.h"

#include <algorithm>
#include <cstdint>
#include <ctime>
#include <map>
#include <optional>
#include <string>
#include <vector>

namespace movebit {

inline std::int64_t now_ms() {
  return static_cast<std::int64_t>(std::time(nullptr)) * 1000;
}

inline bool late_night() {
  std::time_t t = std::time(nullptr);
  std::tm local{};
  localtime_s(&local, &t);
  return local.tm_hour >= 22 || local.tm_hour < 6;
}

/// "45m" / "2h05m" (parity with MainViewModel.FormatMinutes).
inline std::string format_minutes(std::int64_t total) {
  total = std::max<std::int64_t>(0, total);
  if (total < 60) return std::to_string(total) + "m";
  auto m = std::to_string(total % 60);
  if (m.size() < 2) m = "0" + m;
  return std::to_string(total / 60) + "h" + m + "m";
}

inline std::string pad2(std::int64_t n) {
  auto s = std::to_string(n);
  if (s.size() < 2) s = "0" + s;
  return s;
}

/// Localized big-number duration ("2 hr 05 min" / "45 min").
inline std::string format_duration(std::int64_t total) {
  total = std::max<std::int64_t>(0, total);
  return total >= 60
             ? l10n::t("AppHourMin", {l10n::A(total / 60), l10n::A(pad2(total % 60))})
             : l10n::t("AppMinutes", {l10n::A(total)});
}

/// Compact axis label: "·" for zero (parity with FormatMinutesCompact).
inline std::string format_compact(std::int64_t total) {
  if (total == 0) return "·";
  return total >= 60 ? std::to_string(total / 60) + "h"
                     : std::to_string(total) + "m";
}

inline std::string pick_water() {
  return late_night() ? l10n::pick("CopyLateNightWaterPool", 3)
                      : l10n::pick("CopyWaterPool", 8);
}

inline std::string pick_micro() {
  return late_night() ? l10n::pick("CopyLateNightMicroPool", 3)
                      : l10n::pick("CopyMicroPool", 9);
}

inline std::string pick_break_hint() {
  return l10n::pick("CopyBreakHintPool", 10);
}

inline bool celebrated(std::int64_t count) {
  for (std::int64_t at : {3, 5, 8}) {
    if (count == at) return true;
  }
  return false;
}

inline std::string day_key(std::int64_t epoch_ms) {
  return l10n::format_epoch(epoch_ms, "%Y-%m-%d");
}

inline std::string resolve_language(std::string const& configured) {
  if (configured != "auto") return configured;
  char lang[128] = {};
  size_t size = 0;
  getenv_s(&size, lang, sizeof(lang), "LANG");
  std::string value = size > 0 ? lang : "en";
  return value.rfind("zh", 0) == 0 ? "zh" : "en";
}

struct DaySlice {
  rivet_app::DayStats stats;
  std::int64_t day_start_ms;
  bool is_today;
};

/// Zero-filled day list ending today; today's entry is the live state.
template <typename TodayFn, typename HistoryFn>
inline std::vector<DaySlice> history_slice(int days, TodayFn today_fn,
                                           HistoryFn history_fn) {
  std::vector<DaySlice> slices;
  auto const today = today_fn();
  std::string const today_key =
      today.date.empty() ? day_key(now_ms()) : today.date;
  std::map<std::string, rivet_app::DayStats> by_date;
  for (auto const& d : history_fn()) by_date.emplace(d.date, d);

  std::time_t secs = static_cast<std::time_t>(now_ms() / 1000);
  std::tm local{};
  localtime_s(&local, &secs);
  local.tm_hour = 0;
  local.tm_min = 0;
  local.tm_sec = 0;
  std::int64_t const today_start =
      static_cast<std::int64_t>(std::mktime(&local)) * 1000;

  for (int offset = days - 1; offset >= 0; --offset) {
    std::int64_t const day_start =
        today_start - static_cast<std::int64_t>(offset) * 86400000;
    std::string const key = day_key(day_start);
    bool const is_today = key == today_key;
    auto it = by_date.find(key);
    rivet_app::DayStats stats =
        is_today ? today
                 : (it != by_date.end()
                        ? it->second
                        : rivet_app::DayStats{key, 0, 0, 0, 0, 0});
    slices.push_back(DaySlice{stats, day_start, is_today});
  }
  return slices;
}

struct InsightSummary {
  std::int64_t longest;
  std::int64_t average;
  rivet_app::DayStats busiest;
  bool has_busiest;
};

template <typename Slices>
inline InsightSummary insight_summary(Slices const& slices) {
  InsightSummary s{0, 0, rivet_app::DayStats{}, false};
  std::int64_t active_total = 0;
  int active_days = 0;
  for (auto const& slice : slices) {
    s.longest = std::max(s.longest, slice.stats.longest_session_mins);
    if (slice.stats.active_mins > 0) {
      active_total += slice.stats.active_mins;
      ++active_days;
      if (!s.has_busiest || slice.stats.active_mins > s.busiest.active_mins) {
        s.busiest = slice.stats;
        s.has_busiest = true;
      }
    }
  }
  if (active_days > 0) {
    s.average = (active_total + active_days / 2) / active_days;
  }
  return s;
}

struct Model {
  rivet_app::ReminderConfig config{
      45, 30, 5, true, 5, 20, 10, true, 30, 20, true, true, false, "auto"};
  rivet_app::DayStats today{"", 0, 0, 0, 0, 0};
  rivet_app::PauseState paused{false, std::nullopt};
  std::vector<rivet_app::LoopInfo> loops;
  std::optional<rivet_app::BreakState> break_active;
  std::vector<rivet_app::DayStats> history;
  int history_days = 7;
  bool autostart = false;
  std::string data_dir;
  std::string config_status;

  std::optional<rivet_app::LoopInfo> loop(rivet_app::ReminderKind kind) const {
    for (auto const& l : loops) {
      if (l.kind == kind) return l;
    }
    return std::nullopt;
  }
  bool is_paused() const { return paused.paused; }
  std::int64_t current_session_mins() const {
    auto sit = loop(rivet_app::ReminderKind::sit);
    return sit.has_value() ? sit->accumulated_mins : 0;
  }
};

}  // namespace movebit
