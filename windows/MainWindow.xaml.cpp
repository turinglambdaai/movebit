#include "pch.h"
#include "MainWindow.xaml.h"
#if __has_include("MainWindow.g.cpp")
#include "MainWindow.g.cpp"
#endif
#include "GeneratedBackend.hpp"

#include <chrono>
#include <dbghelp.h>
#include <stdexcept>

#pragma comment(lib, "dbghelp.lib")

namespace {

// The launch smoke reported exit-status 0xC0000005 with empty output and the
// runner's WER LocalDumps produced nothing, so the host writes its own
// minidump: an unhandled exception lands as %TEMP%\\movebit-crash.dmp and
// the release job analyzes it on failure.
LONG WINAPI WriteCrashDumpAndContinue(EXCEPTION_POINTERS* info) noexcept {
  wchar_t path[MAX_PATH];
  DWORD const length = ::GetTempPathW(MAX_PATH, path);
  if (length > 0 && length < MAX_PATH - 20) {
    wcscpy_s(path + length, MAX_PATH - length, L"movebit-crash.dmp");
    HANDLE file = ::CreateFileW(path, GENERIC_WRITE, 0, nullptr,
                                CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file != INVALID_HANDLE_VALUE) {
      MINIDUMP_EXCEPTION_INFORMATION mei{GetCurrentThreadId(), info, FALSE};
      ::MiniDumpWriteDump(::GetCurrentProcess(), ::GetCurrentProcessId(), file,
                          MiniDumpNormal, &mei, nullptr, nullptr);
      ::CloseHandle(file);
    }
  }
  return EXCEPTION_CONTINUE_SEARCH;
}

}  // namespace

namespace winrt::RivetHost::implementation {
namespace {

std::filesystem::path executable_path() {
  std::wstring buffer(32768, L'\0');
  auto const length = ::GetModuleFileNameW(nullptr, buffer.data(),
                                          static_cast<DWORD>(buffer.size()));
  if (length == 0 || length == buffer.size()) {
    throw std::runtime_error("GetModuleFileNameW failed");
  }
  buffer.resize(length);
  return std::filesystem::path(buffer);
}

std::string utf8(std::filesystem::path const& path) {
  auto const wide = path.wstring();
  if (wide.empty()) {
    return {};
  }
  auto const size = ::WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS,
                                          wide.data(),
                                          static_cast<int>(wide.size()),
                                          nullptr, 0, nullptr, nullptr);
  if (size <= 0) {
    throw std::runtime_error("WideCharToMultiByte failed");
  }
  std::string result(static_cast<std::size_t>(size), '\0');
  if (::WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS,
                            wide.data(), static_cast<int>(wide.size()),
                            result.data(), size, nullptr, nullptr) != size) {
    throw std::runtime_error("WideCharToMultiByte failed");
  }
  return result;
}

rivet::windows::RacketRuntimeConfig runtime_config() {
  auto const exe = executable_path();
  auto const root = exe.parent_path();
  auto const runtime = root / L"runtime";

  rivet::windows::RacketRuntimeConfig config;
  config.executable_path = utf8(exe);
  config.petite_boot = utf8(runtime / L"petite.boot");
  config.scheme_boot = utf8(runtime / L"scheme.boot");
  config.racket_boot = utf8(runtime / L"racket.boot");
  config.backend_bundle = utf8(root / L"res" / L"core.zo");
  config.module_name = rivet_app::kModuleName;
  config.entry_symbol = rivet_app::kEntryName;
  config.dll_dir = runtime.wstring();
  return config;
}

}  // namespace

using namespace winrt::Microsoft::UI::Xaml;

MainWindow::MainWindow() {
  ::SetUnhandledExceptionFilter(&WriteCrashDumpAndContinue);
  InitializeComponent();
  dispatcher_ = DispatcherQueue();
  Title(L"MoveBit");
  Closed([this](IInspectable const&, WindowEventArgs const&) {
    // Best-effort flush with a 1 s budget (port of FlushToday-on-exit).
    if (api_ != nullptr) {
      auto future = api_->flush_now();
      (void)future.wait_for(std::chrono::seconds(1));
    }
  });
  ApplyTexts();
  InitializeBackendAsync();
}

winrt::fire_and_forget MainWindow::InitializeBackendAsync() {
  auto const dispatcher = DispatcherQueue();
  auto const weak = get_weak();
  auto backend = std::make_shared<rivet::windows::Backend>(runtime_config());

  try {
    // Booting the embedded runtime can block on file I/O, so only startup is
    // moved off the UI thread. RPC/State traffic below is completion-driven.
    co_await winrt::resume_background();
    backend->start();

    dispatcher.TryEnqueue([weak, backend = std::move(backend)]() mutable {
      if (auto window = weak.get()) {
        window->backend_ = std::move(backend);
        try {
          window->api_ = new rivet_app::API(*window->backend_);
          window->SubscribeEvents();
          window->api_->initialize_async(
              [weak](rivet_app::Result<void> init) {
                (void)init;
                if (auto self = weak.get()) {
                  // Re-dispatch: the completion runs on the reader thread.
                  self->dispatcher_.TryEnqueue([weak] {
                    if (auto w = weak.get()) {
                      if (w->api_ == nullptr) return;
                      (void)w->api_->get_active_config_async(
                          [weak](rivet_app::Result<rivet_app::ReminderConfig> c) {
                            if (auto self2 = weak.get()) {
                              self2->dispatcher_.TryEnqueue([weak, c] {
                                if (auto w = weak.get()) {
                                  try {
                                    w->model_.config = c.get();
                                    w->Bootstrap();
                                  } catch (std::exception const& e) {
                                    w->SetStatus(e.what());
                                  }
                                }
                              });
                            }
                          });
                    }
                  });
                }
              });
        } catch (std::exception const& e) {
          window->SetStatus(e.what());
        }
      } else {
        // Never destroy the last Backend reference on its own reader thread.
        std::thread([backend = std::move(backend)]() mutable {
          backend->stop();
        }).detach();
      }
    });
  } catch (std::exception const& e) {
    auto message = std::string(e.what());
    dispatcher.TryEnqueue([weak, message = std::move(message)] {
      if (auto window = weak.get()) {
        window->SetStatus(message);
      }
    });
  }
}

void MainWindow::SubscribeEvents() {
  auto const weak = get_weak();
  backend_->set_event_handler(
      [weak](std::string const& name, rivet::Value const& value) {
        if (auto window = weak.get()) {
          window->dispatcher_.TryEnqueue([weak, name, value] {
            if (auto self = weak.get()) self->HandleEvent(name, value);
          });
        }
      });
}

void MainWindow::Bootstrap() {
  model_.today = rivet_app::DayStats{"", 0, 0, 0, 0, 0};
  auto const weak = get_weak();
  auto const api = api_;

  // Sequential state reads land in model_; the last one renders everything.
  (void)api->get_today_async([weak](rivet_app::Result<rivet_app::DayStats> t) {
    if (auto self = weak.get()) {
      self->dispatcher_.TryEnqueue([weak, t] {
        if (auto w = weak.get()) {
          try {
            w->model_.today = t.get();
          } catch (std::exception const& e) {
            w->SetStatus(e.what());
            return;
          }
          (void)w->api_->get_paused_async(
              [weak](rivet_app::Result<rivet_app::PauseState> p) {
                if (auto self2 = weak.get()) {
                  self2->dispatcher_.TryEnqueue([weak, p] {
                    if (auto w = weak.get()) {
                      try {
                        w->model_.paused = p.get();
                      } catch (std::exception const& e) {
                        w->SetStatus(e.what());
                        return;
                      }
                      (void)w->api_->get_loops_async(
                          [weak](rivet_app::Result<std::vector<rivet_app::LoopInfo>> l) {
                            if (auto self3 = weak.get()) {
                              self3->dispatcher_.TryEnqueue([weak, l] {
                                if (auto w = weak.get()) {
                                  try {
                                    w->model_.loops = l.get();
                                  } catch (std::exception const& e) {
                                    w->SetStatus(e.what());
                                    return;
                                  }
                                  (void)w->api_->get_break_active_async(
                                      [weak](rivet_app::Result<
                                          std::optional<rivet_app::BreakState>>
                                          b) {
                                        if (auto self4 = weak.get()) {
                                          self4->dispatcher_.TryEnqueue([weak, b] {
                                            if (auto w = weak.get()) {
                                              try {
                                                w->model_.break_active = b.get();
                                              } catch (std::exception const& e) {
                                                w->SetStatus(e.what());
                                                return;
                                              }
                                              // Everything is loaded: render.
                                              w->language_ =
                                                  movebit::resolve_language(
                                                      w->model_.config.language);
                                              l10n::g_language = w->language_;
                                              w->ApplyTexts();
                                              w->RefreshTodayCard();
                                              w->RefreshHistoryCard();
                                              w->RefreshInsightCard();
                                              w->RefreshSettingsWidgets();
                                              w->ReloadHistory();
                                              w->ReloadAutostart();
                                              w->ReloadDataDir();
                                              if (!w->model_.config
                                                        .welcome_shown) {
                                                w->ShowOnboarding();
                                              }
                                              w->SetStatus("");
                                            }
                                          });
                                        }
                                      });
                                }
                              });
                            }
                          });
                    }
                  });
            }
          });
        }
      });
    }
  });
}

// ---- events -------------------------------------------------------------------

void MainWindow::HandleEvent(std::string const& name, rivet::Value const& value) {
  try {
    auto event = rivet_app::decode_event(name, value);
    std::visit(
        [&](auto const& e) {
          using T = std::decay_t<decltype(e)>;
          if constexpr (std::is_same_v<T, rivet_app::Tick_statsEvent>) {
            model_.today = e.value;
            RefreshTodayCard();
            RefreshInsightCard();
            RefreshStatesAfterTick();
          } else if constexpr (std::is_same_v<T, rivet_app::Reminders_dueEvent>) {
            HandleRemindersDue(e.value);
          } else if constexpr (std::is_same_v<T, rivet_app::Break_startedEvent>) {
            // ends-at / skip-after / started-at come from the authoritative
            // state; fall back to the event payload so the lock is never
            // silently skipped.
            auto const started = e.value;
            auto const weak = get_weak();
            (void)api_->get_break_active_async(
                [weak, started](
                    rivet_app::Result<std::optional<rivet_app::BreakState>> r) {
                  if (auto self = weak.get()) {
                    self->dispatcher_.TryEnqueue(
                        [weak, started, r] {
                          if (auto w = weak.get()) {
                            std::optional<rivet_app::BreakState> state;
                            try {
                              if (r.succeeded()) state = r.get();
                            } catch (...) {
                            }
                            if (!state.has_value()) {
                              auto const now = movebit::now_ms();
                              state = rivet_app::BreakState{
                                  now + started.duration_ms,
                                  started.skip_after_ms, now};
                            }
                            w->model_.break_active = state;
                            // The fullscreen break outranks transient nudges.
                            w->toast_.Dismiss();
                            w->micro_.Dismiss();
                            if (!w->overlay_.Show(
                                    *state,
                                    w->model_.config.snooze_minutes)) {
                              std::vector<std::string> const lines = {
                                  l10n::t("NotifSitFallback")};
                              w->toast_.Show(l10n::t("NotifSitTitle"), lines,
                                             rivet_app::ReminderKind::sit, {},
                                             w->model_.config.snooze_minutes);
                            }
                          }
                        });
                  }
                });
          } else if constexpr (std::is_same_v<T, rivet_app::Break_endedEvent>) {
            overlay_.Finish(e.value.completed);
            if (e.value.completed &&
                movebit::celebrated(model_.today.sit_breaks)) {
              toast_.Show(
                  l10n::t("NotifCheerTitle"),
                  {l10n::t("NotifCheerBody",
                           {l10n::A(model_.today.sit_breaks),
                            l10n::A(movebit::format_duration(
                                model_.today.active_mins))})},
                  rivet_app::ReminderKind::sit, {},
                  model_.config.snooze_minutes);
            }
          } else if constexpr (std::is_same_v<T, rivet_app::Day_completedEvent>) {
            model_.today = e.value;
            RefreshTodayCard();
            RefreshInsightCard();
            ReloadHistory();
          } else if constexpr (std::is_same_v<T, rivet_app::Config_changedEvent>) {
            ApplyConfig(e.value, false);
          }
        },
        event);
  } catch (std::exception const&) {
    // Unknown events are ignored (forward compatibility).
  }
}

void MainWindow::HandleRemindersDue(rivet_app::RemindersDue const& due) {
  if (due.kinds.size() == 1 &&
      due.kinds[0] == rivet_app::ReminderKind::micro) {
    micro_.Show(model_.config.micro_break_duration_seconds,
                movebit::pick_micro());
    return;
  }

  std::vector<std::string> lines;
  if (std::find(due.kinds.begin(), due.kinds.end(),
                rivet_app::ReminderKind::sit) != due.kinds.end()) {
    lines.push_back(l10n::t("NotifSitBody",
                            {l10n::A(movebit::format_duration(
                                 model_.today.active_mins)),
                             l10n::A(model_.today.sit_breaks)}));
    lines.push_back(l10n::pick("CopySitToastPool", 6));
  }
  if (std::find(due.kinds.begin(), due.kinds.end(),
                rivet_app::ReminderKind::water) != due.kinds.end()) {
    lines.push_back(
        l10n::t("NotifWaterCount", {l10n::A(model_.today.water_reminders)}));
    lines.push_back(movebit::pick_water());
  }
  if (std::find(due.kinds.begin(), due.kinds.end(),
                rivet_app::ReminderKind::micro) != due.kinds.end()) {
    lines.push_back(movebit::pick_micro());
  }

  std::string title = l10n::t("NotifSitTitle");
  auto accent = rivet_app::ReminderKind::sit;
  if (std::find(due.kinds.begin(), due.kinds.end(),
                rivet_app::ReminderKind::water) != due.kinds.end()) {
    title = l10n::t("NotifWaterTitle");
    accent = rivet_app::ReminderKind::water;
  }

  auto const weak = get_weak();
  overlay_.on_skip = [weak] {
    if (auto self = weak.get()) {
      (void)self->api_->skip_break_async([](rivet_app::Result<void>) {});
    }
  };
  overlay_.on_complete = [weak] {
    if (auto self = weak.get()) {
      (void)self->api_->complete_break_async([](rivet_app::Result<void>) {});
    }
  };
  toast_.on_snooze = [weak](std::vector<rivet_app::ReminderKind> const& kinds) {
    if (auto self = weak.get()) {
      for (auto kind : kinds) {
        (void)self->api_->snooze_async(kind, [](rivet_app::Result<void>) {});
      }
    }
  };
  toast_.Show(title, lines, accent, due.kinds, model_.config.snooze_minutes);
}

void MainWindow::RefreshStatesAfterTick() {
  if (api_ == nullptr) return;
  auto const weak = get_weak();
  (void)api_->get_loops_async([weak](rivet_app::Result<
                                     std::vector<rivet_app::LoopInfo>> r) {
    if (auto self = weak.get()) {
      self->dispatcher_.TryEnqueue([weak, r] {
        if (auto w = weak.get()) {
          try {
            w->model_.loops = r.get();
            w->RefreshTodayCard();
          } catch (std::exception const&) {
          }
        }
      });
    }
  });
  (void)api_->get_paused_async([weak](rivet_app::Result<rivet_app::PauseState> r) {
    if (auto self = weak.get()) {
      self->dispatcher_.TryEnqueue([weak, r] {
        if (auto w = weak.get()) {
          try {
            w->model_.paused = r.get();
            w->RefreshTodayCard();
            w->RefreshPauseButton();
          } catch (std::exception const&) {
          }
        }
      });
    }
  });
}

void MainWindow::ApplyConfig(rivet_app::ReminderConfig const& changed,
                             bool announce) {
  bool const language_changed = changed.language != model_.config.language;
  model_.config = changed;
  language_ = movebit::resolve_language(changed.language);
  l10n::g_language = language_;
  if (language_changed || announce) {
    ApplyTexts();
  }
  RefreshTodayCard();
  RefreshHistoryCard();
  RefreshInsightCard();
  RefreshSettingsWidgets();
  RefreshFooter();
}

// ---- config mutations -------------------------------------------------------------

template <typename F>
void MainWindow::MutateConfig(F mutate) {
  // WinUI fires NumberBox::ValueChanged and ToggleSwitch::Toggled while the
  // XAML tree loads — long before the embedded backend (and api_) exists.
  if (api_ == nullptr) return;
  auto const previous = model_.config;
  auto draft = previous;
  mutate(draft);
  model_.config = draft;
  language_ = movebit::resolve_language(draft.language);
  l10n::g_language = language_;
  auto const weak = get_weak();
  // Optimistic save; the backend clamps and re-announces via config-changed.
  // Completions run on the backend reader thread — re-dispatch.
  (void)api_->set_config_async(draft, [weak, previous](
                                          rivet_app::Result<void> result) {
    if (auto self = weak.get()) {
      self->dispatcher_.TryEnqueue([weak, previous, result] {
        if (auto w = weak.get()) {
          if (result.succeeded()) {
            w->model_.config_status =
                l10n::t("SetStatusSaved", {l10n::T(movebit::now_ms())});
          } else {
            w->model_.config = previous;  // roll back the optimistic draft
            w->model_.config_status = l10n::t("SetStatusSaveFailed");
          }
          w->RefreshStatusMarker();
        }
      });
    }
  });
  model_.config_status = l10n::t("SetStatusSaved", {l10n::T(movebit::now_ms())});
  RefreshStatusMarker();
}

void MainWindow::SaveConfig(rivet_app::ReminderConfig draft) {
  MutateConfig([&draft](rivet_app::ReminderConfig& d) { d = draft; });
}

void MainWindow::RefreshStatusMarker() {
  if (ConfigStatus() != nullptr) {
    ConfigStatus().Text(winrt::to_hstring(
        model_.config_status.empty() ? l10n::t("SetStatusDefault")
                                     : model_.config_status));
  }
}

// ---- refresh ---------------------------------------------------------------------

void MainWindow::SetStatus(std::string const& message) {
  StatusBar().Message(winrt::to_hstring(message));
  StatusBar().Severity(
      message.empty()
          ? Microsoft::UI::Xaml::Controls::InfoBarSeverity::Success
          : Microsoft::UI::Xaml::Controls::InfoBarSeverity::Error);
  StatusBar().IsOpen(!message.empty() || true);
}

void MainWindow::ApplyTexts() {
  TodayTitle().Text(winrt::to_hstring(l10n::t("TodayTitle")));
  SitCycleCaption().Text(winrt::to_hstring(l10n::t("TodaySitCycle")));
  TodaySitCaption().Text(winrt::to_hstring(l10n::t("TodaySit")));
  TodayWaterCaption().Text(winrt::to_hstring(l10n::t("TodayWater")));
  TodayMicroCaption().Text(winrt::to_hstring(l10n::t("TodayMicro")));
  InsightTitleLabel().Text(winrt::to_hstring(l10n::t("InsightTitle")));
  InsightCurrentTitle().Text(winrt::to_hstring(l10n::t("InsightCurrent")));
  InsightTodayLongestTitle().Text(
      winrt::to_hstring(l10n::t("InsightTodayLongest")));
  InsightRecentLongestTitle().Text(
      winrt::to_hstring(l10n::t("InsightRecentLongest")));
  InsightRecentAvgTitle().Text(
      winrt::to_hstring(l10n::t("InsightRecentAvg")));
  InsightBusiestLabel().Text(winrt::to_hstring(l10n::t("InsightBusiest")));
  SettingsTitle().Text(winrt::to_hstring(l10n::t("SetTitle")));
  SettingsIntro().Text(winrt::to_hstring(l10n::t("SetIntro")));
  SetSitLabel().Text(winrt::to_hstring(l10n::t("SetSitInterval")));
  SetWaterLabel().Text(winrt::to_hstring(l10n::t("SetWaterInterval")));
  SetAwayLabel().Text(winrt::to_hstring(l10n::t("SetAwayReset")));
  SetForceLabel().Text(winrt::to_hstring(l10n::t("SetForce")));
  SetBreakLabel().Text(winrt::to_hstring(l10n::t("SetBreakDuration")));
  SetSkipLabel().Text(winrt::to_hstring(l10n::t("SetSkipDelay")));
  SetSnoozeLabel().Text(winrt::to_hstring(l10n::t("SetSnooze")));
  SetMicroLabel().Text(winrt::to_hstring(l10n::t("SetMicroToggle")));
  SetMicroParamsLabel().Text(winrt::to_hstring(l10n::t("SetMicroParams")));
  SetSoundLabel().Text(winrt::to_hstring(l10n::t("SetSound")));
  SetAutostartLabel().Text(winrt::to_hstring(l10n::t("SetAutostart")));
  SetLanguageLabel().Text(winrt::to_hstring(l10n::t("SetLanguage")));
  AboutUpdateLabel().Text(winrt::to_hstring(l10n::t("AboutAutoUpdate")));
  AboutUpdateHint().Text(winrt::to_hstring(l10n::t("AboutAutoUpdateHint")));
  LanguageDrop().Items().GetAt(0).as<Controls::ComboBoxItem>().Content(
      winrt::box_value(winrt::to_hstring(l10n::t("LangAuto"))));
  PauseButton().Content(
      winrt::box_value(winrt::to_hstring(l10n::t("TrayPause"))));
  QuitButton().Content(winrt::box_value(winrt::to_hstring(l10n::t("TrayExit"))));
  RefreshStatusMarker();
  RefreshFooter();
}

void MainWindow::RefreshPauseButton() {
  PauseButton().Content(winrt::box_value(
      winrt::to_hstring(l10n::t(model_.is_paused() ? "TrayResume"
                                                   : "TrayPause"))));
}

void MainWindow::RefreshTodayCard() {
  auto const& m = model_;
  TodayDuration().Text(
      winrt::to_hstring(movebit::format_duration(m.today.active_mins)));
  std::wstring status;
  Windows::UI::Color status_color =
      color_from("#16A34A");
  if (m.is_paused()) {
    status_color = color_from("#EA580C");
    auto const until = m.paused.until_ms.value_or(movebit::now_ms());
    status = winrt::to_hstring(
        l10n::t("TplPausedUntil", {l10n::T(until)}));
  } else {
    status = winrt::to_hstring(l10n::t("TplRunning"));
  }
  TodayStatus().Text(status);
  TodayStatus().Foreground(
      brush_from(status_color.R == 0xEA ? "#EA580C"
                                                        : "#16A34A"));

  double fraction = 0;
  if (!m.is_paused() && m.config.sit_reminder_minutes > 0) {
    fraction = std::min(1.0, std::max(0.0,
        static_cast<double>(m.current_session_mins()) /
        static_cast<double>(m.config.sit_reminder_minutes)));
  }
  SitBar().Value(fraction);

  if (m.is_paused()) {
    SitCycleText().Text(L"—");
  } else {
    SitCycleText().Text(winrt::to_hstring(
        l10n::t("TplSitCycleMin", {l10n::A(m.current_session_mins()),
                                   l10n::A(m.config.sit_reminder_minutes)})));
  }
  TodaySit().Text(winrt::to_hstring(m.today.sit_breaks));
  TodayWater().Text(winrt::to_hstring(m.today.water_reminders));
  TodayMicro().Text(winrt::to_hstring(m.today.micro_breaks));
}

void MainWindow::RefreshHistoryCard() {
  auto const is_month = model_.history_days >= 30;
  HistTitle().Text(winrt::to_hstring(
      l10n::t(is_month ? "HistRange30" : "HistRange7")));
  auto slices = movebit::history_slice(
      model_.history_days, [this] { return model_.today; },
      [this] { return model_.history; });
  std::int64_t total = 0;
  for (auto const& s : slices) total += s.stats.active_mins;
  HistTotal().Text(winrt::to_hstring(
      total >= 60
          ? l10n::t("HistTotalLong", {l10n::A(total / 60), l10n::A(total % 60)})
          : l10n::t("HistTotalShort", {l10n::A(total)})));
  ShowHistoryBars();
}

void MainWindow::ShowHistoryBars() {
  auto const slices = movebit::history_slice(
      model_.history_days, [this] { return model_.today; },
      [this] { return model_.history; });
  auto const is_month = model_.history_days >= 30;
  std::int64_t max_minutes = 60;
  for (auto const& s : slices) {
    max_minutes = std::max(max_minutes, s.stats.active_mins);
  }

  HistChartBars().Children().Clear();
  Controls::Grid columns;
  columns.Height(150);
  for (std::size_t i = 0; i <= slices.size(); ++i) {
    Controls::ColumnDefinition col;
    col.Width(GridLength{1, GridUnitType::Star});
    columns.ColumnDefinitions().Append(col);
  }
  for (std::size_t i = 0; i < slices.size(); ++i) {
    auto const& slice = slices[i];
    auto const bar_max = 110.0;
    auto const bar_min = 5.0;
    double const height =
        bar_min + (bar_max - bar_min) *
                      static_cast<double>(slice.stats.active_mins) /
                      static_cast<double>(max_minutes);
    auto bar = Controls::Border{};
    bar.CornerRadius(CornerRadius{4, 4, 4, 4});
    bar.Background(brush_from(slice.is_today ? "#C25E3E"
                                                             : "#D2C8B2"));
    bar.Height(height);
    bar.Width(is_month ? 8.0 : 26.0);
    bar.VerticalAlignment(VerticalAlignment::Bottom);

    auto cell = Controls::StackPanel{};
    cell.Orientation(Controls::Orientation::Vertical);
    cell.VerticalAlignment(VerticalAlignment::Bottom);
    if (!is_month) {
      auto value = make_text(
          winrt::to_hstring(movebit::format_compact(slice.stats.active_mins)),
          10, slice.is_today,
          slice.is_today ? "#C25E3E" : "#A39B8D");
      cell.Children().Append(value);
    }
    cell.Children().Append(bar);

    std::wstring label;
    if (slice.is_today) {
      label = winrt::to_hstring(l10n::t("HistToday"));
    } else if (!is_month) {
      std::time_t const t =
          static_cast<std::time_t>(slice.day_start_ms / 1000);
      std::tm lt{};
      localtime_s(&lt, &t);
      static wchar_t const* zh_days[] = {L"日", L"一", L"二", L"三", L"四",
                                         L"五", L"六"};
      static wchar_t const* en_days[] = {L"Sun", L"Mon", L"Tue", L"Wed",
                                         L"Thu", L"Fri", L"Sat"};
      label = language_ == "en" ? en_days[lt.tm_wday] : zh_days[lt.tm_wday];
    }
    auto label_block = make_text(
        winrt::to_hstring(label.c_str()), 10.5, slice.is_today,
        slice.is_today ? "#C25E3E" : "#6E6A5E");
    Controls::StackPanel full_cell{};
    full_cell.Orientation(Controls::Orientation::Vertical);
    full_cell.VerticalAlignment(VerticalAlignment::Bottom);
    full_cell.Children().Append(cell);
    full_cell.Children().Append(label_block);
    Controls::Grid wrapper;
    wrapper.Children().Append(full_cell);
    columns.Children().Append(wrapper);
    Controls::Grid::SetColumn(wrapper, static_cast<int>(i));
  }
  HistChartBars().Children().Append(columns);
}

void MainWindow::RefreshInsightCard() {
  auto const slices = movebit::history_slice(
      30, [this] { return model_.today; }, [this] { return model_.history; });
  auto const summary = movebit::insight_summary(slices);
  InsightCurrent().Text(
      winrt::to_hstring(movebit::format_minutes(model_.current_session_mins())));
  InsightTodayLongest().Text(winrt::to_hstring(
      movebit::format_minutes(model_.today.longest_session_mins)));
  InsightRecentLongest().Text(
      winrt::to_hstring(movebit::format_minutes(summary.longest)));
  InsightRecentAvg().Text(
      winrt::to_hstring(movebit::format_minutes(summary.average)));
  std::string busiest_text = l10n::t("InsightNone");
  if (summary.has_busiest && summary.busiest.date.size() >= 10) {
    busiest_text = summary.busiest.date.substr(5, 5) + " · " +
                   movebit::format_minutes(summary.busiest.active_mins);
  }
  InsightBusiest().Text(winrt::to_hstring(busiest_text));

  std::int64_t const sit = model_.config.sit_reminder_minutes;
  std::string sedentary;
  if (summary.longest <= 0) {
    sedentary = l10n::t("InsightNoData");
  } else if (summary.longest >= sit * 2) {
    sedentary = l10n::t("InsightLong2",
                        {l10n::A(movebit::format_minutes(summary.longest))});
  } else if (summary.longest >= sit) {
    sedentary = l10n::t("InsightLong1",
                        {l10n::A(movebit::format_minutes(summary.longest))});
  } else {
    sedentary = l10n::t("InsightLong0",
                        {l10n::A(movebit::format_minutes(summary.longest))});
  }
  InsightSedentary().Text(winrt::to_hstring(sedentary));
}

void MainWindow::RefreshSettingsWidgets() {
  auto const& c = model_.config;
  updating_settings_ = true;
  SitBox().Value(static_cast<double>(c.sit_reminder_minutes));
  WaterBox().Value(static_cast<double>(c.water_reminder_minutes));
  AwayBox().Value(static_cast<double>(c.away_reset_minutes));
  BreakBox().Value(static_cast<double>(c.break_duration_minutes));
  SkipBox().Value(static_cast<double>(c.skip_after_seconds));
  SnoozeBox().Value(static_cast<double>(c.snooze_minutes));
  MicroIntervalBox().Value(
      static_cast<double>(c.micro_break_interval_minutes));
  MicroDurationBox().Value(
      static_cast<double>(c.micro_break_duration_seconds));
  ForceSwitch().IsOn(c.force_break_enabled);
  MicroSwitch().IsOn(c.micro_break_enabled);
  SoundSwitch().IsOn(c.sound_enabled);
  UpdatesSwitch().IsOn(c.auto_check_updates);
  AutostartSwitch().IsOn(model_.autostart);
  LanguageDrop().SelectedIndex(c.language == "zh" ? 1
                               : c.language == "en" ? 2 : 0);
  HistRange().SelectedIndex(model_.history_days >= 30 ? 1 : 0);
  updating_settings_ = false;
  RefreshStatusMarker();
  RefreshPauseButton();
}

void MainWindow::RefreshFooter() {
  FooterText().Text(winrt::to_hstring(
      model_.data_dir.empty()
          ? L""
          : winrt::to_hstring(
                l10n::t("MainConfigPath", {l10n::A(model_.data_dir)}))));
  AboutVersion().Text(winrt::to_hstring(
      l10n::t("UpdCurrentVersion", {l10n::A(rivet_app::kVersion)})));
}

// ---- reloads ------------------------------------------------------------------------

void MainWindow::ReloadHistory() {
  if (api_ == nullptr) return;
  // Always fetch 30 days; the 7/30 toggle is pure client-side slicing.
  auto const weak = get_weak();
  (void)api_->get_history_async(
      30, [weak](rivet_app::Result<std::vector<rivet_app::DayStats>> r) {
        if (auto self = weak.get()) {
          self->dispatcher_.TryEnqueue([weak, r] {
            if (auto w = weak.get()) {
              try {
                w->model_.history = r.get();
                w->RefreshHistoryCard();
                w->RefreshInsightCard();
              } catch (std::exception const& e) {
                w->SetStatus(e.what());
              }
            }
          });
        }
      });
}

void MainWindow::ReloadAutostart() {
  if (api_ == nullptr) return;
  auto const weak = get_weak();
  (void)api_->get_autostart_async([weak](rivet_app::Result<bool> r) {
    if (auto self = weak.get()) {
      self->dispatcher_.TryEnqueue([weak, r] {
        if (auto w = weak.get()) {
          try {
            w->model_.autostart = r.get();
            w->updating_settings_ = true;
            w->AutostartSwitch().IsOn(w->model_.autostart);
            w->updating_settings_ = false;
          } catch (std::exception const&) {
          }
        }
      });
    }
  });
}

void MainWindow::ReloadDataDir() {
  if (api_ == nullptr) return;
  auto const weak = get_weak();
  (void)api_->get_diagnostics_async([weak](rivet_app::Result<std::string> r) {
    if (auto self = weak.get()) {
      self->dispatcher_.TryEnqueue([weak, r] {
        if (auto w = weak.get()) {
          try {
            auto const text = r.get();
            static std::string const prefix = "data-dir: ";
            auto pos = text.find(prefix);
            if (pos != std::string::npos) {
              auto const end = text.find('\n', pos);
              w->model_.data_dir = text.substr(
                  pos + prefix.size(),
                  end == std::string::npos ? std::string::npos
                                           : end - pos - prefix.size());
              w->RefreshFooter();
            }
          } catch (std::exception const&) {
          }
        }
      });
    }
  });
}

// ---- onboarding ---------------------------------------------------------------------

void MainWindow::ShowOnboarding() {
  auto const weak = get_weak();
  onboarding_.on_finish = [weak](std::string const& mode, std::int64_t water,
                                 bool sound) {
    if (auto self = weak.get()) {
      auto const sit = mode == "gentle" ? 60 : mode == "strict" ? 30 : 45;
      auto const force = mode != "gentle";
      auto const break_min = mode == "gentle" ? 3 : 5;
      self->MutateConfig([&](rivet_app::ReminderConfig& d) {
        d.sit_reminder_minutes = sit;
        d.water_reminder_minutes = water;
        d.force_break_enabled = force;
        d.break_duration_minutes = break_min;
        d.sound_enabled = sound;
        d.welcome_shown = true;
      });
      self->toast_.Show(l10n::t("ObSettled"), {l10n::t("ObSettledBody")},
                        rivet_app::ReminderKind::water, {},
                        self->model_.config.snooze_minutes);
    }
  };
  onboarding_.Show();
}

// ---- click handlers -------------------------------------------------------------------

void MainWindow::Pause_Click(winrt::Windows::Foundation::IInspectable const&,
                             Microsoft::UI::Xaml::RoutedEventArgs const&) {
  if (api_ == nullptr) return;
  if (model_.is_paused()) {
    (void)api_->resume_async([](rivet_app::Result<void>) {});
  } else {
    (void)api_->pause_1h_async([](rivet_app::Result<void>) {});
  }
}

void MainWindow::Quit_Click(winrt::Windows::Foundation::IInspectable const&,
                            Microsoft::UI::Xaml::RoutedEventArgs const&) {
  Close();
}

void MainWindow::HistRange_SelectionChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::SelectionChangedEventArgs const&) {
  if (updating_settings_) return;
  model_.history_days = HistRange().SelectedIndex() == 1 ? 30 : 7;
  RefreshHistoryCard();
}

void MainWindow::Language_SelectionChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::SelectionChangedEventArgs const&) {
  if (updating_settings_) return;
  static char const* values[] = {"auto", "zh", "en"};
  auto const index = LanguageDrop().SelectedIndex();
  if (index < 0 || index > 2) return;
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.language = values[index];
  });
}

void MainWindow::Sit_ValueChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const&) {
  if (updating_settings_) return;
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.sit_reminder_minutes = static_cast<std::int64_t>(SitBox().Value());
  });
}

void MainWindow::Water_ValueChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const&) {
  if (updating_settings_) return;
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.water_reminder_minutes =
        static_cast<std::int64_t>(WaterBox().Value());
  });
}

void MainWindow::Away_ValueChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const&) {
  if (updating_settings_) return;
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.away_reset_minutes = static_cast<std::int64_t>(AwayBox().Value());
  });
}

void MainWindow::Break_ValueChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const&) {
  if (updating_settings_) return;
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.break_duration_minutes =
        static_cast<std::int64_t>(BreakBox().Value());
  });
}

void MainWindow::Skip_ValueChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const&) {
  if (updating_settings_) return;
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.skip_after_seconds = static_cast<std::int64_t>(SkipBox().Value());
  });
}

void MainWindow::Snooze_ValueChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const&) {
  if (updating_settings_) return;
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.snooze_minutes = static_cast<std::int64_t>(SnoozeBox().Value());
  });
}

void MainWindow::MicroInterval_ValueChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const&) {
  if (updating_settings_) return;
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.micro_break_interval_minutes =
        static_cast<std::int64_t>(MicroIntervalBox().Value());
  });
}

void MainWindow::MicroDuration_ValueChanged(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::Controls::NumberBoxValueChangedEventArgs const&) {
  if (updating_settings_) return;
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.micro_break_duration_seconds =
        static_cast<std::int64_t>(MicroDurationBox().Value());
  });
}

void MainWindow::Force_Toggled(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::RoutedEventArgs const&) {
  if (updating_settings_) return;
  auto const on = ForceSwitch().IsOn();
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.force_break_enabled = on;
  });
}

void MainWindow::Micro_Toggled(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::RoutedEventArgs const&) {
  if (updating_settings_) return;
  auto const on = MicroSwitch().IsOn();
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.micro_break_enabled = on;
  });
}

void MainWindow::Sound_Toggled(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::RoutedEventArgs const&) {
  if (updating_settings_) return;
  auto const on = SoundSwitch().IsOn();
  MutateConfig([&](rivet_app::ReminderConfig& d) { d.sound_enabled = on; });
}

void MainWindow::Autostart_Toggled(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::RoutedEventArgs const&) {
  if (updating_settings_ || api_ == nullptr) return;
  auto const on = AutostartSwitch().IsOn();
  model_.autostart = on;
  model_.config_status =
      l10n::t("SetStatusApplied", {l10n::T(movebit::now_ms())});
  RefreshStatusMarker();
  (void)api_->set_autostart_async(on, [](rivet_app::Result<void>) {});
}

void MainWindow::Updates_Toggled(
    winrt::Windows::Foundation::IInspectable const&,
    Microsoft::UI::Xaml::RoutedEventArgs const&) {
  if (updating_settings_) return;
  auto const on = UpdatesSwitch().IsOn();
  MutateConfig([&](rivet_app::ReminderConfig& d) {
    d.auto_check_updates = on;
  });
}

}  // namespace winrt::RivetHost::implementation
