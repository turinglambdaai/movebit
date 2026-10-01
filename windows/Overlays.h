#pragma once

// Code-built transient windows for the WinUI MoveBit host: the forced-break
// fullscreen overlay, the reminder toast, and the micro-break card. Built in
// code (no XAML/idl) so each is one header; all methods must be called on the
// UI thread, and all of them are owned by MainWindow (which outlives them),
// so handlers capture `this` directly. Mirrors the SwiftUI Overlays.swift
// behavior, with documented v1 simplifications: the countdown ring is a text
// + progress bar instead of a ring.

#include "pch.h"
#include "MoveBitModel.h"

#include <functional>
#include <string>
#include <vector>

namespace winrt::RivetHost::implementation {

using namespace winrt::Microsoft::UI::Xaml;

namespace {

inline Windows::UI::Color color_from(char const* hex) {
  int r = 0, g = 0, b = 0;
  sscanf_s(hex, "#%02x%02x%02x", &r, &g, &b);
  Windows::UI::Color color;
  color.R = static_cast<BYTE>(r);
  color.G = static_cast<BYTE>(g);
  color.B = static_cast<BYTE>(b);
  color.A = 255;
  return color;
}

inline Media::SolidColorBrush brush_from(char const* hex) {
  Media::SolidColorBrush brush;
  brush.Color(color_from(hex));
  return brush;
}

inline Controls::TextBlock make_text(winrt::hstring const& text, double size,
                                     bool bold, char const* hex_color) {
  Controls::TextBlock block;
  block.Text(text);
  block.FontSize(size);
  if (bold) block.FontWeight(Microsoft::UI::Text::FontWeights::SemiBold());
  block.Foreground(brush_from(hex_color));
  block.TextWrapping(Text::TextWrapping::Wrap);
  block.HorizontalAlignment(HorizontalAlignment::Center);
  return block;
}

inline winrt::hstring wide(std::string const& utf8) {
  return winrt::to_hstring(utf8);
}

inline Controls::Button make_button(winrt::hstring const& content,
                                    char const* background_hex,
                                    char const* foreground_hex,
                                    bool bordered) {
  Controls::Button button;
  button.Content(winrt::box_value(winrt::hstring(content)));
  button.Background(brush_from(background_hex));
  button.Foreground(brush_from(foreground_hex));
  if (!bordered) {
    button.BorderThickness(Thickness{0, 0, 0, 0});
  }
  button.CornerRadius(CornerRadius{7, 7, 7, 7});
  button.Padding(Thickness{10, 5, 10, 5});
  return button;
}

inline Controls::ProgressBar make_progress(char const* fill_hex) {
  Controls::ProgressBar bar;
  bar.Maximum(1.0);
  bar.Value(0.0);
  bar.Foreground(brush_from(fill_hex));
  bar.Background(brush_from("#EAE6DC"));
  bar.CornerRadius(CornerRadius{2, 2, 2, 2});
  return bar;
}

inline Microsoft::UI::Dispatching::DispatcherQueue ui_queue() {
  return Microsoft::UI::Dispatching::DispatcherQueue::GetForCurrentThread();
}

inline Microsoft::UI::Dispatching::DispatcherQueueTimer make_timer(
    std::chrono::milliseconds interval, bool repeating,
    std::function<void()> tick) {
  auto timer = ui_queue().CreateTimer();
  timer.Interval(interval);
  timer.IsRepeating(repeating);
  timer.Tick([tick = std::move(tick)](IInspectable const&, IInspectable const&) {
    tick();
  });
  return timer;
}

/// Move a frameless card to the bottom-right (toast) or top-center (micro)
/// of the display that hosts the window.
inline void position_card(Window const& window, int width, bool bottom_right) {
  try {
    auto app_window = window.AppWindow();
    auto const area = Microsoft::UI::Windowing::DisplayArea::GetFromWindowId(
        app_window.Id(),
        Microsoft::UI::Windowing::DisplayAreaFallback::Nearest);
    auto const wa = area.WorkArea();
    if (bottom_right) {
      app_window.Move({wa.X + wa.Width - width - 16,
                       wa.Y + std::max(0, wa.Height - 220)});
    } else {
      // Micro: top-center, in the sight line (28% from the top).
      app_window.Move({wa.X + std::max(0, (wa.Width - width) / 2),
                       wa.Y + static_cast<int>(wa.Height * 0.28)});
    }
  } catch (...) {
  }
}

inline void strip_chrome(Window const& window) {
  try {
    if (auto presenter = window.AppWindow().Presenter()
                             .try_as<Microsoft::UI::Windowing::OverlappedPresenter>()) {
      presenter.SetBorderAndTitleBar(false, false);
      presenter.IsAlwaysOnTop(true);
    }
  } catch (...) {
  }
}

}  // namespace

// ---- forced break overlay ----------------------------------------------------
// One fullscreen dark window per display; countdown text + progress bar; the
// delayed Skip is the only control. Completed runs the droplet goodbye; the
// scheduler stays authoritative for break-ended (watchdog closes at +5 s).

struct BreakOverlay {
  std::vector<Window> windows;
  Controls::TextBlock title_text{nullptr};
  Controls::TextBlock hint_text{nullptr};
  Controls::TextBlock countdown_text{nullptr};
  Controls::ProgressBar ring_bar{nullptr};
  Controls::Button skip_button{nullptr};
  rivet_app::BreakState state{};
  std::int64_t total_sec = 0;
  bool completed_nudged = false;
  bool goodbye = false;
  std::function<void()> on_skip;
  std::function<void()> on_complete;
  Microsoft::UI::Dispatching::DispatcherQueueTimer timer{nullptr};
  Microsoft::UI::Dispatching::DispatcherQueueTimer goodbye_timer{nullptr};

  bool Show(rivet_app::BreakState const& break_state,
            std::int64_t snooze) {
    CloseAll();
    auto displays = Microsoft::UI::Windowing::DisplayArea::FindAll();
    if (displays.Size() == 0) return false;

    state = break_state;
    total_sec = std::max<std::int64_t>(
        0, (state.ends_at_ms - state.started_at_ms) / 1000);
    completed_nudged = false;
    goodbye = false;

    for (std::int32_t i = 0; i < static_cast<std::int32_t>(displays.Size()); ++i) {
      auto const area = displays.GetAt(i);
      Window window;
      auto app_window = window.AppWindow();
      app_window.Move({area.ScreenArea().X, area.ScreenArea().Y});
      app_window.Resize(
          {area.ScreenArea().Width, area.ScreenArea().Height});

      Controls::Grid root;
      root.Background(brush_from("#0D1220"));
      Controls::StackPanel center;
      center.VerticalAlignment(VerticalAlignment::Center);
      center.HorizontalAlignment(HorizontalAlignment::Center);
      center.Spacing(22);

      auto emoji = make_text(L"💧", 60, false, "#F0EEE6");
      title_text = make_text(winrt::to_hstring(l10n::t("BrkTitle")), 30, true,
                             "#F0EEE6");
      countdown_text = make_text(L"0:00", 64, true, "#FFFFFF");
      ring_bar = make_progress("#E8B77A");
      ring_bar.Width(300);
      hint_text = make_text(wide(movebit::pick_break_hint()), 18, false,
                            "#B8B2C7");
      hint_text.MaxWidth(520);
      hint_text.TextAlignment(Text::TextAlignment::Center);

      center.Children().Append(emoji);
      center.Children().Append(title_text);
      center.Children().Append(countdown_text);
      center.Children().Append(ring_bar);
      center.Children().Append(hint_text);
      center.Children().Append(make_text(
          winrt::to_hstring(l10n::t("BrkAutoUnlock")), 12.5, false,
          "#6E6884"));

      skip_button = make_button(
          wide(l10n::t("BrkSkip", {l10n::A(snooze)})), "#1A1730", "#8A849E",
          true);
      skip_button.Visibility(Visibility::Collapsed);
      skip_button.Click([this](IInspectable const&, RoutedEventArgs const&) {
        if (on_skip) on_skip();
      });

      Controls::Grid bottom;
      bottom.VerticalAlignment(VerticalAlignment::Bottom);
      bottom.Margin(Thickness{0, 0, 0, 48});
      bottom.Children().Append(skip_button);

      root.Children().Append(center);
      root.Children().Append(bottom);
      window.Content(root);
      app_window.SetPresenter(
          Microsoft::UI::Windowing::AppWindowPresenterKind::FullScreen);
      window.Activate();
      windows.push_back(window);
    }

    timer = make_timer(std::chrono::milliseconds(500), true, [this] { Tick(); });
    timer.Start();
    return true;
  }

  void Tick() {
    if (goodbye || windows.empty()) return;
    auto const now = movebit::now_ms();
    auto const remaining =
        std::max<std::int64_t>(0, (state.ends_at_ms - now) / 1000);
    wchar_t text[16];
    swprintf_s(text, L"%d:%02d", static_cast<int>(remaining / 60),
               static_cast<int>(remaining % 60));
    countdown_text.Text(text);
    ring_bar.Value(total_sec > 0 ? static_cast<double>(remaining) / total_sec
                                 : 0.0);
    skip_button.Visibility(
        now >= state.started_at_ms + state.skip_after_ms
            ? Visibility::Visible
            : Visibility::Collapsed);

    if (remaining <= 0) {
      if (!completed_nudged) {
        completed_nudged = true;
        if (on_complete) on_complete();
      }
      // Watchdog: if the event never arrives (backend tick gap), close.
      if (now > state.ends_at_ms + 5000) CloseAll();
    }
  }

  void Finish(bool completed) {
    if (!completed) {
      CloseAll();
      return;
    }
    goodbye = true;
    title_text.Text(winrt::to_hstring(l10n::t("BrkDone")));
    hint_text.Text(winrt::to_hstring(l10n::t("BrkGoodbye")));
    countdown_text.Text(L"💪");
    skip_button.Visibility(Visibility::Collapsed);
    ring_bar.Opacity(0.35);
    goodbye_timer = make_timer(std::chrono::milliseconds(950), false,
                               [this] { CloseAll(); });
    goodbye_timer.Start();
  }

  void CloseAll() {
    if (timer) timer.Stop();
    if (goodbye_timer) goodbye_timer.Stop();
    for (auto& window : windows) {
      window.Close();
    }
    windows.clear();
    title_text = {nullptr};
    hint_text = {nullptr};
    countdown_text = {nullptr};
    ring_bar = {nullptr};
    skip_button = {nullptr};
  }
};

// ---- reminder toast -----------------------------------------------------------
// One card stands in for every kind due this tick; snoozing a merged toast
// postpones every merged kind. 9 s auto-close.

struct ToastCard {
  Window window{nullptr};
  Controls::TextBlock title_text{nullptr};
  Controls::StackPanel lines_panel{nullptr};
  std::vector<rivet_app::ReminderKind> kinds;
  std::function<void(std::vector<rivet_app::ReminderKind> const&)> on_snooze;
  Microsoft::UI::Dispatching::DispatcherQueueTimer timer{nullptr};

  void Show(std::string const& title, std::vector<std::string> const& lines,
            rivet_app::ReminderKind accent,
            std::vector<rivet_app::ReminderKind> merged_kinds,
            std::int64_t snooze_minutes) {
    Dismiss();
    kinds = std::move(merged_kinds);

    window = Window{};
    strip_chrome(window);

    Controls::Grid root;
    root.Background(brush_from("#FFFDFB"));
    root.BorderBrush(brush_from("#E2DCCC"));
    root.BorderThickness(Thickness{1, 1, 1, 1});
    root.CornerRadius(CornerRadius{14, 14, 14, 14});
    root.Width(380);

    Controls::StackPanel body;
    body.Orientation(Controls::Orientation::Vertical);
    body.Padding(Thickness{24, 16, 20, 14});
    body.Spacing(10);

    title_text = make_text(wide(title), 17, true, "#26241F");
    title_text.HorizontalAlignment(HorizontalAlignment::Left);
    body.Children().Append(title_text);

    lines_panel = Controls::StackPanel{};
    lines_panel.Spacing(3);
    for (auto const& line : lines) {
      auto block = make_text(wide(line), 13.5, false, "#6E6A5E");
      block.HorizontalAlignment(HorizontalAlignment::Left);
      lines_panel.Children().Append(block);
    }
    body.Children().Append(lines_panel);

    Controls::StackPanel buttons;
    buttons.Orientation(Controls::Orientation::Horizontal);
    buttons.HorizontalAlignment(HorizontalAlignment::Right);
    buttons.Spacing(10);
    if (!kinds.empty()) {
      auto snooze_button = make_button(
          wide(l10n::t("NotifSnooze", {l10n::A(snooze_minutes)})), "#FFFDFB",
          "#6E6A5E", true);
      snooze_button.BorderBrush(brush_from("#E2DCCC"));
      snooze_button.Click([this](IInspectable const&, RoutedEventArgs const&) {
        if (on_snooze) on_snooze(kinds);
        Dismiss();
      });
      buttons.Children().Append(snooze_button);
    }
    auto got_it =
        make_button(winrt::to_hstring(l10n::t("NotifGotIt")), "#F3EEE3",
                    "#26241F", false);
    got_it.Click([this](IInspectable const&, RoutedEventArgs const&) {
      Dismiss();
    });
    buttons.Children().Append(got_it);
    body.Children().Append(buttons);

    // Accent bar on the left edge.
    auto accent_bar = Controls::Grid{};
    accent_bar.Width(5);
    accent_bar.HorizontalAlignment(HorizontalAlignment::Left);
    accent_bar.VerticalAlignment(VerticalAlignment::Stretch);
    accent_bar.Background(brush_from(
        accent == rivet_app::ReminderKind::water
            ? "#3E7EC2"
            : accent == rivet_app::ReminderKind::micro ? "#8A849E" : "#EA580C"));

    Controls::Grid content;
    content.Children().Append(accent_bar);
    content.Children().Append(body);
    window.Content(content);

    position_card(window, 380, true);
    window.Activate();

    if (!timer) {
      timer = make_timer(std::chrono::milliseconds(9000), false,
                         [this] { Dismiss(); });
    }
    timer.Stop();
    timer.Start();
  }

  void Dismiss() {
    if (timer) timer.Stop();
    if (window) {
      window.Close();
      window = {nullptr};
      title_text = {nullptr};
      lines_panel = {nullptr};
    }
  }
};

// ---- micro break ---------------------------------------------------------------
// Small top-center card with a shrinking progress bar. Click / timeout
// dismisses; never locks, never steals focus, silent by design.

struct MicroCard {
  Window window{nullptr};
  Controls::ProgressBar bar{nullptr};
  double progress = 1.0;
  std::int64_t total_ms = 1000;
  Microsoft::UI::Dispatching::DispatcherQueueTimer timer{nullptr};

  void Show(std::int64_t duration_seconds, std::string const& title) {
    Dismiss();
    window = Window{};
    strip_chrome(window);

    Controls::Grid root;
    root.Background(brush_from("#FFFFFF"));
    root.BorderBrush(brush_from("#EAE2D6"));
    root.BorderThickness(Thickness{1, 1, 1, 1});
    root.CornerRadius(CornerRadius{18, 18, 18, 18});
    root.Width(400);

    Controls::StackPanel body;
    body.Orientation(Controls::Orientation::Vertical);
    body.Padding(Thickness{26, 18, 26, 18});
    body.Spacing(5);

    Controls::StackPanel head;
    head.Orientation(Controls::Orientation::Horizontal);
    head.Spacing(10);
    head.Children().Append(make_text(L"👀", 22, false, "#26241F"));
    auto title_block = make_text(wide(title), 18, true, "#26241F");
    title_block.HorizontalAlignment(HorizontalAlignment::Left);
    head.Children().Append(title_block);
    body.Children().Append(head);

    auto hint = make_text(winrt::to_hstring(l10n::t("MicroHint")), 11.5,
                          false, "#A39B8D");
    hint.HorizontalAlignment(HorizontalAlignment::Left);
    body.Children().Append(hint);

    bar = make_progress("#C25E3E");
    bar.Value(1.0);
    body.Children().Append(bar);
    root.Children().Append(body);
    window.Content(root);

    // Click anywhere dismisses.
    root.PointerPressed([this](IInspectable const&,
                               Input::PointerRoutedEventArgs const&) {
      Dismiss();
    });

    total_ms = std::max<std::int64_t>(1, duration_seconds) * 1000;
    progress = 1.0;
    position_card(window, 400, false);
    window.Activate();

    timer = make_timer(std::chrono::milliseconds(250), true, [this] {
      progress = std::max(0.0, progress - 250.0 / total_ms);
      if (bar) bar.Value(progress);
      if (progress <= 0.0) Dismiss();
    });
    timer.Start();
  }

  void Dismiss() {
    if (timer) timer.Stop();
    if (window) {
      window.Close();
      window = {nullptr};
      bar = {nullptr};
    }
  }
};

}  // namespace winrt::RivetHost::implementation
