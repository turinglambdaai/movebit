#pragma once

// Shell_NotifyIcon tray presence for the WinUI host. Owns a plain hidden
// Win32 window (NOT HWND_MESSAGE — it must receive the TaskbarCreated
// broadcast so the icon re-registers after an Explorer restart). The window
// is pumped by the app's normal UI-thread message loop, so every callback
// below runs on the UI thread and may touch XAML directly.
//
// Left click  -> on_open (show/focus the main window)
// Right click -> context menu: open / pause toggle / check updates / exit
//
// This carries the tray-menu actions (pause, exit) until… well, this IS the
// tray era of the rivet host; it is the permanent home of exit.

#include "pch.h"
#include "GeneratedStrings.h"

#include <shellapi.h>
#include <functional>

namespace winrt::RivetHost::implementation {

inline winrt::hstring tray_wide(std::string const& utf8) {
  return winrt::to_hstring(utf8);
}

struct TrayIcon {
  std::function<void()> on_open;
  std::function<void()> on_pause_toggle;
  std::function<void()> on_check_updates;
  std::function<void()> on_exit;

  TrayIcon() = default;
  ~TrayIcon() { Remove(); }

  TrayIcon(TrayIcon const&) = delete;
  TrayIcon& operator=(TrayIcon const&) = delete;

  void SetPaused(bool paused) { paused_ = paused; }

  void Show(std::wstring const& tooltip) {
    if (!window_) {
      CreateHiddenWindow();
    }
    tooltip_ = tooltip;
    Add();
  }

  void SetTooltip(std::wstring const& tooltip) {
    tooltip_ = tooltip;
    if (added_) {
      NOTIFYICONDATAW data = MakeData();
      data.uFlags |= NIF_TIP;
      wcsncpy_s(data.szTip, tooltip_.c_str(), _TRUNCATE);
      Shell_NotifyIconW(NIM_MODIFY, &data);
    }
  }

  // Transient info balloon (e.g. the first close-to-tray hint).
  void ShowInfo(std::wstring const& title, std::wstring const& text) {
    if (!added_) return;
    NOTIFYICONDATAW data = MakeData();
    data.uFlags = NIF_INFO;
    wcsncpy_s(data.szInfoTitle, title.c_str(), _TRUNCATE);
    wcsncpy_s(data.szInfo, text.c_str(), _TRUNCATE);
    data.dwInfoFlags = NIIF_RESPECT_QUIET_TIME;
    Shell_NotifyIconW(NIM_MODIFY, &data);
  }

  void Remove() {
    if (added_) {
      NOTIFYICONDATAW data = MakeData();
      Shell_NotifyIconW(NIM_DELETE, &data);
      added_ = false;
    }
    if (window_) {
      DestroyWindow(window_);
      window_ = nullptr;
      UnregisterClassW(class_name_, GetModuleHandleW(nullptr));
    }
  }

 private:
  static constexpr UINT kCallbackMessage = WM_APP + 1;
  static constexpr UINT kId = 1;
  enum MenuId { kMenuOpen = 1, kMenuPauseToggle = 2, kMenuCheckUpdates = 3, kMenuExit = 4 };

  wchar_t const* class_name_ = L"MoveBitTrayWnd";
  HWND window_ = nullptr;
  HICON icon_ = nullptr;
  bool added_ = false;
  bool paused_ = false;
  std::wstring tooltip_;
  UINT taskbar_created_message_ = 0;

  NOTIFYICONDATAW MakeData() const {
    NOTIFYICONDATAW data{};
    data.cbSize = sizeof(data);
    data.hWnd = window_;
    data.uID = kId;
    data.uCallbackMessage = kCallbackMessage;
    data.hIcon = icon_;
    return data;
  }

  void Add() {
    NOTIFYICONDATAW data = MakeData();
    data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
    wcsncpy_s(data.szTip, tooltip_.c_str(), _TRUNCATE);
    added_ = Shell_NotifyIconW(NIM_ADD, &data) != FALSE;
    if (added_) {
      NOTIFYICONDATAW version = MakeData();
      version.uVersion = NOTIFYICON_VERSION;
      Shell_NotifyIconW(NIM_SETVERSION, &version);
    }
  }

  static LRESULT CALLBACK WndProc(HWND window, UINT message, WPARAM wparam,
                                  LPARAM lparam) {
    auto* self = reinterpret_cast<TrayIcon*>(
        GetWindowLongPtrW(window, GWLP_USERDATA));
    if (self) return self->HandleMessage(window, message, wparam, lparam);
    if (message == WM_NCCREATE) {
      auto* create = reinterpret_cast<CREATESTRUCTW*>(lparam);
      SetWindowLongPtrW(window, GWLP_USERDATA,
                        reinterpret_cast<LONG_PTR>(create->lpCreateParams));
      return DefWindowProcW(window, message, wparam, lparam);
    }
    return DefWindowProcW(window, message, wparam, lparam);
  }

  LRESULT HandleMessage(HWND window, UINT message, WPARAM wparam,
                        LPARAM lparam) {
    if (message == taskbar_created_message_ && taskbar_created_message_) {
      // Explorer restarted and took the icon with it; re-register.
      added_ = false;
      Add();
      return 0;
    }
    if (message == kCallbackMessage) {
      switch (lparam) {
        case WM_LBUTTONUP:
          if (on_open) on_open();
          return 0;
        case WM_CONTEXTMENU:
        case WM_RBUTTONUP:
          ShowContextMenu(window);
          return 0;
        default:
          return 0;
      }
    }
    if (message == WM_COMMAND) {
      switch (LOWORD(wparam)) {
        case kMenuOpen:
          if (on_open) on_open();
          return 0;
        case kMenuPauseToggle:
          if (on_pause_toggle) on_pause_toggle();
          return 0;
        case kMenuCheckUpdates:
          if (on_check_updates) on_check_updates();
          return 0;
        case kMenuExit:
          if (on_exit) on_exit();
          return 0;
      }
    }
    return DefWindowProcW(window, message, wparam, lparam);
  }

  void ShowContextMenu(HWND window) {
    POINT point;
    GetCursorPos(&point);
    HMENU menu = CreatePopupMenu();
    if (!menu) return;
    AppendMenuW(menu, MF_STRING, kMenuOpen, tray_wide(l10n::t("TrayOpen")).c_str());
    AppendMenuW(menu, MF_STRING, kMenuPauseToggle,
                tray_wide(l10n::t(paused_ ? "TrayResume" : "TrayPause")).c_str());
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, kMenuCheckUpdates,
                tray_wide(l10n::t("TrayCheckUpdate")).c_str());
    AppendMenuW(menu, MF_STRING, kMenuExit,
                tray_wide(l10n::t("TrayExit")).c_str());
    // TrackPopupMenu steals focus semantics: the window must be foreground or
    // the menu will not dismiss on outside clicks.
    SetForegroundWindow(window);
    TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, point.x, point.y,
                   0, window, nullptr);
    DestroyMenu(menu);
  }

  void CreateHiddenWindow() {
    WNDCLASSW wc{};
    wc.lpfnWndProc = &WndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = class_name_;
    RegisterClassW(&wc);
    taskbar_created_message_ = RegisterWindowMessageW(L"TaskbarCreated");
    window_ = CreateWindowExW(0, class_name_, L"MoveBitTray", WS_OVERLAPPED,
                              0, 0, 0, 0, nullptr, nullptr, wc.hInstance,
                              this);
    icon_ = static_cast<HICON>(LoadImageW(
        GetModuleHandleW(nullptr), L"IDI_RIVET_APP_ICON", IMAGE_ICON,
        GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON),
        LR_DEFAULTCOLOR));
    if (!icon_) {
      icon_ = static_cast<HICON>(LoadImageW(
          GetModuleHandleW(nullptr), MAKEINTRESOURCEW(1), IMAGE_ICON,
          GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON),
          LR_DEFAULTCOLOR));
    }
  }
};

}  // namespace winrt::RivetHost::implementation
