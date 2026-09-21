namespace MoveBit.Models;

/// User-tunable reminder settings, persisted as JSON under the user config directory.
public sealed class ReminderConfig
{
    /// Minutes of active computer use before a sit reminder ("stand up, stretch, bathroom break").
    public int SitReminderMinutes { get; set; } = 45;

    /// Minutes of active computer use before a water reminder.
    public int WaterReminderMinutes { get; set; } = 30;

    /// Idle duration that counts as "user stepped away"; all cycles reset on return.
    public int AwayResetMinutes { get; set; } = 5;

    /// When true, sit reminders take over all screens with a full-screen break lock
    /// instead of a passive toast. Water reminders always stay as toasts.
    public bool ForceBreakEnabled { get; set; } = true;

    /// How long the forced break lock lasts.
    public int BreakDurationMinutes { get; set; } = 5;

    /// Seconds after the break starts before the "skip" button appears (0 = immediately).
    public int SkipAfterSeconds { get; set; } = 20;

    /// Micro breaks: a light "stand up, look far away" nudge on its own faster cycle.
    /// Screen-center card, no lock, no sound — the evidence-friendly layer between long breaks.
    public bool MicroBreakEnabled { get; set; } = true;

    public int MicroBreakIntervalMinutes { get; set; } = 30;

    public int MicroBreakDurationSeconds { get; set; } = 20;

    /// Play a system sound when a reminder pops up.
    public bool SoundEnabled { get; set; } = true;

    /// Check GitHub Releases quietly after startup and every few hours. Updates are never
    /// installed silently; the user explicitly chooses "更新并重启" after a version is found.
    public bool AutoCheckUpdates { get; set; } = true;

    /// False until the user has seen the first-run welcome card (config absent = first run).
    public bool WelcomeShown { get; set; }

    public ReminderConfig Clone() => new()
    {
        SitReminderMinutes = SitReminderMinutes,
        WaterReminderMinutes = WaterReminderMinutes,
        AwayResetMinutes = AwayResetMinutes,
        ForceBreakEnabled = ForceBreakEnabled,
        BreakDurationMinutes = BreakDurationMinutes,
        SkipAfterSeconds = SkipAfterSeconds,
        MicroBreakEnabled = MicroBreakEnabled,
        MicroBreakIntervalMinutes = MicroBreakIntervalMinutes,
        MicroBreakDurationSeconds = MicroBreakDurationSeconds,
        SoundEnabled = SoundEnabled,
        AutoCheckUpdates = AutoCheckUpdates,
        WelcomeShown = WelcomeShown,
    };
}
