#pragma once

#include "pch.h"
#include "MainWindow.g.h"
#include "MoveBitModel.h"
#include "Overlays.h"
#include "Onboarding.h"
#include "TrayIcon.h"

#include <map>
#include <optional>

namespace winrt::RivetHost::implementation {

struct MainWindow : MainWindowT<MainWindow> {
  MainWindow();

  // XAML event handlers.
  void Status_Click(winrt::Windows::Foundation::IInspectable const& sender,
                    Microsoft::UI::Xaml::RoutedEventArgs const& args);
  void HistRange_SelectionChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::SelectionChangedEventArgs const& args);
  void Language_SelectionChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::SelectionChangedEventArgs const& args);
  void Sit_ValueChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const& args);
  void Water_ValueChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const& args);
  void Away_ValueChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const& args);
  void Break_ValueChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const& args);
  void Skip_ValueChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const& args);
  void Snooze_ValueChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const& args);
  void MicroInterval_ValueChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const& args);
  void MicroDuration_ValueChanged(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const& args);
  void Force_Toggled(winrt::Windows::Foundation::IInspectable const& sender,
                     Microsoft::UI::Xaml::RoutedEventArgs const& args);
  void Micro_Toggled(winrt::Windows::Foundation::IInspectable const& sender,
                     Microsoft::UI::Xaml::RoutedEventArgs const& args);
  void Sound_Toggled(winrt::Windows::Foundation::IInspectable const& sender,
                     Microsoft::UI::Xaml::RoutedEventArgs const& args);
  void Autostart_Toggled(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::RoutedEventArgs const& args);
  void Updates_Toggled(winrt::Windows::Foundation::IInspectable const& sender,
                       Microsoft::UI::Xaml::RoutedEventArgs const& args);
  void UpdateCheck_Click(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::RoutedEventArgs const& args);
  void UpdateCopyError_Click(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::RoutedEventArgs const& args);
  void HandleUpdateCheck(
      rivet_app::Result<rivet_app::UpdateCheck> const& result);
  void StartUpdateDownload();
  void PollUpdateProgress();
  void ApplyStagedUpdate();
  void ChangeDataDir_Click(
      winrt::Windows::Foundation::IInspectable const& sender,
      Microsoft::UI::Xaml::RoutedEventArgs const& args);

 private:
  winrt::fire_and_forget InitializeBackendAsync();
  void SubscribeEvents();
  void Bootstrap();
  void HandleEvent(std::string const& name, rivet::Value const& value);
  void HandleRemindersDue(rivet_app::RemindersDue const& due);
  void ApplyConfig(rivet_app::ReminderConfig const& changed, bool announce);
  void RefreshStatesAfterTick();
  void ReloadHistory();
  void ReloadAutostart();
  void ReloadDataDir();

  void ApplyTexts();
  void RefreshTodayCard();
  void RefreshHistoryCard();
  void RefreshInsightCard();
  void RefreshSettingsWidgets();
  void RefreshStatusMarker();
  void RefreshFooter();
  void ShowHistoryBars();
  void ShowOnboarding();
  void SetStatus(std::string const& message);
  // Update failure text must leave the screen with the user (family
  // UPDATE.md rule): the hint carries the failure and the copy button
  // (UpdCopyError) puts exactly that string on the clipboard.
  void ShowUpdateErrorHint(winrt::hstring const& text);
  void ClearUpdateErrorHint();
  void SetWindowIconFromResources();
  winrt::fire_and_forget PickDataFolderAsync();

  template <typename F>
  void MutateConfig(F mutate);
  void SaveConfig(rivet_app::ReminderConfig draft);

  Microsoft::UI::Dispatching::DispatcherQueue dispatcher_{nullptr};
  std::shared_ptr<rivet::windows::Backend> backend_;
  rivet_app::API* api_ = nullptr;
  movebit::Model model_;
  std::string language_ = "zh";
  bool updating_settings_ = false;
  bool suppressing_numberbox_ = false;

  BreakOverlay overlay_;
  ToastCard toast_;
  MicroCard micro_;
  OnboardingWizard onboarding_;
  TrayUi tray_;
  bool really_quitting_ = false;
  bool tray_hint_shown_ = false;
  // Update button state machine: idle | checking | available | downloading |
  // ready | applying | error | up-to-date.
  std::string update_phase_ = "idle";
  winrt::hstring last_update_error_;
  Microsoft::UI::Dispatching::DispatcherQueueTimer update_progress_timer_{nullptr};

  void InitTray();
  void HideToTray();
  void BringToForeground();
  void TogglePause();
};

}  // namespace winrt::RivetHost::implementation

namespace winrt::RivetHost::factory_implementation {

struct MainWindow : MainWindowT<MainWindow, implementation::MainWindow> {};

}  // namespace winrt::RivetHost::factory_implementation
