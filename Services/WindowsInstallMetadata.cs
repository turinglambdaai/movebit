using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MoveBit.Services;

/// <summary>
/// Keeps Windows Apps & Features metadata aligned after MoveBit's own in-app updater
/// replaces the installed executable. Portable copies simply do not have this registry key.
/// </summary>
internal static class WindowsInstallMetadata
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{E4B9E01D-65D0-4C25-BCF1-3CC22B8C9E02}_is1";

    [ModuleInitializer]
    internal static void SyncInstalledDisplayVersion()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey, writable: true);
            if (key is null)
            {
                return;
            }

            var version = typeof(WindowsInstallMetadata).Assembly.GetName().Version;
            if (version is null)
            {
                return;
            }

            var displayVersion = $"{Math.Max(version.Major, 0)}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
            key.SetValue("DisplayVersion", displayVersion, RegistryValueKind.String);
        }
        catch (Exception)
        {
            // Installed-app metadata is cosmetic. It must never prevent MoveBit from starting.
        }
    }
}
