using System;
using System.Collections.Generic;
using System.IO;
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

    private readonly List<BreakOverlayWindow> _overlays = [];
    private DispatcherTimer? _breakTimer;
    private TimeSpan _breakRemaining;
    private bool _breakActive;
    private ReminderEvent? _breakEvent;
    private bool _breakSkipped;

    public MainViewModel ViewModel => _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _configStore = new ConfigStore();
        _config = _configStore.Load();

        IIdleProvider idle = OperatingSystem.IsWindows() ? new Win32IdleProvider() : new NullIdleProvider();
        _scheduler = new ReminderScheduler(_config, TimeProvider.System, idle);
        _scheduler.ReminderFired += OnReminderFired;

        _viewModel = new MainViewModel(_config, _scheduler, _configStore);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += (_, _) => OnTimerTick();
        _timer.Start();

        CreateTrayIcon();
        UpdateTrayState();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Tray app: closing/hiding windows (including transient toasts) must not exit the process.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnTimerTick()
    {
        _scheduler.Tick();
        _viewModel.RefreshStats();
        UpdateTrayState();
    }

    private void OnReminderFired(object? sender, ReminderEvent e)
    {
        Dispatcher.UIThread.Post(() => ShowReminder(e));
    }

    private void ShowReminder(ReminderEvent e)
    {
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
        FinishBreakInternal(); // a second sit reminder while a break is still up: restart it

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
        _breakSkipped = false;
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
        _breakTimer.Tick += (_, _) => OnBreakTimerTick(e);
        _breakTimer.Start();
    }

    private void OnBreakTimerTick(ReminderEvent e)
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
            FinishBreakInternal();
        }
    }

    private void OnBreakSkipped(object? sender, EventArgs e)
    {
        _breakSkipped = true;
        FinishBreakInternal();
        _scheduler.Snooze(ReminderKind.Sit, minutes: 10);
    }

    private void FinishBreakInternal()
    {
        _breakTimer?.Stop();
        _breakTimer = null;
        _breakActive = false;

        foreach (var overlay in _overlays)
        {
            overlay.SkipRequested -= OnBreakSkipped;
            overlay.PlayGoodbye(overlay.ForceClose); // goodbye anim on primary, instant on others
        }

        _overlays.Clear();

        // Milestone cheer: only for breaks that ran to completion, low-frequency by design.
        var completed = _breakEvent?.CountToday ?? 0;
        if (!_breakSkipped && BreakCopy.ShouldCelebrate(completed))
        {
            Dispatcher.UIThread.Post(() => ShowNotification(
                "水滴为你鼓掌 💧",
                $"今天第 {completed} 次久坐休息，你的身体谢谢你。",
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

        var exitItem = new NativeMenuItem { Header = "退出 MoveBit" };
        exitItem.Click += (_, _) => Shutdown();

        var menu = new NativeMenu();
        menu.Add(openItem);
        menu.Add(_pauseItem);
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

        if (_scheduler.IsPaused)
        {
            var until = _scheduler.PausedUntil!.Value.LocalDateTime;
            _trayIcon.ToolTipText = $"MoveBit · 已暂停至 {until:HH:mm}";
            _pauseItem.Header = "恢复提醒";
        }
        else
        {
            _trayIcon.ToolTipText = $"MoveBit · 今日活跃 {FormatDuration(_scheduler.Stats.ActiveTime)} · 久坐 {FormatDuration(_scheduler.SitCycleElapsed)}/{_config.SitReminderMinutes} 分钟";
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

    private void Shutdown()
    {
        FinishBreakInternal();
        _notification?.Close();

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
        _configStore.Save(_config);
        _timer.Stop();
        _trayIcon?.Dispose();
    }

    internal static string FormatDuration(TimeSpan t)
    {
        var total = (int)t.TotalMinutes;
        return total >= 60 ? $"{total / 60} 小时 {total % 60} 分钟" : $"{Math.Max(total, 0)} 分钟";
    }
}
