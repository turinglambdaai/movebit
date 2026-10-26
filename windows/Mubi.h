#pragma once

// Mubi（沐比）for the WinUI host — the canonical character from the v1
// design system (issue #56; assets/mubi/tokens.json + docs/MUBI-DESIGN-
// SYSTEM.md). Renders the canonical path data with XAML Path elements and
// delivers character-planned health cues (issue #51: reminders first).
//
// Safety contract (issue #54): the companion window is non-activating and
// fully pointer-through (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE); Done /
// Later / Hide / Focus live in the tray menu. WinUI 3 window surfaces are
// opaque, so Mubi sits on a compact rounded card (light/dark art-directed)
// — a documented platform difference from the macOS free-floating panel.
//
// Geometry: path strings below are the canonical master data (generator:
// scripts/gen-mubi-art.mjs). Verify against assets/mubi/tokens.json when
// touching art — never redraw or approximate.

#include "pch.h"
#include "GeneratedStrings.h"
// The C++/WinRT projection only materializes the namespaces a translation
// unit includes explicitly; Mubi draws with XAML shapes.
#include "winrt/Microsoft.UI.Xaml.Shapes.h"

#include <functional>
#include <string>

namespace winrt::RivetHost::implementation {

using namespace winrt::Microsoft::UI::Xaml;
using namespace winrt::Microsoft::UI::Xaml::Shapes;
using namespace winrt::Microsoft::UI::Xaml::Media;

// ---- minimal SVG path parser (canonical data is absolute M/L/C/Z only) ----

namespace mubi_detail {

inline void skip_separators(std::wstring::const_iterator& it,
                            std::wstring::const_iterator end) {
  while (it != end && (*it == L' ' || *it == L',')) ++it;
}

inline double read_number(std::wstring::const_iterator& it,
                          std::wstring::const_iterator end) {
  skip_separators(it, end);
  std::wstring digits;
  if (it != end && *it == L'-') {
    digits.push_back(L'-');
    ++it;
  }
  while (it != end && (iswdigit(*it) || *it == L'.')) {
    digits.push_back(*it);
    ++it;
  }
  return _wtof(digits.c_str());
}

inline Windows::Foundation::Point read_point(
    std::wstring::const_iterator& it, std::wstring::const_iterator end) {
  double const x = read_number(it, end);
  double const y = read_number(it, end);
  return {static_cast<float>(x), static_cast<float>(y)};
}

// Builds a WinUI PathGeometry from a canonical M path string.
inline Media::PathGeometry parse(std::wstring const& d) {
  Media::PathGeometry geometry;
  auto figure = Media::PathFigure();
  bool open = false;
  auto it = d.begin();
  auto const end = d.end();
  while (it != end) {
    skip_separators(it, end);
    if (it == end) break;
    wchar_t const command = *it;
    ++it;
    if (command == L'M') {
      if (open) {
        figure.IsClosed(true);
        geometry.Figures().Append(figure);
        figure = Media::PathFigure();
      }
      figure.StartPoint(read_point(it, end));
      open = true;
    } else if (command == L'L') {
      auto segment = Media::LineSegment();
      segment.Point(read_point(it, end));
      figure.Segments().Append(segment);
    } else if (command == L'C') {
      auto p1 = read_point(it, end);
      auto p2 = read_point(it, end);
      auto p3 = read_point(it, end);
      auto segment = Media::BezierSegment();
      segment.Point1(p1);
      segment.Point2(p2);
      segment.Point3(p3);
      figure.Segments().Append(segment);
    } else if (command == L'Z') {
      if (open) {
        figure.IsClosed(true);
        open = false;
      }
    }
  }
  if (open) figure.IsClosed(true);
  geometry.Figures().Append(figure);
  return geometry;
}

inline Media::Brush solid(char const* hex, double opacity = 1.0) {
  Media::SolidColorBrush brush;
  brush.Color(color_from(hex));
  brush.Opacity(opacity);
  return brush;
}

inline Shapes::Path path_shape(wchar_t const* d, Media::Brush const& brush) {
  Shapes::Path path;
  path.Data(parse(d));
  path.Fill(brush);
  path.Stretch(Stretch::None);
  path.StrokeThickness(0);
  return path;
}

inline Shapes::Path stroke_shape(wchar_t const* d, Media::Brush const& brush,
                                double width) {
  Shapes::Path path;
  path.Data(parse(d));
  path.Fill(nullptr);
  path.Stroke(brush);
  path.StrokeThickness(width);
  path.StrokeStartLineCap(PenLineCap::Round);
  path.StrokeEndLineCap(PenLineCap::Round);
  path.Stretch(Stretch::None);
  return path;
}

}  // namespace mubi_detail

// ---- the character card ----------------------------------------------------

// Headless packaged acceptance skips the companion so the UIA probe sees
// exactly the main window (mirrors the macOS acceptance posture).
inline bool acceptance_mode() {
  static bool const value = [] {
    char buffer[16] = {};
    size_t size = 0;
    getenv_s(&size, buffer, sizeof(buffer), "MOVEBIT_ACCEPTANCE");
    return size > 0 && buffer[0] == '1';
  }();
  return value;
}

struct MubiCompanion {
  Window window{nullptr};
  Controls::Canvas canvas{nullptr};
  Controls::Border card{nullptr};
  Controls::Border capsule{nullptr};
  Controls::TextBlock capsule_text{nullptr};
  Controls::Grid root{nullptr};
  Media::ScaleTransform canvas_scale{nullptr};

  double body_height = 78;   // logical px (S 60 / M 78 / L 104)
  std::string motion = "subtle";
  std::string dock = "bottom-right";
  bool labels_always = false;
  bool quiet = false;
  bool pointer_through_applied = false;

  std::function<void()> on_expired;  // label fade ended -> idle pose

  static double body_height_for(std::string const& size) {
    if (size == "S") return 60;
    if (size == "L") return 104;
    return 78;
  }

  void ApplyConfig(std::string const& size, std::string const& m,
                   std::string const& d, std::string const& labels) {
    body_height = body_height_for(size);
    motion = m;
    dock = d;
    labels_always = labels == "always";
    if (window != nullptr) {
      Rebuild();
    }
  }

  void SetQuiet(bool enabled) {
    quiet = enabled;
    Show(quiet ? MubiState::Focus : MubiState::Idle, L"");
  }

  enum class MubiState { Idle, Water, Stand, Eyes, Ack, Focus };

  void Show(MubiState state, std::wstring const& message) {
    if (acceptance_mode()) return;
    EnsureWindow();
    Rebuild(state, message);
    Position();
    window.Activate();
    ApplyPointerThrough();
  }

  void ShowIdle() { Show(quiet ? MubiState::Focus : MubiState::Idle, L""); }

  void ShowAck() {
    if (quiet) return;
    Show(MubiState::Ack, L"");
  }

  void Dismiss() {
    if (window != nullptr) {
      window.Close();
      window = {nullptr};
      canvas = {nullptr};
      card = {nullptr};
      capsule = {nullptr};
      capsule_text = {nullptr};
      root = {nullptr};
      canvas_scale = {nullptr};
      pointer_through_applied = false;
    }
  }

 private:
  void EnsureWindow() {
    if (window != nullptr) return;
    window = Window{};
    strip_chrome(window);
    apply_window_icon(window);
  }

  // Full rebuild: WinUI has no cheap partial update path here, and the card
  // is tiny. dark = the app follows the system theme.
  void Rebuild(MubiState state = MubiState::Idle,
               std::wstring const& message = L"") {
    bool const dark =_root_is_dark();
    double const s = body_height / 108.0;
    double const card_pad = 12.0;
    double const canvas_w = 120.0 * s;
    double const canvas_h = 140.0 * s;
    double const card_w = canvas_w + card_pad * 2;
    double const card_h = canvas_h + card_pad * 2;

    using namespace mubi_detail;

    auto accent = [&](char const* hex) { return solid(hex); };

    // Canvas: 120x140 design units scaled up by s.
    canvas = Controls::Canvas{};
    canvas.Width(120);
    canvas.Height(140);
    canvas_scale = Media::ScaleTransform();
    canvas_scale.ScaleX(s);
    canvas_scale.ScaleY(s);
    canvas.RenderTransform(canvas_scale);

    auto dark_or = [&](char const* light_hex) {
      return dark ? "#E4D6BC" : light_hex;
    };

    canvas.Children().Append(
        path_shape(L"M 52 24 C 47 13, 40 7, 32 6 C 30 14, 36 23, 45 27 C 48 28.2, 51 26.5, 52 24 Z",
                   solid(dark_or("#F1E4CC"))));
    canvas.Children().Append(
        path_shape(L"M 74 23 C 71 14, 66.5 8, 60.5 6 C 58.5 13, 62.5 21.5, 70 25 C 72.3 25.9, 73.8 25, 74 23 Z",
                   solid(dark_or("#F1E4CC"))));
    canvas.Children().Append(
        path_shape(L"M 48 22 C 44.5 15.5, 39.5 10.5, 34.5 8.5 C 33.5 13.5, 38 20.5, 45 24.5 Z",
                   solid("#D8C8B5", 0.35)));
    canvas.Children().Append(
        path_shape(L"M 58 16 C 82 18, 106 46, 109 82 C 111 104, 92 124, 60 124 C 33 124, 12.5 106, 11 84 C 9.5 52, 34 14, 58 16 Z",
                   solid(dark ? "#EDE2CC" : "#F6EEDF")));

    // Face at the frozen placement; state decides open/closed/props.
    auto eye = [&](double cx, double cy) {
      auto ellipse = Shapes::Ellipse{};
      ellipse.Width(6);
      ellipse.Height(7.2);
      ellipse.Fill(solid("#2D2A28"));
      Controls::Canvas::SetLeft(ellipse, cx - 3);
      Controls::Canvas::SetTop(ellipse, cy - 3.6);
      canvas.Children().Append(ellipse);
    };
    auto closed_arc = [&](wchar_t const* d) {
      canvas.Children().Append(
          stroke_shape(d, solid("#2D2A28"), 2.2));
    };
    if (state == MubiState::Eyes || state == MubiState::Focus) {
      closed_arc(L"M 43 65 C 45 67.6, 48 67.6, 50 65");
      closed_arc(L"M 70 65 C 72 67.6, 75 67.6, 77 65");
    } else {
      eye(46.5, 65);
      eye(73.5, 65);
      if (state == MubiState::Ack) {
        canvas.Children().Append(
            stroke_shape(L"M 57.5 74 C 59 75.4, 61 75.4, 62.5 74",
                         solid("#2D2A28"), 1.6));
      }
    }

    // Per-state props (static state artwork on this leg; the motion budget
    // tuning happens with packaged desktop review, documented in #56 D3).
    if (state == MubiState::Water) {
      auto cup = Controls::Grid{};
      cup.Children().Append(
          path_shape(L"M -7 -9 L 7 -9 L 5.4 9 C 5.2 10.6, 4 11.5, 2.4 11.5 L -2.4 11.5 C -4 11.5, -5.2 10.6, -5.4 9 Z",
                     solid("#62B6FF")));
      cup.Children().Append(
          path_shape(L"M -4.6 -6.5 L 4.4 -6.5 L 3.6 1.5 L -4 1.5 Z",
                     solid("#8FCBFF", 0.85)));
      // WinRT setters return void — compose the transform explicitly.
      auto cup_transform = Media::CompositeTransform{};
      cup_transform.TranslateX(97);
      cup_transform.TranslateY(51);
      cup_transform.Rotation(-10);
      cup_transform.ScaleX(0.9);
      cup_transform.ScaleY(0.9);
      cup.RenderTransform(cup_transform);
      canvas.Children().Append(cup);
    } else if (state == MubiState::Stand) {
      canvas.Children().Append(
          stroke_shape(L"M 42 11 C 47 4, 53 2, 58 4", accent("#FF8C6B"), 3));
      canvas.Children().Append(
          stroke_shape(L"M 78 11 C 73 4, 67 2, 62 4", accent("#FF8C6B"), 3));
    } else if (state == MubiState::Eyes) {
      canvas.Children().Append(
          stroke_shape(L"M 92 38 C 96.5 40.5, 99 45, 98 50", accent("#65C8B8"), 3));
    } else if (state == MubiState::Ack) {
      canvas.Children().Append(
          path_shape(L"M 84 56 L 86 60.5 L 90.5 62 L 86 63.5 L 84 68 L 82 63.5 L 77.5 62 L 82 60.5 Z",
                     accent("#F2C96B")));
    } else if (state == MubiState::Focus) {
      auto moon = Shapes::Ellipse{};
      moon.Width(16);
      moon.Height(16);
      moon.Fill(solid("#8DA1B5", 0.75));
      Controls::Canvas::SetLeft(moon, 88);
      Controls::Canvas::SetTop(moon, 30);
      canvas.Children().Append(moon);
    }

    // Card + capsule layout.
    card = Controls::Border{};
    card.Background(solid(dark ? "#33302B" : "#FFFDFB"));
    card.BorderBrush(solid(dark ? "#4A463F" : "#E2DCCC"));
    card.BorderThickness(Thickness{1, 1, 1, 1});
    card.CornerRadius(CornerRadius{18, 18, 18, 18});
    card.Padding(Thickness{card_pad, card_pad, card_pad, card_pad});
    card.Child(canvas);

    root = Controls::Grid{};
    root.RowDefinitions().Append(Controls::RowDefinition{});
    auto auto_row = Controls::RowDefinition{};
    auto_row.Height(GridLengthHelper::Auto());
    root.RowDefinitions().Append(auto_row);

    Controls::Grid::SetRow(card, 0);
    root.Children().Append(card);

    bool const show_label = labels_always || !message.empty();
    if (show_label) {
      capsule = Controls::Border{};
      capsule.Background(solid(dark ? "#2A2723" : "#FFFDFB"));
      capsule.CornerRadius(CornerRadius{12, 12, 12, 12});
      capsule.Padding(Thickness{12, 6, 12, 6});
      capsule.Margin(Thickness{0, 8, 0, 0});
      capsule.HorizontalAlignment(HorizontalAlignment::Center);
      capsule.VerticalAlignment(VerticalAlignment::Bottom);
      capsule_text = make_text(
          winrt::hstring(message.empty() ? std::wstring(L"MoveBit") : message),
          12, true, dark ? "#F1EEE7" : "#2D2A28");
      capsule_text.TextAlignment(TextAlignment::Center);
      capsule.Child(capsule_text);
      Controls::Grid::SetRow(capsule, 1);
      root.Children().Append(capsule);
    }

    // The window surface is opaque; the card IS the window content.
    Controls::Grid outer;
    outer.Background(brush_from(dark ? "#33302B" : "#FFFDFB"));
    outer.CornerRadius(CornerRadius{20, 20, 20, 20});
    outer.Children().Append(root);
    window.Content(outer);

    double const label_h = show_label ? 40.0 : 0.0;
    window.AppWindow().Resize(
        {static_cast<int>(card_w + 2), static_cast<int>(card_h + label_h + 2)});
  }

  bool _root_is_dark() const {
    try {
      return window != nullptr &&
             window.Content().try_as<FrameworkElement>()
                 .ActualTheme() == ElementTheme::Dark;
    } catch (...) {
      // Before first layout: ask the OS.
      HIGHCONTRAST hc{sizeof(HIGHCONTRAST)};
      if (::SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(hc), &hc, 0) &&
          (hc.dwFlags & HCF_HIGHCONTRASTON)) {
        return true;
      }
      return false;
    }
  }

  void ApplyPointerThrough() {
    if (pointer_through_applied) return;
    if (auto window_native = window.try_as<::IWindowNative>()) {
      HWND hwnd = nullptr;
      if (SUCCEEDED(window_native->get_WindowHandle(&hwnd)) && hwnd) {
        LONG_PTR const ex = ::GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        // Whole-window pointer-through + never activate + no taskbar (#54).
        ::SetWindowLongPtrW(hwnd, GWL_EXSTYLE,
                            ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE |
                                WS_EX_TOOLWINDOW);
        pointer_through_applied = true;
      }
    }
  }

  void Position() {
    if (auto window_native = window.try_as<::IWindowNative>()) {
      HWND hwnd = nullptr;
      if (SUCCEEDED(window_native->get_WindowHandle(&hwnd)) && hwnd) {
        HMONITOR monitor = ::MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        MONITORINFO info{sizeof(MONITORINFO)};
        if (::GetMonitorInfoW(monitor, &info)) {
          auto const wa = info.rcWork;
          auto const size = window.AppWindow().Size();
          int const clearance = 16;
          int x = 0;
          int y = 0;
          if (dock == "bottom-left") {
            x = wa.left + clearance;
            y = wa.bottom - size.Height - clearance;
          } else if (dock == "top-right") {
            x = wa.right - size.Width - clearance;
            y = wa.top + clearance;
          } else {  // bottom-right
            x = wa.right - size.Width - clearance;
            y = wa.bottom - size.Height - clearance;
          }
          window.AppWindow().Move({x, y});
        }
      }
    }
  }
};

}  // namespace winrt::RivetHost::implementation
