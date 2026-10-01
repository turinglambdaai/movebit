#pragma once

// Four-step first-run wizard, code-built (no XAML/idl): meet the droplet ->
// strictness pact -> water + sound -> contract summary. Mirrors the SwiftUI
// OnboardingView; closing the window midway persists the current selections
// (MainWindow handles Closed -> persist + settle toast).

#include "pch.h"
#include "MoveBitModel.h"

#include <functional>
#include <string>

namespace winrt::RivetHost::implementation {

using namespace winrt::Microsoft::UI::Xaml;

struct OnboardingWizard {
  Window window{nullptr};
  int page = 0;
  std::string mode = "standard";
  std::int64_t water = 30;
  bool sound = true;

  Controls::StackPanel page_host{nullptr};
  Controls::StackPanel intro_page{nullptr};
  Controls::StackPanel strict_page{nullptr};
  Controls::StackPanel water_page{nullptr};
  Controls::StackPanel pact_page{nullptr};
  Controls::TextBlock dots{nullptr};
  Controls::TextBlock summary{nullptr};
  Controls::Button skip_button{nullptr};
  Controls::Button next_button{nullptr};
  Controls::ToggleSwitch sound_switch{nullptr};
  Controls::Button gentle_button{nullptr};
  Controls::Button standard_button{nullptr};
  Controls::Button strict_button{nullptr};
  Controls::Button water30{nullptr};
  Controls::Button water45{nullptr};
  Controls::Button water60{nullptr};
  std::function<void(std::string const& mode, std::int64_t water, bool sound)>
      on_finish;

  constexpr static int kLastPage = 3;

  void Show() {
    if (window) {
      window.Activate();
      return;
    }
    window = Window{};
    window.Title(winrt::to_hstring(l10n::t("ObTitle")));
    window.AppWindow().Resize({500, 640});

    Controls::ScrollViewer scroll;
    Controls::StackPanel root;
    root.Orientation(Controls::Orientation::Vertical);
    root.Padding(Thickness{32, 28, 32, 28});
    root.Spacing(14);

    page_host = Controls::StackPanel{};
    page_host.Spacing(12);
    BuildIntroPage();
    BuildStrictPage();
    BuildWaterPage();
    BuildPactPage();
    page_host.Children().Append(intro_page);

    dots = make_text(L"● ○ ○ ○", 14, false, "#C25E3E");

    Controls::StackPanel controls;
    controls.Orientation(Controls::Orientation::Horizontal);
    skip_button = make_button(winrt::to_hstring(l10n::t("ObSkip")), "#00000000",
                              "#A39B8D", false);
    skip_button.Click([this](IInspectable const&, RoutedEventArgs const&) {
      SkipToDefaults();
    });
    Controls::Grid spring;
    spring.HorizontalAlignment(HorizontalAlignment::Stretch);
    next_button = make_button(winrt::to_hstring(l10n::t("ObStart")), "#C25E3E",
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
      // Closing midway counts as seen; MainWindow persists through on_finish.
      window = {nullptr};
    });
    window.Activate();
  }

  bool IsOpen() const { return window != nullptr; }

  void BuildIntroPage() {
    intro_page = Controls::StackPanel{};
    intro_page.Spacing(14);
    intro_page.HorizontalAlignment(HorizontalAlignment::Center);
    intro_page.Children().Append(make_text(L"💧", 64, false, "#26241F"));
    auto hello = make_text(winrt::to_hstring(l10n::t("ObHello")), 26, true,
                           "#26241F");
    intro_page.Children().Append(hello);
    auto intro = make_text(winrt::to_hstring(l10n::t("ObIntro")), 14.5, false,
                           "#6E6A5E");
    intro.TextAlignment(TextAlignment::Center);
    intro.MaxWidth(380);
    intro_page.Children().Append(intro);
    auto steps = make_text(winrt::to_hstring(l10n::t("ObSteps")), 13, false,
                           "#A39B8D");
    steps.TextAlignment(TextAlignment::Center);
    intro_page.Children().Append(steps);
  }

  Controls::Button make_mode_button(std::string const& id,
                                    std::string const& title,
                                    std::string const& badge,
                                    std::string const& desc) {
    winrt::hstring text = wide(title);
    if (!badge.empty()) text = text + L"  ·  " + wide(badge);
    text = text + L"\n" + wide(desc);
    auto button = make_button(text, "#FFFFFF", "#26241F", true);
    button.BorderBrush(brush_from("#EAE2D6"));
    button.CornerRadius(CornerRadius{12, 12, 12, 12});
    button.Padding(Thickness{14, 14, 14, 14});
    button.HorizontalAlignment(HorizontalAlignment::Stretch);
    button.Click([this, id](IInspectable const&, RoutedEventArgs const&) {
      mode = id;
      RefreshModeCards();
    });
    return button;
  }

  void BuildStrictPage() {
    strict_page = Controls::StackPanel{};
    strict_page.Spacing(12);
    strict_page.Children().Append(
        make_text(winrt::to_hstring(l10n::t("ObStrictTitle")), 22, true,
                  "#26241F"));
    auto q = make_text(winrt::to_hstring(l10n::t("ObStrictQ")), 13, false,
                       "#6E6A5E");
    q.HorizontalAlignment(HorizontalAlignment::Left);
    strict_page.Children().Append(q);
    gentle_button = make_mode_button("gentle", l10n::t("ObModeGentle"), "",
                                     l10n::t("ObModeGentleDesc"));
    standard_button = make_mode_button(
        "standard", l10n::t("ObModeStandard"), l10n::t("ObBadgeRecommended"),
        l10n::t("ObModeStandardDesc"));
    strict_button = make_mode_button("strict", l10n::t("ObModeStrict"),
                                     l10n::t("ObBadgeEvidence"),
                                     l10n::t("ObModeStrictDesc"));
    strict_page.Children().Append(gentle_button);
    strict_page.Children().Append(standard_button);
    strict_page.Children().Append(strict_button);
    auto footnote = make_text(
        winrt::to_hstring(l10n::t("ObStrictFootnote")), 11, false, "#A39B8D");
    footnote.HorizontalAlignment(HorizontalAlignment::Left);
    strict_page.Children().Append(footnote);
    RefreshModeCards();
  }

  void RefreshModeCards() {
    auto style = [&](Controls::Button const& button, bool selected) {
      button.Background(brush_from(selected ? "#FBEFE2" : "#FFFFFF"));
      button.BorderBrush(brush_from(selected ? "#C25E3E" : "#EAE2D6"));
      button.Foreground(brush_from(selected ? "#C25E3E" : "#26241F"));
    };
    style(gentle_button, mode == "gentle");
    style(standard_button, mode == "standard");
    style(strict_button, mode == "strict");
  }

  Controls::Button make_water_button(int minutes,
                                     std::string const& label) {
    auto button = make_button(wide(label), "#FFFFFF", "#26241F", true);
    button.BorderBrush(brush_from("#EAE2D6"));
    button.CornerRadius(CornerRadius{12, 12, 12, 12});
    button.Padding(Thickness{14, 12, 14, 12});
    button.HorizontalAlignment(HorizontalAlignment::Stretch);
    button.Click([this, minutes](IInspectable const&, RoutedEventArgs const&) {
      water = minutes;
      RefreshWaterButtons();
    });
    return button;
  }

  void BuildWaterPage() {
    water_page = Controls::StackPanel{};
    water_page.Spacing(12);
    water_page.Children().Append(
        make_text(winrt::to_hstring(l10n::t("ObWaterTitle")), 20, true,
                  "#26241F"));
    Controls::StackPanel row;
    row.Orientation(Controls::Orientation::Horizontal);
    row.Spacing(10);
    water30 = make_water_button(
        30, l10n::t("ObMin30"));
    water45 = make_water_button(
        45, l10n::t("ObMin45"));
    water60 = make_water_button(
        60, l10n::t("ObMin60"));
    row.Children().Append(water30);
    row.Children().Append(water45);
    row.Children().Append(water60);
    water_page.Children().Append(row);

    auto sound_title = make_text(
        winrt::to_hstring(l10n::t("ObSoundTitle")), 15, true, "#26241F");
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
    auto hint = make_text(winrt::to_hstring(l10n::t("ObSoundHint")), 12,
                          false, "#6E6A5E");
    sound_row.Children().Append(hint);
    water_page.Children().Append(sound_row);
    RefreshWaterButtons();
  }

  void RefreshWaterButtons() {
    auto style = [&](Controls::Button const& button, int minutes) {
      bool const selected = water == minutes;
      button.Background(brush_from(selected ? "#FBEFE2" : "#FFFFFF"));
      button.BorderBrush(brush_from(selected ? "#C25E3E" : "#EAE2D6"));
      button.Foreground(brush_from(selected ? "#C25E3E" : "#26241F"));
    };
    style(water30, 30);
    style(water45, 45);
    style(water60, 60);
  }

  void BuildPactPage() {
    pact_page = Controls::StackPanel{};
    pact_page.Spacing(14);
    pact_page.Children().Append(
        make_text(winrt::to_hstring(l10n::t("ObPactTitle")), 22, true,
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
    auto outro = make_text(winrt::to_hstring(l10n::t("ObPactOutro")), 13,
                           false, "#A39B8D");
    outro.HorizontalAlignment(HorizontalAlignment::Left);
    pact_page.Children().Append(outro);
  }

  std::int64_t SitMinutes() const {
    return mode == "gentle" ? 60 : mode == "strict" ? 30 : 45;
  }
  bool ForceEnabled() const { return mode != "gentle"; }
  std::int64_t BreakDuration() const { return mode == "gentle" ? 3 : 5; }

  void GoNext() {
    if (page >= kLastPage) {
      if (on_finish) on_finish(mode, water, sound);
      Close();
      return;
    }
    ++page;
    if (page == kLastPage) {
      std::string text;
      text += ForceEnabled()
                  ? l10n::t("ObSummaryForced",
                            {l10n::A(SitMinutes()), l10n::A(BreakDuration())})
                  : l10n::t("ObSummaryToast", {l10n::A(SitMinutes())});
      text += "\n";
      text += l10n::t("ObSummaryWater", {l10n::A(water)});
      text += "\n";
      text += l10n::t("ObSummaryAway", {l10n::A(5)});
      text += "\n";
      text += l10n::t("ObSummarySound", {l10n::A(sound ? l10n::t("ObOn")
                                                       : l10n::t("ObOff"))});
      summary.Text(wide(text));
    }
    Controls::StackPanel pages[] = {intro_page, strict_page, water_page,
                                    pact_page};
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
    mode = "standard";
    water = 30;
    sound = true;
    if (on_finish) on_finish(mode, water, sound);
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
