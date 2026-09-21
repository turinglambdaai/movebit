using System;
using System.Runtime.InteropServices;

namespace MoveBit.Services;

/// <summary>
/// Linux/X11 idle detection through the XScreenSaver extension. Native Wayland
/// sessions intentionally fall back to unknown idle time until a compositor-neutral
/// idle protocol is available to the app.
/// </summary>
public sealed class LinuxX11IdleProvider : IIdleProvider
{
    public TimeSpan? GetIdleTime()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return null;
        }

        IntPtr display = IntPtr.Zero;
        IntPtr infoPointer = IntPtr.Zero;

        try
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero)
            {
                return null;
            }

            infoPointer = XScreenSaverAllocInfo();
            if (infoPointer == IntPtr.Zero)
            {
                return null;
            }

            var screen = XDefaultScreen(display);
            var root = XRootWindow(display, screen);
            if (XScreenSaverQueryInfo(display, root, infoPointer) == 0)
            {
                return null;
            }

            var info = Marshal.PtrToStructure<XScreenSaverInfo>(infoPointer);
            return TimeSpan.FromMilliseconds((double)info.Idle);
        }
        catch (Exception ex) when (ex is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or SEHException)
        {
            return null;
        }
        finally
        {
            if (infoPointer != IntPtr.Zero)
            {
                _ = XFree(infoPointer);
            }

            if (display != IntPtr.Zero)
            {
                _ = XCloseDisplay(display);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XScreenSaverInfo
    {
        public nuint Window;
        public int State;
        public int Kind;
        public nuint TilOrSince;
        public nuint Idle;
        public nuint EventMask;
    }

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern nuint XRootWindow(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XFree(IntPtr data);

    [DllImport("libXss.so.1")]
    private static extern IntPtr XScreenSaverAllocInfo();

    [DllImport("libXss.so.1")]
    private static extern int XScreenSaverQueryInfo(IntPtr display, nuint drawable, IntPtr saverInfo);
}
