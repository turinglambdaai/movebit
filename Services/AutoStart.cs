using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace MoveBit.Services;

/// <summary>
/// Login autostart for a health tool whose whole value is "always running".
/// Windows: HKCU Run key (no admin needed). macOS: LaunchAgent. Linux: XDG autostart.
/// </summary>
public static class AutoStart
{
    private const string Name = "MoveBit";
    private const string WindowsRunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        try
        {
            return OperatingSystem.IsWindows() ? WindowsIsEnabled() : UnixIsEnabled();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Debug.WriteLine($"autostart state check failed: {ex.Message}");
            return false;
        }
    }

    public static void Enable() => SetEnabled(true);

    public static void Disable() => SetEnabled(false);

    private static void SetEnabled(bool enable)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                WindowsSet(enable);
            }
            else
            {
                UnixSet(enable);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Autostart is best-effort; the app itself keeps running either way.
            Debug.WriteLine($"autostart toggle failed: {ex.Message}");
        }
    }

    private static string ExePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MoveBit.dll");

    // --- Windows -----------------------------------------------------------

    private static bool WindowsIsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(WindowsRunPath, writable: false);
        return key?.GetValue(Name) is not null;
    }

    private static void WindowsSet(bool enable)
    {
        using var key = enable
            ? Registry.CurrentUser.CreateSubKey(WindowsRunPath, writable: true)
            : Registry.CurrentUser.OpenSubKey(WindowsRunPath, writable: true);

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
        if (OperatingSystem.IsMacOS())
        {
            if (enable)
            {
                var exe = SecurityElement.Escape(ExePath) ?? string.Empty;
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
                    Exec={QuoteDesktopExec(ExePath)}
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

    private static string QuoteDesktopExec(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal) + "\"";
}
