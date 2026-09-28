using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using MoveBit.Models;
using MoveBit.Services;
using MoveBit.ViewModels;

namespace MoveBit;

public class App : Application
{
    private ReminderConfig _config = null!;
    private ConfigStore _configStore = null!;
    private ReminderScheduler _scheduler = null!;
    private DispatcherTimer _timer = null!;
    private TrayIcon? _trayIcon;
    private Window? _screenProbe;
    private MainWindow? _mainWindow;
    private NotificationWindow? _notification;
    private MainViewModel _viewModel = null!;
    private NativeMenuItem _pauseItem = null!;
    private NativeMenuItem _updateItem = null!;
    private HistoryStore _history = null!;
    private MicroBreakWindow? _microWindow;
    private int _flushCounter;

    private readonly List<BreakOverlayWindow> _overlays = [];
    private readonly List<ReminderEvent> _tickReminders = [];
    private DispatcherTimer? _breakTimer;
    private TimeSpan _breakRemaining;
    private bool _breakActive;
    private ReminderEvent? _breakEvent;

    private readonly CancellationTokenSource _updateCancellation = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private UpdateInfo? _availableUpdate;

    public MainViewModel ViewModel => _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _configStore = new ConfigStore();
        _config = _configStore.Load();
        L10n.Apply(_config.Language);

        _history = new HistoryStore();
        _history.Load();

        _scheduler = new ReminderScheduler(_config, TimeProvider.System, IdleProviderFactory.Create());
        _scheduler.ReminderFired += OnReminderFired;
        _scheduler.DayCompleted += OnDayCompleted;

        // A mid-day restart must keep the morning's activity: the fresh session would
        // otherwise flush session-only numbers over the persisted record.
        var persistedToday = _history.GetRecent(1);
        if (persistedToday is [.., { Date: var date, Record: var record }] && date == _scheduler.Stats.Date)
        {
            _scheduler.RestoreToday(record);
        }

        _viewModel = new MainViewModel(_config, _scheduler, _configStore, _history);
        _viewModel.LocalizationChanged += (_, _) => UpdateTrayState();

#if PRO
        // Pro overlay: wire the paid sync feature into the app. The MIT build
        // never compiles this call (MoveBit.Sync lives in the private repo).
        Sync.Bootstrap.Initialize(_config, _scheduler, _history, _viewModel);
#endif

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => OnTimerTick();
        _timer.Start();

        StartActivationServer();

        CreateTrayIcon();
        UpdateTrayState();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Tray app: closing/hiding windows (including transient toasts) must not exit the process.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += OnExit;
        }

        // First run: walk the user through a strictness pact with the droplet. A tool
        // that locks every screen in 45 minutes owes the user an explicit agreement.
        if (!_config.WelcomeShown)
        {
            ShowOnboarding();
        }

        _ = RunAutomaticUpdateLoopAsync(_updateCancellation.Token);

        base.OnFrameworkInitializationCompleted();
    }

    // --- Online update -----------------------------------------------------

    private async Task RunAutomaticUpdateLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_config.AutoCheckUpdates)
            {
                try
                {
                    await CheckForUpdatesAsync(userInitiated: false, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(6), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task CheckOrInstallUpdateFromUiAsync()
    {
        if (_availableUpdate is { } update)
        {
            await InstallUpdateAsync(update);
            return;
        }

        await CheckForUpdatesAsync(userInitiated: true, CancellationToken.None);
    }

    private async Task CheckForUpdatesAsync(bool userInitiated, CancellationToken cancellationToken)
    {
        var entered = false;
        try
        {
            if (userInitiated)
            {
                await _updateGate.WaitAsync(cancellationToken);
                entered = true;
                await Dispatcher.UIThread.InvokeAsync(() =>
                    _viewModel.SetUpdateState(L10n.T("Upd.Checking"), L10n.T("Upd.CheckingShort"), busy: true));
            }
            else
            {
                entered = await _updateGate.WaitAsync(0, cancellationToken);
                if (!entered)
                {
                    return;
                }
            }

            var result = await UpdateService.CheckForUpdateAsync(cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() => ApplyUpdateCheckResult(result, userInitiated));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (userInitiated)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    _viewModel.SetUpdateState(L10n.T("Upd.CheckCancelled"), L10n.T("Tray.CheckUpdate")));
            }
        }
        finally
        {
            if (entered)
            {
                _updateGate.Release();
            }
        }
    }

    private void ApplyUpdateCheckResult(UpdateCheckResult result, bool userInitiated)
    {
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable when result.Update is { } update:
                _availableUpdate = update;
                _updateItem.Header = L10n.T("Tray.UpdateTo", update.TagName);
                _viewModel.SetUpdateState(
                    L10n.T("Upd.Found", update.TagName),
                    L10n.T("Upd.ActionUpdate"));
                break;

            case UpdateCheckStatus.UpToDate:
                _availableUpdate = null;
                _updateItem.Header = L10n.T("Tray.CheckUpdate");
                _viewModel.SetUpdateState(
                    userInitiated
                        ? L10n.T("Upd.Latest", UpdateService.CurrentVersionText)
                        : L10n.T("Upd.CurrentUpToDate", UpdateService.CurrentVersionText),
                    L10n.T("Tray.CheckUpdate"));
                break;

            case UpdateCheckStatus.UnsupportedPlatform:
                _availableUpdate = null;
                _updateItem.Header = L10n.T("Tray.CheckUpdate");
                _viewModel.SetUpdateState(L10n.T("Upd.Unsupported"), L10n.T("Tray.CheckUpdate"));
                break;

            default:
                _availableUpdate = null;
                _updateItem.Header = L10n.T("Tray.CheckUpdate");
                _viewModel.SetUpdateState(
                    result.ErrorMessage ?? L10n.T("Upd.CheckFailed"),
                    L10n.T("Upd.ActionRetry"));
                break;
        }

        UpdateTrayState();
    }

    private async Task InstallUpdateAsync(UpdateInfo update)
    {
        if (!await _updateGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _viewModel.SetUpdateState($"准备更新到 {update.TagName}…", "更新中…", busy: true));

            var progress = new Progress<UpdateProgress>(p =>
                Dispatcher.UIThread.Post(() =>
                {
                    var text = p.Stage switch
                    {
                        UpdateStage.Downloading when p.Percentage is { } percent => L10n.T("Upd.DownloadingPct", update.TagName, percent),
                        UpdateStage.Downloading => L10n.T("Upd.Downloading", update.TagName),
                        UpdateStage.Verifying => L10n.T("Upd.Verifying"),
                        UpdateStage.Preparing => L10n.T("Upd.Staging"),
                        UpdateStage.Restarting => L10n.T("Upd.RestartPrep"),
                        _ => L10n.T("Upd.Generic"),
                    };
                    _viewModel.SetUpdateState(text, L10n.T("Upd.ActionUpdating"), busy: true);
                }));

            var result = await UpdateService.DownloadAndApplyAsync(update, progress, CancellationToken.None);
            switch (result.Status)
            {
                case UpdateInstallStatus.Restarting:
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        _viewModel.SetUpdateState(L10n.T("Upd.Restarting"), L10n.T("Upd.ActionUpdating"), busy: true));
                    _updateCancellation.Cancel();
                    FlushToday();
                    _configStore.Save(_config);
                    await Task.Delay(100);
                    await Dispatcher.UIThread.InvokeAsync(Shutdown);
                    break;

                case UpdateInstallStatus.PermissionDenied:
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        _viewModel.SetUpdateState(result.ErrorMessage ?? L10n.T("Upd.NotWritable"), L10n.T("Upd.ActionRetry")));
                    break;

                case UpdateInstallStatus.NoUpdate:
                    _availableUpdate = null;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _updateItem.Header = L10n.T("Tray.CheckUpdate");
                        _viewModel.SetUpdateState(L10n.T("Upd.Latest", UpdateService.CurrentVersionText), L10n.T("Tray.CheckUpdate"));
                    });
                    break;

                default:
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        _viewModel.SetUpdateState(result.ErrorMessage ?? L10n.T("Upd.Failed"), L10n.T("Upd.ActionRetry")));
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _viewModel.SetUpdateState(L10n.T("Upd.Cancelled"), L10n.T("Upd.ActionRetry")));
        }
        finally
        {
            _updateGate.Release();
        }
    }

    // --- Second-instance activation ----------------------------------------

    private System.Net.Sockets.TcpListener? _activationListener;

    /// <summary>
    /// Listens on the loopback activation port so a second launch (blocked by the
    /// mutex) can ask this instance to surface its window. Best-effort: if the port
    /// is taken, single-instancing still holds via the mutex.
    /// </summary>
    private void StartActivationServer()
    {
        try
        {
            _activationListener = new System.Net.Sockets.TcpListener(
                System.Net.IPAddress.Loopback, SingleInstanceGuard.ActivationPort);
            _activationListener.Start();
            _ = AcceptActivationsAsync();
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException)
        {
            _activationListener = null;
        }
    }

    private async Task AcceptActivationsAsync()
    {
        while (_activationListener is { } listener)
        {
            System.Net.Sockets.TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync();
            }
            catch (System.Net.Sockets.SocketException)
            {
                break; // listener stopped on shutdown
            }

            _ = HandleActivationAsync(client);
        }
    }

    private async Task HandleActivationAsync(System.Net.Sockets.TcpClient client)
    {
        try
        {
            using var _ = client;
            using var reader = new StreamReader(client.GetStream());
            var line = await reader.ReadLineAsync();

            if (line == "activate")
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Don't fight the break lock: a window popping over the overlay
                    // would undercut the whole point of the break.
                    if (!_breakActive)
                    {
                        ShowMainWindow();
                    }
                });
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            // A poke that never finishes is fine; the sender already gave up fast.
        }
    }

    private void OnTimerTick()
    {
        if (_breakActive)
        {
            // A forced break is rest, not active work. Keep the scheduler's wall-clock
            // anchor current without advancing any reminder cycle or daily active time.
            _scheduler.DiscardElapsedSinceLastTick();
        }
        else
        {
            _scheduler.Tick();
        }

        FlushTickReminders();
        _viewModel.RefreshStats();
        UpdateTrayState();

        // Flush today's stats every ~5 minutes so a crash or power loss never
        // costs more than a few minutes of history.
        if (++_flushCounter >= 10)
        {
            _flushCounter = 0;
            FlushToday();
        }
    }

    private void OnDayCompleted(object? sender, DayStats closingDay)
    {
        _history.SaveDay(closingDay.Date, ToRecord(closingDay));
    }

    private void FlushToday()
    {
        _history.SaveDay(_scheduler.Stats.Date, ToRecord(_scheduler.Stats));
    }

    private static DayRecord ToRecord(DayStats stats) => new(
        (int)stats.ActiveTime.TotalMinutes,
        stats.SitReminders,
        stats.WaterReminders,
        stats.MicroBreaks);

    private void OnReminderFired(object? sender, ReminderEvent e)
    {
        // Raised synchronously on the UI thread inside scheduler.Tick(); the batch is
        // dispatched once per tick in FlushTickReminders so overlapping timers merge
        // into a single interruption instead of one window per timer.
        _tickReminders.Add(e);
    }

    /// <summary>
    /// One interruption per tick, ever: a sit event that starts a forced break swallows
    /// the rest, and otherwise every kind due in this tick merges into one toast —
    /// water and micro-break due together become one card, never two.
    /// </summary>
    private void FlushTickReminders()
    {
        if (_tickReminders.Count == 0)
        {
            return;
        }

        var events = _tickReminders.ToArray();
        _tickReminders.Clear();

        ReminderEvent? sit = null, water = null, micro = null;
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case ReminderKind.Sit when sit is null:
                    sit = e;
                    break;
                case ReminderKind.Water when water is null:
                    water = e;
                    break;
                case ReminderKind.Micro when micro is null:
                    micro = e;
                    break;
            }
        }

        if (sit is { } && _config.ForceBreakEnabled)
        {
            // The full-screen takeover is its own notification — silent on purpose,
            // a loud beep plus a screen lock is exactly the office embarrassment
            // that gets health tools uninstalled.
            StartForcedBreak(sit);
            return;
        }

        var lines = new List<string>();
        var kinds = new List<ReminderKind>();
        string title = "喝口水吧 💧";
        var kind = ReminderKind.Water;

        if (sit is { } s)
        {
            kind = ReminderKind.Sit;
            title = L10n.T("Notif.SitTitle");
            lines.Add(L10n.T("Notif.SitBody", FormatDuration(s.ActiveTimeToday), s.CountToday));
            lines.Add(BreakCopy.PickSit());
            kinds.Add(ReminderKind.Sit);
        }

        if (water is { } w)
        {
            if (sit is null)
            {
                title = L10n.T("Notif.WaterTitle");
                lines.Add(L10n.T("Notif.WaterCount", w.CountToday));
            }

            lines.Add(BreakCopy.PickWater());
            kinds.Add(ReminderKind.Water);
        }

        if (sit is null && water is null)
        {
            ShowMicroBreak();
            return;
        }

        if (micro is { })
        {
            lines.Add(BreakCopy.PickMicro());
            kinds.Add(ReminderKind.Micro);
        }

        if (_config.SoundEnabled)
        {
            ReminderSound.Play();
        }

        ShowNotification(title, string.Join("\n", lines), kind, kinds);
    }

    private void ShowMicroBreak()
    {
        // Micro breaks: screen-center nudge, silent by design (they fire often).
        _microWindow?.Close();
        _microWindow = new MicroBreakWindow(
            BreakCopy.PickMicro(),
            TimeSpan.FromSeconds(_config.MicroBreakDurationSeconds));
        _microWindow.Show();
    }

    // --- Forced break ------------------------------------------------------

    /// <summary>Takes over every connected screen with a topmost break lock.</summary>
    private void StartForcedBreak(ReminderEvent e)
    {
        if (_breakActive || _overlays.Count > 0)
        {
            FinishBreakInternal();
        }

        // A full-screen break has priority over transient nudges/toasts that may
        // already be visible from a previous cycle.
        _notification?.Close();
        _notification = null;
        _microWindow?.Close();
        _microWindow = null;

        var screens = EnumerateScreens();
        if (screens.Count == 0)
        {
            ShowNotification(L10n.T("Notif.SitTitle"), L10n.T("Notif.SitFallback"), ReminderKind.Sit);
            return;
        }

        var primary = screens.Find(s => s.IsPrimary) ?? screens[0];
        foreach (var screen in screens)
        {
            var overlay = new BreakOverlayWindow
            {
                TargetBounds = screen.Bounds,
                TargetScaling = screen.Scaling,
                IsPrimary = ReferenceEquals(screen, primary),
                SnoozeMinutes = _config.SnoozeMinutes,
            };
            overlay.SkipRequested += OnBreakSkipped;
            _overlays.Add(overlay);
            overlay.Show();
        }

        _breakActive = true;
        _breakEvent = e;
        _breakRemaining = TimeSpan.FromMinutes(_config.BreakDurationMinutes);
        var hint = BreakCopy.PickHint();
        foreach (var overlay in _overlays)
        {
            overlay.UpdateCountdown(_breakRemaining, _breakRemaining, skipAvailable: false);
            if (overlay.IsPrimary)
            {
                overlay.SetHint(hint);
            }
        }

        _breakTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _breakTimer.Tick += (_, _) => OnBreakTimerTick();
        _breakTimer.Start();
    }

    private void OnBreakTimerTick()
    {
        _breakRemaining -= TimeSpan.FromSeconds(1);

        var elapsed = TimeSpan.FromMinutes(_config.BreakDurationMinutes) - _breakRemaining;
        var total = TimeSpan.FromMinutes(_config.BreakDurationMinutes);
        var skipAvailable = elapsed >= TimeSpan.FromSeconds(_config.SkipAfterSeconds);
        var rotateHint = (int)elapsed.TotalSeconds > 0 && (int)elapsed.TotalSeconds % 25 == 0;

        foreach (var overlay in _overlays)
        {
            overlay.UpdateCountdown(_breakRemaining, total, skipAvailable);
            if (rotateHint && overlay.IsPrimary)
            {
                overlay.SetHint(BreakCopy.PickHint());
            }
        }

        if (_breakRemaining <= TimeSpan.Zero)
        {
            FinishBreakInternal(completed: true);
        }
    }

    private void OnBreakSkipped(object? sender, EventArgs e)
    {
        FinishBreakInternal();
        _scheduler.Snooze(ReminderKind.Sit);
    }

    private void FinishBreakInternal(bool completed = false)
    {
        var completedCount = completed ? _breakEvent?.CountToday ?? 0 : 0;

        _breakTimer?.Stop();
        _breakTimer = null;
        _breakActive = false;
        _breakEvent = null;

        foreach (var overlay in _overlays)
        {
            overlay.SkipRequested -= OnBreakSkipped;
            if (completed)
            {
                overlay.PlayGoodbye(overlay.ForceClose);
            }
            else
            {
                overlay.ForceClose();
            }
        }

        _overlays.Clear();

        if (completed)
        {
            // A completed forced break is a real break: restart every reminder cycle,
            // just like returning after being away from the desk.
            _scheduler.CompleteBreak();
        }
        else
        {
            // Skipping/teardown still must not count time spent under the overlay as work.
            _scheduler.DiscardElapsedSinceLastTick();
        }

        if (completed && BreakCopy.ShouldCelebrate(completedCount))
        {
            var active = FormatDuration(_scheduler.Stats.ActiveTime);
            Dispatcher.UIThread.Post(() => ShowNotification(
                L10n.T("Notif.CheerTitle"),
                L10n.T("Notif.CheerBody", completedCount, active),
                ReminderKind.Sit));
        }
    }

    /// True while the break lock covers the screens (used by tests / future tray state).
    public bool IsBreakActive => _breakActive;

    // --- Screens -----------------------------------------------------------

    private List<Screen> EnumerateScreens()
    {
        if (_screenProbe is null)
        {
            // A never-visible 1px window: Avalonia exposes Screen enumeration only
            // through an attached window, and the tray app has none.
            _screenProbe = new Window
            {
                SystemDecorations = SystemDecorations.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                Background = Brushes.Transparent,
                Width = 1,
                Height = 1,
            };
            _screenProbe.Show();
            _screenProbe.Hide();
        }

        return [.. _screenProbe.Screens.All];
    }

    // --- Notifications -----------------------------------------------------

    private void ShowNotification(string title, string body, ReminderKind kind, IReadOnlyList<ReminderKind>? snoozeKinds = null)
    {
        _notification?.Close();

        _notification = new NotificationWindow
        {
            Title = title,
            NotificationTitle = title,
            NotificationBody = body,
            ReminderKind = kind,
            SnoozeMinutes = _config.SnoozeMinutes,
        };
        if (snoozeKinds is { Count: > 0 } merged)
        {
            // A merged toast stands in for every timer due this tick; snoozing it
            // must postpone all of them, not just the one that colored the card.
            _notification.Snoozed += (_, _) =>
            {
                foreach (var snoozedKind in merged)
                {
                    _scheduler.Snooze(snoozedKind);
                }
            };
        }
        else
        {
            _notification.Snoozed += (_, k) => _scheduler.Snooze(k);
        }

        _notification.Show();
    }

    // --- Tray --------------------------------------------------------------

    private void CreateTrayIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://MoveBit/Assets/icon.png"));
        var icon = new WindowIcon(stream);

        var openItem = new NativeMenuItem { Header = L10n.T("Tray.Open") };
        openItem.Click += (_, _) => ShowMainWindow();

        _pauseItem = new NativeMenuItem { Header = L10n.T("Tray.Pause") };
        _pauseItem.Click += (_, _) => TogglePause();

        _updateItem = new NativeMenuItem { Header = L10n.T("Tray.CheckUpdate") };
        _updateItem.Click += async (_, _) => await CheckOrInstallUpdateFromUiAsync();

        var exitItem = new NativeMenuItem { Header = L10n.T("Tray.Exit") };
        exitItem.Click += (_, _) => Shutdown();

        var menu = new NativeMenu();
        menu.Add(openItem);
        menu.Add(_pauseItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_updateItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);

        _trayIcon = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "MoveBit",
            Menu = menu,
        };
        _trayIcon.Clicked += (_, _) => ShowMainWindow();
    }

    private void TogglePause()
    {
        if (_scheduler.IsPaused)
        {
            _scheduler.Resume();
        }
        else
        {
            _scheduler.PauseFor(TimeSpan.FromHours(1));
        }

        _viewModel.RefreshStats();
        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        if (_trayIcon is null)
        {
            return;
        }

        var updatePrefix = _availableUpdate is { } update ? L10n.T("Tray.UpdatePrefix", update.TagName) : string.Empty;
        _updateItem.Header = _availableUpdate is { } up ? L10n.T("Tray.UpdateTo", up.TagName) : L10n.T("Tray.CheckUpdate");
        if (_scheduler.IsPaused)
        {
            var until = _scheduler.PausedUntil!.Value.LocalDateTime;
            _trayIcon.ToolTipText = L10n.T("Tray.Paused", updatePrefix, until);
            _pauseItem.Header = L10n.T("Tray.Resume");
        }
        else
        {
            _trayIcon.ToolTipText = L10n.T(
                "Tray.Running",
                updatePrefix,
                FormatDuration(_scheduler.Stats.ActiveTime),
                FormatDuration(_scheduler.SitCycleElapsed),
                _config.SitReminderMinutes);
            _pauseItem.Header = L10n.T("Tray.Pause");
        }
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow { DataContext = _viewModel };
            _mainWindow.Closing += (_, e) =>
            {
                // Tray app: closing the window hides it; exit goes through the tray menu.
                if (_mainWindow.HideOnClose)
                {
                    e.Cancel = true;
                    _mainWindow.Hide();
                }
            };
        }

        _viewModel.RefreshStats();
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ShowOnboarding()
    {
        var onboarding = new OnboardingWindow(_config)
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };

        // Finished (or skipped): persist the pact and slip into the tray.
        onboarding.Finished += () =>
        {
            _config.WelcomeShown = true;
            _configStore.Save(_config);
            _viewModel.RefreshStats();
            ShowNotification(L10n.T("Ob.Settled"), L10n.T("Ob.SettledBody"), ReminderKind.Water);
        };

        // Closed via the title bar without finishing: still counts as seen, so the
        // onboarding cannot nag on every launch. Whatever was selected last is kept.
        onboarding.Closed += (_, _) =>
        {
            if (!_config.WelcomeShown)
            {
                _config.WelcomeShown = true;
                _configStore.Save(_config);
                _viewModel.RefreshStats();
            }
        };

        onboarding.Show();
    }

    private void Shutdown()
    {
        FinishBreakInternal();
        _notification?.Close();
        _microWindow?.Close();

        if (_mainWindow is { } window)
        {
            window.HideOnClose = false;
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        _updateCancellation.Cancel();
        FlushToday();
        _configStore.Save(_config);
        _timer.Stop();
        _activationListener?.Stop();
        _trayIcon?.Dispose();
    }

    internal static string FormatDuration(TimeSpan t)
    {
        var total = (int)t.TotalMinutes;
        return total >= 60 ? L10n.T("App.HourMin", total / 60, total % 60) : L10n.T("App.Minutes", Math.Max(total, 0));
    }
}
