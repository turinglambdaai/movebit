using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MoveBit.Services;

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    UnsupportedPlatform,
    Failed,
}

public enum UpdateInstallStatus
{
    Restarting,
    NoUpdate,
    UnsupportedPlatform,
    PermissionDenied,
    Failed,
}

public enum UpdateStage
{
    Checking,
    Downloading,
    Verifying,
    Preparing,
    Restarting,
}

public sealed record UpdateProgress(UpdateStage Stage, int? Percentage = null);

public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string ReleaseNotes,
    Uri ReleasePage,
    Uri PackageUri,
    Uri ChecksumUri,
    string PackageName);

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateInfo? Update = null,
    string? ErrorMessage = null);

public sealed record UpdateInstallResult(
    UpdateInstallStatus Status,
    string? ErrorMessage = null);

/// <summary>
/// Cross-platform updater for MoveBit's existing GitHub Release ZIPs. It never silently
/// installs an update: discovery may be automatic, but applying requires an explicit user
/// action. The package is verified against its published SHA-256 sidecar before extraction.
/// </summary>
public static class UpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/turinglambdaai/movebit/releases/latest";
    private const string ProductUrl = "https://github.com/turinglambdaai/movebit";
    private const string UpdatesFolderName = "movebit-updates";
    private static readonly HttpClient Client = CreateHttpClient();

    public static Version CurrentVersion => NormalizeVersion(
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0));

    public static string CurrentVersionText =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static string? CurrentPlatformAssetName => GetCurrentPlatformAssetName();

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var packageName = GetCurrentPlatformAssetName();
        if (packageName is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UnsupportedPlatform);
        }

        try
        {
            TryCleanupStaleUpdates();

            using var response = await Client.GetAsync(LatestReleaseApi, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, cancellationToken: cancellationToken);
            if (release is null || !TryParseVersionTag(release.TagName, out var remoteVersion))
            {
                return new UpdateCheckResult(UpdateCheckStatus.Failed, ErrorMessage: "GitHub 返回了无效的版本信息。");
            }

            if (remoteVersion.CompareTo(CurrentVersion) <= 0)
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
            }

            var package = release.Assets.Find(a => string.Equals(a.Name, packageName, StringComparison.Ordinal));
            var checksum = release.Assets.Find(a => string.Equals(a.Name, packageName + ".sha256", StringComparison.Ordinal));
            if (package?.BrowserDownloadUrl is null || checksum?.BrowserDownloadUrl is null)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Failed,
                    ErrorMessage: $"最新版本缺少 {packageName} 或对应的 SHA-256 校验文件。");
            }

            var releasePage = Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var parsedPage)
                ? parsedPage
                : new Uri(ProductUrl + "/releases/latest");

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                new UpdateInfo(
                    remoteVersion,
                    release.TagName,
                    release.Body ?? string.Empty,
                    releasePage,
                    new Uri(package.BrowserDownloadUrl),
                    new Uri(checksum.BrowserDownloadUrl),
                    packageName));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or UriFormatException)
        {
            TryLogFailure("check", ex);
            return new UpdateCheckResult(UpdateCheckStatus.Failed, ErrorMessage: "无法连接更新服务器，请稍后重试。");
        }
    }

    public static async Task<UpdateInstallResult> DownloadAndApplyAsync(
        UpdateInfo update,
        IProgress<UpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var packageName = GetCurrentPlatformAssetName();
        if (packageName is null)
        {
            return new UpdateInstallResult(UpdateInstallStatus.UnsupportedPlatform);
        }

        if (!string.Equals(packageName, update.PackageName, StringComparison.Ordinal))
        {
            return new UpdateInstallResult(UpdateInstallStatus.Failed, "更新包与当前平台不匹配。");
        }

        if (update.Version.CompareTo(CurrentVersion) <= 0)
        {
            return new UpdateInstallResult(UpdateInstallStatus.NoUpdate);
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return new UpdateInstallResult(UpdateInstallStatus.Failed, "无法确定当前程序路径。");
        }

        var installDirectory = Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(installDirectory) || !Directory.Exists(installDirectory))
        {
            return new UpdateInstallResult(UpdateInstallStatus.Failed, "无法确定安装目录。");
        }

        if (!CanWriteDirectory(installDirectory))
        {
            return new UpdateInstallResult(
                UpdateInstallStatus.PermissionDenied,
                "MoveBit 所在目录不可写。请把便携版放到你的用户目录，或使用有写权限的位置后再更新。");
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            UpdatesFolderName,
            $"{update.Version.Major}.{update.Version.Minor}.{update.Version.Build}-{Guid.NewGuid():N}");
        var packagePath = Path.Combine(root, update.PackageName);
        var checksumPath = packagePath + ".sha256";
        var stagingDirectory = Path.Combine(root, "staging");

        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(stagingDirectory);

            progress?.Report(new UpdateProgress(UpdateStage.Downloading, 0));
            await DownloadFileAsync(update.PackageUri, packagePath, progress, cancellationToken);
            await DownloadFileAsync(update.ChecksumUri, checksumPath, null, cancellationToken);

            progress?.Report(new UpdateProgress(UpdateStage.Verifying));
            var checksumText = await File.ReadAllTextAsync(checksumPath, cancellationToken);
            if (!ChecksumMatches(packagePath, checksumText))
            {
                throw new InvalidDataException("下载包的 SHA-256 校验失败。");
            }

            progress?.Report(new UpdateProgress(UpdateStage.Preparing));
            ExtractZipSafely(packagePath, stagingDirectory);

            var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "MoveBit.exe" : "MoveBit";
            if (!File.Exists(Path.Combine(stagingDirectory, executableName)))
            {
                throw new InvalidDataException($"更新包中缺少 {executableName}。");
            }

            var helperPath = WriteUpdaterHelper(root);
            progress?.Report(new UpdateProgress(UpdateStage.Restarting));
            StartUpdaterHelper(
                helperPath,
                Environment.ProcessId,
                stagingDirectory,
                installDirectory,
                executableName,
                root);

            return new UpdateInstallResult(UpdateInstallStatus.Restarting);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDeleteDirectory(root);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   or IOException
                                   or InvalidDataException
                                   or UnauthorizedAccessException
                                   or CryptographicException
                                   or Win32Exception)
        {
            TryLogFailure("install", ex);
            TryDeleteDirectory(root);
            return new UpdateInstallResult(UpdateInstallStatus.Failed, ex.Message);
        }
    }

    /// <summary>Pure helper used by tests and release validation.</summary>
    public static bool IsNewerRelease(string tagName, Version currentVersion)
        => TryParseVersionTag(tagName, out var candidate)
           && candidate.CompareTo(NormalizeVersion(currentVersion)) > 0;

    /// <summary>Verifies a sha256sum-style sidecar against a downloaded file.</summary>
    public static bool ChecksumMatches(string filePath, string checksumText)
    {
        if (string.IsNullOrWhiteSpace(checksumText))
        {
            return false;
        }

        var token = checksumText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (token is null || token.Length != 64)
        {
            return false;
        }

        try
        {
            var expected = Convert.FromHexString(token);
            using var stream = File.OpenRead(filePath);
            var actual = SHA256.HashData(stream);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            $"MoveBit/{CurrentVersionText} (+{ProductUrl})");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        return client;
    }

    private static string? GetCurrentPlatformAssetName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return "MoveBit-windows-x64.zip";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
            && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            return "MoveBit-macos-arm64.zip";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            return "MoveBit-linux-x64.zip";
        }

        return null;
    }

    private static bool TryParseVersionTag(string? tagName, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return false;
        }

        var trimmed = tagName.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        var suffixIndex = trimmed.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            trimmed = trimmed[..suffixIndex];
        }

        var parts = trimmed.Split('.');
        if (parts.Length is < 2 or > 4)
        {
            return false;
        }

        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
            {
                return false;
            }
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    private static Version NormalizeVersion(Version version)
        => new(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));

    private static async Task DownloadFileAsync(
        Uri uri,
        string destinationPath,
        IProgress<UpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            if (total is > 0 && progress is not null)
            {
                var percentage = (int)Math.Clamp(received * 100 / total.Value, 0, 100);
                progress.Report(new UpdateProgress(UpdateStage.Downloading, percentage));
            }
        }
    }

    private static void ExtractZipSafely(string zipPath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        var pathComparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, pathComparison))
            {
                throw new InvalidDataException("更新包包含不安全的文件路径。");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static bool CanWriteDirectory(string directory)
    {
        var probe = Path.Combine(directory, $".movebit-update-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(probe);
            return false;
        }
    }

    private static string WriteUpdaterHelper(string root)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var path = Path.Combine(root, "apply-update.ps1");
            File.WriteAllText(path, WindowsUpdaterScript);
            return path;
        }

        var shellPath = Path.Combine(root, "apply-update.sh");
        File.WriteAllText(shellPath, UnixUpdaterScript.Replace("\r\n", "\n", StringComparison.Ordinal));
        File.SetUnixFileMode(
            shellPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shellPath;
    }

    private static void StartUpdaterHelper(
        string helperPath,
        int processId,
        string source,
        string target,
        string executableName,
        string root)
    {
        var psi = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            psi.FileName = "powershell.exe";
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-NonInteractive");
            psi.ArgumentList.Add("-ExecutionPolicy");
            psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-File");
            psi.ArgumentList.Add(helperPath);
        }
        else
        {
            psi.FileName = "/bin/sh";
            psi.ArgumentList.Add(helperPath);
        }

        psi.ArgumentList.Add(processId.ToString());
        psi.ArgumentList.Add(source);
        psi.ArgumentList.Add(target);
        psi.ArgumentList.Add(executableName);
        psi.ArgumentList.Add(root);

        var started = Process.Start(psi);
        if (started is null)
        {
            throw new IOException("无法启动更新辅助进程。");
        }
    }

    private static void TryCleanupStaleUpdates()
    {
        try
        {
            var parent = Path.Combine(Path.GetTempPath(), UpdatesFolderName);
            if (!Directory.Exists(parent))
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-3);
            foreach (var directory in Directory.EnumerateDirectories(parent))
            {
                if (Directory.GetLastWriteTimeUtc(directory) < cutoff)
                {
                    TryDeleteDirectory(directory);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup is best-effort.
        }
    }

    private static void TryLogFailure(string phase, Exception ex)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "movebit");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "update.log"),
                $"[{DateTimeOffset.Now:O}] {phase}: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
        }
        catch
        {
            // Updating must never make the reminder app unusable.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort temp cleanup.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort probe cleanup.
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; init; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }
    }

    private const string WindowsUpdaterScript = """
param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Target,
    [Parameter(Mandatory=$true)][string]$ExecutableName,
    [Parameter(Mandatory=$true)][string]$Root
)

$ErrorActionPreference = 'Stop'
$Backup = Join-Path $Root 'backup'
$Created = New-Object System.Collections.Generic.List[string]

while (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
    Start-Sleep -Milliseconds 200
}

function Restore-Backup {
    foreach ($path in $Created) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }

    if (Test-Path -LiteralPath $Backup) {
        $backupRoot = (Resolve-Path -LiteralPath $Backup).Path.TrimEnd('\')
        Get-ChildItem -LiteralPath $Backup -Recurse -File -Force | ForEach-Object {
            $relative = $_.FullName.Substring($backupRoot.Length).TrimStart('\')
            $destination = Join-Path $Target $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        }
    }
}

try {
    New-Item -ItemType Directory -Path $Backup -Force | Out-Null
    $sourceRoot = (Resolve-Path -LiteralPath $Source).Path.TrimEnd('\')

    Get-ChildItem -LiteralPath $Source -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.Length).TrimStart('\')
        $destination = Join-Path $Target $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null

        if (Test-Path -LiteralPath $destination) {
            $backupPath = Join-Path $Backup $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $backupPath) -Force | Out-Null
            Copy-Item -LiteralPath $destination -Destination $backupPath -Force
        } else {
            $Created.Add($destination)
        }

        Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
    }

    Start-Process -FilePath (Join-Path $Target $ExecutableName)
} catch {
    Restore-Backup
    $oldExe = Join-Path $Target $ExecutableName
    if (Test-Path -LiteralPath $oldExe) {
        Start-Process -FilePath $oldExe
    }
    exit 1
}
""";

    private const string UnixUpdaterScript = """
#!/bin/sh
PROCESS_ID="$1"
SOURCE="$2"
TARGET="$3"
EXECUTABLE_NAME="$4"
ROOT="$5"
BACKUP="$ROOT/backup"
CREATED="$ROOT/created-files.txt"

while kill -0 "$PROCESS_ID" 2>/dev/null; do
    sleep 1
done

mkdir -p "$BACKUP"
: > "$CREATED"

restore_backup() {
    if [ -f "$CREATED" ]; then
        while IFS= read -r path; do
            [ -n "$path" ] && rm -f "$path"
        done < "$CREATED"
    fi

    if [ -d "$BACKUP" ]; then
        cd "$BACKUP" || return 1
        find . -type f -print | while IFS= read -r rel; do
            clean="${rel#./}"
            destination="$TARGET/$clean"
            mkdir -p "$(dirname "$destination")"
            cp -p "$BACKUP/$clean" "$destination"
        done
    fi
}

apply_update() {
    cd "$SOURCE" || return 1
    find . -type f -print | while IFS= read -r rel; do
        clean="${rel#./}"
        source_file="$SOURCE/$clean"
        destination="$TARGET/$clean"
        mkdir -p "$(dirname "$destination")" || exit 1

        if [ -f "$destination" ]; then
            backup_file="$BACKUP/$clean"
            mkdir -p "$(dirname "$backup_file")" || exit 1
            cp -p "$destination" "$backup_file" || exit 1
        else
            printf '%s\n' "$destination" >> "$CREATED"
        fi

        cp -p "$source_file" "$destination" || exit 1
    done
}

if apply_update; then
    chmod +x "$TARGET/$EXECUTABLE_NAME" 2>/dev/null || true
    nohup "$TARGET/$EXECUTABLE_NAME" >/dev/null 2>&1 &
    exit 0
else
    restore_backup
    if [ -f "$TARGET/$EXECUTABLE_NAME" ]; then
        chmod +x "$TARGET/$EXECUTABLE_NAME" 2>/dev/null || true
        nohup "$TARGET/$EXECUTABLE_NAME" >/dev/null 2>&1 &
    fi
    exit 1
fi
""";
}
