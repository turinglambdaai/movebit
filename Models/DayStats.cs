namespace MoveBit.Models;

/// Per-day activity counters shown in the main window and tray tooltip.
public sealed class DayStats
{
    public DateOnly Date { get; set; }

    /// Total active (non-idle) computer time today.
    public TimeSpan ActiveTime { get; set; }

    public int SitReminders { get; set; }

    public int WaterReminders { get; set; }
}
