using System;
using System.Runtime.InteropServices;

namespace MoveBit.Services;

/// <summary>Best-effort system sound on reminder. Windows uses MessageBeep; other platforms are silent.</summary>
internal static class ReminderSound
{
    private const uint MbIconAsterisk = 0x40;

    public static void Play()
    {
        if (OperatingSystem.IsWindows())
        {
            MessageBeep(MbIconAsterisk);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint type);
}
