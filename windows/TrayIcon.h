#pragma once

// App-side tray controller. The capability layer (Shell_NotifyIcon
// registration, tooltip relabeling, balloon, TaskbarCreated re-registration)
// is rivet's first-party rivet::system::TrayIcon; this struct binds it to the
// main window — subclassing the HWND for the activation callback and the
// TaskbarCreated broadcast — and owns the application semantics: the context
// menu (labels from the i18n single source) and the action bindings. The
// subclass and callbacks run on the WinUI UI thread.
//
// Left click  -> on_open (show/focus the main window)
// Right click -> open / pause toggle / check updates / exit

#include "pch.h"
#include "GeneratedStrings.h"
#include "system_services.hpp"

#include <commctrl.h>
#include <shellapi.h>
#include <functional>
#include <memory>

#pragma comment(lib, "comctl32.lib")

namespace winrt::RivetHost::implementation {

inline std::wstring tray_wide(std::string const& utf8) {
  auto const wide = winrt::to_hstring(utf8);
  return std::wstring{wide.c_str()};
}

struct TrayController {
  std::function<void()> on_open;
  std::function<void()> on_pause_toggle;
  std::function<void()> on_check_updates;
  std::function<void()> on_exit;

  void Attach(winrt::Microsoft::UI::Xaml::Window const& window) {
    hwnd_ = rivet::system::NativeWindowHandle(window);
    UINT const callback_message = WM_APP + 7;
    taskbar_created_message_ = RegisterWindowMessageW(L"TaskbarCreated");
    SetWindowSubclass(hwnd_, &TrayController::SubclassProc, kSubclassId,
                      reinterpret_cast<DWORD_PTR>(this));
    tray_ = std::make_unique<rivet::system::TrayIcon>(
        hwnd_, kIconId, callback_message, L"MoveBit", LoadAppIcon());
  }

  void Detach() {
    tray_.reset();
    if (hwnd_) {
      RemoveWindowSubclass(hwnd_, &TrayController::SubclassProc, kSubclassId);
      hwnd_ = nullptr;
    }
  }

  void SetPaused(bool paused) { paused_ = paused; }

  void SetTooltip(std::wstring const& tooltip) {
    if (tray_) tray_->SetTooltip(tooltip);
  }

  void ShowInfo(std::string const& title, std::string const& body) {
    if (tray_) tray_->Notify(tray_wide(title), tray_wide(body));
  }

 private:
  static constexpr UINT kIconId = 1;
  static constexpr UINT_PTR kSubclassId = 0x4D42;  // 'MB'
  static constexpr UINT kCallbackMessage = WM_APP + 7;
  enum MenuId {
    kMenuOpen = 1,
    kMenuPauseToggle = 2,
    kMenuCheckUpdates = 3,
    kMenuExit = 4
  };

  HWND hwnd_ = nullptr;
  std::unique_ptr<rivet::system::TrayIcon> tray_;
  bool paused_ = false;
  UINT taskbar_created_message_ = 0;

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

  static LRESULT CALLBACK SubclassProc(HWND window, UINT message,
                                       WPARAM wparam, LPARAM lparam,
                                       UINT_PTR subclass_id,
                                       DWORD_PTR ref_data) {
    auto* self = reinterpret_cast<TrayController*>(ref_data);
    if (self) {
      if (message == self->taskbar_created_message_) {
        // Explorer restarted and took the icon with it; rivet re-registers.
        if (self->tray_) self->tray_->Readd();
        return 0;
      }
      if (message == self->kCallbackMessage) {
        if (lparam == WM_LBUTTONUP) {
          if (self->on_open) self->on_open();
          return 0;
        }
        if (lparam == WM_CONTEXTMENU || lparam == WM_RBUTTONUP) {
          self->ShowContextMenu(window);
          return 0;
        }
        return 0;
      }
      if (message == WM_COMMAND) {
        switch (LOWORD(wparam)) {
          case kMenuOpen:
            if (self->on_open) self->on_open();
            return 0;
          case kMenuPauseToggle:
            if (self->on_pause_toggle) self->on_pause_toggle();
            return 0;
          case kMenuCheckUpdates:
            if (self->on_check_updates) self->on_check_updates();
            return 0;
          case kMenuExit:
            if (self->on_exit) self->on_exit();
            return 0;
        }
      }
    }
    (void)subclass_id;
    (void)ref_data;
    return DefSubclassProc(window, message, wparam, lparam);
  }

  void ShowContextMenu(HWND window) {
    POINT point;
    ::GetCursorPos(&point);
    HMENU menu = ::CreatePopupMenu();
    if (!menu) return;
    ::AppendMenuW(menu, MF_STRING, kMenuOpen,
                  tray_wide(l10n::t("TrayOpen")).c_str());
    ::AppendMenuW(menu, MF_STRING, kMenuPauseToggle,
                  tray_wide(l10n::t(paused_ ? "TrayResume" : "TrayPause"))
                      .c_str());
    ::AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    ::AppendMenuW(menu, MF_STRING, kMenuCheckUpdates,
                  tray_wide(l10n::t("TrayCheckUpdate")).c_str());
    ::AppendMenuW(menu, MF_STRING, kMenuExit,
                  tray_wide(l10n::t("TrayExit")).c_str());
    // The menu must dismiss on outside clicks: the owning window has to be
    // foreground before TrackPopupMenu (classic Shell quirk).
    ::SetForegroundWindow(window);
    ::TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, point.x, point.y,
                     0, window, nullptr);
    ::DestroyMenu(menu);
  }
};

}  // namespace winrt::RivetHost::implementation
