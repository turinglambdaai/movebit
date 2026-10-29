#include "pch.h"
#include "App.xaml.h"
#include "MainWindow.xaml.h"

#include <fstream>

namespace winrt::RivetHost::implementation {

// Unhandled XAML exceptions terminate the app without a WER crash record
// (no dump, no event-log entry), which reads as a "silent exit" in packaged
// desktop testing. Persist the message and the failing source site so a
// field repro yields its own evidence.
static void log_unhandled(std::wstring const& message,
                          std::wstring const& site) {
  try {
    wchar_t path[MAX_PATH]{};
    DWORD const n = ::GetTempPathW(MAX_PATH, path);
    if (n == 0 || n >= MAX_PATH) return;
    std::wstring const file = std::wstring(path) + L"RivetHost-unhandled.log";
    std::wofstream out(file, std::ios::app);
    if (!out) return;
    SYSTEMTIME st{};
    ::GetLocalTime(&st);
    out << st.wYear << L'-' << st.wMonth << L'-' << st.wDay << L' '
        << st.wHour << L':' << st.wMinute << L':' << st.wSecond << L' '
        << message << L" @ " << site << L"\n";
  } catch (...) {
    // The logger must never become the crash.
  }
}

App::App() {
  InitializeComponent();

#if defined(_DEBUG) && !defined(DISABLE_XAML_GENERATED_BREAK_ON_UNHANDLED_EXCEPTION)
  UnhandledException([](winrt::Windows::Foundation::IInspectable const&,
                        Microsoft::UI::Xaml::UnhandledExceptionEventArgs const& e) {
    if (::IsDebuggerPresent()) {
      auto const message = e.Message();
      (void)message;
      __debugbreak();
    }
  });
#endif

  UnhandledException(
      [](winrt::Windows::Foundation::IInspectable const&,
         Microsoft::UI::Xaml::UnhandledExceptionEventArgs const& e) {
        std::wstring message;
        try {
          message = e.Message();
        } catch (...) {}
        try {
          auto const datum = e.Exception();
          if (datum) {
            message += L" (exception object present)";
          }
        } catch (...) {}
        log_unhandled(message, L"");
      });
}

void App::OnLaunched(Microsoft::UI::Xaml::LaunchActivatedEventArgs const&) {
  window_ = winrt::make<MainWindow>();
  window_.Activate();
}

}  // namespace winrt::RivetHost::implementation
