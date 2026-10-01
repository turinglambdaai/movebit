#pragma once

#include "pch.h"
#include "MainWindow.g.h"
#include "MoveBitModel.h"
#include "Overlays.h"
#include "Onboarding.h"

#include <map>
#include <optional>

namespace winrt::RivetHost::implementation {

struct MainWindow : MainWindowT<MainWindow> {
  MainWindow();

  // XAML event handlers.
  void Pause_Click(winrt::Windows::Foundation::IInspectable const& sender,
                   Microsoft::UI::Xaml::RoutedEventArgs const& args);
  void Quit_Click(winrt::Windows::Foundation::IInspectable const& sender,
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
  void RefreshPauseButton();
  void RefreshFooter();
  void ShowHistoryBars();
  void ShowOnboarding();
  void SetStatus(std::string const& message);

  template <typename F>
  void MutateConfig(F mutate);
  void SaveConfig(rivet_app::ReminderConfig draft);

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
};

}  // namespace winrt::RivetHost::implementation

namespace winrt::RivetHost::factory_implementation {

struct MainWindow : MainWindowT<MainWindow, implementation::MainWindow> {};

}  // namespace winrt::RivetHost::factory_implementation
