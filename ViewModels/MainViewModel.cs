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
    double BarWidth,
    double ColumnWidth,
    bool ShowValue,
    bool IsToday);

/// Bindable wrapper around the config + scheduler stats for the main window.
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ReminderConfig _config;
    private readonly ReminderScheduler _scheduler;
    private readonly ConfigStore _store;
    private readonly HistoryStore _history;
    private string _settingsStatusText = L10n.T("Set.StatusDefault");
    private string _updateStatusText = L10n.T("Upd.CurrentVersion", UpdateService.CurrentVersionText);
    private string _updateActionText = L10n.T("Tray.CheckUpdate");
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

            _settingsStatusText = L10n.T("Set.StatusApplied", DateTime.Now);
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

    /// Raised after the UI language changed, so App can rebuild chrome that lives
    /// outside the binding system (tray menu, tray tooltip).
    public event EventHandler? LocalizationChanged;

    /// 0 = follow system, 1 = 中文, 2 = English (ComboBox order in the settings card).
    public int LanguageIndex
    {
        get => _config.Language switch { "zh" => 1, "en" => 2, _ => 0 };
        set
        {
            var lang = value switch { 1 => "zh", 2 => "en", _ => "auto" };
            if (_config.Language == lang)
            {
                return;
            }

            _config.Language = lang;
            PersistSettings();
            L10n.Apply(lang);
            Relocalize();
            OnPropertyChanged();
            LocalizationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Relocalize()
    {
        RefreshStats();

        _settingsStatusText = L10n.T("Set.StatusDefault");
        OnPropertyChanged(nameof(SettingsStatusText));

        // Leave an in-flight update check alone; re-label only the idle state.
        if (!_updateBusy)
        {
            SetUpdateState(L10n.T("Upd.CurrentVersion", UpdateService.CurrentVersionText), L10n.T("Tray.CheckUpdate"));
        }
    }

    // --- Sync (Pro builds only) ----------------------------------------------

    /// The sync card is compiled into the MIT build but only ever visible in Pro.
    public bool SyncSectionVisible => BuildInfo.IsPro;

    public string SyncStatusText { get; private set; } = "";

    /// Called by the Pro overlay's bootstrap; a no-op path in the MIT build.
    public void SetSyncStatus(string text)
    {
        SyncStatusText = text;
        OnPropertyChanged(nameof(SyncStatusText));
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
        IsPaused ? "—" : L10n.T("Tpl.SitCycleMin", (int)_scheduler.SitCycleElapsed.TotalMinutes, _config.SitReminderMinutes);

    public int SitCycleMinutes => IsPaused ? 0 : (int)_scheduler.SitCycleElapsed.TotalMinutes;

    public int SitIntervalMax => _config.SitReminderMinutes;

    public int SitReminders => _scheduler.Stats.SitReminders;

    public int WaterReminders => _scheduler.Stats.WaterReminders;

    public int MicroBreaks => _scheduler.Stats.MicroBreaks;

    public bool IsPaused => _scheduler.IsPaused;

    public string PauseText => _scheduler.PausedUntil is { } until
        ? L10n.T("Tpl.PausedUntil", until.LocalDateTime)
        : L10n.T("Tpl.Running");

    public IBrush StatusDotBrush =>
        IsPaused ? new SolidColorBrush(Color.FromRgb(0xEA, 0x58, 0x0C))
                 : new SolidColorBrush(Color.FromRgb(0x16, 0xA3, 0x4A));

    public string ConfigPathText => string.Format(L10n.Culture, L10n.T("Main.ConfigPath"), _store);

    // --- History -----------------------------------------------------------

    public IReadOnlyList<HistoryBar> HistoryBars => _historyBars;

    /// Dashed daily-average reference line over the bar plot (rendered overlay).
    public bool ShowAvgLine { get; private set; }

    /// Vertical translate for the average line, measured up from the plot baseline.
    public double AvgLineTranslate { get; private set; }

    public string AvgLineText { get; private set; } = L10n.T("Hist.Avg", "—");

    public string HistoryRangeTitle => _historyDays == 30 ? L10n.T("Hist.Range30") : L10n.T("Hist.Range7");

    public string HistoryTotalText { get; private set; } = L10n.T("Hist.TotalShort", 0);

    public bool IsWeekSelected => _historyDays == 7;

    public bool IsMonthSelected => _historyDays == 30;

    /// UniformGrid column count for the chart, so bars tile the full card width.
    public int HistoryColumns => _historyDays;

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
        OnPropertyChanged(nameof(HistoryColumns));
        OnPropertyChanged(nameof(ShowAvgLine));
        OnPropertyChanged(nameof(AvgLineTranslate));
        OnPropertyChanged(nameof(AvgLineText));
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
        var isMonth = _historyDays == 30;
        // Plot area is 110 DIP tall; bars grow from 5 to 86 DIP so a per-bar
        // value label (17 DIP) still fits above the tallest bar.
        const double barMin = 5.0, barMax = 86.0;
        var barWidth = isMonth ? 8.0 : 36.0;
        var columnWidth = isMonth ? 13.0 : 46.0;
        var bars = new List<HistoryBar>(days.Count);

        foreach (var (date, _) in days)
        {
            var record = byDate[date];
            var isToday = date == live.Date;
            var dayLabel = isToday
                ? (isMonth ? date.Day.ToString() : L10n.T("Hist.Today"))
                : isMonth ? (date.Day % 5 == 0 ? date.Day.ToString() : "") : date.ToString("ddd", L10n.Culture);
            bars.Add(new HistoryBar(
                dayLabel,
                FormatMinutesCompact(record.ActiveMinutes),
                L10n.T("Hist.Tooltip", date, record.ActiveMinutes, record.SitBreaks, record.LongestSessionMinutes),
                barMin + (barMax - barMin) * record.ActiveMinutes / maxMinutes,
                barWidth,
                columnWidth,
                !isMonth,
                isToday));
        }

        _historyBars = bars;
        HistoryTotalText = totalMinutes >= 60
            ? L10n.T("Hist.TotalLong", totalMinutes / 60, totalMinutes % 60)
            : L10n.T("Hist.TotalShort", totalMinutes);

        var activeDays = byDate.Values.Where(record => record.ActiveMinutes > 0).ToList();
        ShowAvgLine = activeDays.Count > 0;
        if (ShowAvgLine)
        {
            var average = activeDays.Average(record => (double)record.ActiveMinutes);
            AvgLineTranslate = -(barMin + (barMax - barMin) * average / maxMinutes);
            AvgLineText = L10n.T("Hist.Avg", FormatMinutesCompact((int)Math.Round(average)));
        }
    }

    // --- Work-pattern insights --------------------------------------------

    public string CurrentSessionText => FormatDuration(_scheduler.CurrentSessionActiveTime);

    public string TodayLongestSessionText => FormatDuration(_scheduler.Stats.LongestSession);

    public string RecentLongestSessionText { get; private set; } = "0m";

    public string RecentAverageText { get; private set; } = "0m";

    public string BusiestDayText { get; private set; } = L10n.T("Insight.None");

    public string SedentaryInsightText { get; private set; } = L10n.T("Insight.NoData");

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
            ? L10n.T("Insight.None")
            : $"{busiest.Date.ToString("MM-dd", L10n.Culture)} · {FormatMinutes(busiest.Record.ActiveMinutes)}";

        if (longest <= 0)
        {
            SedentaryInsightText = L10n.T("Insight.NoData");
        }
        else if (longest >= _config.SitReminderMinutes * 2)
        {
            SedentaryInsightText = L10n.T("Insight.Long2", FormatMinutes(longest));
        }
        else if (longest >= _config.SitReminderMinutes)
        {
            SedentaryInsightText = L10n.T("Insight.Long1", FormatMinutes(longest));
        }
        else
        {
            SedentaryInsightText = L10n.T("Insight.Long0", FormatMinutes(longest));
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
        OnPropertyChanged(nameof(ShowAvgLine));
        OnPropertyChanged(nameof(AvgLineTranslate));
        OnPropertyChanged(nameof(AvgLineText));
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
            ? L10n.T("Set.StatusSaved", DateTime.Now)
            : L10n.T("Set.StatusSaveFailed");
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
