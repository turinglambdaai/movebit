using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MoveBit.Services;

namespace MoveBit;

/// <summary>
/// Full-screen topmost break lock. One instance per connected screen. Every overlay
/// renders the same countdown state supplied by <see cref="App"/> so multiple monitors
/// stay visually identical. Cannot be closed by the user — only the owning code closes
/// it when the break finishes or is skipped.
/// </summary>
public partial class BreakOverlayWindow : Window
{
    private bool _allowClose;

    /// Device-pixel bounds of the screen this overlay should cover.
    public PixelRect TargetBounds { get; init; }

    /// DPI scaling of the target screen, used to convert bounds to DIP.
    public double TargetScaling { get; init; }

    /// Retained for screen ownership/diagnostics. All screens now render the full break UI.
    public bool IsPrimary { get; init; }

    public event EventHandler? SkipRequested;

    public BreakOverlayWindow()
    {
        InitializeComponent();
    }

    public void UpdateCountdown(TimeSpan remaining, TimeSpan total, bool skipAvailable)
    {
        var displaySeconds = BreakCountdown.DisplaySeconds(remaining);
        CountdownText.Text = $"{displaySeconds / 60:0}:{displaySeconds % 60:00}";
        SkipButton.IsVisible = skipAvailable;

        // A real Arc avoids dash-pattern geometry drift. The numeric countdown and
        // the sweep angle are both derived from the exact same remaining TimeSpan.
        ProgressRing.SweepAngle = 360 * BreakCountdown.ProgressFraction(remaining, total);
    }

    /// <summary>Rotate the break tip text (driven by App every ~25 s during the break).</summary>
    public void SetHint(string hint)
    {
        BreakHint.Text = hint;
    }

    /// <summary>
    /// Droplet goodbye: title flips, icon floats away, then the caller closes the window.
    /// Every monitor runs the same goodbye animation so no display appears to finish early.
    /// </summary>
    public void PlayGoodbye(Action onDone)
    {
        BreakTitle.Text = "休息完成";
        CountdownText.Text = "💪";
        CountdownText.FontSize = 56;
        RingSub.IsVisible = false;
        BreakHint.Text = "回去工作吧，我随叫随到";
        ProgressRing.Opacity = 0.35;

        // Float the droplet up and out (~0.6 s), then hand control back on the UI thread.
        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(42) };
        timer.Tick += (_, _) =>
        {
            step++;
            DropIcon.Opacity = Math.Max(0, 1.0 - (double)step / 14);
            DropIcon.Margin = new Thickness(0, -3 * step, 0, 0);
            if (step >= 14)
            {
                timer.Stop();
                onDone();
            }
        };
        timer.Start();
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Cover the full screen bounds, taskbar included.
        Position = TargetBounds.Position;
        Width = TargetBounds.Width / TargetScaling;
        Height = TargetBounds.Height / TargetScaling;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (!_allowClose)
        {
            e.Cancel = true; // Alt+F4 must not break out of the break
        }
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        SkipRequested?.Invoke(this, EventArgs.Empty);
    }
}
