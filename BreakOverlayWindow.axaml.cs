using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MoveBit;

/// <summary>
/// Full-screen topmost break lock. One instance per connected screen; the countdown
/// itself is driven by <see cref="App"/> so all screens tick in sync. Cannot be closed
/// by the user — only the owning code closes it (break finished or skipped).
/// </summary>
public partial class BreakOverlayWindow : Window
{
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

    public void UpdateCountdown(TimeSpan remaining, bool skipAvailable)
    {
        CountdownText.Text = $"{(int)remaining.TotalMinutes:0}:{remaining.Seconds:00}";
        CountdownText.IsVisible = IsPrimary;
        BreakTitle.IsVisible = IsPrimary;
        BreakHint.IsVisible = IsPrimary;
        CycleText.IsVisible = IsPrimary;
        SkipButton.IsVisible = IsPrimary && skipAvailable;
    }

    /// Rotate the break tip text (driven by App every ~25 s during the break).
    public void SetHint(string hint)
    {
        BreakHint.Text = hint;
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
