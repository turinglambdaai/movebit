using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using MoveBit.Models;
using MoveBit.Services;

namespace MoveBit.ViewModels;

/// One bar in the activity-history chart.
public sealed record HistoryBar(
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
    private string _settingsStatusText = "可直接输入数字 · 修改后自动保存";
    private string _updateStatusText = $"当前版本 v{UpdateService.CurrentVersionText}";
    private string _updateActionText = "检查更新";
    private bool _updateBusy;
    private int _historyDays = 7;
    private IReadOnlyList<HistoryBar> _historyBars = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(ReminderConfig config, ReminderScheduler scheduler, ConfigStore store, HistoryStore history)
    {
        _config = config;
        _scheduler = scheduler;
        _store = store;
        _history = history;
        RefreshStats();
    }

    // --- Settings (saved on change) ---

    public string SettingsStatusText => _settingsStatusText;

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
                PersistSettings();
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

    public decimal? SnoozeMinutes
    {
        get => _config.SnoozeMinutes;
        set => SetSetting(value, v => _config.SnoozeMinutes = Clamp(v, 5, 60));
    }

    public bool SoundEnabled
    {
        get => _config.SoundEnabled;
        set
        {
            if (_config.SoundEnabled != value)
            {
                _config.SoundEnabled = value;
                PersistSettings();
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
                PersistSettings();
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

            _settingsStatusText = $"✓ 已应用 · {DateTime.Now:HH:mm:ss}";
            OnPropertyChanged(nameof(SettingsStatusText));
            OnPropertyChanged();
        }
    }

    public bool AutoCheckUpdates
    {
        get => _config.AutoCheckUpdates;
        set
        {
            if (_config.AutoCheckUpdates != value)
            {
                _config.AutoCheckUpdates = value;
                PersistSettings();
                OnPropertyChanged();
            }
        }
    }

    // --- Online update -----------------------------------------------------

    public string VersionText => $"MoveBit v{UpdateService.CurrentVersionText}";

    public string UpdateStatusText => _updateStatusText;

    public string UpdateActionText => _updateActionText;

    public bool CanUpdateAction => !_updateBusy;

    public void SetUpdateState(string statusText, string actionText, bool busy = false)
    {
        _updateStatusText = statusText;
        _updateActionText = actionText;
        _updateBusy = busy;
        OnPropertyChanged(nameof(UpdateStatusText));
        OnPropertyChanged(nameof(UpdateActionText));
        OnPropertyChanged(nameof(CanUpdateAction));
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

    public IBrush StatusDotBrush =>
        IsPaused ? new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C))
                 : new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));

    public string ConfigPathText => _store.ToString();

    // --- History -----------------------------------------------------------

    public IReadOnlyList<HistoryBar> HistoryBars => _historyBars;

    public string HistoryRangeTitle => _historyDays == 30 ? "最近 30 天" : "最近 7 天";

    public string HistoryTotalText { get; private set; } = "暂无记录";

    public bool IsWeekSelected => _historyDays == 7;

    public bool IsMonthSelected => _historyDays == 30;

    public void ShowHistoryRange(int days)
    {
        var normalized = days >= 30 ? 30 : 7;
        if (_historyDays == normalized)
        {
            return;
        }

        _historyDays = normalized;
        RebuildHistory();
        OnPropertyChanged(nameof(HistoryBars));
        OnPropertyChanged(nameof(HistoryRangeTitle));
        OnPropertyChanged(nameof(HistoryTotalText));
        OnPropertyChanged(nameof(IsWeekSelected));
        OnPropertyChanged(nameof(IsMonthSelected));
    }

    private void ObserveLiveInsights()
    {
        var live = _scheduler.Stats;
        _history.ObserveLongestSession(live.Date, (int)Math.Ceiling(live.LongestSession.TotalMinutes));
    }

    private DayRecord LiveRecord()
    {
        var live = _scheduler.Stats;
        return new DayRecord(
            (int)live.ActiveTime.TotalMinutes,
            live.SitReminders,
            live.WaterReminders,
            live.MicroBreaks,
            (int)Math.Ceiling(live.LongestSession.TotalMinutes));
    }

    private void RebuildHistory()
    {
        var days = _history.GetRecent(_historyDays);
        var live = _scheduler.Stats;
        var byDate = days.ToDictionary(item => item.Date, item => item.Record);
        if (byDate.ContainsKey(live.Date))
        {
            byDate[live.Date] = LiveRecord();
        }

        var totalMinutes = byDate.Values.Sum(record => record.ActiveMinutes);
        var maxMinutes = Math.Max(60.0, byDate.Values.Select(record => (double)record.ActiveMinutes).DefaultIfEmpty().Max());
        var bars = new List<HistoryBar>(days.Count);

        foreach (var (date, _) in days)
        {
            var record = byDate[date];
            var isToday = date == live.Date;
            var dayLabel = _historyDays == 30 ? date.Day.ToString() : date.ToString("ddd");
            bars.Add(new HistoryBar(
                dayLabel,
                FormatMinutesCompact(record.ActiveMinutes),
                $"{date:yyyy-MM-dd} · 活跃 {record.ActiveMinutes} 分钟 · 久坐提醒 {record.SitBreaks} 次 · 最长连续 {record.LongestSessionMinutes} 分钟",
                8 + 72.0 * record.ActiveMinutes / maxMinutes,
                isToday ? TodayBrush : PastBrush,
                isToday));
        }

        _historyBars = bars;
        HistoryTotalText = totalMinutes >= 60
            ? $"合计 {totalMinutes / 60} 小时 {totalMinutes % 60} 分钟"
            : $"合计 {totalMinutes} 分钟";
    }

    // --- Work-pattern insights --------------------------------------------

    public string CurrentSessionText => FormatDuration(_scheduler.CurrentSessionActiveTime);

    public string TodayLongestSessionText => FormatDuration(_scheduler.Stats.LongestSession);

    public string RecentLongestSessionText { get; private set; } = "0m";

    public string RecentAverageText { get; private set; } = "0m";

    public string BusiestDayText { get; private set; } = "暂无";

    public string SedentaryInsightText { get; private set; } = "暂无足够数据";

    private void RebuildInsights()
    {
        var days = _history.GetRecent(30);
        var live = _scheduler.Stats;
        var records = days
            .Select(item => item.Date == live.Date ? (item.Date, Record: LiveRecord()) : item)
            .ToList();

        var longest = records.Max(item => item.Record.LongestSessionMinutes);
        RecentLongestSessionText = FormatMinutes(longest);

        var activeDays = records.Where(item => item.Record.ActiveMinutes > 0).ToList();
        var average = activeDays.Count == 0 ? 0 : (int)Math.Round(activeDays.Average(item => item.Record.ActiveMinutes));
        RecentAverageText = FormatMinutes(average);

        var busiest = activeDays.OrderByDescending(item => item.Record.ActiveMinutes).FirstOrDefault();
        BusiestDayText = busiest == default
            ? "暂无"
            : $"{busiest.Date:MM-dd} · {FormatMinutes(busiest.Record.ActiveMinutes)}";

        if (longest <= 0)
        {
            SedentaryInsightText = "暂无足够数据";
        }
        else if (longest >= _config.SitReminderMinutes * 2)
        {
            SedentaryInsightText = $"最长连续工作达到 {FormatMinutes(longest)}，建议更早离开座位。";
        }
        else if (longest >= _config.SitReminderMinutes)
        {
            SedentaryInsightText = $"最长连续工作 {FormatMinutes(longest)}，已达到一次久坐提醒周期。";
        }
        else
        {
            SedentaryInsightText = $"最长连续工作 {FormatMinutes(longest)}，目前低于久坐提醒周期。";
        }
    }

    public void RefreshStats()
    {
        ObserveLiveInsights();
        RebuildHistory();
        RebuildInsights();
        OnPropertyChanged(nameof(HistoryBars));
        OnPropertyChanged(nameof(HistoryRangeTitle));
        OnPropertyChanged(nameof(HistoryTotalText));
        OnPropertyChanged(nameof(CurrentSessionText));
        OnPropertyChanged(nameof(TodayLongestSessionText));
        OnPropertyChanged(nameof(RecentLongestSessionText));
        OnPropertyChanged(nameof(RecentAverageText));
        OnPropertyChanged(nameof(BusiestDayText));
        OnPropertyChanged(nameof(SedentaryInsightText));
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
        PersistSettings();
        OnPropertyChanged();
        RefreshStats();
    }

    private void PersistSettings()
    {
        _settingsStatusText = _store.TrySave(_config)
            ? $"✓ 已保存 · {DateTime.Now:HH:mm:ss}"
            : "⚠ 已在本次运行中生效，但写入配置文件失败";
        OnPropertyChanged(nameof(SettingsStatusText));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

    private static string FormatDuration(TimeSpan t)
    {
        var total = Math.Max(0, (int)t.TotalMinutes);
        return FormatMinutes(total);
    }

    private static string FormatMinutes(int total)
    {
        total = Math.Max(0, total);
        return total >= 60 ? $"{total / 60}h{total % 60:D2}m" : $"{total}m";
    }

    private static string FormatMinutesCompact(int total)
    {
        total = Math.Max(0, total);
        if (total == 0)
        {
            return "·";
        }

        return total >= 60 ? $"{total / 60}h" : $"{total}m";
    }
}
