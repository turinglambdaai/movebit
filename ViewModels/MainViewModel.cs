using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MoveBit.Models;
using MoveBit.Services;

namespace MoveBit.ViewModels;

/// Bindable wrapper around the config + scheduler stats for the main window.
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ReminderConfig _config;
    private readonly ReminderScheduler _scheduler;
    private readonly ConfigStore _store;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel(ReminderConfig config, ReminderScheduler scheduler, ConfigStore store)
    {
        _config = config;
        _scheduler = scheduler;
        _store = store;
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

    // --- Today stats (refreshed on every scheduler tick) ---

    public string ActiveTimeText => FormatDuration(_scheduler.Stats.ActiveTime);

    public string SitCycleText =>
        IsPaused ? "—" : $"{(int)_scheduler.SitCycleElapsed.TotalMinutes} / {_config.SitReminderMinutes} 分钟";

    public int SitReminders => _scheduler.Stats.SitReminders;

    public int WaterReminders => _scheduler.Stats.WaterReminders;

    public bool IsPaused => _scheduler.IsPaused;

    public string PauseText => _scheduler.PausedUntil is { } until
        ? $"提醒已暂停，至 {until.LocalDateTime:HH:mm}"
        : "提醒运行中";

    public string ConfigPathText => _store.ToString();

    public void RefreshStats()
    {
        OnPropertyChanged(nameof(ActiveTimeText));
        OnPropertyChanged(nameof(SitCycleText));
        OnPropertyChanged(nameof(SitReminders));
        OnPropertyChanged(nameof(WaterReminders));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseText));
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
