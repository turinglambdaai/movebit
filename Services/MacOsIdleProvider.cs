using System;
using System.Runtime.InteropServices;

namespace MoveBit.Services;

/// <summary>
/// macOS session-wide idle detection through CoreGraphics. The HID system source
/// observes keyboard/mouse activity independently of which application is focused.
/// </summary>
public sealed class MacOsIdleProvider : IIdleProvider
{
    private const int HidSystemState = 1;
    private const uint AnyInputEvent = uint.MaxValue;

    public TimeSpan? GetIdleTime()
    {
        try
        {
            var seconds = CGEventSourceSecondsSinceLastEventType(HidSystemState, AnyInputEvent);
            if (!double.IsFinite(seconds) || seconds < 0)
            {
                return null;
            }

            return TimeSpan.FromSeconds(seconds);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);
}
