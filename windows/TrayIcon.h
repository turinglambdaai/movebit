#pragma once

// App-side tray bindings. Everything mechanical — HWND subclassing, the
// activation callback, TaskbarCreated recovery, the popup menu, WM_COMMAND
// routing — is rivet's first-party rivet::system::TrayController. What stays
// here is the application layer: menu labels from the i18n single source and
// the action callbacks (open / pause toggle / check updates / exit).
//
// Left click  -> on_open (show/focus the main window)
// Right click -> the menu below

#include "pch.h"
#include "GeneratedStrings.h"
#include "system_services.hpp"

#include <functional>
#include <memory>

namespace winrt::RivetHost::implementation {

inline std::wstring tray_wide(std::string const& utf8) {
  auto const wide = winrt::to_hstring(utf8);
  return std::wstring{wide.c_str()};
}

struct TrayUi {
  std::function<void()> on_open;
  std::function<void()> on_pause_toggle;
  std::function<void()> on_check_updates;
  std::function<void()> on_exit;

  void Attach(winrt::Microsoft::UI::Xaml::Window const& window) {
    paused_ = false;
    controller_ = std::make_unique<rivet::system::TrayController>(
        rivet::system::NativeWindowHandle(window), kIconId, kCallbackMessage,
        L"MoveBit", LoadAppIcon(), BuildMenu(),
        [this] {
          if (on_open) on_open();
        });
  }

  void Detach() { controller_.reset(); }

  void SetPaused(bool paused) {
    if (paused_ == paused) return;
    paused_ = paused;
    if (controller_) controller_->SetMenu(BuildMenu());
  }

  void SetTooltip(std::wstring const& tooltip) {
    if (controller_) controller_->SetTooltip(tooltip);
  }

  void ShowInfo(std::string const& title, std::string const& body) {
    if (controller_) controller_->Notify(tray_wide(title), tray_wide(body));
  }

 private:
  static constexpr UINT kIconId = 1;
  static constexpr UINT kCallbackMessage = WM_APP + 7;
  enum MenuId {
    kMenuOpen = 1,
    kMenuPauseToggle = 2,
    kMenuCheckUpdates = 3,
    kMenuExit = 4
  };

  bool paused_ = false;
  std::unique_ptr<rivet::system::TrayController> controller_;

  static HICON LoadAppIcon() {
    HICON icon = static_cast<HICON>(::LoadImageW(
        ::GetModuleHandleW(nullptr), L"IDI_RIVET_APP_ICON", IMAGE_ICON,
        ::GetSystemMetrics(SM_CXSMICON), ::GetSystemMetrics(SM_CYSMICON),
        LR_DEFAULTCOLOR));
    if (!icon) {
      icon = static_cast<HICON>(::LoadImageW(
          ::GetModuleHandleW(nullptr), MAKEINTRESOURCEW(1), IMAGE_ICON,
          ::GetSystemMetrics(SM_CXSMICON), ::GetSystemMetrics(SM_CYSMICON),
          LR_DEFAULTCOLOR));
    }
    return icon;
  }

  std::vector<rivet::system::TrayMenuItem> BuildMenu() const {
    using rivet::system::TrayMenuItem;
    auto bind = [this](std::function<void()> const& action) {
      return [this, action] {
        if (action) action();
      };
    };
    return {
        TrayMenuItem(kMenuOpen, tray_wide(l10n::t("TrayOpen")),
                     bind(on_open)),
        TrayMenuItem(kMenuPauseToggle,
                     tray_wide(l10n::t(paused_ ? "TrayResume" : "TrayPause")),
                     bind(on_pause_toggle)),
        TrayMenuItem(TrayMenuItem::Type::separator),
        TrayMenuItem(kMenuCheckUpdates,
                     tray_wide(l10n::t("TrayCheckUpdate")),
                     bind(on_check_updates)),
        TrayMenuItem(kMenuExit, tray_wide(l10n::t("TrayExit")),
                     bind(on_exit)),
    };
  }
};

}  // namespace winrt::RivetHost::implementation
