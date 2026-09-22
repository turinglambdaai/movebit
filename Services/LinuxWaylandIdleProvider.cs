using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MoveBit.Services;

/// <summary>
/// Wayland idle detection without compositor-specific native libraries.
///
/// GNOME/Mutter exposes the same precise idle counter it uses internally through
/// org.gnome.Mutter.IdleMonitor. Some other desktops expose the freedesktop
/// ScreenSaver GetSessionIdleTime method. We query those session-bus APIs through
/// busctl/gdbus, cache the first backend that works, and fail closed (null) when the
/// compositor exposes no trustworthy counter. Returning null deliberately preserves
/// MoveBit's natural-time fallback instead of inventing an idle duration.
/// </summary>
public sealed class LinuxWaylandIdleProvider : IIdleProvider
{
    private enum Backend
    {
        Unknown,
        MutterBusctl,
        MutterGdbus,
        ScreenSaverBusctl,
        ScreenSaverGdbus,
        Unsupported,
    }

    private static readonly Regex NumberPattern = new(@"(?<![A-Za-z0-9])\d+(?![A-Za-z0-9])", RegexOptions.Compiled);
    private Backend _backend;

    public TimeSpan? GetIdleTime()
    {
        if (!OperatingSystem.IsLinux()
            || !string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_backend != Backend.Unknown && _backend != Backend.Unsupported)
        {
            if (TryRead(_backend, out var cached))
            {
                return cached;
            }

            // Session services can restart after shell/compositor upgrades. Re-probe once
            // instead of permanently losing idle detection for the rest of this process.
            _backend = Backend.Unknown;
        }

        foreach (var candidate in new[]
                 {
                     Backend.MutterBusctl,
                     Backend.MutterGdbus,
                     Backend.ScreenSaverBusctl,
                     Backend.ScreenSaverGdbus,
                 })
        {
            if (TryRead(candidate, out var idle))
            {
                _backend = candidate;
                return idle;
            }
        }

        _backend = Backend.Unsupported;
        return null;
    }

    private static bool TryRead(Backend backend, out TimeSpan idle)
    {
        idle = default;
        string? output;
        var scale = 1.0;

        switch (backend)
        {
            case Backend.MutterBusctl:
                output = Run("busctl", "--user", "call",
                    "org.gnome.Mutter.IdleMonitor",
                    "/org/gnome/Mutter/IdleMonitor/Core",
                    "org.gnome.Mutter.IdleMonitor",
                    "GetIdletime");
                break;

            case Backend.MutterGdbus:
                output = Run("gdbus", "call", "--session",
                    "--dest", "org.gnome.Mutter.IdleMonitor",
                    "--object-path", "/org/gnome/Mutter/IdleMonitor/Core",
                    "--method", "org.gnome.Mutter.IdleMonitor.GetIdletime");
                break;

            case Backend.ScreenSaverBusctl:
                output = Run("busctl", "--user", "call",
                    "org.freedesktop.ScreenSaver",
                    "/ScreenSaver",
                    "org.freedesktop.ScreenSaver",
                    "GetSessionIdleTime");
                scale = 1000.0; // ScreenSaver API reports seconds.
                break;

            case Backend.ScreenSaverGdbus:
                output = Run("gdbus", "call", "--session",
                    "--dest", "org.freedesktop.ScreenSaver",
                    "--object-path", "/ScreenSaver",
                    "--method", "org.freedesktop.ScreenSaver.GetSessionIdleTime");
                scale = 1000.0;
                break;

            default:
                return false;
        }

        if (string.IsNullOrWhiteSpace(output) || !TryParseFirstUnsigned(output, out var raw))
        {
            return false;
        }

        var milliseconds = raw * scale;
        if (double.IsInfinity(milliseconds) || milliseconds < 0)
        {
            return false;
        }

        idle = TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }

    /// Visible for deterministic unit tests of D-Bus command output parsing.
    public static bool TryParseFirstUnsigned(string output, out ulong value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var match = NumberPattern.Match(output);
        return match.Success
            && ulong.TryParse(match.Value, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static string? Run(string executable, params string[] arguments)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (!process.Start())
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(750))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It exited between the timeout and Kill().
                }

                return null;
            }

            _ = stderr.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? stdout.GetAwaiter().GetResult().Trim() : null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
