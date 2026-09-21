using Avalonia;
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

    private async void OnUpdateClicked(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            await app.CheckOrInstallUpdateFromUiAsync();
        }
    }
}
