#pragma once

// Three-step first-run wizard, code-built (no XAML/idl): meet MoveBit ->
// reminders (water cadence, sit cadence, sound) -> contract summary. Mirrors
// the SwiftUI OnboardingView; closing the window midway persists the current
// selections (MainWindow handles Closed -> persist + settle toast).
// The v1 reboot (issue #55) removed the strictness pact: there is exactly one
// gentle, non-blocking reminder experience.

#include "pch.h"
#include "MoveBitModel.h"
#include "Overlays.h"

#include <functional>
#include <string>

namespace winrt::RivetHost::implementation {

using namespace winrt::Microsoft::UI::Xaml;

struct OnboardingWizard {
  Window window{nullptr};
  int page = 0;
  std::int64_t water = 30;
  std::int64_t sit = 45;
  bool sound = true;

  Controls::StackPanel page_host{nullptr};
  Controls::StackPanel intro_page{nullptr};
  Controls::StackPanel water_page{nullptr};
  Controls::StackPanel pact_page{nullptr};
  Controls::TextBlock dots{nullptr};
  Controls::TextBlock summary{nullptr};
  Controls::Button skip_button{nullptr};
  Controls::Button next_button{nullptr};
  Controls::ToggleSwitch sound_switch{nullptr};
  Controls::Button water30{nullptr};
  Controls::Button water45{nullptr};
  Controls::Button water60{nullptr};
  Controls::Button sit45{nullptr};
  Controls::Button sit60{nullptr};
  Controls::Button sit90{nullptr};
  std::function<void(std::int64_t sit, std::int64_t water, bool sound)>
      on_finish;
  // Fired when the window closes any other way (title-bar X): closing counts
  // as seen, so the wizard cannot nag on every launch (old-app parity).
  std::function<void()> on_cancel;
  bool finished_ = false;

  constexpr static int kLastPage = 2;

  void Show() {
    if (window) {
      window.Activate();
      return;
    }
    window = Window{};
    window.Title(winrt::to_hstring(l10n::t("ob.title")));
    apply_window_icon(window);
    window.AppWindow().Resize({500, 640});
    // Center in the display's work area (old app used CenterScreen; a
    // first-run wizard pinned to a corner reads as an error dialog).
    try {
      auto const area = Microsoft::UI::Windowing::DisplayArea::GetFromWindowId(
          window.AppWindow().Id(),
          Microsoft::UI::Windowing::DisplayAreaFallback::Nearest);
      auto const wa = area.WorkArea();
      window.AppWindow().Move({wa.X + std::max(0, (wa.Width - 500) / 2),
                               wa.Y + std::max(0, (wa.Height - 640) / 3)});
    } catch (...) {
    }

    Controls::ScrollViewer scroll;
    Controls::StackPanel root;
    root.Orientation(Controls::Orientation::Vertical);
    root.Padding(Thickness{32, 28, 32, 28});
    root.Spacing(14);

    page_host = Controls::StackPanel{};
    page_host.Spacing(12);
    BuildIntroPage();
    BuildWaterPage();
    BuildPactPage();
    page_host.Children().Append(intro_page);

    dots = make_text(L"● ○ ○", 14, false, "#C25E3E");

    Controls::StackPanel controls;
    controls.Orientation(Controls::Orientation::Horizontal);
    skip_button = make_button(winrt::to_hstring(l10n::t("ob.skip")), "#00000000",
                              "#A39B8D", false);
    skip_button.Click([this](IInspectable const&, RoutedEventArgs const&) {
      SkipToDefaults();
    });
    Controls::Grid spring;
    spring.HorizontalAlignment(HorizontalAlignment::Stretch);
    next_button = make_button(winrt::to_hstring(l10n::t("ob.start")), "#C25E3E",
                              "#FFFFFF", false);
    next_button.CornerRadius(CornerRadius{8, 8, 8, 8});
    next_button.Padding(Thickness{18, 9, 18, 9});
    next_button.Click([this](IInspectable const&, RoutedEventArgs const&) {
      GoNext();
    });
    controls.Children().Append(skip_button);
    controls.Children().Append(spring);
    controls.Children().Append(next_button);

    root.Children().Append(page_host);
    root.Children().Append(dots);
    root.Children().Append(controls);
    scroll.Content(root);
    scroll.IsVerticalScrollChainingEnabled(false);
    window.Content(scroll);
    window.Closed([this](IInspectable const&, WindowEventArgs const&) {
      window = {nullptr};
      if (!finished_ && on_cancel) on_cancel();
    });
    window.Activate();
  }

  bool IsOpen() const { return window != nullptr; }

  void BuildIntroPage() {
    intro_page = Controls::StackPanel{};
    intro_page.Spacing(14);
    intro_page.HorizontalAlignment(HorizontalAlignment::Center);
    intro_page.Children().Append(make_text(L"💧", 64, false, "#26241F"));
    auto hello = make_text(winrt::to_hstring(l10n::t("ob.hello")), 26, true,
                           "#26241F");
    intro_page.Children().Append(hello);
    auto intro = make_text(winrt::to_hstring(l10n::t("ob.intro")), 14.5, false,
                           "#6E6A5E");
    intro.TextAlignment(TextAlignment::Center);
    intro.MaxWidth(380);
    intro_page.Children().Append(intro);
    auto steps = make_text(winrt::to_hstring(l10n::t("ob.steps")), 13, false,
                           "#A39B8D");
    steps.TextAlignment(TextAlignment::Center);
    intro_page.Children().Append(steps);
  }

  Controls::Button make_choice_button(std::int64_t value,
                                      std::int64_t* target,
                                      std::string const& label) {
    auto button = make_button(wide(label), "#FFFFFF", "#26241F", true);
    button.BorderBrush(brush_from("#EAE2D6"));
    button.CornerRadius(CornerRadius{12, 12, 12, 12});
    button.Padding(Thickness{14, 12, 14, 12});
    button.HorizontalAlignment(HorizontalAlignment::Stretch);
    button.Click([this, value, target](IInspectable const&, RoutedEventArgs const&) {
      *target = value;
      RefreshChoiceButtons();
    });
    return button;
  }

  void BuildWaterPage() {
    water_page = Controls::StackPanel{};
    water_page.Spacing(12);
    water_page.Children().Append(
        make_text(winrt::to_hstring(l10n::t("ob.waterTitle")), 20, true,
                  "#26241F"));
    Controls::StackPanel row;
    row.Orientation(Controls::Orientation::Horizontal);
    row.Spacing(10);
    water30 = make_choice_button(30, &water, l10n::t("ob.min30"));
    water45 = make_choice_button(45, &water, l10n::t("ob.min45"));
    water60 = make_choice_button(60, &water, l10n::t("ob.min60"));
    row.Children().Append(water30);
    row.Children().Append(water45);
    row.Children().Append(water60);
    water_page.Children().Append(row);

    Controls::StackPanel sit_row;
    sit_row.Orientation(Controls::Orientation::Horizontal);
    sit_row.Spacing(10);
    sit45 = make_choice_button(45, &sit, l10n::t("ob.sit45"));
    sit60 = make_choice_button(60, &sit, l10n::t("ob.sit60"));
    sit90 = make_choice_button(90, &sit, l10n::t("ob.sit90"));
    sit_row.Children().Append(sit45);
    sit_row.Children().Append(sit60);
    sit_row.Children().Append(sit90);
    water_page.Children().Append(sit_row);

    auto sound_title = make_text(
        winrt::to_hstring(l10n::t("ob.soundTitle")), 15, true, "#26241F");
    sound_title.HorizontalAlignment(HorizontalAlignment::Left);
    water_page.Children().Append(sound_title);
    Controls::StackPanel sound_row;
    sound_row.Orientation(Controls::Orientation::Horizontal);
    sound_row.Spacing(8);
    sound_switch = Controls::ToggleSwitch{};
    sound_switch.IsOn(sound);
    sound_switch.Toggled([this](IInspectable const&, RoutedEventArgs const&) {
      sound = sound_switch.IsOn();
    });
    sound_row.Children().Append(sound_switch);
    auto hint = make_text(winrt::to_hstring(l10n::t("ob.soundHint")), 12,
                          false, "#6E6A5E");
    sound_row.Children().Append(hint);
    water_page.Children().Append(sound_row);
    RefreshChoiceButtons();
  }

  void RefreshChoiceButtons() {
    auto style = [&](Controls::Button const& button, std::int64_t value,
                     std::int64_t const& target) {
      bool const selected = target == value;
      button.Background(brush_from(selected ? "#FBEFE2" : "#FFFFFF"));
      button.BorderBrush(brush_from(selected ? "#C25E3E" : "#EAE2D6"));
      button.Foreground(brush_from(selected ? "#C25E3E" : "#26241F"));
    };
    style(water30, 30, water);
    style(water45, 45, water);
    style(water60, 60, water);
    style(sit45, 45, sit);
    style(sit60, 60, sit);
    style(sit90, 90, sit);
  }

  void BuildPactPage() {
    pact_page = Controls::StackPanel{};
    pact_page.Spacing(14);
    pact_page.Children().Append(
        make_text(winrt::to_hstring(l10n::t("ob.pactTitle")), 22, true,
                  "#26241F"));
    auto card = Controls::Border{};
    card.Background(brush_from("#FFFFFF"));
    card.BorderBrush(brush_from("#EAE2D6"));
    card.BorderThickness(Thickness{1, 1, 1, 1});
    card.CornerRadius(CornerRadius{12, 12, 12, 12});
    card.Padding(Thickness{16, 16, 16, 16});
    summary = make_text(L"", 13.5, false, "#6E6A5E");
    summary.HorizontalAlignment(HorizontalAlignment::Left);
    card.Child(summary);
    pact_page.Children().Append(card);
    auto outro = make_text(winrt::to_hstring(l10n::t("ob.pactOutro")), 13,
                           false, "#A39B8D");
    outro.HorizontalAlignment(HorizontalAlignment::Left);
    pact_page.Children().Append(outro);
  }

  void GoNext() {
    if (page >= kLastPage) {
      finished_ = true;
      if (on_finish) on_finish(sit, water, sound);
      Close();
      return;
    }
    ++page;
    if (page == kLastPage) {
      std::string text;
      text += l10n::t("ob.summaryToast", {l10n::A(sit)});
      text += "\n";
      text += l10n::t("ob.summaryWater", {l10n::A(water)});
      text += "\n";
      text += l10n::t("ob.summaryAway", {l10n::A(5)});
      text += "\n";
      text += l10n::t("ob.summarySound", {l10n::A(sound ? l10n::t("ob.on")
                                                       : l10n::t("ob.off"))});
      summary.Text(wide(text));
    }
    Controls::StackPanel pages[] = {intro_page, water_page, pact_page};
    for (int i = 0; i <= kLastPage; ++i) {
      pages[i].Visibility(i == page ? Visibility::Visible
                                    : Visibility::Collapsed);
    }
    std::wstring dots_text;
    for (int i = 0; i <= kLastPage; ++i) {
      dots_text += i == page ? L"● " : L"○ ";
    }
    dots.Text(winrt::to_hstring(dots_text.c_str()));
    next_button.Content(winrt::box_value(winrt::hstring(wide(l10n::t(
        page == kLastPage ? "ObLaunch" : page == 0 ? "ObStart" : "ObNext")))));
    skip_button.Visibility(page < kLastPage ? Visibility::Visible
                                            : Visibility::Collapsed);
  }

  void SkipToDefaults() {
    // Skip = the shipped defaults, as if the user opted out of choices.
    water = 30;
    sit = 45;
    sound = true;
    finished_ = true;
    if (on_finish) on_finish(sit, water, sound);
    Close();
  }

  void Close() {
    if (window) {
      window.Close();
      window = {nullptr};
    }
  }
};

}  // namespace winrt::RivetHost::implementation
