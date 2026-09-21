using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using MoveBit.Models;
using MoveBit.Services;

namespace MoveBit.ViewModels;

/// One bar in the weekly activity chart.
public sealed record WeekBar(
    string DayLabel,
    string MinutesText,
    string Tooltip,
    double BarHeight,
    IBrush BarBrush,
    bool IsToday);

/// Bindable wrapper around the config + scheduler stats for the main window.
public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly IBrush TodayBrush = new SolidColorBrush(Color.FromRgb(0xC2, 0x5E, 0x3E));
    private static readonly IBrush PastBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD2, 0xC4));

    private readonly ReminderConfig _config;
    private readonly ReminderScheduler _scheduler;
    private readonly ConfigStore _store;
    private readonly HistoryStore _history;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(ReminderConfig config, ReminderScheduler scheduler, ConfigStore store, HistoryStore history)
    {
        _config = config;
        _scheduler = scheduler;
        _store = store;
        _history = history;
    }

    // --- Settings (saved on change) ---

    public decimal? SitReminderMinutes
    {
        get => _config.SitReminderMinutes;
        set => SetSetting(value, v => _config.SitReminderMinutes = Clamp(v, 10, 240));
    }

    public decimal? WaterReminderMinutes
    {
        get => _config.WaterReminderMinutes;
        set => SetSetting(value, v => _config.WaterReminderMinutes = Clamp(v, 5, 180));
    }

    public decimal? AwayResetMinutes
    {
        get => _config.AwayResetMinutes;
        set => SetSetting(value, v => _config.AwayResetMinutes = Clamp(v, 1, 60));
    }

    public bool ForceBreakEnabled
    {
        get => _config.ForceBreakEnabled;
        set
        {
            if (_config.ForceBreakEnabled != value)
            {
                _config.ForceBreakEnabled = value;
                _store.Save(_config);
                OnPropertyChanged();
            }
        }
    }

    public decimal? BreakDurationMinutes
    {
        get => _config.BreakDurationMinutes;
        set => SetSetting(value, v => _config.BreakDurationMinutes = Clamp(v, 1, 30));
    }

    public decimal? SkipAfterSeconds
    {
        get => _config.SkipAfterSeconds;
        set => SetSetting(value, v => _config.SkipAfterSeconds = Clamp(v, 0, 120));
    }

    public bool SoundEnabled
    {
        get => _config.SoundEnabled;
        set
        {
            if (_config.SoundEnabled != value)
            {
                _config.SoundEnabled = value;
                _store.Save(_config);
                OnPropertyChanged();
            }
        }
    }

    public bool MicroBreakEnabled
    {
        get => _config.MicroBreakEnabled;
        set
        {
            if (_config.MicroBreakEnabled != value)
            {
                _config.MicroBreakEnabled = value;
                _store.Save(_config);
                OnPropertyChanged();
            }
        }
    }

    public decimal? MicroBreakIntervalMinutes
    {
        get => _config.MicroBreakIntervalMinutes;
        set => SetSetting(value, v => _config.MicroBreakIntervalMinutes = Clamp(v, 10, 60));
    }

    public decimal? MicroBreakDurationSeconds
    {
        get => _config.MicroBreakDurationSeconds;
        set => SetSetting(value, v => _config.MicroBreakDurationSeconds = Clamp(v, 10, 60));
    }

    /// Login autostart lives in the OS (registry / LaunchAgent / XDG), not in config.json.
    public bool AutoStartEnabled
    {
        get => AutoStart.IsEnabled();
        set
        {
            if (value)
            {
                AutoStart.Enable();
            }
            else
            {
                AutoStart.Disable();
            }

            OnPropertyChanged();
        }
    }

    // --- Today stats (refreshed on every scheduler tick) ---

    public string ActiveTimeText => FormatDuration(_scheduler.Stats.ActiveTime);

    public string SitCycleText =>
        IsPaused ? "—" : $"{(int)_scheduler.SitCycleElapsed.TotalMinutes} / {_config.SitReminderMinutes} 分钟";

    public int SitCycleMinutes => IsPaused ? 0 : (int)_scheduler.SitCycleElapsed.TotalMinutes;

    public int SitIntervalMax => _config.SitReminderMinutes;

    public int SitReminders => _scheduler.Stats.SitReminders;

    public int WaterReminders => _scheduler.Stats.WaterReminders;

    public int MicroBreaks => _scheduler.Stats.MicroBreaks;

    public bool IsPaused => _scheduler.IsPaused;

    public string PauseText => _scheduler.PausedUntil is { } until
        ? $"已暂停至 {until.LocalDateTime:HH:mm}"
        : "运行中";

    /// Green dot while running, orange while paused.
    public Avalonia.Media.IBrush StatusDotBrush =>
        IsPaused ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0xEA, 0x58, 0x0C))
                 : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(0x16, 0xA3, 0x4A));

    public string ConfigPathText => _store.ToString();

    // --- Weekly chart -------------------------------------------------------

    private IReadOnlyList<WeekBar> _weekBars = [];

    public IReadOnlyList<WeekBar> WeekBars => _weekBars;

    public string WeekTotalText { get; private set; } = "本周：暂无记录";

    private void RebuildWeek()
    {
        var days = _history.GetRecent(7);
        var byDate = new Dictionary<DateOnly, DayRecord>();
        foreach (var (date, record) in days)
        {
            byDate[date] = record;
        }

        // Today runs on live scheduler data — the history file lags up to one flush.
        var live = _scheduler.Stats;
        if (byDate.TryGetValue(live.Date, out _))
        {
            byDate[live.Date] = new DayRecord(
                (int)live.ActiveTime.TotalMinutes, live.SitReminders, live.WaterReminders, live.MicroBreaks);
        }

        var totalMinutes = 0;
        var maxMinutes = 60.0; // baseline keeps one light day from filling the whole chart
        foreach (var record in byDate.Values)
        {
            totalMinutes += record.ActiveMinutes;
            maxMinutes = Math.Max(maxMinutes, record.ActiveMinutes);
        }

        var bars = new List<WeekBar>(days.Count);
        foreach (var (date, _) in days)
        {
            var record = byDate[date];
            var isToday = date == live.Date;
            bars.Add(new WeekBar(
                date.ToString("ddd"),
                record.ActiveMinutes >= 60 ? $"{record.ActiveMinutes / 60}h{record.ActiveMinutes % 60:D2}" : $"{record.ActiveMinutes}m",
                $"{date:MM-dd} · 活跃 {record.ActiveMinutes} 分钟 · 休息 {record.SitBreaks} 次 · 微休息 {record.MicroBreaks} 次",
                8 + 72.0 * record.ActiveMinutes / maxMinutes,
                isToday ? TodayBrush : PastBrush,
                isToday));
        }

        _weekBars = bars;
        WeekTotalText = totalMinutes >= 60
            ? $"本周活跃 {totalMinutes / 60} 小时 {totalMinutes % 60} 分钟"
            : $"本周活跃 {totalMinutes} 分钟";
    }

    public void RefreshStats()
    {
        RebuildWeek();
        OnPropertyChanged(nameof(WeekBars));
        OnPropertyChanged(nameof(WeekTotalText));
        OnPropertyChanged(nameof(ActiveTimeText));
        OnPropertyChanged(nameof(SitCycleText));
        OnPropertyChanged(nameof(SitCycleMinutes));
        OnPropertyChanged(nameof(SitIntervalMax));
        OnPropertyChanged(nameof(SitReminders));
        OnPropertyChanged(nameof(WaterReminders));
        OnPropertyChanged(nameof(MicroBreaks));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseText));
        OnPropertyChanged(nameof(StatusDotBrush));
    }

    private void SetSetting(decimal? value, Action<int> apply)
    {
        if (value is not { } v)
        {
            return;
        }

        apply((int)v);
        _store.Save(_config);
        OnPropertyChanged();
        RefreshStats();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    private static string FormatDuration(TimeSpan t)
    {
        var total = (int)t.TotalMinutes;
        return total >= 60 ? $"{total / 60}h{total % 60:D2}m" : $"{Math.Max(total, 0)}m";
    }
}
