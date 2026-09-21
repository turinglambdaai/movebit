using System;
using System.Runtime.InteropServices;

namespace MoveBit.Services;

/// <summary>
/// Windows idle detection via GetLastInputInfo — session-wide (any app's input counts),
/// which is exactly what a sedentary-work monitor needs.
/// </summary>
public sealed class Win32IdleProvider : IIdleProvider
{
    public TimeSpan? GetIdleTime()
    {
        var info = default(LASTINPUTINFO);
        info.cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>();
        if (!GetLastInputInfo(ref info))
        {
            return null;
        }

        // Both operands are GetTickCount-style uint tick counts; unchecked subtraction
        // handles the ~49.7-day wraparound correctly.
        var idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
        return TimeSpan.FromMilliseconds(idleMs);
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }
}
