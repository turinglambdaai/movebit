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

        _history = new HistoryStore();
        _history.Load();

        _scheduler = new ReminderScheduler(_config, TimeProvider.System, IdleProviderFactory.Create());
        _scheduler.ReminderFired += OnReminderFired;
        _scheduler.DayCompleted += OnDayCompleted;

        _viewModel = new MainViewModel(_config, _scheduler, _configStore, _history);

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
                    _viewModel.SetUpdateState("正在检查 GitHub Releases…", "检查中…", busy: true));
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
                    _viewModel.SetUpdateState("检查更新已取消。", "检查更新"));
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
                _updateItem.Header = $"更新并重启 {update.TagName}";
                _viewModel.SetUpdateState(
                    $"发现新版本 {update.TagName}。已验证发布资产存在，点击后下载、校验并重启。",
                    "更新并重启");
                break;

            case UpdateCheckStatus.UpToDate:
                _availableUpdate = null;
                _updateItem.Header = "检查更新";
                _viewModel.SetUpdateState(
                    userInitiated
                        ? $"已是最新版本 · v{UpdateService.CurrentVersionText}"
                        : $"当前版本 v{UpdateService.CurrentVersionText} · 已是最新",
                    "检查更新");
                break;

            case UpdateCheckStatus.UnsupportedPlatform:
                _availableUpdate = null;
                _updateItem.Header = "检查更新";
                _viewModel.SetUpdateState("当前 CPU / 系统组合暂无官方在线更新包。", "检查更新");
                break;

            default:
                _availableUpdate = null;
                _updateItem.Header = "检查更新";
                _viewModel.SetUpdateState(
                    result.ErrorMessage ?? "检查更新失败，请稍后重试。",
                    "重试");
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
                        UpdateStage.Downloading when p.Percentage is { } percent => $"正在下载 {update.TagName}… {percent}%",
                        UpdateStage.Downloading => $"正在下载 {update.TagName}…",
                        UpdateStage.Verifying => "正在校验 SHA-256…",
                        UpdateStage.Preparing => "校验通过，正在准备替换文件…",
                        UpdateStage.Restarting => "准备重启到新版本…",
                        _ => "正在更新…",
                    };
                    _viewModel.SetUpdateState(text, "更新中…", busy: true);
                }));

            var result = await UpdateService.DownloadAndApplyAsync(update, progress, CancellationToken.None);
            switch (result.Status)
            {
                case UpdateInstallStatus.Restarting:
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        _viewModel.SetUpdateState("更新已校验并准备完成，MoveBit 正在重启…", "正在重启…", busy: true));
                    _updateCancellation.Cancel();
                    FlushToday();
                    _configStore.Save(_config);
                    await Task.Delay(100);
                    await Dispatcher.UIThread.InvokeAsync(Shutdown);
                    break;

                case UpdateInstallStatus.PermissionDenied:
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        _viewModel.SetUpdateState(result.ErrorMessage ?? "应用目录不可写，无法自动更新。", "重试"));
                    break;

                case UpdateInstallStatus.NoUpdate:
                    _availableUpdate = null;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        _updateItem.Header = "检查更新";
                        _viewModel.SetUpdateState($"已是最新版本 · v{UpdateService.CurrentVersionText}", "检查更新");
                    });
                    break;

                default:
                    await Dispatcher.UIThread.InvokeAsync(() =>
                        _viewModel.SetUpdateState(result.ErrorMessage ?? "更新失败，当前版本未被替换。", "重试"));
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                _viewModel.SetUpdateState("更新已取消，当前版本保持不变。", "重试"));
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
        Dispatcher.UIThread.Post(() => ShowReminder(e));
    }

    private void ShowReminder(ReminderEvent e)
    {
        // A scheduler tick can make sit/water/micro become due together. The sit event
        // is emitted first; once it starts a forced break, suppress queued lower-priority
        // reminders so nothing pops over the screen-covering break UI.
        if (_breakActive)
        {
            return;
        }

        // Micro breaks: screen-center nudge, silent by design (they fire often).
        if (e.Kind == ReminderKind.Micro)
        {
            _microWindow?.Close();
            _microWindow = new MicroBreakWindow(
                BreakCopy.Pick(BreakCopy.MicroLines),
                TimeSpan.FromSeconds(_config.MicroBreakDurationSeconds));
            _microWindow.Show();
            return;
        }

        if (e.Kind == ReminderKind.Sit && _config.ForceBreakEnabled)
        {
            // The full-screen takeover is its own notification — silent on purpose,
            // a loud beep plus a screen lock is exactly the office embarrassment
            // that gets health tools uninstalled.
            StartForcedBreak(e);
            return;
        }

        if (_config.SoundEnabled)
        {
            ReminderSound.Play();
        }

        var (title, body) = e.Kind switch
        {
            ReminderKind.Sit => (
                "该起身动一动了 🚶",
                $"已连续工作 {FormatDuration(e.ActiveTimeToday)}，今天第 {e.CountToday} 次提醒。\n{BreakCopy.Pick(BreakCopy.SitLines)}"),
            _ => (
                "喝口水吧 💧",
                $"今天第 {e.CountToday} 次提醒。\n{BreakCopy.Pick(BreakCopy.WaterLines)}"),
        };

        ShowNotification(title, body, e.Kind);
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
            ShowNotification("该起身动一动了 🚶", "已连续工作过久，请离开椅子休息。", ReminderKind.Sit);
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
            };
            overlay.SkipRequested += OnBreakSkipped;
            _overlays.Add(overlay);
            overlay.Show();
        }

        _breakActive = true;
        _breakEvent = e;
        _breakRemaining = TimeSpan.FromMinutes(_config.BreakDurationMinutes);
        var hint = BreakCopy.Pick(BreakCopy.BreakHints);
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
                overlay.SetHint(BreakCopy.Pick(BreakCopy.BreakHints));
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
        _scheduler.Snooze(ReminderKind.Sit, minutes: 10);
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
            Dispatcher.UIThread.Post(() => ShowNotification(
                "水滴为你鼓掌 💧",
                $"今天第 {completedCount} 次久坐休息，你的身体谢谢你。",
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

    private void ShowNotification(string title, string body, ReminderKind kind)
    {
        _notification?.Close();

        _notification = new NotificationWindow
        {
            Title = title,
            NotificationTitle = title,
            NotificationBody = body,
            ReminderKind = kind,
        };
        _notification.Snoozed += (_, k) => _scheduler.Snooze(k, minutes: 10);
        _notification.Show();
    }

    // --- Tray --------------------------------------------------------------

    private void CreateTrayIcon()
    {
        using var stream = AssetLoader.Open(new Uri("avares://MoveBit/Assets/icon.png"));
        var icon = new WindowIcon(stream);

        var openItem = new NativeMenuItem { Header = "设置与统计" };
        openItem.Click += (_, _) => ShowMainWindow();

        _pauseItem = new NativeMenuItem { Header = "暂停提醒 1 小时" };
        _pauseItem.Click += (_, _) => TogglePause();

        _updateItem = new NativeMenuItem { Header = "检查更新" };
        _updateItem.Click += async (_, _) => await CheckOrInstallUpdateFromUiAsync();

        var exitItem = new NativeMenuItem { Header = "退出 MoveBit" };
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

        var updatePrefix = _availableUpdate is { } update ? $"有更新 {update.TagName} · " : string.Empty;
        if (_scheduler.IsPaused)
        {
            var until = _scheduler.PausedUntil!.Value.LocalDateTime;
            _trayIcon.ToolTipText = $"MoveBit · {updatePrefix}已暂停至 {until:HH:mm}";
            _pauseItem.Header = "恢复提醒";
        }
        else
        {
            _trayIcon.ToolTipText = $"MoveBit · {updatePrefix}今日活跃 {FormatDuration(_scheduler.Stats.ActiveTime)} · 久坐 {FormatDuration(_scheduler.SitCycleElapsed)}/{_config.SitReminderMinutes} 分钟";
            _pauseItem.Header = "暂停提醒 1 小时";
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
            ShowNotification("就位 💧", "我会安静待在托盘，到点见。", ReminderKind.Water);
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
        return total >= 60 ? $"{total / 60} 小时 {total % 60} 分钟" : $"{Math.Max(total, 0)} 分钟";
    }
}
