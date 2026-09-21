using System;
using System.Threading;
using Avalonia;
using MoveBit.Services;

namespace MoveBit;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // One instance only: two schedulers would double every reminder and clobber
        // each other's history writes. A second launch pokes the first to show its
        // window, then exits — so re-launching feels like "bring it up", not nothing.
        using var mutex = new Mutex(initiallyOwned: true, SingleInstanceGuard.MutexName, out var firstInstance);
        if (!firstInstance)
        {
            SingleInstanceGuard.TryActivateRunningInstance();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
