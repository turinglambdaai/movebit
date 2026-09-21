using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MoveBit;

/// <summary>
/// Full-screen topmost break lock. One instance per connected screen; the countdown
/// itself is driven by <see cref="App"/> so all screens tick in sync. Cannot be closed
/// by the user — only the owning code closes it (break finished or skipped).
/// </summary>
public partial class BreakOverlayWindow : Window
{
    // Circumference of the 270 DIP progress ring, measured on the stroke mid-line (260).
    private const double RingCircumference = Math.PI * 260;

    private bool _allowClose;

    /// Device-pixel bounds of the screen this overlay should cover.
    public PixelRect TargetBounds { get; init; }

    /// DPI scaling of the target screen, used to convert bounds to DIP.
    public double TargetScaling { get; init; }

    /// The primary-screen overlay renders the countdown; the others just dim.
    public bool IsPrimary { get; init; }

    public event EventHandler? SkipRequested;

    public BreakOverlayWindow()
    {
        InitializeComponent();
    }

    public void UpdateCountdown(TimeSpan remaining, TimeSpan total, bool skipAvailable)
    {
        CountdownText.Text = $"{(int)remaining.TotalMinutes:0}:{remaining.Seconds:00}";
        SkipButton.IsVisible = IsPrimary && skipAvailable;

        // Warm arc drains clockwise from 12 o'clock as the break passes.
        var totalSeconds = Math.Max(total.TotalSeconds, 1);
        var fraction = Math.Clamp(remaining.TotalSeconds / totalSeconds, 0, 1);
        var arc = RingCircumference * fraction;
        ProgressRing.StrokeDashArray = [arc, RingCircumference - arc];
        ProgressRing.StrokeDashOffset = RingCircumference / 4; // rotate start to 12 o'clock
    }

    /// <summary>Rotate the break tip text (driven by App every ~25 s during the break).</summary>
    public void SetHint(string hint)
    {
        BreakHint.Text = hint;
    }

    /// <summary>
    /// Droplet goodbye: title flips, icon floats away, then the caller closes the window.
    /// Secondary overlays (no visible content) skip straight to <paramref name="onDone"/>.
    /// </summary>
    public void PlayGoodbye(Action onDone)
    {
        if (!IsPrimary)
        {
            onDone();
            return;
        }

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

        // Secondary overlays only dim their screen; hide the countdown furniture for one
        // clean frame instead of flashing it until the first UpdateCountdown call.
        if (!IsPrimary)
        {
            DropIcon.IsVisible = false;
            BreakTitle.IsVisible = false;
            CountdownText.IsVisible = false;
            RingSub.IsVisible = false;
            BreakHint.IsVisible = false;
            CycleText.IsVisible = false;
        }
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
