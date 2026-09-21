using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MoveBit;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// True while the app is running: closing hides to tray. Set false only on real shutdown.
    public bool HideOnClose { get; set; } = true;

    private void OnTestNotification(object? sender, RoutedEventArgs e)
    {
        (App.Current as App)?.TestNotification();
    }
}
