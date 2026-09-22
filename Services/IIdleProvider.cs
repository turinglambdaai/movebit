using System;

namespace MoveBit.Services;

/// <summary>
/// Reports how long the user has been idle (no keyboard/mouse input anywhere in the session).
/// Returns null when the current platform/session cannot provide trustworthy idle data.
/// </summary>
public interface IIdleProvider
{
    TimeSpan? GetIdleTime();
}

/// Fallback for unsupported platforms/session types.
public sealed class NullIdleProvider : IIdleProvider
{
    public TimeSpan? GetIdleTime() => null;
}

public static class IdleProviderFactory
{
    public static IIdleProvider Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Win32IdleProvider();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsIdleProvider();
        }

        if (OperatingSystem.IsLinux())
        {
            return string.Equals(
                Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
                "wayland",
                StringComparison.OrdinalIgnoreCase)
                ? new LinuxWaylandIdleProvider()
                : new LinuxX11IdleProvider();
        }

        return new NullIdleProvider();
    }
}
