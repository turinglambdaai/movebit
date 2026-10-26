// MoveBit Linux host — GTK4 windows over one embedded Racket CS backend.
// Port of the SwiftUI host (macos-host): every policy decision — tick,
// scheduling, break lifecycle, persistence — lives in the backend; this file
// renders state and forwards user actions. Boot the runtime off the UI
// thread, drive the product through the generated typed client
// (rivet_app::API), and dispatch every completion/event back to the main loop
// before touching widgets.
//
// Platform notes (documented gaps vs the macOS host):
//   • No tray: the StatusNotifierItem contract is upstream issue #118; the
//     main window is the hub (closing it flushes and quits).
//   • Toast/micro cards are frameless keep-above windows; GTK4 removed
//     toplevel positioning, so the WM chooses placement.
#include <gtk/gtk.h>
#include <pango/pangocairo.h>

#include <algorithm>
#include <atomic>
#include <cerrno>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <filesystem>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <unistd.h>
#include <vector>

#include "GeneratedBackend.hpp"
#include "GeneratedStrings.hpp"
#include "system_services.hpp"

namespace {

// ---- copy/format helpers (parity with the Swift CopyPool/CopyFormat) ------

std::int64_t now_ms() {
  struct timespec ts;
  clock_gettime(CLOCK_REALTIME, &ts);
  return static_cast<std::int64_t>(ts.tv_sec) * 1000 +
         static_cast<std::int64_t>(ts.tv_nsec) / 1000000;
}

bool late_night() {
  std::time_t t = std::time(nullptr);
  std::tm local{};
#if defined(_WIN32)
  localtime_s(&local, &t);
#else
  localtime_r(&t, &local);
#endif
  return local.tm_hour >= 22 || local.tm_hour < 6;
}

/// "45m" / "2h05m" (parity with MainViewModel.FormatMinutes).
std::string format_minutes(std::int64_t total) {
  total = std::max<std::int64_t>(0, total);
  if (total < 60) return std::to_string(total) + "m";
  std::string h = std::to_string(total / 60);
  std::string m = std::to_string(total % 60);
  if (m.size() < 2) m = "0" + m;
  return h + "h" + m + "m";
}

std::string pad2(std::int64_t n) {
  std::string s = std::to_string(n);
  if (s.size() < 2) s = "0" + s;
  return s;
}

/// Localized big-number duration ("2 hr 05 min" / "45 min").
std::string format_duration(std::int64_t total) {
  total = std::max<std::int64_t>(0, total);
  return total >= 60
             ? l10n::t("AppHourMin", {l10n::A(total / 60), l10n::A(pad2(total % 60))})
             : l10n::t("AppMinutes", {l10n::A(total)});
}

/// Compact axis label: "·" for zero (parity with FormatMinutesCompact).
std::string format_compact(std::int64_t total) {
  if (total == 0) return "·";
  return total >= 60 ? std::to_string(total / 60) + "h"
                     : std::to_string(total) + "m";
}

std::string pick_water() {
  return late_night() ? l10n::pick("CopyLateNightWaterPool", 3)
                      : l10n::pick("CopyWaterPool", 8);
}

std::string pick_micro() {
  return late_night() ? l10n::pick("CopyLateNightMicroPool", 3)
                      : l10n::pick("CopyMicroPool", 9);
}

std::string day_key(std::int64_t epoch_ms) {
  return l10n::format_epoch(epoch_ms, "%Y-%m-%d");
}

// ---- model ----------------------------------------------------------------

struct Model {
  rivet_app::ReminderConfig config{
      45, 30, 5, 10, true, 30, 20, true, true, true, false, "auto"};
  rivet_app::DayStats today{"", 0, 0, 0, 0, 0};
  rivet_app::PauseState paused{false, std::nullopt};
  std::vector<rivet_app::LoopInfo> loops;
  // Cues currently pending or on screen (backend reconciliation view).
  std::vector<rivet_app::HealthCue> pending_cues;
  bool quiet = false;
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

struct AppState {
  GtkWindow* window{nullptr};
  GtkButton* pause_button{nullptr};
  GtkBox* content_root{nullptr};  // cleared + rebuilt on language change

  std::unique_ptr<rivet::linux_runtime::Backend> backend;
  std::unique_ptr<rivet_app::API> api;
  std::mutex startup_mutex;
  std::thread startup_thread;
  std::unique_ptr<rivet::linux_runtime::Backend> startup_backend;
  std::string startup_error;
  std::atomic<bool> shutting_down{false};

  Model model;
  std::string language{"zh"};
  bool updating_settings{false};

  // Toast (bottom reminder card). cue_id carries the v1 lifecycle payload;
  // 0 marks a plain informational toast without actions.
  GtkWindow* toast_window{nullptr};
  guint toast_timeout_id{0};
  rivet_app::ReminderKind toast_accent = rivet_app::ReminderKind::sit;
  std::int64_t toast_cue_id = 0;
  GtkLabel* toast_title_label{nullptr};
  GtkBox* toast_lines_box{nullptr};
  GtkButton* toast_snooze_button{nullptr};

  // Micro eye-rest card.
  GtkWindow* micro_window{nullptr};
  guint micro_timeout_id{0};
  double micro_progress = 1.0;
  std::int64_t micro_total_ms = 1000;
  std::int64_t micro_cue_id = 0;
  GtkProgressBar* micro_bar{nullptr};

  // Onboarding wizard.
  GtkWindow* onboarding_window{nullptr};
  int onboarding_page = 0;
  std::int64_t onboarding_water = 30;
  std::int64_t onboarding_sit = 45;
  bool onboarding_sound = true;
  GtkStack* onboarding_stack{nullptr};
  GtkButton* onboarding_skip{nullptr};
  GtkButton* onboarding_next{nullptr};
  GtkLabel* onboarding_pact_summary{nullptr};
  GtkLabel* onboarding_dots{nullptr};

  void set_status(std::string const& text) {
    // Startup errors surface in the window subtitle position; once the main
    // content exists the status line doubles as the config-status footer.
    if (status_label != nullptr) {
      gtk_label_set_text(status_label, text.c_str());
    }
  }

  GtkLabel* status_label{nullptr};
};

AppState g_state;

std::string resolve_language(std::string const& configured) {
  if (configured != "auto") return configured;
  const char* lang = getenv("LANG");
  std::string l = lang != nullptr ? lang : "en";
  return l.rfind("zh", 0) == 0 ? "zh" : "en";
}

void apply_language() {
  l10n::g_language = g_state.language;
}

struct DaySlice {
  rivet_app::DayStats stats;
  std::int64_t day_start_ms;
  bool is_today;
};

/// Zero-filled day list ending today; today's entry is the live state.
std::vector<DaySlice> history_slice(int days) {
  std::vector<DaySlice> slices;
  std::string const today_key =
      g_state.model.today.date.empty() ? day_key(now_ms())
                                       : g_state.model.today.date;
  std::map<std::string, rivet_app::DayStats> by_date;
  for (auto const& d : g_state.model.history) {
    by_date.emplace(d.date, d);
  }
  // Local midnight of today, then walk back by whole days.
  std::time_t secs = static_cast<std::time_t>(now_ms() / 1000);
  std::tm local{};
#if defined(_WIN32)
  localtime_s(&local, &secs);
#else
  localtime_r(&secs, &local);
#endif
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
        is_today ? g_state.model.today
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
  rivet_app::DayStats busiest;  // date empty when none
  bool has_busiest;
};

/// 30-day aggregates over active days (parity with RebuildInsights).
InsightSummary insight_summary(int days) {
  auto slices = history_slice(days);
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

// ---- UI dispatch (completions/events arrive on the backend reader thread) --

struct UiTask {
  std::function<void()> run;
};

int run_ui_task(gpointer user_data) {
  auto* task = static_cast<UiTask*>(user_data);
  task->run();
  delete task;
  return G_SOURCE_REMOVE;
}

void dispatch_ui(std::function<void()> run) {
  g_idle_add(run_ui_task, new UiTask{std::move(run)});
}

template <typename T, typename Then>
void on_result(rivet_app::Result<T> result, Then then) {
  dispatch_ui([result = std::move(result), then = std::move(then)]() mutable {
    if (!result.succeeded()) {
      try {
        std::rethrow_exception(result.error);
      } catch (std::exception const& e) {
        g_state.set_status(e.what());
      }
      return;
    }
    try {
      then(result.get());
    } catch (std::exception const& e) {
      g_state.set_status(e.what());
    }
  });
}

/// Void-result variant: `then` takes no argument (there is nothing to unwrap).
template <typename Then>
void on_result(rivet_app::Result<void> result, Then then) {
  dispatch_ui([result = std::move(result), then = std::move(then)]() mutable {
    if (!result.succeeded()) {
      try {
        std::rethrow_exception(result.error);
      } catch (std::exception const& e) {
        g_state.set_status(e.what());
      }
      return;
    }
    try {
      then();
    } catch (std::exception const& e) {
      g_state.set_status(e.what());
    }
  });
}

// ---- main window rendering --------------------------------------------------

GtkWidget* make_card();
void build_main_content();
void refresh_pause_button();
void refresh_today_card();
void refresh_history_card();
void refresh_insight_card();
void refresh_settings_widgets();
void refresh_status_label();
void schedule_chart_redraw();

// Main-window widgets, rebuilt with the content.
struct MainWidgets {
  GtkLabel* today_duration{nullptr};
  GtkLabel* today_status{nullptr};
  GtkProgressBar* sit_bar{nullptr};
  GtkLabel* sit_cycle_text{nullptr};
  GtkLabel* today_sit{nullptr};
  GtkLabel* today_water{nullptr};
  GtkLabel* today_micro{nullptr};
  GtkLabel* hist_title{nullptr};
  GtkLabel* hist_total{nullptr};
  GtkDrawingArea* hist_chart{nullptr};
  GtkDropDown* hist_range{nullptr};
  GtkLabel* insight_metrics[4]{nullptr, nullptr, nullptr, nullptr};
  GtkLabel* insight_busiest{nullptr};
  GtkLabel* insight_sedentary{nullptr};
  GtkLabel* footer{nullptr};
  GtkLabel* about_version{nullptr};
  GtkButton* updates_button{nullptr};
  GtkLabel* updates_hint{nullptr};
  GtkLabel* config_status{nullptr};

  // Settings widgets (kept to apply config without re-signaling).
  GtkSpinButton* sit_spin{nullptr};
  GtkSpinButton* water_spin{nullptr};
  GtkSpinButton* away_spin{nullptr};
  GtkSpinButton* snooze_spin{nullptr};
  GtkSpinButton* micro_interval_spin{nullptr};
  GtkSpinButton* micro_duration_spin{nullptr};
  GtkSwitch* motion_switch{nullptr};
  GtkSwitch* micro_switch{nullptr};
  GtkSwitch* sound_switch{nullptr};
  GtkSwitch* autostart_switch{nullptr};
  GtkSwitch* updates_switch{nullptr};
  GtkDropDown* language_drop{nullptr};
} mw;

/// Optimistic save with a targeted mutation; the backend clamps and
/// re-announces via config-changed.
template <typename F>
void mutate_config(F mutate) {
  if (g_state.api == nullptr) return;
  auto const previous = g_state.model.config;
  auto draft = previous;
  mutate(draft);
  g_state.model.config = draft;
  g_state.language = resolve_language(draft.language);
  apply_language();
  (void)g_state.api->set_config_async(draft, [previous](rivet_app::Result<void> result) {
    dispatch_ui([result = std::move(result), previous]() mutable {
      if (result.succeeded()) {
        g_state.model.config_status =
            l10n::t("SetStatusSaved", {l10n::T(now_ms())});
      } else {
        g_state.model.config = previous;  // roll back the optimistic draft
        g_state.model.config_status = l10n::t("SetStatusSaveFailed");
      }
      refresh_status_label();
    });
  });
  refresh_status_label();
}

void set_autostart_host(bool enabled) {
  if (g_state.api == nullptr) return;
  g_state.model.autostart = enabled;
  (void)g_state.api->set_autostart_async(
      enabled, [](rivet_app::Result<void> result) {
        dispatch_ui([result = std::move(result)]() mutable {
          g_state.model.config_status =
              result.succeeded()
                  ? l10n::t("SetStatusApplied", {l10n::T(now_ms())})
                  : l10n::t("SetStatusSaveFailed");
          refresh_status_label();
        });
      });
}

GtkWidget* label_with(char const* text, char const* css) {
  auto* w = gtk_label_new(text);
  if (css != nullptr) gtk_widget_add_css_class(w, css);
  gtk_label_set_xalign(GTK_LABEL(w), 0.0f);
  return w;
}

/// GtkWidget*-friendly gtk_box_append: GTK classes are opaque, so upcasts
/// must go through the GTK_TYPE_CHECK_INSTANCE_CAST macros, which accept any
/// GTK pointer flavor.
template <typename Parent, typename Child>
void box_append(Parent* parent, Child* child) {
  gtk_box_append(GTK_BOX(parent), GTK_WIDGET(child));
}

GtkWidget* make_card() {
  auto* card = gtk_box_new(GTK_ORIENTATION_VERTICAL, 12);
  gtk_widget_add_css_class(card, "card");
  gtk_widget_set_margin_top(card, 18);
  gtk_widget_set_margin_bottom(card, 18);
  gtk_widget_set_margin_start(card, 18);
  gtk_widget_set_margin_end(card, 18);
  return card;
}

GtkSpinButton* add_stepper_row(GtkWidget* parent, char const* label, double value,
                               double min, double max, double step,
                               GCallback changed) {
  auto* row = GTK_BOX(gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8));
  box_append(row, label_with(label, "row-label"));
  auto* spin = gtk_spin_button_new_with_range(min, max, step);
  gtk_spin_button_set_value(GTK_SPIN_BUTTON(spin), value);
  gtk_spin_button_set_numeric(GTK_SPIN_BUTTON(spin), TRUE);
  g_signal_connect(spin, "value-changed", changed, nullptr);
  box_append(row, spin);
  box_append(parent, GTK_WIDGET(row));
  return GTK_SPIN_BUTTON(spin);
}

GtkSwitch* add_toggle_row(GtkWidget* parent, char const* label, bool on,
                          GCallback toggled) {
  auto* row = GTK_BOX(gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8));
  box_append(row, label_with(label, "row-label"));
  auto* sw = gtk_switch_new();
  gtk_switch_set_active(GTK_SWITCH(sw), on);
  g_signal_connect(sw, "state-set", toggled, nullptr);
  auto* box = GTK_BOX(gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 0));
  gtk_widget_set_hexpand(GTK_WIDGET(box), TRUE);
  gtk_widget_set_halign(GTK_WIDGET(box), GTK_ALIGN_END);
  box_append(box, sw);
  box_append(row, GTK_WIDGET(box));
  box_append(parent, GTK_WIDGET(row));
  return GTK_SWITCH(sw);
}

// ---- settings callbacks -----------------------------------------------------

void on_sit_changed(GtkSpinButton* spin, gpointer) {
  if (g_state.updating_settings) return;
  mutate_config([&](auto& d) {
    d.sit_reminder_minutes =
        static_cast<std::int64_t>(gtk_spin_button_get_value(spin));
  });
}
void on_water_changed(GtkSpinButton* spin, gpointer) {
  if (g_state.updating_settings) return;
  mutate_config([&](auto& d) {
    d.water_reminder_minutes =
        static_cast<std::int64_t>(gtk_spin_button_get_value(spin));
  });
}
void on_away_changed(GtkSpinButton* spin, gpointer) {
  if (g_state.updating_settings) return;
  mutate_config([&](auto& d) {
    d.away_reset_minutes =
        static_cast<std::int64_t>(gtk_spin_button_get_value(spin));
  });
}
void on_snooze_changed(GtkSpinButton* spin, gpointer) {
  if (g_state.updating_settings) return;
  mutate_config([&](auto& d) {
    d.snooze_minutes =
        static_cast<std::int64_t>(gtk_spin_button_get_value(spin));
  });
}
void on_micro_interval_changed(GtkSpinButton* spin, gpointer) {
  if (g_state.updating_settings) return;
  mutate_config([&](auto& d) {
    d.micro_break_interval_minutes =
        static_cast<std::int64_t>(gtk_spin_button_get_value(spin));
  });
}
void on_micro_duration_changed(GtkSpinButton* spin, gpointer) {
  if (g_state.updating_settings) return;
  mutate_config([&](auto& d) {
    d.micro_break_duration_seconds =
        static_cast<std::int64_t>(gtk_spin_button_get_value(spin));
  });
}
int on_motion_set(GtkSwitch*, gboolean state, gpointer) {
  if (!g_state.updating_settings) {
    mutate_config([&](auto& d) { d.motion_enabled = state != FALSE; });
  }
  return FALSE;
}
int on_micro_set(GtkSwitch*, gboolean state, gpointer) {
  if (!g_state.updating_settings) {
    mutate_config([&](auto& d) { d.micro_break_enabled = state != FALSE; });
  }
  return FALSE;
}
int on_sound_set(GtkSwitch*, gboolean state, gpointer) {
  if (!g_state.updating_settings) {
    mutate_config([&](auto& d) { d.sound_enabled = state != FALSE; });
  }
  return FALSE;
}
int on_autostart_set(GtkSwitch*, gboolean state, gpointer) {
  if (!g_state.updating_settings) set_autostart_host(state != FALSE);
  return FALSE;
}
int on_updates_set(GtkSwitch*, gboolean state, gpointer) {
  if (!g_state.updating_settings) {
    mutate_config([&](auto& d) { d.auto_check_updates = state != FALSE; });
  }
  return FALSE;
}
void on_language_changed(GtkDropDown* drop, GParamSpec*, gpointer) {
  if (g_state.updating_settings) return;
  static const char* values[] = {"auto", "zh", "en"};
  auto const selected = gtk_drop_down_get_selected(drop);
  if (selected < 0 || selected > 2) return;
  mutate_config([&](auto& d) { d.language = values[selected]; });
}
void on_hist_range_changed(GtkDropDown* drop, GParamSpec*, gpointer) {
  auto const selected = gtk_drop_down_get_selected(drop);
  g_state.model.history_days = selected == 1 ? 30 : 7;
  refresh_history_card();
}

// ---- history chart ----------------------------------------------------------

void draw_history_chart(GtkDrawingArea*, cairo_t* cr, int width, int height,
                        gpointer) {
  auto slices = history_slice(g_state.model.history_days);
  bool const is_month = g_state.model.history_days >= 30;
  std::int64_t max_minutes = 60;
  std::vector<std::int64_t> active;
  for (auto const& s : slices) {
    max_minutes = std::max(max_minutes, s.stats.active_mins);
    if (s.stats.active_mins > 0) active.push_back(s.stats.active_mins);
  }

  auto set_color = [&](char const* hex, double alpha = 1.0) {
    int r, g, b;
    sscanf(hex, "#%02x%02x%02x", &r, &g, &b);
    cairo_set_source_rgba(cr, r / 255.0, g / 255.0, b / 255.0, alpha);
  };
  auto const label_h = 16.0;
  auto const baseline = height - label_h;
  auto const bar_area = baseline - 6.0;
  auto const bar_min = 5.0;
  auto const bar_max = bar_area - 14.0;
  auto const col_w = static_cast<double>(width) / slices.size();

  auto draw_text = [&](char const* text, double x, double y,
                       char const* color, bool bold, double size) {
    PangoLayout* layout = pango_cairo_create_layout(cr);
    PangoAttrList* attrs = pango_attr_list_new();
    pango_attr_list_insert(attrs, pango_attr_size_new_absolute(
                                      static_cast<int>(size * PANGO_SCALE)));
    if (bold) {
      pango_attr_list_insert(attrs, pango_attr_weight_new(PANGO_WEIGHT_SEMIBOLD));
    }
    pango_layout_set_attributes(layout, attrs);
    pango_attr_list_unref(attrs);
    PangoFontDescription* font =
        pango_font_description_from_string("Sans");
    pango_layout_set_font_description(layout, font);
    pango_font_description_free(font);
    pango_layout_set_text(layout, text, -1);
    int tw = 0, th = 0;
    pango_layout_get_pixel_size(layout, &tw, &th);
    set_color(color);
    cairo_move_to(cr, x - tw / 2.0, y - th);
    pango_cairo_show_layout(cr, layout);
    g_object_unref(layout);
  };

  // Bars.
  for (std::size_t i = 0; i < slices.size(); ++i) {
    auto const& slice = slices[i];
    double const h = bar_min + (bar_max - bar_min) *
                                   static_cast<double>(slice.stats.active_mins) /
                                   static_cast<double>(max_minutes);
    double const bar_w = is_month ? 8.0 : 26.0;
    double const x = i * col_w + (col_w - bar_w) / 2.0;
    set_color(slice.is_today ? "#C25E3E" : "#D2C8B2");
    double const radius = 4.0;
    double const y = baseline - h;
    cairo_new_sub_path(cr);
    cairo_arc(cr, x + radius, y + radius, radius, M_PI, 1.5 * M_PI);
    cairo_line_to(cr, x + bar_w - radius, y);
    cairo_arc(cr, x + bar_w - radius, y + radius, radius, 1.5 * M_PI, 2 * M_PI);
    cairo_line_to(cr, x + bar_w, y + h);
    cairo_line_to(cr, x, y + h);
    cairo_close_path(cr);
    cairo_fill(cr);

    if (!is_month) {
      draw_text(format_compact(slice.stats.active_mins).c_str(),
                i * col_w + col_w / 2.0, y - 4,
                slice.is_today ? "#C25E3E" : "#A39B8D", slice.is_today, 10);
    }
    std::string day_label;
    if (slice.is_today) {
      day_label = l10n::t("HistToday");
    } else if (!is_month) {
      std::time_t t = static_cast<std::time_t>(slice.day_start_ms / 1000);
      std::tm lt{};
#if defined(_WIN32)
      localtime_s(&lt, &t);
#else
      localtime_r(&t, &lt);
#endif
      static char const* zh_days[] = {"日", "一", "二", "三", "四", "五", "六"};
      static char const* en_days[] = {"Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"};
      day_label = g_state.language == "en" ? en_days[lt.tm_wday]
                                           : zh_days[lt.tm_wday];
    } else if (!slice.stats.date.empty()) {
      int day = std::atoi(slice.stats.date.c_str() + 8);
      if (day % 5 == 0) day_label = std::to_string(day);
    }
    if (!day_label.empty()) {
      draw_text(day_label.c_str(), i * col_w + col_w / 2.0, height - 2,
                slice.is_today ? "#C25E3E" : "#6E6A5E", slice.is_today, 10.5);
    }
  }

  // Dashed daily-average line over active days.
  if (!active.empty()) {
    std::int64_t sum = 0;
    for (auto v : active) sum += v;
    std::int64_t const average = (sum + static_cast<std::int64_t>(active.size()) / 2) /
                                 static_cast<std::int64_t>(active.size());
    double const y = baseline - (bar_min + (bar_max - bar_min) *
                                            static_cast<double>(average) /
                                            static_cast<double>(max_minutes));
    set_color("#A39B8D", 0.55);
    cairo_set_line_width(cr, 1.0);
    double const dash[2] = {4.0, 3.0};
    cairo_set_dash(cr, dash, 2, 0);
    cairo_move_to(cr, 0, y);
    cairo_line_to(cr, width, y);
    cairo_stroke(cr);
    cairo_set_dash(cr, nullptr, 0, 0);
    draw_text(
        l10n::t("HistAvg", {l10n::A(format_compact(average))}).c_str(),
        width - 4, y - 2, "#A39B8D", false, 10);
  }

  // Baseline.
  set_color("#EAE2D6");
  cairo_set_line_width(cr, 1.0);
  cairo_move_to(cr, 0, baseline);
  cairo_line_to(cr, width, baseline);
  cairo_stroke(cr);
}

void schedule_chart_redraw() {
  if (mw.hist_chart != nullptr) {
    gtk_widget_queue_draw(GTK_WIDGET(mw.hist_chart));
  }
}

// ---- refresh ----------------------------------------------------------------

void refresh_status_label() {
  if (mw.config_status != nullptr) {
    gtk_label_set_text(
        mw.config_status,
        (g_state.model.config_status.empty()
             ? l10n::t("SetStatusDefault")
             : g_state.model.config_status)
            .c_str());
  }
}

void refresh_pause_button() {
  if (g_state.pause_button != nullptr) {
    gtk_button_set_label(
        g_state.pause_button,
        g_state.model.is_paused() ? l10n::t("TrayResume").c_str()
                                  : l10n::t("TrayPause").c_str());
  }
}

void refresh_today_card() {
  auto const& model = g_state.model;
  if (mw.today_duration != nullptr) {
    gtk_label_set_text(mw.today_duration, format_duration(model.today.active_mins).c_str());
  }
  if (mw.today_status != nullptr) {
    std::string status;
    if (model.is_paused() && model.paused.until_ms.has_value()) {
      status = l10n::t("TplPausedUntil", {l10n::T(*model.paused.until_ms)});
    } else if (model.is_paused()) {
      status = l10n::t("TplPausedUntil", {l10n::T(now_ms())});
    } else {
      status = l10n::t("TplRunning");
    }
    gtk_label_set_text(mw.today_status, status.c_str());
    gtk_widget_remove_css_class(GTK_WIDGET(mw.today_status), "ok");
    gtk_widget_remove_css_class(GTK_WIDGET(mw.today_status), "paused");
    gtk_widget_add_css_class(GTK_WIDGET(mw.today_status),
                             model.is_paused() ? "paused" : "ok");
  }
  if (mw.sit_bar != nullptr) {
    double fraction = 0;
    if (!model.is_paused() && model.config.sit_reminder_minutes > 0) {
      fraction = std::min(
          1.0, std::max(0.0, static_cast<double>(model.current_session_mins()) /
                                 static_cast<double>(model.config.sit_reminder_minutes)));
    }
    gtk_progress_bar_set_fraction(mw.sit_bar, fraction);
  }
  if (mw.sit_cycle_text != nullptr) {
    std::string text = "—";
    if (!model.is_paused()) {
      text = l10n::t("TplSitCycleMin", {l10n::A(model.current_session_mins()),
                                        l10n::A(model.config.sit_reminder_minutes)});
    }
    gtk_label_set_text(mw.sit_cycle_text, text.c_str());
  }
  if (mw.today_sit != nullptr) {
    gtk_label_set_text(mw.today_sit, std::to_string(model.today.sit_breaks).c_str());
  }
  if (mw.today_water != nullptr) {
    gtk_label_set_text(mw.today_water,
                       std::to_string(model.today.water_reminders).c_str());
  }
  if (mw.today_micro != nullptr) {
    gtk_label_set_text(mw.today_micro,
                       std::to_string(model.today.micro_breaks).c_str());
  }
}

void refresh_history_card() {
  if (mw.hist_title != nullptr) {
    gtk_label_set_text(mw.hist_title,
                       (g_state.model.history_days >= 30 ? l10n::t("HistRange30")
                                                         : l10n::t("HistRange7"))
                           .c_str());
  }
  if (mw.hist_total != nullptr) {
    auto slices = history_slice(g_state.model.history_days);
    std::int64_t total = 0;
    for (auto const& s : slices) total += s.stats.active_mins;
    std::string text =
        total >= 60 ? l10n::t("HistTotalLong", {l10n::A(total / 60), l10n::A(total % 60)})
                    : l10n::t("HistTotalShort", {l10n::A(total)});
    gtk_label_set_text(mw.hist_total, text.c_str());
  }
  schedule_chart_redraw();
}

void refresh_insight_card() {
  if (mw.insight_metrics[0] == nullptr) return;
  auto const& model = g_state.model;
  auto summary = insight_summary(30);
  gtk_label_set_text(mw.insight_metrics[0], format_minutes(model.current_session_mins()).c_str());
  gtk_label_set_text(mw.insight_metrics[1], format_minutes(model.today.longest_session_mins).c_str());
  gtk_label_set_text(mw.insight_metrics[2], format_minutes(summary.longest).c_str());
  gtk_label_set_text(mw.insight_metrics[3], format_minutes(summary.average).c_str());
  if (mw.insight_busiest != nullptr) {
    std::string text = l10n::t("InsightNone");
    if (summary.has_busiest && summary.busiest.date.size() >= 10) {
      text = summary.busiest.date.substr(5, 5) + " · " +
             format_minutes(summary.busiest.active_mins);
    }
    gtk_label_set_text(mw.insight_busiest, text.c_str());
  }
  if (mw.insight_sedentary != nullptr) {
    std::int64_t const longest = summary.longest;
    std::int64_t const sit = model.config.sit_reminder_minutes;
    std::string text;
    if (longest <= 0) {
      text = l10n::t("InsightNoData");
    } else if (longest >= sit * 2) {
      text = l10n::t("InsightLong2", {l10n::A(format_minutes(longest))});
    } else if (longest >= sit) {
      text = l10n::t("InsightLong1", {l10n::A(format_minutes(longest))});
    } else {
      text = l10n::t("InsightLong0", {l10n::A(format_minutes(longest))});
    }
    gtk_label_set_text(mw.insight_sedentary, text.c_str());
  }
}

void refresh_settings_widgets() {
  auto const& c = g_state.model.config;
  g_state.updating_settings = true;
  if (mw.sit_spin != nullptr) {
    gtk_spin_button_set_value(mw.sit_spin, c.sit_reminder_minutes);
    gtk_spin_button_set_value(mw.water_spin, c.water_reminder_minutes);
    gtk_spin_button_set_value(mw.away_spin, c.away_reset_minutes);
    gtk_spin_button_set_value(mw.snooze_spin, c.snooze_minutes);
    gtk_spin_button_set_value(mw.micro_interval_spin, c.micro_break_interval_minutes);
    gtk_spin_button_set_value(mw.micro_duration_spin, c.micro_break_duration_seconds);
    gtk_switch_set_active(mw.motion_switch, c.motion_enabled);
    gtk_switch_set_active(mw.micro_switch, c.micro_break_enabled);
    gtk_switch_set_active(mw.sound_switch, c.sound_enabled);
    gtk_switch_set_active(mw.autostart_switch, g_state.model.autostart);
    gtk_switch_set_active(mw.updates_switch, c.auto_check_updates);
    gtk_drop_down_set_selected(
        mw.language_drop,
        c.language == "zh" ? 1 : c.language == "en" ? 2 : 0);
  }
  g_state.updating_settings = false;
  refresh_status_label();
}

void refresh_footer() {
  if (mw.footer != nullptr) {
    gtk_label_set_text(
        mw.footer,
        g_state.model.data_dir.empty()
            ? ""
            : l10n::t("MainConfigPath", {l10n::A(g_state.model.data_dir)}).c_str());
  }
  if (mw.about_version != nullptr) {
    gtk_label_set_text(mw.about_version,
                       l10n::t("UpdCurrentVersion", {l10n::A(rivet_app::kVersion)}).c_str());
  }
}

void refresh_all() {
  refresh_pause_button();
  refresh_today_card();
  refresh_history_card();
  refresh_insight_card();
  refresh_settings_widgets();
  refresh_footer();
}

// ---- build main content ------------------------------------------------------

void flush_now_sync();
void reload_history();

void on_pause_clicked(GtkButton*, gpointer) {
  if (g_state.api == nullptr) return;
  if (g_state.model.is_paused()) {
    (void)g_state.api->resume_async([](rivet_app::Result<void> r) {
      on_result(std::move(r), []() {});
    });
  } else {
    (void)g_state.api->pause_1h_async([](rivet_app::Result<void> r) {
      on_result(std::move(r), []() {});
    });
  }
}

void on_quit_clicked(GtkButton*, gpointer) {
  flush_now_sync();
  if (g_state.window != nullptr) gtk_window_destroy(g_state.window);
  g_state.window = nullptr;
}

// ---- updates (UPDATE.md) ----------------------------------------------------
//
// The backend verifies the signed feed and downloads the artifact; this host
// owns consent, progress, and the install shape's final dialog. Three shapes
// (parity with the taskly host): AppImage — the backend has already swapped
// the sibling .AppImage over the running image, so the dialog only offers
// the re-exec; deb/rpm — package-manager guidance, never downloads; portable
// tarball — manual swap hint plus the file's folder. Failure text is always
// selectable: a bug report starts from that string.

struct UpdateFlowState {
  guint poll_source{0};        // 400 ms update_state poll
  GtkWindow* progress_dialog{nullptr};
  GtkProgressBar* progress_bar{nullptr};
  GtkLabel* progress_label{nullptr};
  std::string pending_version;  // captured at consent for the progress title
  // Last polled backend phase: the cancel gesture fires only when the
  // backend really is still downloading (a cancel fired after "downloaded"
  // would START a new download — the toggle doubles as the start).
  std::string last_phase;
  std::int64_t download_started_ms{0};  // monotonic ms; 30-min backstop
  bool check_in_flight{false};
  bool download_in_flight{false};
  bool reflected_pending{false};  // one-shot silent-check surfacing
};

UpdateFlowState g_update;

// Install shape for the update flow: the AppImage runtime exports APPIMAGE
// pointing at the running image; deb/rpm payloads land under /opt/movebit
// (symlinked from /usr/bin) — those upgrade through the package manager;
// everything else is the portable tarball layout.
std::string install_shape() {
  char const* appimage = getenv("APPIMAGE");
  if (appimage != nullptr && appimage[0] != '\0') return "appimage";
  std::error_code ec;
  auto const exe = std::filesystem::read_symlink("/proc/self/exe", ec);
  if (!ec) {
    auto const path = exe.string();
    if (path.rfind("/opt/movebit/", 0) == 0 || path.rfind("/usr/", 0) == 0) {
      return "package";
    }
  }
  return "tarball";
}

void stop_update_poll() {
  if (g_update.poll_source != 0) {
    g_source_remove(g_update.poll_source);
    g_update.poll_source = 0;
  }
}

void close_update_progress() {
  if (g_update.progress_dialog != nullptr) {
    gtk_window_destroy(g_update.progress_dialog);
    g_update.progress_dialog = nullptr;
    g_update.progress_bar = nullptr;
    g_update.progress_label = nullptr;
  }
}

void show_update_message(std::string const& title, std::string const& body) {
  auto* dialog = gtk_dialog_new_with_buttons(
      title.c_str(), g_state.window,
      static_cast<GtkDialogFlags>(GTK_DIALOG_MODAL |
                                  GTK_DIALOG_DESTROY_WITH_PARENT),
      l10n::t("DialogClose").c_str(), GTK_RESPONSE_CLOSE, nullptr);
  auto* area = gtk_dialog_get_content_area(GTK_DIALOG(dialog));
  auto* text = gtk_label_new(body.c_str());
  gtk_label_set_wrap(GTK_LABEL(text), TRUE);
  // Error text must be copyable — a bug report starts from this string.
  gtk_label_set_selectable(GTK_LABEL(text), TRUE);
  gtk_label_set_max_width_chars(GTK_LABEL(text), 60);
  gtk_widget_set_margin_top(text, 12);
  gtk_widget_set_margin_bottom(text, 6);
  gtk_widget_set_margin_start(text, 12);
  gtk_widget_set_margin_end(text, 12);
  gtk_box_append(GTK_BOX(area), text);
  g_signal_connect(
      dialog, "response",
      G_CALLBACK(+[](GtkDialog* dialog, gint, gpointer) {
        gtk_window_destroy(GTK_WINDOW(dialog));
      }),
      nullptr);
  gtk_window_present(GTK_WINDOW(dialog));
}

void show_update_error(std::string const& message) {
  show_update_message(
      "MoveBit",
      message.empty() ? std::string(l10n::t("UpdCheckFailed")) : message);
}

void set_updates_hint(std::string const& text) {
  if (mw.updates_hint != nullptr) {
    gtk_label_set_text(mw.updates_hint, text.c_str());
  }
}

// AppImage: the backend already swapped the sibling .AppImage over the
// running image atomically (sidecar-checksummed), so the only step left is
// the user's consent to re-exec. Declining keeps the current (old) process;
// the next launch is the new version regardless.
void show_appimage_restart_dialog() {
  auto* dialog = gtk_dialog_new_with_buttons(
      l10n::t("UpdDownloadedTitle").c_str(), g_state.window,
      static_cast<GtkDialogFlags>(GTK_DIALOG_MODAL |
                                  GTK_DIALOG_DESTROY_WITH_PARENT),
      l10n::t("DialogCancel").c_str(), GTK_RESPONSE_CLOSE,
      l10n::t("UpdActionRestartInstall").c_str(), GTK_RESPONSE_ACCEPT,
      nullptr);
  auto* area = gtk_dialog_get_content_area(GTK_DIALOG(dialog));
  auto* hint = gtk_label_new(l10n::t("UpdAppImageHint").c_str());
  gtk_label_set_wrap(GTK_LABEL(hint), TRUE);
  gtk_label_set_max_width_chars(GTK_LABEL(hint), 60);
  gtk_widget_set_margin_top(hint, 12);
  gtk_widget_set_margin_bottom(hint, 6);
  gtk_widget_set_margin_start(hint, 12);
  gtk_widget_set_margin_end(hint, 12);
  gtk_box_append(GTK_BOX(area), hint);
  g_signal_connect(
      dialog, "response",
      G_CALLBACK(+[](GtkDialog* dialog, gint response, gpointer) {
        gtk_window_destroy(GTK_WINDOW(dialog));
        if (response != GTK_RESPONSE_ACCEPT) return;
        char const* self = getenv("APPIMAGE");
        if (self == nullptr || self[0] == '\0') return;
        execl(self, self, static_cast<char*>(nullptr));
        // execl only returns on failure — surface it, the old image keeps
        // running either way.
        show_update_error(std::string("re-exec failed: ") +
                          std::strerror(errno));
      }),
      nullptr);
  gtk_window_present(GTK_WINDOW(dialog));
}

// Tarball installs: hand off the verified archive. Installation stays manual
// (nothing is ever executed or moved by the host), so the dialog explains
// the step and points at the file's folder.
void show_downloaded_dialog(std::string const& path) {
  auto* dialog = gtk_dialog_new_with_buttons(
      l10n::t("UpdDownloadedTitle").c_str(), g_state.window,
      static_cast<GtkDialogFlags>(GTK_DIALOG_MODAL |
                                  GTK_DIALOG_DESTROY_WITH_PARENT),
      l10n::t("DialogClose").c_str(), GTK_RESPONSE_CLOSE, nullptr);
  auto* area = gtk_dialog_get_content_area(GTK_DIALOG(dialog));
  auto* content = gtk_box_new(GTK_ORIENTATION_VERTICAL, 8);
  gtk_widget_set_margin_top(content, 12);
  gtk_widget_set_margin_bottom(content, 6);
  gtk_widget_set_margin_start(content, 12);
  gtk_widget_set_margin_end(content, 12);
  gtk_box_append(GTK_BOX(area), content);

  auto* hint = gtk_label_new(l10n::t("UpdInstallHint").c_str());
  gtk_label_set_wrap(GTK_LABEL(hint), TRUE);
  gtk_label_set_max_width_chars(GTK_LABEL(hint), 60);
  gtk_box_append(GTK_BOX(content), hint);

  // The backend stages the archive under <data-dir>/updates/.
  if (!path.empty()) {
    auto* path_label = gtk_label_new(path.c_str());
    gtk_label_set_selectable(GTK_LABEL(path_label), TRUE);
    gtk_label_set_wrap(GTK_LABEL(path_label), TRUE);
    gtk_widget_add_css_class(path_label, "caption");
    gtk_box_append(GTK_BOX(content), path_label);

    auto* open = gtk_button_new_with_label(l10n::t("UpdOpenFolder").c_str());
    g_object_set_data_full(G_OBJECT(open), "update-open-path",
                           new std::string(path),
                           +[](gpointer data) {
                             delete static_cast<std::string*>(data);
                           });
    g_signal_connect(
        open, "clicked",
        G_CALLBACK(+[](GtkButton* button, gpointer) {
          auto const* p = static_cast<std::string const*>(
              g_object_get_data(G_OBJECT(button), "update-open-path"));
          if (p == nullptr || g_state.window == nullptr) return;
          // Parent directory of the file, POSIX paths only: plain string
          // arithmetic keeps this noexcept inside a GTK callback.
          auto const slash = p->find_last_of('/');
          if (slash == std::string::npos || slash == 0) return;
          std::string const dir = p->substr(0, slash);
          GFile* folder = g_file_new_for_path(dir.c_str());
          gchar* uri = g_file_get_uri(folder);
          gtk_show_uri(GTK_WINDOW(g_state.window), uri, GDK_CURRENT_TIME);
          g_free(uri);
          g_object_unref(folder);
        }),
        nullptr);
    gtk_box_append(GTK_BOX(content), open);
  }

  g_signal_connect(
      dialog, "response",
      G_CALLBACK(+[](GtkDialog* dialog, gint, gpointer) {
        gtk_window_destroy(GTK_WINDOW(dialog));
      }),
      nullptr);
  gtk_window_present(GTK_WINDOW(dialog));
}

int on_update_poll_tick(gpointer) {
  if (g_state.api == nullptr || g_update.progress_dialog == nullptr) {
    g_update.poll_source = 0;
    return G_SOURCE_REMOVE;
  }
  // Host-side backstop: the backend's own watchdog (stall 120 s / total
  // 30 min) normally lands the terminal state first; if the backend itself
  // is wedged, break out of an endless progress bar — fire the cancel
  // toggle so a half-alive backend resets too, then surface the failure.
  if (g_update.download_started_ms != 0 &&
      g_get_monotonic_time() / 1000 - g_update.download_started_ms >
          30LL * 60 * 1000) {
    if (g_state.api != nullptr && g_update.last_phase == "downloading") {
      (void)g_state.api->start_update_download_async(
          [](rivet_app::Result<void>) {});
    }
    stop_update_poll();
    close_update_progress();
    g_update.download_in_flight = false;
    g_update.download_started_ms = 0;
    g_update.poll_source = 0;
    set_updates_hint(l10n::t("UpdCheckFailed"));
    show_update_error(l10n::t("UpdCheckFailed"));
    return G_SOURCE_REMOVE;
  }
  (void)g_state.api->get_update_state_async(
      [](rivet_app::Result<rivet_app::UpdateState> result) {
        dispatch_ui([result = std::move(result)]() mutable {
          if (g_update.progress_dialog == nullptr) return;  // detached UI
          if (!result.succeeded()) return;  // keep polling through hiccups
          rivet_app::UpdateState const& state = result.get();
          g_update.last_phase = state.phase;
          auto const percent =
              std::clamp<std::int64_t>(state.percent, 0, 100);
          if (g_update.progress_bar != nullptr) {
            gtk_progress_bar_set_fraction(g_update.progress_bar,
                                          static_cast<double>(percent) /
                                              100.0);
          }
          if (g_update.progress_label != nullptr) {
            gtk_label_set_text(
                g_update.progress_label,
                l10n::t("UpdDownloadingPct",
                        {l10n::A(g_update.pending_version),
                         l10n::A(percent)})
                    .c_str());
          }
          if (state.phase != "downloaded" && state.phase != "error" &&
              state.phase != "idle") {
            return;
          }
          stop_update_poll();
          close_update_progress();
          g_update.download_in_flight = false;
          g_update.download_started_ms = 0;
          if (state.phase == "idle") {
            // The cancel gesture landed: a cancel is not a failure — the
            // hint says so and the update stays one click away.
            set_updates_hint(l10n::t("UpdCancelled"));
            return;
          }
          if (state.phase == "error") {
            set_updates_hint(state.message.value_or("").empty()
                                 ? std::string(l10n::t("UpdCheckFailed"))
                                 : state.message.value_or(""));
            show_update_error(state.message.value_or(""));
            return;
          }
          if (install_shape() == "appimage") {
            show_appimage_restart_dialog();
            return;
          }
          show_downloaded_dialog(state.downloaded_path.value_or(""));
        });
      });
  return G_SOURCE_CONTINUE;
}

void start_update_download() {
  if (g_state.api == nullptr || g_update.download_in_flight) return;
  g_update.download_in_flight = true;
  g_update.download_started_ms = g_get_monotonic_time() / 1000;
  g_update.last_phase = "downloading";

  auto* dialog = gtk_dialog_new_with_buttons(
      l10n::t("UpdDownloading", {l10n::A(g_update.pending_version)}).c_str(),
      g_state.window,
      static_cast<GtkDialogFlags>(GTK_DIALOG_MODAL |
                                  GTK_DIALOG_DESTROY_WITH_PARENT),
      l10n::t("DialogCancel").c_str(), GTK_RESPONSE_CANCEL, nullptr);
  auto* area = gtk_dialog_get_content_area(GTK_DIALOG(dialog));
  auto* content = gtk_box_new(GTK_ORIENTATION_VERTICAL, 8);
  gtk_widget_set_margin_top(content, 12);
  gtk_widget_set_margin_bottom(content, 6);
  gtk_widget_set_margin_start(content, 12);
  gtk_widget_set_margin_end(content, 12);
  gtk_box_append(GTK_BOX(area), content);
  auto* bar = gtk_progress_bar_new();
  gtk_widget_set_size_request(bar, 300, -1);
  gtk_box_append(GTK_BOX(content), bar);
  auto* label =
      gtk_label_new(l10n::t("UpdDownloading",
                            {l10n::A(g_update.pending_version)})
                        .c_str());
  gtk_box_append(GTK_BOX(content), label);

  // Cancel really cancels now: the start RPC doubles as the cancel gesture
  // (the backend's toggle contract). Fire it while the backend is still
  // downloading, detach the progress UI, and show the cancelled hint — the
  // update itself stays one click away (the offer was never withdrawn).
  g_signal_connect(
      dialog, "response",
      G_CALLBACK(+[](GtkDialog* dialog, gint, gpointer) {
        if (g_state.api != nullptr && g_update.last_phase == "downloading") {
          (void)g_state.api->start_update_download_async(
              [](rivet_app::Result<void>) {});
        }
        stop_update_poll();
        g_update.progress_dialog = nullptr;
        g_update.progress_bar = nullptr;
        g_update.progress_label = nullptr;
        g_update.download_in_flight = false;
        g_update.download_started_ms = 0;
        set_updates_hint(l10n::t("UpdCancelled"));
        gtk_window_destroy(GTK_WINDOW(dialog));
      }),
      nullptr);
  // DESTROY_WITH_PARENT: if the main window goes away the dialog dies with
  // it — clear the raw pointers and the poll so no tick touches dead
  // widgets.
  g_signal_connect(
      dialog, "destroy",
      G_CALLBACK(+[](GtkWidget*, gpointer) {
        g_update.progress_dialog = nullptr;
        g_update.progress_bar = nullptr;
        g_update.progress_label = nullptr;
        stop_update_poll();
      }),
      nullptr);
  gtk_window_present(GTK_WINDOW(dialog));
  g_update.progress_dialog = GTK_WINDOW(dialog);
  g_update.progress_bar = GTK_PROGRESS_BAR(bar);
  g_update.progress_label = GTK_LABEL(label);
  g_update.poll_source = g_timeout_add(400, on_update_poll_tick, nullptr);

  (void)g_state.api->start_update_download_async(
      [](rivet_app::Result<void> result) {
        dispatch_ui([result = std::move(result)]() mutable {
          if (g_update.progress_dialog == nullptr) return;  // detached UI
          if (result.succeeded()) return;  // the poll owns the rest
          // The download never started; stop staring at a 0% bar.
          stop_update_poll();
          close_update_progress();
          g_update.download_in_flight = false;
          g_update.download_started_ms = 0;
          std::string message = "unknown failure";
          try {
            std::rethrow_exception(result.error);
          } catch (std::exception const& e) {
            message = e.what();
          } catch (...) {
          }
          set_updates_hint(message);
          show_update_error(message);
        });
      });
}

void show_update_consent(rivet_app::UpdateCheck const& check) {
  std::string const version = check.available_version.value_or("?");
  if (install_shape() == "package") {
    // deb/rpm upgrade through the package manager; never download or exec.
    show_update_message(l10n::t("UpdAvailableTitle"),
                        l10n::t("UpdUsePackageManager"));
    return;
  }
  g_update.pending_version = version;
  auto* dialog = gtk_dialog_new_with_buttons(
      l10n::t("UpdAvailableTitle").c_str(), g_state.window,
      static_cast<GtkDialogFlags>(GTK_DIALOG_MODAL |
                                  GTK_DIALOG_DESTROY_WITH_PARENT),
      l10n::t("DialogCancel").c_str(), GTK_RESPONSE_CANCEL,
      l10n::t("UpdActionUpdate").c_str(), GTK_RESPONSE_ACCEPT, nullptr);
  auto* area = gtk_dialog_get_content_area(GTK_DIALOG(dialog));
  auto* text =
      gtk_label_new(l10n::t("UpdFound", {l10n::A(version)}).c_str());
  gtk_label_set_wrap(GTK_LABEL(text), TRUE);
  gtk_label_set_max_width_chars(GTK_LABEL(text), 60);
  gtk_widget_set_margin_top(text, 12);
  gtk_widget_set_margin_bottom(text, 6);
  gtk_widget_set_margin_start(text, 12);
  gtk_widget_set_margin_end(text, 12);
  gtk_box_append(GTK_BOX(area), text);
  g_signal_connect(
      dialog, "response",
      G_CALLBACK(+[](GtkDialog* dialog, gint response, gpointer) {
        bool const go = response == GTK_RESPONSE_ACCEPT;
        gtk_window_destroy(GTK_WINDOW(dialog));
        if (go) {
          start_update_download();
        }
      }),
      nullptr);
  gtk_window_present(GTK_WINDOW(dialog));
}

void on_check_updates_clicked(GtkButton*, gpointer) {
  if (g_state.api == nullptr || g_update.check_in_flight ||
      g_update.download_in_flight) {
    return;
  }
  g_update.check_in_flight = true;
  if (mw.updates_button != nullptr) {
    gtk_button_set_label(mw.updates_button, l10n::t("UpdCheckingShort").c_str());
    gtk_widget_set_sensitive(GTK_WIDGET(mw.updates_button), FALSE);
  }
  set_updates_hint(l10n::t("UpdChecking"));
  (void)g_state.api->check_updates_async(
      [](rivet_app::Result<rivet_app::UpdateCheck> result) {
        dispatch_ui([result = std::move(result)]() mutable {
          g_update.check_in_flight = false;
          if (mw.updates_button != nullptr) {
            gtk_button_set_label(mw.updates_button,
                                 l10n::t("AboutCheckUpdates").c_str());
            gtk_widget_set_sensitive(GTK_WIDGET(mw.updates_button), TRUE);
          }
          if (!result.succeeded()) {
            std::string message = "unknown failure";
            try {
              std::rethrow_exception(result.error);
            } catch (std::exception const& e) {
              message = e.what();
            } catch (...) {
            }
            set_updates_hint(message);
            show_update_error(message);
            return;
          }
          rivet_app::UpdateCheck const& check = result.get();
          if (check.status == "available") {
            set_updates_hint(
                l10n::t("UpdFound",
                        {l10n::A(check.available_version.value_or(""))}));
            show_update_consent(check);
            return;
          }
          if (check.status == "up-to-date") {
            set_updates_hint(l10n::t("UpdCurrentUpToDate",
                                     {l10n::A(check.current_version)}));
            return;
          }
          // "error": show the real reason — check failures are network or
          // signing conditions, not user mistakes.
          std::string const message = check.error.value_or("");
          set_updates_hint(message.empty()
                               ? std::string(l10n::t("UpdCheckFailed"))
                               : message);
          show_update_error(message);
        });
      });
}

// One-shot surfacing of the backend's silent auto-check (startup + every
// 6 h): a pending update turns into the About hint; everything else stays
// invisible.
void reflect_pending_update() {
  if (g_update.reflected_pending || g_state.api == nullptr) return;
  g_update.reflected_pending = true;
  (void)g_state.api->get_update_state_async(
      [](rivet_app::Result<rivet_app::UpdateState> result) {
        dispatch_ui([result = std::move(result)]() mutable {
          if (!result.succeeded()) return;
          rivet_app::UpdateState const& state = result.get();
          if (state.phase == "idle" && state.available_version.has_value()) {
            set_updates_hint(l10n::t(
                "UpdFound", {l10n::A(state.available_version.value_or(""))}));
          }
        });
      });
}

GtkWidget* counter_tile(char const* emoji, char const* label_text,
                        GtkLabel** value_out) {
  auto* box = gtk_box_new(GTK_ORIENTATION_VERTICAL, 2);
  auto* top = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 4);
  box_append(GTK_BOX(top), label_with(emoji, nullptr));
  auto* value = gtk_label_new("0");
  gtk_widget_add_css_class(value, "counter-value");
  box_append(GTK_BOX(top), value);
  box_append(box, top);
  box_append(box, label_with(label_text, "caption"));
  *value_out = GTK_LABEL(value);
  return box;
}

void build_main_content() {
  if (g_state.content_root == nullptr) return;

  // Clear previous content (language switch rebuilds everything).
  while (GtkWidget* child = gtk_widget_get_first_child(GTK_WIDGET(g_state.content_root))) {
    gtk_box_remove(g_state.content_root, child);
  }
  mw = MainWidgets{};

  auto* scroll = gtk_scrolled_window_new();
  gtk_widget_set_vexpand(scroll, TRUE);
  auto* columns = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 14);
  gtk_widget_set_margin_top(columns, 18);
  gtk_widget_set_margin_bottom(columns, 18);
  gtk_widget_set_margin_start(columns, 20);
  gtk_widget_set_margin_end(columns, 20);
  auto* left = gtk_box_new(GTK_ORIENTATION_VERTICAL, 14);
  gtk_widget_set_hexpand(left, TRUE);
  gtk_widget_set_valign(left, GTK_ALIGN_START);
  auto* right = gtk_box_new(GTK_ORIENTATION_VERTICAL, 14);
  gtk_widget_set_hexpand(right, TRUE);
  gtk_widget_set_valign(right, GTK_ALIGN_START);

  // --- today card ---
  {
    auto* card = make_card();
    auto* head = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
    box_append(GTK_BOX(head), label_with(l10n::t("TodayTitle").c_str(), "section"));
    auto* status = gtk_label_new("");
    gtk_widget_add_css_class(status, "status");
    gtk_widget_set_hexpand(status, TRUE);
    gtk_label_set_xalign(GTK_LABEL(status), 1.0f);
    box_append(GTK_BOX(head), status);
    mw.today_status = GTK_LABEL(status);
    box_append(GTK_BOX(card), head);

    auto* duration = gtk_label_new("0m");
    gtk_widget_add_css_class(duration, "big-number");
    gtk_label_set_xalign(GTK_LABEL(duration), 0.0f);
    box_append(GTK_BOX(card), duration);
    mw.today_duration = GTK_LABEL(duration);

    auto* bar = gtk_progress_bar_new();
    gtk_progress_bar_set_fraction(GTK_PROGRESS_BAR(bar), 0.0);
    gtk_widget_add_css_class(bar, "sit-bar");
    box_append(GTK_BOX(card), bar);
    mw.sit_bar = GTK_PROGRESS_BAR(bar);

    auto* cycle = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
    auto* cycle_left = gtk_box_new(GTK_ORIENTATION_VERTICAL, 2);
    auto* cycle_text = gtk_label_new("—");
    gtk_widget_add_css_class(cycle_text, "row-strong");
    gtk_label_set_xalign(GTK_LABEL(cycle_text), 0.0f);
    box_append(GTK_BOX(cycle_left), cycle_text);
    box_append(GTK_BOX(cycle_left), label_with(l10n::t("TodaySitCycle").c_str(), "caption"));
    gtk_widget_set_hexpand(cycle_left, TRUE);
    box_append(GTK_BOX(cycle), cycle_left);
    box_append(GTK_BOX(cycle), counter_tile("🚶", l10n::t("TodaySit").c_str(), &mw.today_sit));
    box_append(GTK_BOX(cycle), counter_tile("💧", l10n::t("TodayWater").c_str(), &mw.today_water));
    box_append(GTK_BOX(cycle), counter_tile("👀", l10n::t("TodayMicro").c_str(), &mw.today_micro));
    box_append(GTK_BOX(card), cycle);
    box_append(left, card);
  }

  // --- history card ---
  {
    auto* card = make_card();
    auto* head = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
    auto* head_left = gtk_box_new(GTK_ORIENTATION_VERTICAL, 2);
    auto* title = gtk_label_new(l10n::t("HistRange7").c_str());
    gtk_widget_add_css_class(title, "section");
    gtk_label_set_xalign(GTK_LABEL(title), 0.0f);
    box_append(GTK_BOX(head_left), title);
    auto* total = gtk_label_new("");
    gtk_widget_add_css_class(total, "row-strong");
    gtk_label_set_xalign(GTK_LABEL(total), 0.0f);
    box_append(GTK_BOX(head_left), total);
    gtk_widget_set_hexpand(head_left, TRUE);
    box_append(GTK_BOX(head), head_left);
    std::string const range7 = l10n::t("HistToggle7");
    std::string const range30 = l10n::t("HistToggle30");
    const char* const range_items[] = {range7.c_str(), range30.c_str(), nullptr};
    auto* range = gtk_drop_down_new_from_strings(range_items);
    gtk_drop_down_set_selected(GTK_DROP_DOWN(range), 0);
    g_signal_connect(range, "notify::selected", G_CALLBACK(on_hist_range_changed), nullptr);
    box_append(GTK_BOX(head), range);
    mw.hist_range = GTK_DROP_DOWN(range);
    mw.hist_title = GTK_LABEL(title);
    mw.hist_total = GTK_LABEL(total);
    box_append(GTK_BOX(card), head);

    auto* chart = gtk_drawing_area_new();
    gtk_widget_set_size_request(chart, -1, 140);
    gtk_drawing_area_set_draw_func(GTK_DRAWING_AREA(chart), draw_history_chart,
                                   nullptr, nullptr);
    box_append(GTK_BOX(card), chart);
    mw.hist_chart = GTK_DRAWING_AREA(chart);
    box_append(left, card);
  }

  // --- insight card ---
  {
    auto* card = make_card();
    box_append(GTK_BOX(card), label_with(l10n::t("InsightTitle").c_str(), "section"));
    auto* grid = gtk_grid_new();
    gtk_grid_set_column_spacing(GTK_GRID(grid), 10);
    gtk_grid_set_row_spacing(GTK_GRID(grid), 10);
    std::string const titles[4] = {l10n::t("InsightCurrent"),
                                   l10n::t("InsightTodayLongest"),
                                   l10n::t("InsightRecentLongest"),
                                   l10n::t("InsightRecentAvg")};
    for (int i = 0; i < 4; ++i) {
      auto* tile = gtk_box_new(GTK_ORIENTATION_VERTICAL, 3);
      gtk_widget_add_css_class(tile, "metric-tile");
      box_append(GTK_BOX(tile), label_with(titles[i].c_str(), "caption"));
      auto* value = gtk_label_new("0m");
      gtk_widget_add_css_class(value, "metric-value");
      gtk_label_set_xalign(GTK_LABEL(value), 0.0f);
      box_append(GTK_BOX(tile), value);
      gtk_grid_attach(GTK_GRID(grid), tile, i % 2, i / 2, 1, 1);
      mw.insight_metrics[i] = GTK_LABEL(value);
    }
    box_append(GTK_BOX(card), grid);
    auto* busiest = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
    box_append(GTK_BOX(busiest), label_with(l10n::t("InsightBusiest").c_str(), "caption"));
    auto* busiest_value = gtk_label_new("");
    box_append(GTK_BOX(busiest), busiest_value);
    mw.insight_busiest = GTK_LABEL(busiest_value);
    box_append(GTK_BOX(card), busiest);
    auto* sedentary = gtk_label_new("");
    gtk_widget_add_css_class(sedentary, "caption");
    gtk_label_set_wrap(GTK_LABEL(sedentary), TRUE);
    gtk_label_set_xalign(GTK_LABEL(sedentary), 0.0f);
    box_append(GTK_BOX(card), sedentary);
    mw.insight_sedentary = GTK_LABEL(sedentary);
    box_append(left, card);
  }

  // --- settings card ---
  {
    auto* card = make_card();
    auto* head = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
    box_append(GTK_BOX(head), label_with(l10n::t("SetTitle").c_str(), "section"));
    auto* status = gtk_label_new("");
    gtk_widget_add_css_class(status, "accent-label");
    gtk_widget_set_hexpand(status, TRUE);
    gtk_label_set_xalign(GTK_LABEL(status), 1.0f);
    gtk_label_set_wrap(GTK_LABEL(status), TRUE);
    box_append(GTK_BOX(head), status);
    mw.config_status = GTK_LABEL(status);
    box_append(GTK_BOX(card), head);
    box_append(GTK_BOX(card), label_with(l10n::t("SetIntro").c_str(), "caption"));

    auto& c = g_state.model.config;
    mw.sit_spin = add_stepper_row(card, l10n::t("SetSitInterval").c_str(),
                                  c.sit_reminder_minutes, 10, 240, 5,
                                  G_CALLBACK(on_sit_changed));
    mw.water_spin = add_stepper_row(card, l10n::t("SetWaterInterval").c_str(),
                                    c.water_reminder_minutes, 5, 180, 5,
                                    G_CALLBACK(on_water_changed));
    mw.away_spin = add_stepper_row(card, l10n::t("SetAwayReset").c_str(),
                                   c.away_reset_minutes, 1, 60, 1,
                                   G_CALLBACK(on_away_changed));
    mw.motion_switch = add_toggle_row(card, l10n::t("SetMotion").c_str(),
                                      c.motion_enabled,
                                      G_CALLBACK(on_motion_set));
    mw.snooze_spin = add_stepper_row(card, l10n::t("SetSnooze").c_str(),
                                     c.snooze_minutes, 5, 60, 5,
                                     G_CALLBACK(on_snooze_changed));
    mw.micro_switch = add_toggle_row(card, l10n::t("SetMicroToggle").c_str(),
                                     c.micro_break_enabled,
                                     G_CALLBACK(on_micro_set));
    auto* micro_row = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
    box_append(GTK_BOX(micro_row), label_with(l10n::t("SetMicroParams").c_str(), "row-label"));
    mw.micro_interval_spin = add_stepper_row(
        micro_row, "", c.micro_break_interval_minutes, 10, 60, 5,
        G_CALLBACK(on_micro_interval_changed));
    mw.micro_duration_spin = add_stepper_row(
        micro_row, "", c.micro_break_duration_seconds, 10, 60, 5,
        G_CALLBACK(on_micro_duration_changed));
    box_append(GTK_BOX(card), micro_row);
    mw.sound_switch = add_toggle_row(card, l10n::t("SetSound").c_str(),
                                     c.sound_enabled, G_CALLBACK(on_sound_set));
    mw.autostart_switch = add_toggle_row(
        card, l10n::t("SetAutostart").c_str(), g_state.model.autostart,
        G_CALLBACK(on_autostart_set));

    auto* lang_row = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
    box_append(GTK_BOX(lang_row), label_with(l10n::t("SetLanguage").c_str(), "row-label"));
    std::string const lang_auto = l10n::t("LangAuto");
    const char* const lang_items[] = {lang_auto.c_str(), "\u4e2d\u6587",
                                      "English", nullptr};
    auto* lang = gtk_drop_down_new_from_strings(lang_items);
    gtk_drop_down_set_selected(
        GTK_DROP_DOWN(lang),
        c.language == "zh" ? 1 : c.language == "en" ? 2 : 0);
    g_signal_connect(lang, "notify::selected", G_CALLBACK(on_language_changed), nullptr);
    box_append(GTK_BOX(lang_row), lang);
    mw.language_drop = GTK_DROP_DOWN(lang);
    box_append(GTK_BOX(card), lang_row);
    box_append(right, card);
  }

  // --- about card ---
  {
    auto* card = make_card();
    auto* version = gtk_label_new("");
    gtk_widget_add_css_class(version, "row-strong");
    gtk_label_set_xalign(GTK_LABEL(version), 0.0f);
    box_append(GTK_BOX(card), version);
    mw.about_version = GTK_LABEL(version);
    // Updates: manual check entry + a hint line for the state machine's
    // outcome (progress lives in its own dialog). The hint is selectable —
    // it may carry a failure message a bug report starts from.
    {
      auto* row = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
      auto* check_btn =
          gtk_button_new_with_label(l10n::t("AboutCheckUpdates").c_str());
      g_signal_connect(check_btn, "clicked",
                       G_CALLBACK(on_check_updates_clicked), nullptr);
      gtk_box_append(GTK_BOX(row), check_btn);
      mw.updates_button = GTK_BUTTON(check_btn);
      auto* hint = gtk_label_new("");
      gtk_label_set_wrap(GTK_LABEL(hint), TRUE);
      gtk_label_set_selectable(GTK_LABEL(hint), TRUE);
      gtk_label_set_max_width_chars(GTK_LABEL(hint), 40);
      gtk_label_set_xalign(GTK_LABEL(hint), 0.0f);
      gtk_widget_add_css_class(hint, "caption");
      gtk_widget_set_hexpand(hint, TRUE);
      gtk_box_append(GTK_BOX(row), hint);
      mw.updates_hint = GTK_LABEL(hint);
      box_append(GTK_BOX(card), row);
    }
    mw.updates_switch = add_toggle_row(card, l10n::t("AboutAutoUpdate").c_str(),
                                       g_state.model.config.auto_check_updates,
                                       G_CALLBACK(on_updates_set));
    box_append(GTK_BOX(card),
                   label_with(l10n::t("AboutAutoUpdateHint").c_str(), "caption"));
    // Website / issue tracker / license — the family About rows.
    auto* links = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 16);
    gtk_box_append(
        GTK_BOX(links),
        gtk_link_button_new_with_label("https://movebit.jrtx.site",
                                       l10n::t("AboutWebsite").c_str()));
    gtk_box_append(
        GTK_BOX(links),
        gtk_link_button_new_with_label(
            "https://github.com/turinglambdaai/movebit/issues",
            l10n::t("AboutReportIssue").c_str()));
    box_append(GTK_BOX(card), links);
    box_append(GTK_BOX(card),
               label_with(l10n::t("AboutLicense").c_str(), "caption"));
    box_append(right, card);
  }

  // --- footer (config path) ---
  {
    auto* footer = gtk_label_new("");
    gtk_widget_add_css_class(footer, "footer");
    gtk_label_set_xalign(GTK_LABEL(footer), 0.0f);
    box_append(right, footer);
    mw.footer = GTK_LABEL(footer);
  }

  box_append(GTK_BOX(columns), left);
  box_append(GTK_BOX(columns), right);
  gtk_scrolled_window_set_child(GTK_SCROLLED_WINDOW(scroll), columns);
  box_append(g_state.content_root, scroll);
  refresh_all();
}

void apply_global_css() {
  auto* provider = gtk_css_provider_new();
  gtk_css_provider_load_from_string(provider, R"CSS(
    .app-bg { background: #F5F2EB; }
    .card {
      background: #FFFFFF;
      border: 1px solid #EAE2D6;
      border-radius: 16px;
      padding: 6px 4px;
    }
    .section { font-size: 13px; color: #6E6A5E; }
    .caption { font-size: 11px; color: #6E6A5E; }
    .footer { font-size: 11px; color: #A39B8D; }
    .row-label { font-size: 13px; color: #26241F; }
    .row-strong { font-size: 14px; font-weight: 600; color: #26241F; }
    .big-number { font-size: 44px; font-weight: 700; color: #26241F; }
    .counter-value { font-size: 14px; font-weight: 600; color: #26241F; }
    .metric-tile { background: #EAE6DC; border-radius: 12px; padding: 12px; }
    .metric-value { font-size: 20px; font-weight: 600; color: #26241F; }
    .status { font-size: 12.5px; }
    .status.ok { color: #16A34A; }
    .status.paused { color: #EA580C; }
    .accent-label { font-size: 11.5px; color: #C25E3E; }
    .sit-bar { min-height: 9px; }
    .sit-bar progress { background: #C25E3E; border-radius: 4px; }
    .sit-bar trough { background: #EAE6DC; border-radius: 4px; }
                    border: 1px solid #3A3550; border-radius: 8px; padding: 7px 16px; }
                         background: #1A1730; border: 1px solid #5A527A;
                         border-radius: 8px; padding: 7px 16px; }
    .toast-bg { background: #FFFDFB; border-radius: 14px; border: 1px solid #E2DCCC; }
    .toast-title { font-size: 17px; font-weight: 600; color: #26241F; }
    .toast-line { font-size: 13.5px; color: #6E6A5E; }
    .toast-btn { font-size: 12.5px; color: #26241F; background: #F3EEE3;
                 border-radius: 7px; padding: 5px 10px; }
    .toast-btn-muted { font-size: 12.5px; color: #6E6A5E; background: transparent;
                       border: 1px solid #E2DCCC; border-radius: 7px; padding: 5px 10px; }
    .micro-bg { background: #FFFFFF; border-radius: 18px; border: 1px solid #EAE2D6; }
    .micro-title { font-size: 18px; font-weight: 600; color: #26241F; }
    .micro-hint { font-size: 11.5px; color: #A39B8D; }
    .micro-bar trough { background: #EAE6DC; border-radius: 2px; min-height: 4px; }
    .micro-bar progress { background: #C25E3E; border-radius: 2px; min-height: 4px; }
    .ob-bg { background: #F5F2EB; }
    .ob-card { background: #FFFFFF; border: 1px solid #EAE2D6;
               border-radius: 12px; padding: 14px; }
    .ob-card-selected { background: #FBEFE2; border: 1px solid #C25E3E;
                        border-radius: 12px; padding: 14px; }
    .ob-title { font-size: 22px; font-weight: 700; color: #26241F; }
    .ob-hello { font-size: 26px; font-weight: 700; color: #26241F; }
    .ob-text { font-size: 14.5px; color: #6E6A5E; }
    .ob-strong { font-size: 15px; font-weight: 600; color: #26241F; }
    .ob-caption { font-size: 11px; color: #A39B8D; }
    .ob-badge { font-size: 10.5px; color: #C25E3E; background: #FBEFE2;
                border-radius: 5px; padding: 2px 6px; }
    .ob-primary { font-size: 14px; color: #FFFFFF; background: #C25E3E;
                  border-radius: 8px; padding: 9px 18px; }
    .dot { font-size: 18px; color: #EAE6DC; }
    .dot-active { font-size: 18px; color: #C25E3E; }
  )CSS");
  gtk_style_context_add_provider_for_display(
      gdk_display_get_default(), GTK_STYLE_PROVIDER(provider),
      GTK_STYLE_PROVIDER_PRIORITY_APPLICATION);
  g_object_unref(provider);
}

// ---- toast --------------------------------------------------------------------

void on_toast_hide(GtkButton*, gpointer);
void cue_displayed(std::int64_t id);

void toast_dismiss() {
  g_state.toast_cue_id = 0;
  if (g_state.toast_timeout_id != 0) {
    g_source_remove(g_state.toast_timeout_id);
    g_state.toast_timeout_id = 0;
  }
  if (g_state.toast_window != nullptr) {
    gtk_window_destroy(g_state.toast_window);
    g_state.toast_window = nullptr;
    g_state.toast_title_label = nullptr;
    g_state.toast_lines_box = nullptr;
    g_state.toast_snooze_button = nullptr;
  }
}

void toast_restart_auto_close() {
  if (g_state.toast_timeout_id != 0) {
    g_source_remove(g_state.toast_timeout_id);
  }
  g_state.toast_timeout_id = g_timeout_add(
      9000,
      +[](gpointer) -> int {
        g_state.toast_timeout_id = 0;
        on_toast_hide(nullptr, nullptr);
        return G_SOURCE_REMOVE;
      },
      nullptr);
}

// Later: defers every kind merged into the cue by the snooze minutes.
void on_toast_snooze(GtkButton*, gpointer) {
  auto cue_id = g_state.toast_cue_id;
  if (g_state.api != nullptr && cue_id != 0) {
    (void)g_state.api->snooze_cue_async(cue_id, [](rivet_app::Result<void>) {});
    (void)g_state.api->get_loops_async([](rivet_app::Result<std::vector<rivet_app::LoopInfo>> r) {
      on_result(std::move(r), [](std::vector<rivet_app::LoopInfo> const& loops) {
        g_state.model.loops = loops;
        refresh_today_card();
      });
    });
  }
  toast_dismiss();
}

// Done: self-reported acknowledgement — never proof of a real break.
void on_toast_done(GtkButton*, gpointer) {
  auto cue_id = g_state.toast_cue_id;
  if (g_state.api != nullptr && cue_id != 0) {
    (void)g_state.api->acknowledge_cue_async(cue_id,
                                             [](rivet_app::Result<void>) {});
  }
  toast_dismiss();
}

// Hide: no completion claimed, no deferral; the normal schedule continues.
void on_toast_hide(GtkButton*, gpointer) {
  auto cue_id = g_state.toast_cue_id;
  if (g_state.api != nullptr && cue_id != 0) {
    (void)g_state.api->dismiss_cue_async(cue_id, [](rivet_app::Result<void>) {});
  }
  toast_dismiss();
}

GtkWindow* toast_window_new(GtkApplication* app) {
  auto* window = gtk_application_window_new(app);
  gtk_widget_add_css_class(GTK_WIDGET(window), "toast-bg");
  gtk_window_set_decorated(GTK_WINDOW(window), FALSE);
  gtk_window_set_default_size(GTK_WINDOW(window), 380, -1);
  return GTK_WINDOW(window);
}

void toast_show(std::string const& title, std::vector<std::string> const& lines,
                rivet_app::ReminderKind accent, std::int64_t cue_id,
                std::int64_t snooze_minutes, GtkApplication* app) {
  toast_dismiss();
  auto* window = toast_window_new(app);
  g_state.toast_window = window;
  g_state.toast_accent = accent;
  g_state.toast_cue_id = cue_id;

  auto* root = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 0);
  auto* accent_bar = gtk_label_new("");
  gtk_widget_set_size_request(accent_bar, 5, -1);
  gtk_widget_set_valign(accent_bar, GTK_ALIGN_FILL);
  char const* accent_css = accent == rivet_app::ReminderKind::water
                               ? "accent-water"
                               : accent == rivet_app::ReminderKind::micro
                                     ? "accent-micro"
                                     : "accent-sit";
  gtk_widget_add_css_class(accent_bar, accent_css);
  box_append(GTK_BOX(root), accent_bar);

  auto* body = gtk_box_new(GTK_ORIENTATION_VERTICAL, 10);
  gtk_widget_set_margin_top(body, 16);
  gtk_widget_set_margin_bottom(body, 14);
  gtk_widget_set_margin_start(body, 20);
  gtk_widget_set_margin_end(body, 20);

  auto* title_label = gtk_label_new(title.c_str());
  gtk_widget_add_css_class(title_label, "toast-title");
  gtk_label_set_xalign(GTK_LABEL(title_label), 0.0f);
  gtk_label_set_wrap(GTK_LABEL(title_label), TRUE);
  box_append(GTK_BOX(body), title_label);
  g_state.toast_title_label = GTK_LABEL(title_label);

  auto* lines_box = gtk_box_new(GTK_ORIENTATION_VERTICAL, 3);
  for (auto const& line : lines) {
    auto* line_label = gtk_label_new(line.c_str());
    gtk_widget_add_css_class(line_label, "toast-line");
    gtk_label_set_xalign(GTK_LABEL(line_label), 0.0f);
    gtk_label_set_wrap(GTK_LABEL(line_label), TRUE);
    box_append(GTK_BOX(lines_box), line_label);
  }
  box_append(GTK_BOX(body), lines_box);
  g_state.toast_lines_box = GTK_BOX(lines_box);

  auto* buttons = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 10);
  gtk_widget_set_halign(buttons, GTK_ALIGN_END);
  if (cue_id != 0) {
    auto* hide = gtk_button_new_with_label(l10n::t("NotifHide").c_str());
    gtk_widget_add_css_class(hide, "toast-btn-muted");
    g_signal_connect(hide, "clicked", G_CALLBACK(on_toast_hide), nullptr);
    box_append(GTK_BOX(buttons), hide);
    auto* snooze = gtk_button_new_with_label(
        l10n::t("NotifSnooze", {l10n::A(snooze_minutes)}).c_str());
    gtk_widget_add_css_class(snooze, "toast-btn-muted");
    g_signal_connect(snooze, "clicked", G_CALLBACK(on_toast_snooze), nullptr);
    box_append(GTK_BOX(buttons), snooze);
    g_state.toast_snooze_button = GTK_BUTTON(snooze);
    auto* done = gtk_button_new_with_label(l10n::t("NotifDone").c_str());
    gtk_widget_add_css_class(done, "toast-btn");
    g_signal_connect(done, "clicked", G_CALLBACK(on_toast_done), nullptr);
    box_append(GTK_BOX(buttons), done);
  } else {
    auto* got_it = gtk_button_new_with_label(l10n::t("NotifGotIt").c_str());
    gtk_widget_add_css_class(got_it, "toast-btn");
    g_signal_connect(
        got_it, "clicked",
        G_CALLBACK(+[](GtkButton*, gpointer) { toast_dismiss(); }), nullptr);
    box_append(GTK_BOX(buttons), got_it);
  }
  box_append(GTK_BOX(body), buttons);
  box_append(GTK_BOX(root), body);
  gtk_window_set_child(GTK_WINDOW(window), root);

  // Hovering holds the card open (restart the 9 s timer on enter/leave).
  auto* motion = gtk_event_controller_motion_new();
  g_signal_connect(motion, "enter", G_CALLBACK(+[](GtkEventControllerMotion*,
                                                    gdouble, gdouble, gpointer) {
                     toast_restart_auto_close();
                   }),
                   nullptr);
  g_signal_connect(motion, "leave", G_CALLBACK(+[](GtkEventControllerMotion*,
                                                    gpointer) {
                     toast_restart_auto_close();
                   }),
                   nullptr);
  gtk_widget_add_controller(GTK_WIDGET(window), motion);

  toast_restart_auto_close();
  gtk_window_present(GTK_WINDOW(window));
}

// ---- micro break ----------------------------------------------------------------

void micro_close() {
  if (g_state.micro_timeout_id != 0) {
    g_source_remove(g_state.micro_timeout_id);
    g_state.micro_timeout_id = 0;
  }
  if (g_state.micro_window != nullptr) {
    gtk_window_destroy(g_state.micro_window);
    g_state.micro_window = nullptr;
    g_state.micro_bar = nullptr;
  }
  g_state.micro_cue_id = 0;
}

// The eye rest ran its course: acknowledge the cue (self-reported).
void micro_finish() {
  auto cue_id = g_state.micro_cue_id;
  micro_close();
  if (g_state.api != nullptr && cue_id != 0) {
    (void)g_state.api->acknowledge_cue_async(cue_id,
                                             [](rivet_app::Result<void>) {});
  }
}

// Click / Esc / a newer cue: end early as a Hide — no completion claimed.
void micro_dismiss() {
  auto cue_id = g_state.micro_cue_id;
  micro_close();
  if (g_state.api != nullptr && cue_id != 0) {
    (void)g_state.api->dismiss_cue_async(cue_id, [](rivet_app::Result<void>) {});
  }
}

void micro_show(std::int64_t duration_seconds, std::string const& title,
                std::int64_t cue_id, GtkApplication* app) {
  micro_dismiss();
  g_state.micro_cue_id = cue_id;
  auto* window = gtk_application_window_new(app);
  gtk_widget_add_css_class(GTK_WIDGET(window), "micro-bg");
  gtk_window_set_decorated(GTK_WINDOW(window), FALSE);
  gtk_window_set_default_size(GTK_WINDOW(window), 400, -1);
  g_state.micro_window = GTK_WINDOW(window);

  auto* body = gtk_box_new(GTK_ORIENTATION_VERTICAL, 5);
  gtk_widget_set_margin_top(body, 18);
  gtk_widget_set_margin_bottom(body, 18);
  gtk_widget_set_margin_start(body, 26);
  gtk_widget_set_margin_end(body, 26);
  auto* head = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 10);
  box_append(GTK_BOX(head), label_with("👀", nullptr));
  auto* title_label = gtk_label_new(title.c_str());
  gtk_widget_add_css_class(title_label, "micro-title");
  gtk_label_set_xalign(GTK_LABEL(title_label), 0.0f);
  box_append(GTK_BOX(head), title_label);
  box_append(GTK_BOX(body), head);
  auto* hint = gtk_label_new(l10n::t("MicroHint").c_str());
  gtk_widget_add_css_class(hint, "micro-hint");
  gtk_label_set_xalign(GTK_LABEL(hint), 0.0f);
  box_append(GTK_BOX(body), hint);
  auto* bar = gtk_progress_bar_new();
  gtk_progress_bar_set_fraction(GTK_PROGRESS_BAR(bar), 1.0);
  gtk_widget_add_css_class(bar, "micro-bar");
  box_append(GTK_BOX(body), bar);
  g_state.micro_bar = GTK_PROGRESS_BAR(bar);
  gtk_window_set_child(GTK_WINDOW(window), body);

  // Click anywhere dismisses.
  auto* click = gtk_gesture_click_new();
  g_signal_connect(
      click, "released",
      G_CALLBACK(+[](GtkGestureClick*, gint, gdouble, gdouble, gpointer) {
        micro_dismiss();
      }),
      nullptr);
  gtk_widget_add_controller(GTK_WIDGET(window), GTK_EVENT_CONTROLLER(click));
  // Escape dismisses too.
  auto* key = gtk_event_controller_key_new();
  g_signal_connect(
      key, "key-pressed",
      G_CALLBACK(+[](GtkEventControllerKey*, guint keyval, guint, GdkModifierType,
                     gpointer) -> gboolean {
        if (keyval == GDK_KEY_Escape) {
          micro_dismiss();
          return TRUE;
        }
        return FALSE;
      }),
      nullptr);
  gtk_widget_add_controller(GTK_WIDGET(window), key);

  g_state.micro_progress = 1.0;
  g_state.micro_total_ms = std::max<std::int64_t>(1, duration_seconds) * 1000;
  g_state.micro_timeout_id = g_timeout_add(
      250,
      +[](gpointer) -> int {
        g_state.micro_progress =
            std::max(0.0, g_state.micro_progress - 250.0 /
                                                     g_state.micro_total_ms);
        if (g_state.micro_bar != nullptr) {
          gtk_progress_bar_set_fraction(g_state.micro_bar, g_state.micro_progress);
        }
        if (g_state.micro_progress <= 0.0) {
          micro_finish();
          return G_SOURCE_REMOVE;
        }
        return G_SOURCE_CONTINUE;
      },
      nullptr);
  gtk_window_present(GTK_WINDOW(window));
}

// ---- reminders due ----------------------------------------------------------------

void handle_health_cue(rivet_app::HealthCue const& cue, GtkApplication* app) {
  // The backend planned the delivery; the host only renders it. Transitional
  // cards here — R4 swaps these for the edge-docked character.
  auto const& model = g_state.model;
  auto const& kinds = cue.kinds;
  if (kinds.size() == 1 && kinds[0] == rivet_app::ReminderKind::micro) {
    micro_show(model.config.micro_break_duration_seconds, pick_micro(),
               cue.id, app);
    cue_displayed(cue.id);
    return;
  }

  std::vector<std::string> lines;
  if (std::find(kinds.begin(), kinds.end(), rivet_app::ReminderKind::sit) !=
      kinds.end()) {
    lines.push_back(l10n::t("NotifSitBody",
                            {l10n::A(format_duration(model.today.active_mins)),
                             l10n::A(model.today.sit_breaks)}));
    lines.push_back(l10n::pick("CopySitToastPool", 6));
  }
  if (std::find(kinds.begin(), kinds.end(), rivet_app::ReminderKind::water) !=
      kinds.end()) {
    lines.push_back(l10n::t("NotifWaterCount", {l10n::A(model.today.water_reminders)}));
    lines.push_back(pick_water());
  }
  if (std::find(kinds.begin(), kinds.end(), rivet_app::ReminderKind::micro) !=
      kinds.end()) {
    lines.push_back(pick_micro());
  }

  std::string title = l10n::t("NotifSitTitle");
  rivet_app::ReminderKind accent = rivet_app::ReminderKind::sit;
  if (std::find(kinds.begin(), kinds.end(), rivet_app::ReminderKind::water) !=
      kinds.end()) {
    title = l10n::t("NotifWaterTitle");
    accent = rivet_app::ReminderKind::water;
  }

  // Sound parity: the old Avalonia app was silent on non-Windows platforms,
  // so no bell here regardless of sound_enabled.
  toast_show(title, lines, accent, cue.id, model.config.snooze_minutes, app);
  cue_displayed(cue.id);
}

void show_toast_plain(std::string const& title, std::vector<std::string> const& lines,
                      rivet_app::ReminderKind accent, GtkApplication* app) {
  toast_show(title, lines, accent, 0, g_state.model.config.snooze_minutes, app);
}

void cue_displayed(std::int64_t id) {
  if (g_state.api != nullptr && id != 0) {
    (void)g_state.api->cue_displayed_async(id, [](rivet_app::Result<void>) {});
  }
}

// ---- events --------------------------------------------------------------------

void reload_history() {
  if (g_state.api == nullptr) return;
  // Always fetch 30 days; the 7/30 toggle is pure client-side slicing.
  (void)g_state.api->get_history_async(
      30, [](rivet_app::Result<std::vector<rivet_app::DayStats>> r) {
        on_result(std::move(r),
                  [](std::vector<rivet_app::DayStats> const& history) {
                    g_state.model.history = history;
                    refresh_history_card();
                    refresh_insight_card();
                  });
      });
}

void refresh_states_after_tick() {
  if (g_state.api == nullptr) return;
  (void)g_state.api->get_loops_async(
      [](rivet_app::Result<std::vector<rivet_app::LoopInfo>> r) {
        on_result(std::move(r),
                  [](std::vector<rivet_app::LoopInfo> const& loops) {
                    g_state.model.loops = loops;
                    refresh_today_card();
                  });
      });
  (void)g_state.api->get_paused_async(
      [](rivet_app::Result<rivet_app::PauseState> r) {
        on_result(std::move(r), [](rivet_app::PauseState const& paused) {
          g_state.model.paused = paused;
          refresh_today_card();
          refresh_pause_button();
        });
      });
  (void)g_state.api->get_pending_cues_async(
      [](rivet_app::Result<std::vector<rivet_app::HealthCue>> r) {
        on_result(std::move(r),
                  [](std::vector<rivet_app::HealthCue> const& cues) {
                    g_state.model.pending_cues = cues;
                  });
      });
}

void apply_config(rivet_app::ReminderConfig const& changed,
                  bool announce, GtkApplication* app);

void handle_event(std::string const& name, rivet::Value const& value,
                  GtkApplication* app) {
  try {
    auto event = rivet_app::decode_event(name, value);
    std::visit(
        [&](auto const& e) {
          using T = std::decay_t<decltype(e)>;
          if constexpr (std::is_same_v<T, rivet_app::Tick_statsEvent>) {
            dispatch_ui([stats = e.value] {
              g_state.model.today = stats;
              refresh_today_card();
              refresh_insight_card();
              refresh_states_after_tick();
            });
          } else if constexpr (std::is_same_v<T, rivet_app::Health_cueEvent>) {
            dispatch_ui([app, cue = e.value] { handle_health_cue(cue, app); });
          } else if constexpr (std::is_same_v<T, rivet_app::Day_completedEvent>) {
            dispatch_ui([stats = e.value] {
              g_state.model.today = stats;
              refresh_today_card();
              refresh_insight_card();
              reload_history();
            });
          } else if constexpr (std::is_same_v<T, rivet_app::Config_changedEvent>) {
            dispatch_ui([app, changed = e.value] { apply_config(changed, false, app); });
          }
        },
        event);
  } catch (std::exception const&) {
    // Unknown events are ignored (forward compatibility).
  }
}

void apply_config(rivet_app::ReminderConfig const& changed, bool announce,
                  GtkApplication* app) {
  (void)app;
  bool const language_changed = changed.language != g_state.model.config.language;
  g_state.model.config = changed;
  g_state.language = resolve_language(changed.language);
  apply_language();
  if (language_changed || announce) {
    // Static texts bake the language in — rebuild the whole main content.
    build_main_content();
  } else {
    refresh_all();
  }
}

// ---- onboarding ------------------------------------------------------------------

void onboarding_persist(bool welcome_shown) {
  auto& s = g_state;
  // One set-config call writes the whole first-run choice. There is no
  // strictness pact in v1: one gentle, non-blocking reminder experience.
  mutate_config([&](auto& d) {
    d.sit_reminder_minutes = s.onboarding_sit;
    d.water_reminder_minutes = s.onboarding_water;
    d.sound_enabled = s.onboarding_sound;
    d.welcome_shown = welcome_shown;
  });
}

void onboarding_finish(GtkApplication* app) {
  onboarding_persist(true);
  if (g_state.onboarding_window != nullptr) {
    gtk_window_destroy(g_state.onboarding_window);
    g_state.onboarding_window = nullptr;
  }
  show_toast_plain(l10n::t("ObSettled"), {l10n::t("ObSettledBody")},
                   rivet_app::ReminderKind::water, app);
}

void onboarding_show(GtkApplication* app) {
  if (g_state.onboarding_window != nullptr) {
    gtk_window_present(g_state.onboarding_window);
    return;
  }
  auto& s = g_state;
  s.onboarding_page = 0;
  s.onboarding_water = 30;
  s.onboarding_sit = 45;
  s.onboarding_sound = true;

  auto* window = gtk_application_window_new(app);
  gtk_widget_add_css_class(GTK_WIDGET(window), "ob-bg");
  gtk_window_set_title(GTK_WINDOW(window), l10n::t("ObTitle").c_str());
  gtk_window_set_default_size(GTK_WINDOW(window), 500, 640);
  s.onboarding_window = GTK_WINDOW(window);

  auto* root = gtk_box_new(GTK_ORIENTATION_VERTICAL, 14);
  gtk_widget_set_margin_top(root, 28);
  gtk_widget_set_margin_bottom(root, 28);
  gtk_widget_set_margin_start(root, 32);
  gtk_widget_set_margin_end(root, 32);

  auto* stack = gtk_stack_new();
  gtk_stack_set_transition_type(GTK_STACK(stack), GTK_STACK_TRANSITION_TYPE_SLIDE_LEFT_RIGHT);
  gtk_widget_set_vexpand(stack, TRUE);
  s.onboarding_stack = GTK_STACK(stack);

  // Page 0: intro.
  {
    auto* page = gtk_box_new(GTK_ORIENTATION_VERTICAL, 14);
    gtk_widget_set_valign(page, GTK_ALIGN_START);
    auto* emoji = gtk_label_new("💧");
    auto* emoji_attrs = pango_attr_list_new();
    pango_attr_list_insert(emoji_attrs,
                           pango_attr_size_new_absolute(64 * PANGO_SCALE));
    gtk_label_set_attributes(GTK_LABEL(emoji), emoji_attrs);
    pango_attr_list_unref(emoji_attrs);
    box_append(GTK_BOX(page), emoji);
    auto* hello = gtk_label_new(l10n::t("ObHello").c_str());
    gtk_widget_add_css_class(hello, "ob-hello");
    box_append(GTK_BOX(page), hello);
    auto* intro = gtk_label_new(l10n::t("ObIntro").c_str());
    gtk_widget_add_css_class(intro, "ob-text");
    gtk_label_set_wrap(GTK_LABEL(intro), TRUE);
    gtk_label_set_justify(GTK_LABEL(intro), GTK_JUSTIFY_CENTER);
    box_append(GTK_BOX(page), intro);
    auto* steps = gtk_label_new(l10n::t("ObSteps").c_str());
    gtk_widget_add_css_class(steps, "ob-caption");
    gtk_label_set_wrap(GTK_LABEL(steps), TRUE);
    gtk_label_set_justify(GTK_LABEL(steps), GTK_JUSTIFY_CENTER);
    box_append(GTK_BOX(page), steps);
    gtk_stack_add_named(GTK_STACK(stack), page, "0");
  }

  // Page 1: reminders — water cadence, sit cadence, sound.
  {
    auto* page = gtk_box_new(GTK_ORIENTATION_VERTICAL, 12);
    gtk_widget_set_valign(page, GTK_ALIGN_START);
    auto* title = gtk_label_new(l10n::t("ObWaterTitle").c_str());
    gtk_widget_add_css_class(title, "ob-title");
    gtk_label_set_xalign(GTK_LABEL(title), 0.0f);
    box_append(GTK_BOX(page), title);
    auto* row = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 10);
    for (auto minutes : {30, 45, 60}) {
      std::string label =
          l10n::t(minutes == 30 ? "ObMin30" : minutes == 45 ? "ObMin45" : "ObMin60");
      auto* card = gtk_button_new_with_label(label.c_str());
      gtk_widget_add_css_class(
          card, s.onboarding_water == minutes ? "ob-card-selected" : "ob-card");
      auto* child = gtk_button_get_child(GTK_BUTTON(card));
      gtk_label_set_xalign(GTK_LABEL(child), 0.5f);
      g_object_set_data(G_OBJECT(card), "water",
                        GINT_TO_POINTER(static_cast<gint>(minutes)));
      g_signal_connect(
          card, "clicked",
          G_CALLBACK(+[](GtkButton* button, gpointer) {
            g_state.onboarding_water = static_cast<std::int64_t>(
                GPOINTER_TO_INT(g_object_get_data(G_OBJECT(button), "water")));
          }),
          nullptr);
      box_append(GTK_BOX(row), card);
    }
    box_append(GTK_BOX(page), row);
    auto* sit_row = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 10);
    for (auto minutes : {45, 60, 90}) {
      std::string label =
          l10n::t(minutes == 45 ? "ObSit45" : minutes == 60 ? "ObSit60" : "ObSit90");
      auto* card = gtk_button_new_with_label(label.c_str());
      gtk_widget_add_css_class(
          card, s.onboarding_sit == minutes ? "ob-card-selected" : "ob-card");
      auto* child = gtk_button_get_child(GTK_BUTTON(card));
      gtk_label_set_xalign(GTK_LABEL(child), 0.5f);
      g_object_set_data(G_OBJECT(card), "sit",
                        GINT_TO_POINTER(static_cast<gint>(minutes)));
      g_signal_connect(
          card, "clicked",
          G_CALLBACK(+[](GtkButton* button, gpointer) {
            g_state.onboarding_sit = static_cast<std::int64_t>(
                GPOINTER_TO_INT(g_object_get_data(G_OBJECT(button), "sit")));
          }),
          nullptr);
      box_append(GTK_BOX(sit_row), card);
    }
    box_append(GTK_BOX(page), sit_row);
    auto* sound_title = gtk_label_new(l10n::t("ObSoundTitle").c_str());
    gtk_widget_add_css_class(sound_title, "ob-strong");
    gtk_label_set_xalign(GTK_LABEL(sound_title), 0.0f);
    box_append(GTK_BOX(page), sound_title);
    auto* sound_row = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
    auto* sound_switch = gtk_switch_new();
    gtk_switch_set_active(GTK_SWITCH(sound_switch), s.onboarding_sound);
    g_signal_connect(
        sound_switch, "state-set",
        G_CALLBACK(+[](GtkSwitch*, gboolean state, gpointer) -> gboolean {
          g_state.onboarding_sound = state != FALSE;
          return FALSE;
        }),
        nullptr);
    box_append(GTK_BOX(sound_row), sound_switch);
    auto* sound_hint = gtk_label_new(l10n::t("ObSoundHint").c_str());
    gtk_widget_add_css_class(sound_hint, "ob-text");
    box_append(GTK_BOX(sound_row), sound_hint);
    box_append(GTK_BOX(page), sound_row);
    gtk_stack_add_named(GTK_STACK(stack), page, "1");
  }

  // Page 2: contract summary.
  {
    auto* page = gtk_box_new(GTK_ORIENTATION_VERTICAL, 14);
    gtk_widget_set_valign(page, GTK_ALIGN_START);
    auto* title = gtk_label_new(l10n::t("ObPactTitle").c_str());
    gtk_widget_add_css_class(title, "ob-title");
    gtk_label_set_xalign(GTK_LABEL(title), 0.0f);
    box_append(GTK_BOX(page), title);
    auto* summary_card = gtk_box_new(GTK_ORIENTATION_VERTICAL, 8);
    gtk_widget_add_css_class(summary_card, "ob-card");
    auto* summary = gtk_label_new("");
    gtk_widget_add_css_class(summary, "ob-text");
    gtk_label_set_xalign(GTK_LABEL(summary), 0.0f);
    gtk_label_set_wrap(GTK_LABEL(summary), TRUE);
    box_append(GTK_BOX(summary_card), summary);
    box_append(GTK_BOX(page), summary_card);
    s.onboarding_pact_summary = GTK_LABEL(summary);
    auto* outro = gtk_label_new(l10n::t("ObPactOutro").c_str());
    gtk_widget_add_css_class(outro, "ob-caption");
    gtk_label_set_wrap(GTK_LABEL(outro), TRUE);
    gtk_label_set_xalign(GTK_LABEL(outro), 0.0f);
    box_append(GTK_BOX(page), outro);
    gtk_stack_add_named(GTK_STACK(stack), page, "2");
  }

  auto* dots = gtk_label_new("●○○");
  gtk_widget_add_css_class(dots, "dot-active");
  s.onboarding_dots = GTK_LABEL(dots);

  auto* controls = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 8);
  auto* skip = gtk_button_new_with_label(l10n::t("ObSkip").c_str());
  gtk_widget_add_css_class(skip, "flat");
  g_signal_connect(
      skip, "clicked",
      G_CALLBACK(+[](GtkButton*, gpointer user_data) {
        g_state.onboarding_water = 30;
        g_state.onboarding_sit = 45;
        g_state.onboarding_sound = true;
        onboarding_finish(GTK_APPLICATION(user_data));
      }),
      app);
  auto* next = gtk_button_new_with_label(l10n::t("ObStart").c_str());
  gtk_widget_add_css_class(next, "ob-primary");
  g_object_set_data(G_OBJECT(next), "app", app);
  g_signal_connect(
      next, "clicked",
      G_CALLBACK(+[](GtkButton* button, gpointer) {
        auto* app = GTK_APPLICATION(g_object_get_data(G_OBJECT(button), "app"));
        constexpr int kLastPage = 2;
        if (g_state.onboarding_page >= kLastPage) {
          onboarding_finish(app);
          return;
        }
        g_state.onboarding_page += 1;
        if (g_state.onboarding_page == kLastPage) {
          // Build the contract summary for the current selections.
          std::string text;
          text += l10n::t("ObSummaryToast", {l10n::A(g_state.onboarding_sit)});
          text += "\n";
          text += l10n::t("ObSummaryWater", {l10n::A(g_state.onboarding_water)});
          text += "\n";
          text += l10n::t("ObSummaryAway", {l10n::A(g_state.model.config.away_reset_minutes)});
          text += "\n";
          text += l10n::t("ObSummarySound",
                          {l10n::A(g_state.onboarding_sound ? l10n::t("ObOn")
                                                            : l10n::t("ObOff"))});
          gtk_label_set_text(g_state.onboarding_pact_summary, text.c_str());
        }
        gtk_stack_set_visible_child_name(
            g_state.onboarding_stack,
            std::to_string(g_state.onboarding_page).c_str());
        std::string dots;
        for (int i = 0; i <= kLastPage; ++i) {
          dots += i == g_state.onboarding_page ? "●" : "○";
        }
        gtk_label_set_text(g_state.onboarding_dots, dots.c_str());
        gtk_button_set_label(
            button,
            l10n::t(g_state.onboarding_page == kLastPage ? "ObLaunch" : "ObNext")
                .c_str());
        gtk_widget_set_visible(GTK_WIDGET(g_state.onboarding_skip),
                               g_state.onboarding_page < kLastPage);
      }),
      nullptr);
  box_append(GTK_BOX(controls), skip);
  auto* spacer = gtk_box_new(GTK_ORIENTATION_HORIZONTAL, 0);
  gtk_widget_set_hexpand(spacer, TRUE);
  box_append(GTK_BOX(controls), spacer);
  box_append(GTK_BOX(controls), next);
  g_state.onboarding_skip = GTK_BUTTON(skip);
  g_state.onboarding_next = GTK_BUTTON(next);

  box_append(GTK_BOX(root), stack);
  box_append(GTK_BOX(root), dots);
  box_append(GTK_BOX(root), controls);
  gtk_window_set_child(GTK_WINDOW(window), root);
  gtk_window_present(GTK_WINDOW(window));
}

// ---- startup (same contract as the Rivet template) ------------------------------

std::filesystem::path executable_path() {
  return std::filesystem::read_symlink("/proc/self/exe");
}

struct RuntimeLayout {
  std::filesystem::path petite_boot;
  std::filesystem::path scheme_boot;
  std::filesystem::path racket_boot;
  std::filesystem::path core;
};

std::optional<RuntimeLayout> discover_runtime_layout() {
  std::filesystem::path const exe = executable_path();
  std::filesystem::path const roots[] = {exe.parent_path()};
  for (auto const& root : roots) {
    RuntimeLayout layout{
        root / "runtime" / "petite.boot",
        root / "runtime" / "scheme.boot",
        root / "runtime" / "racket.boot",
        root / "res" / "core.zo",
    };
    if (std::filesystem::exists(layout.petite_boot) &&
        std::filesystem::exists(layout.scheme_boot) &&
        std::filesystem::exists(layout.racket_boot) &&
        std::filesystem::exists(layout.core)) {
      return layout;
    }
  }
  return std::nullopt;
}

void flush_now_sync() {
  if (g_state.api == nullptr) return;
  // Best-effort flush with a 1 s budget (port of FlushToday-on-exit).
  auto future = g_state.api->flush_now();
  (void)future.wait_for(std::chrono::seconds(1));
}

void reload_autostart() {
  if (g_state.api == nullptr) return;
  (void)g_state.api->get_autostart_async(
      [](rivet_app::Result<bool> r) {
        dispatch_ui([r = std::move(r)]() mutable {
          if (r.succeeded()) {
            g_state.model.autostart = r.get();
            if (mw.autostart_switch != nullptr) {
              g_state.updating_settings = true;
              gtk_switch_set_active(mw.autostart_switch, g_state.model.autostart);
              g_state.updating_settings = false;
            }
          }
        });
      });
}

void reload_data_dir() {
  if (g_state.api == nullptr) return;
  (void)g_state.api->get_diagnostics_async(
      [](rivet_app::Result<std::string> r) {
        on_result(std::move(r), [](std::string const& text) {
          static std::string const prefix = "data-dir: ";
          std::size_t pos = 0;
          while ((pos = text.find(prefix, pos)) != std::string::npos) {
            auto end = text.find('\n', pos);
            g_state.model.data_dir = text.substr(
                pos + prefix.size(),
                end == std::string::npos ? std::string::npos : end - pos - prefix.size());
            break;
          }
          refresh_footer();
        });
      });
}

void bootstrap(GtkApplication* app) {
  auto const& config = g_state.model.config;
  g_state.language = resolve_language(config.language);
  apply_language();
  build_main_content();
  reload_history();
  reload_autostart();
  reload_data_dir();
  reflect_pending_update();
  if (!config.welcome_shown) {
    onboarding_show(app);
  }
}

int on_backend_finished(gpointer user_data) {
  auto* app = GTK_APPLICATION(user_data);
  if (g_state.startup_thread.joinable()) {
    g_state.startup_thread.join();
  }

  std::unique_ptr<rivet::linux_runtime::Backend> backend;
  std::string error;
  {
    std::lock_guard lock(g_state.startup_mutex);
    backend = std::move(g_state.startup_backend);
    error = std::move(g_state.startup_error);
  }

  if (g_state.shutting_down.load(std::memory_order_acquire)) {
    if (backend != nullptr) {
      backend->stop();
    }
    return G_SOURCE_REMOVE;
  }
  if (!error.empty()) {
    g_state.set_status(std::string("Backend error: ") + error);
    return G_SOURCE_REMOVE;
  }
  if (backend == nullptr) {
    g_state.set_status("Backend error: startup completed without a backend");
    return G_SOURCE_REMOVE;
  }

  g_state.backend = std::move(backend);
  g_state.api = std::make_unique<rivet_app::API>(*g_state.backend);
  g_state.backend->set_event_handler(
      [app](std::string const& name, rivet::Value const& value) {
        handle_event(name, value, app);
      });

  // initialize seeds states and starts the 30 s backend tick. Each state read
  // lands in g_state.model as it arrives; the last one renders everything.
  auto chain = [app](rivet_app::Result<void> init) {
    on_result(std::move(init), [app]() {
      (void)g_state.api->get_active_config_async(
          [app](rivet_app::Result<rivet_app::ReminderConfig> c) {
            on_result(std::move(c), [app](rivet_app::ReminderConfig const& config) {
              g_state.model.config = config;
              (void)g_state.api->get_today_async(
                  [app](rivet_app::Result<rivet_app::DayStats> t) {
                    on_result(std::move(t), [app](rivet_app::DayStats const& today) {
                      g_state.model.today = today;
                      (void)g_state.api->get_paused_async(
                          [app](rivet_app::Result<rivet_app::PauseState> p) {
                            on_result(std::move(p), [app](rivet_app::PauseState const& paused) {
                              g_state.model.paused = paused;
                              (void)g_state.api->get_loops_async(
                                  [app](rivet_app::Result<std::vector<rivet_app::LoopInfo>> l) {
                                    on_result(std::move(l), [app](std::vector<rivet_app::LoopInfo> const& loops) {
                                      g_state.model.loops = loops;
                                      (void)g_state.api->get_pending_cues_async(
                                          [app](rivet_app::Result<std::vector<rivet_app::HealthCue>> q) {
                                            on_result(std::move(q), [app](std::vector<rivet_app::HealthCue> const& cues) {
                                              dispatch_ui([app, cues] {
                                                g_state.model.pending_cues = cues;
                                                bootstrap(app);
                                              });
                                            });
                                          });
                                    });
                                  });
                            });
                          });
                    });
                  });
            });
          });
    });
  };
  // Honest capability report (issue #54): GTK4 has no proven safe character
  // surface yet — Wayland cannot reliably position top-levels or guarantee
  // click-through — so delivery plans as a notification until R4 proves it.
  (void)g_state.api->set_host_capability_async(false, false,
                                               [](rivet_app::Result<void>) {});
  (void)g_state.api->initialize_async(chain);
  return G_SOURCE_REMOVE;
}

void start_backend(GtkApplication* app) {
  auto layout = discover_runtime_layout();
  if (!layout.has_value()) {
    g_state.set_status(
        "Missing Rivet runtime layout (runtime/*.boot, res/core.zo) next to "
        "the executable. Build with raco rivet build/dev.");
    return;
  }

  rivet::linux_runtime::RacketRuntimeConfig config;
  config.executable_path = executable_path().string();
  config.petite_boot = layout->petite_boot.string();
  config.scheme_boot = layout->scheme_boot.string();
  config.racket_boot = layout->racket_boot.string();
  config.backend_bundle = layout->core.string();
  config.module_name = rivet_app::kModuleName;
  config.entry_symbol = rivet_app::kEntryName;

  // Booting the embedded runtime blocks on file I/O; only startup runs off
  // the main loop. Everything after completion dispatches back through
  // g_idle_add.
  g_state.startup_thread = std::thread([app, config = std::move(config)]() mutable {
    auto backend =
        std::make_unique<rivet::linux_runtime::Backend>(std::move(config));
    try {
      backend->start();
      {
        std::lock_guard lock(g_state.startup_mutex);
        g_state.startup_backend = std::move(backend);
      }
    } catch (std::exception const& e) {
      std::lock_guard lock(g_state.startup_mutex);
      g_state.startup_error = e.what();
    }
    g_idle_add(on_backend_finished, app);
  });
}

// ---- window / app lifecycle --------------------------------------------------------

gboolean on_main_close_request(GtkWindow*, gpointer) {
  flush_now_sync();
  return FALSE;  // proceed with close; shutdown handler stops the backend
}

void on_activate(GtkApplication* app, gpointer) {
  if (g_state.window != nullptr) {
    gtk_window_present(g_state.window);
    return;
  }

  // Single instance: second launches surface the primary's window.
  static std::unique_ptr<rivet::system::SingleInstanceLease> lease;
  lease = std::make_unique<rivet::system::SingleInstanceLease>("site.jrtx.movebit");
  if (!lease->is_primary()) {
    lease->forward_arguments({});
    std::exit(0);
  }
  lease->set_activation_handler([](std::vector<std::string>) {
    dispatch_ui([] {
      if (g_state.window != nullptr) gtk_window_present(g_state.window);
    });
  });

  auto* window = gtk_application_window_new(app);
  gtk_widget_add_css_class(GTK_WIDGET(window), "app-bg");
  gtk_window_set_title(GTK_WINDOW(window), "MoveBit");
  gtk_window_set_default_size(GTK_WINDOW(window), 860, 760);

  auto* header = gtk_header_bar_new();
  auto* pause = gtk_button_new_with_label(l10n::t("TrayPause").c_str());
  g_signal_connect(pause, "clicked", G_CALLBACK(on_pause_clicked), nullptr);
  gtk_header_bar_pack_start(GTK_HEADER_BAR(header), pause);
  g_state.pause_button = GTK_BUTTON(pause);
  auto* quit = gtk_button_new_with_label(l10n::t("TrayExit").c_str());
  g_signal_connect(quit, "clicked", G_CALLBACK(on_quit_clicked), nullptr);
  gtk_header_bar_pack_end(GTK_HEADER_BAR(header), quit);
  gtk_window_set_titlebar(GTK_WINDOW(window), header);

  auto* root = gtk_box_new(GTK_ORIENTATION_VERTICAL, 0);
  g_state.content_root = GTK_BOX(root);
  gtk_window_set_child(GTK_WINDOW(window), root);
  g_signal_connect(window, "close-request", G_CALLBACK(on_main_close_request), nullptr);

  g_state.window = GTK_WINDOW(window);
  apply_global_css();
  gtk_window_present(GTK_WINDOW(window));
  start_backend(app);
}

void on_shutdown(GApplication*, gpointer) {
  g_state.shutting_down.store(true, std::memory_order_release);
  toast_dismiss();
  micro_dismiss();
  if (g_state.startup_thread.joinable()) {
    g_state.startup_thread.join();
  }
  std::unique_ptr<rivet::linux_runtime::Backend> startup_backend;
  {
    std::lock_guard lock(g_state.startup_mutex);
    startup_backend = std::move(g_state.startup_backend);
  }
  if (startup_backend != nullptr) {
    startup_backend->stop();
  }
  if (g_state.backend != nullptr) {
    g_state.backend->stop();
  }
}

}  // namespace

int main(int argc, char** argv) {
  auto* app = gtk_application_new("site.jrtx.movebit",
                                  G_APPLICATION_DEFAULT_FLAGS);
  g_signal_connect(app, "activate", G_CALLBACK(on_activate), nullptr);
  g_signal_connect(app, "shutdown", G_CALLBACK(on_shutdown), nullptr);
  int const status = g_application_run(G_APPLICATION(app), argc, argv);
  g_object_unref(app);
  return status;
}
