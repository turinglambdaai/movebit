using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MoveBit.Services;

/// <summary>
/// Login autostart for a health tool whose whole value is "always running".
/// Windows: HKCU Run key (no admin needed). macOS: LaunchAgent. Linux: XDG autostart.
/// </summary>
public static class AutoStart
{
    private const string Name = "MoveBit";

    public static bool IsEnabled() => OperatingSystem.IsWindows() ? WindowsIsEnabled() : UnixIsEnabled();

    public static void Enable()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsSet(true);
        }
        else
        {
            UnixSet(true);
        }
    }

    public static void Disable()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsSet(false);
        }
        else
        {
            UnixSet(false);
        }
    }

    private static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MoveBit.dll");

    // --- Windows -----------------------------------------------------------

    private static RegistryKey? OpenRunKey(bool writable)
    {
        return Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable);
    }

    private static bool WindowsIsEnabled()
    {
        using var key = OpenRunKey(writable: false);
        return key?.GetValue(Name) is not null;
    }

    private static void WindowsSet(bool enable)
    {
        using var key = OpenRunKey(writable: true);
        if (key is null)
        {
            return;
        }

        if (enable)
        {
            // Quoted path: Program Files style spaces must survive command-line parsing.
            key.SetValue(Name, $"\"{ExePath}\"");
        }
        else
        {
            key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }

    // --- macOS / Linux -----------------------------------------------------

    private static string PlistPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.turinglambdaai.movebit.plist");

    private static string DesktopPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "autostart", "movebit.desktop");

    private static bool UnixIsEnabled() =>
        OperatingSystem.IsMacOS() ? File.Exists(PlistPath) : File.Exists(DesktopPath);

    private static void UnixSet(bool enable)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                if (enable)
                {
                    var exe = ExePath;
                    var plist = $"""
                        <?xml version="1.0" encoding="UTF-8"?>
                        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                        <plist version="1.0">
                        <dict>
                          <key>Label</key><string>com.turinglambdaai.movebit</string>
                          <key>ProgramArguments</key>
                          <array><string>{exe}</string></array>
                          <key>RunAtLoad</key><true/>
                        </dict>
                        </plist>
                        """;
                    Directory.CreateDirectory(Path.GetDirectoryName(PlistPath)!);
                    File.WriteAllText(PlistPath, plist);
                }
                else if (File.Exists(PlistPath))
                {
                    File.Delete(PlistPath);
                }
            }
            else
            {
                if (enable)
                {
                    var entry = $"""
                        [Desktop Entry]
                        Type=Application
                        Name=MoveBit
                        Exec={ExePath}
                        X-GNOME-Autostart-enabled=true
                        """;
                    Directory.CreateDirectory(Path.GetDirectoryName(DesktopPath)!);
                    File.WriteAllText(DesktopPath, entry);
                }
                else if (File.Exists(DesktopPath))
                {
                    File.Delete(DesktopPath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Autostart is best-effort; the app itself keeps running either way.
            Debug.WriteLine($"autostart toggle failed: {ex.Message}");
        }
    }
}
