using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MoveBit;

public partial class MainWindow : Window
{
    private const double DefaultWidth = 520;
    private const double DefaultHeight = 900;
    private const double MinimumWidth = 460;
    private const double MinimumHeight = 560;
    private const double MaximumComfortableWidth = 720;
    private const double ScreenMargin = 24;

    public MainWindow()
    {
        InitializeComponent();

        // Keep MoveBit compact by default, but let users make better use of larger displays.
        // The XAML values remain a safe design-time fallback; runtime sizing is authoritative.
        CanResize = true;
        Width = DefaultWidth;
        Height = DefaultHeight;
        MinWidth = MinimumWidth;
        MinHeight = MinimumHeight;
        MaxWidth = MaximumComfortableWidth;

        // Re-evaluate constraints when the window moves between displays or DPI changes.
        PositionChanged += (_, _) => FitToCurrentScreen();
        ScalingChanged += (_, _) => FitToCurrentScreen();
    }

    /// True while the app is running: closing hides to tray. Set false only on real shutdown.
    public bool HideOnClose { get; set; } = true;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitToCurrentScreen();
    }

    private void FitToCurrentScreen()
    {
        var screen = Screens.ScreenFromWindow(this);
        var scaling = Math.Max(screen.Scaling, 0.1);
        var workingArea = screen.WorkingArea;

        // WorkingArea is reported in physical pixels while Window dimensions use DIPs.
        // Leave breathing room around the native window chrome and taskbar/dock.
        var availableWidth = Math.Max(MinimumWidth, workingArea.Width / scaling - ScreenMargin * 2);
        var availableHeight = Math.Max(MinimumHeight, workingArea.Height / scaling - ScreenMargin * 2);

        MaxWidth = Math.Min(MaximumComfortableWidth, availableWidth);
        MaxHeight = availableHeight;

        if (Width > MaxWidth)
        {
            Width = MaxWidth;
        }

        if (Height > MaxHeight)
        {
            Height = MaxHeight;
        }
    }

    private async void OnUpdateClicked(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            await app.CheckOrInstallUpdateFromUiAsync();
        }
    }
}
