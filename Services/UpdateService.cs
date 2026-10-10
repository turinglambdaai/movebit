using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Math.EC.Rfc8032;

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
    Uri ReleasePage,
    Uri PackageUri,
    string Sha256,
    long Size,
    string PackageName);

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    UpdateInfo? Update = null,
    string? ErrorMessage = null);

public sealed record UpdateInstallResult(
    UpdateInstallStatus Status,
    string? ErrorMessage = null);

/// <summary>One platform entry of the signed update feed's inner manifest.</summary>
public sealed record SignedFeedArtifact(
    string Platform,
    string Architecture,
    Uri Url,
    string Sha256,
    long Size,
    string Installer);

/// <summary>The parsed inner manifest carried (and signed) by the feed wrapper.</summary>
public sealed record SignedFeedManifest(
    string ApplicationId,
    Version Version,
    string Channel,
    string MinimumVersion,
    IReadOnlyList<SignedFeedArtifact> Artifacts);

/// <summary>
/// Cross-platform updater for MoveBit's signed GitHub Release feed. It never
/// silently installs an update: discovery may be automatic, but applying requires
/// an explicit user action. The feed is a single Ed25519-signed
/// <c>update-manifest.json</c> (family wrapper: base64 payload + signature block);
/// the signature, the pinned key-id, and the artifact SHA-256/size are all verified
/// before anything is staged.
/// </summary>
/// <remarks>
/// Feed design: discovery fetches the moving
/// <c>releases/latest/download/update-manifest.json</c> (HttpClient follows the
/// 302 to the CDN — rivet#153), verifies the Ed25519 signature over the exact
/// payload bytes with the pinned public key (<see cref="PinnedFeedPublicKey"/>,
/// managed by <c>scripts/update-keys.sh</c>), then selects this platform's
/// portable archive from the signed artifact list. The archive's SHA-256 and
/// size are checked against the signed values — there are no out-of-band
/// checksums to trust.
/// </remarks>
public static class UpdateService
{
#if PRO
    // Pro builds must never see the public release channel: applying a public
    // update would replace the Pro binary with the MIT build. The Pro channel
    // is a license-gated feed on the product site (endpoint TODO with release);
    // until it exists Pro reports that no feed is configured.
    private static readonly string? SignedFeedUrl = null;
    private const string ProductUrl = "https://jrtx.site/movebit/pro";
#else
    private static readonly string? SignedFeedUrl =
        "https://github.com/turinglambdaai/movebit/releases/latest/download/update-manifest.json";
    private const string ProductUrl = "https://github.com/turinglambdaai/movebit";
#endif

    /// <summary>Ed25519 public key (raw 32 bytes, base64) that update manifests must
    /// be signed with. From the keypair managed by scripts/update-keys.sh; the
    /// private half lives only in the release signing secret and the release
    /// manager's backup. Rotating requires a release that pins the next key.</summary>
    public static readonly byte[] PinnedFeedPublicKey =
        Convert.FromBase64String("t4Z+rtAk2NtcgFBUX+r139X1c2EnVOaI/HxkN4vs914=");

    /// <summary>Manifests signed under any other key-id are rejected before any
    /// crypto runs. Must match RIVET_UPDATE_KEY_ID in the release pipeline.</summary>
    public const string PinnedFeedKeyId = "movebit-2026-10";

    /// <summary>The feed wrapper schema this verifier understands; unknown schemas fail closed.</summary>
    public const int FeedSchemaVersion = 1;

    /// <summary>A manifest claiming another application's identity is rejected —
    /// a stray or cross-app feed must never select artifacts here.</summary>
    public const string FeedApplicationId = "site.jrtx.movebit";

    /// <summary>Only the stable channel is consumed by this build.</summary>
    public const string FeedChannel = "stable";

    private const string UpdatesFolderName = "movebit-updates";
    private static readonly HttpClient Client = CreateHttpClient();

    public static Version CurrentVersion => NormalizeVersion(
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0));

    public static string CurrentVersionText =>
        $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

    public static string? CurrentPlatformAssetPattern => GetCurrentPlatformTarget()?.AssetPatternText;

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        var target = GetCurrentPlatformTarget();
        if (target is null)
        {
            return new UpdateCheckResult(UpdateCheckStatus.UnsupportedPlatform);
        }

        if (SignedFeedUrl is not { } feedUrl)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Failed,
                ErrorMessage: "Pro 更新通道尚未开放，请前往 jrtx.site 检查更新。");
        }

        try
        {
            TryCleanupStaleUpdates();

            using var response = await Client.GetAsync(feedUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var wrapperJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!TryReadSignedFeed(wrapperJson, PinnedFeedPublicKey, PinnedFeedKeyId, out var manifest)
                || manifest is null)
            {
                // Verification failure is deliberately indistinguishable from a
                // broken feed to a would-be attacker: the update is refused, never
                // downgraded to an unsigned path.
                return new UpdateCheckResult(
                    UpdateCheckStatus.Failed,
                    ErrorMessage: "更新清单签名验证失败，已拒绝本次更新。");
            }

            // Clients below the feed's minimum updatable version must not ride
            // this channel (same rule as rivet's select-update).
            if (TryParseVersionTag(manifest.MinimumVersion, out var minimum)
                && CurrentVersion.CompareTo(NormalizeVersion(minimum)) < 0)
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
            }

            if (manifest.Version.CompareTo(CurrentVersion) <= 0)
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);
            }

            var artifact = SelectArtifact(manifest, target.Os, target.Arch);
            if (artifact is null)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Failed,
                    ErrorMessage: $"最新版本缺少 {target.ArchiveDescription} 更新包。");
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                new UpdateInfo(
                    manifest.Version,
                    $"v{FormatVersion(manifest.Version)}",
                    new Uri($"{ProductUrl}/releases/tag/v{FormatVersion(manifest.Version)}"),
                    artifact.Url,
                    artifact.Sha256,
                    artifact.Size,
                    Path.GetFileName(artifact.Url.LocalPath)));
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
        var target = GetCurrentPlatformTarget();
        if (target is null)
        {
            return new UpdateInstallResult(UpdateInstallStatus.UnsupportedPlatform);
        }

        // The update record must describe the archive this platform consumes.
        if (!target.AssetPattern().IsMatch(update.PackageName))
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
        var stagingDirectory = Path.Combine(root, "staging");

        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(stagingDirectory);

            progress?.Report(new UpdateProgress(UpdateStage.Downloading, 0));
            await DownloadFileAsync(update.PackageUri, packagePath, progress, cancellationToken);

            progress?.Report(new UpdateProgress(UpdateStage.Verifying));
            // The only accepted checksum is the one inside the signed manifest;
            // there is no sidecar to fetch, so a swapped mirror file cannot pass.
            if (!Sha256Matches(packagePath, update.Sha256, update.Size))
            {
                throw new InvalidDataException("下载包的 SHA-256 校验失败（与签名清单不符）。");
            }

            progress?.Report(new UpdateProgress(UpdateStage.Preparing));
            if (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ExtractZipSafely(packagePath, stagingDirectory);
            }
            else
            {
                ExtractTarGzSafely(packagePath, stagingDirectory);
            }

            var executableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "MoveBit.exe" : "MoveBit";
            if (!StagingContainsExecutable(stagingDirectory, executableName))
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

    /// <summary>Verifies a downloaded file against the hex SHA-256 recorded in the
    /// signed feed manifest (case-insensitive), and, when <paramref name="expectedSize"/>
    /// is non-negative, the exact byte size.</summary>
    public static bool Sha256Matches(string filePath, string sha256Hex, long expectedSize = -1)
    {
        if (string.IsNullOrWhiteSpace(sha256Hex) || sha256Hex.Length != 64)
        {
            return false;
        }

        try
        {
            var expected = Convert.FromHexString(sha256Hex);
            if (expectedSize >= 0 && new FileInfo(filePath).Length != expectedSize)
            {
                return false;
            }

            using var stream = File.OpenRead(filePath);
            var actual = SHA256.HashData(stream);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses and verifies a signed feed wrapper (the family
    /// <c>update-manifest.json</c>: base64 payload + Ed25519 signature block).
    /// Every gate must pass before the inner manifest is surfaced:
    /// schema 1, the ed25519 algorithm, the pinned key-id, the signature over the
    /// exact payload bytes, and payload fields (application id, channel, https
    /// artifact URLs, well-formed hex digests). Any failure returns false.
    /// </summary>
    public static bool TryReadSignedFeed(
        string wrapperJson,
        ReadOnlySpan<byte> publicKey,
        string expectedKeyId,
        out SignedFeedManifest? manifest)
    {
        manifest = null;
        try
        {
            var wrapper = JsonSerializer.Deserialize<SignedManifestWrapper>(wrapperJson);
            if (wrapper?.Payload is not { } payloadBase64
                || wrapper.Signature is not { } signature
                || signature.Value is not { } signatureBase64)
            {
                return false;
            }

            if (wrapper.Schema != FeedSchemaVersion)
            {
                return false;
            }

            if (!string.Equals(signature.Algorithm, "ed25519", StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(signature.KeyId, expectedKeyId, StringComparison.Ordinal))
            {
                return false;
            }

            var payload = Convert.FromBase64String(payloadBase64);
            var signatureBytes = Convert.FromBase64String(signatureBase64);
            if (signatureBytes.Length != Ed25519.SignatureSize || publicKey.Length != Ed25519.PublicKeySize)
            {
                return false;
            }

            if (!Ed25519.Verify(signatureBytes, publicKey, payload))
            {
                return false;
            }

            return TryReadManifestPayload(payload, out manifest);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
        {
            // Malformed JSON, bad base64, or an unusable key: reject, never throw.
            return false;
        }
    }

    /// <summary>Selects the feed artifact for one platform/architecture
    /// (case-insensitive ordinal match), or null when the feed does not serve it.</summary>
    public static SignedFeedArtifact? SelectArtifact(SignedFeedManifest manifest, string os, string architecture)
        => manifest.Artifacts.FirstOrDefault(artifact =>
            string.Equals(artifact.Platform, os, StringComparison.OrdinalIgnoreCase)
            && string.Equals(artifact.Architecture, architecture, StringComparison.OrdinalIgnoreCase));

    /// <summary>Three-part version text (the form the feed and release tags use).</summary>
    private static string FormatVersion(Version version)
        => $"{version.Major}.{version.Minor}.{version.Build}";

    private static bool TryReadManifestPayload(byte[] payload, out SignedFeedManifest? manifest)
    {
        manifest = null;
        var dto = JsonSerializer.Deserialize<ManifestPayload>(payload);
        if (dto is null
            || dto.ApplicationId is not { } applicationId
            || dto.Channel is not { } channel
            || !TryParseVersionTag(dto.Version, out var version)
            || !string.Equals(applicationId, FeedApplicationId, StringComparison.Ordinal)
            || !string.Equals(channel, FeedChannel, StringComparison.Ordinal)
            || dto.Artifacts is null
            || dto.Artifacts.Count == 0)
        {
            return false;
        }

        var artifacts = new List<SignedFeedArtifact>(dto.Artifacts.Count);
        foreach (var artifact in dto.Artifacts)
        {
            if (artifact?.Platform is null
                || artifact.Architecture is null
                || artifact.Url is null
                || artifact.Sha256 is null
                || artifact.Size < 0)
            {
                return false;
            }

            // Artifact origins are pinned to https: a signature over an http URL
            // would still let a network position swap the archive in transit.
            if (!Uri.TryCreate(artifact.Url, UriKind.Absolute, out var url)
                || url.Scheme != Uri.UriSchemeHttps)
            {
                return false;
            }

            if (artifact.Sha256.Length != 64 || !IsHex(artifact.Sha256))
            {
                return false;
            }

            artifacts.Add(new SignedFeedArtifact(
                artifact.Platform,
                artifact.Architecture,
                url,
                artifact.Sha256.ToLowerInvariant(),
                artifact.Size,
                artifact.Installer ?? string.Empty));
        }

        manifest = new SignedFeedManifest(
            applicationId,
            version,
            channel,
            dto.MinimumVersion ?? "0.0.0",
            artifacts);
        return true;
    }

    private static bool IsHex(string text)
    {
        foreach (var c in text)
        {
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // The manifest is the only buffered read; artifact downloads stream with
        // ResponseHeadersRead and are unaffected by this cap.
        client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            $"MoveBit/{CurrentVersionText} (+{ProductUrl})");
        return client;
    }

    /// <summary>The portable archive the update feed serves to one platform, expressed with the
    /// release pipeline's naming scheme: <c>movebit-&lt;version&gt;-&lt;os&gt;-&lt;arch&gt;&lt;extension&gt;</c>,
    /// always lowercase. Windows ARM64 is served the x64 archive (Windows on ARM runs it
    /// through x64 emulation); Linux arm64 has no release artifact and stays unsupported.</summary>
    private sealed record PlatformTarget(string Os, string Arch, string Extension)
    {
        /// <summary>Anchored pattern for the exact asset names the release publishes.</summary>
        public Regex AssetPattern() => new(
            $"^movebit-\\d+\\.\\d+\\.\\d+-{Os}-{Arch}{Regex.Escape(Extension)}$",
            RegexOptions.Compiled);

        public string AssetPatternText => $"movebit-<version>-{Os}-{Arch}{Extension}";

        public string ArchiveDescription => $"movebit-*-{Os}-{Arch}{Extension}";
    }

    /// <summary>Pure helper used by tests: does <paramref name="assetName"/> name the
    /// portable archive this target platform consumes?</summary>
    public static bool MatchesPlatformAsset(string assetName, string os, string arch, string extension)
        => new PlatformTarget(os, arch, extension).AssetPattern().IsMatch(assetName);

    private static PlatformTarget? GetCurrentPlatformTarget()
    {
        var arm64 = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // ARM64 Windows is documented to run the x64 build under emulation.
            return new PlatformTarget("windows", "x64", ".zip");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new PlatformTarget("macos", arm64 ? "arm64" : "x64", ".zip");
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return arm64 ? null : new PlatformTarget("linux", "x64", ".tar.gz");
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

    /// <summary>Extracts a gzip-compressed tar (the Linux update artifact) with the same
    /// path-traversal protection as the ZIP path. Uses the BCL's System.Formats.Tar.</summary>
    private static void ExtractTarGzSafely(string tarGzPath, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;

        using var sourceStream = new FileStream(
            tarGzPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);
        using var gzipStream = new GZipStream(sourceStream, CompressionMode.Decompress);
        using var reader = new TarReader(gzipStream);
        while (reader.GetNextEntry() is { } entry)
        {
            var entryPath = entry.Name.Replace('\\', '/');
            var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entryPath));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.Ordinal))
            {
                throw new InvalidDataException("更新包包含不安全的文件路径。");
            }

            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(destinationPath);
                    break;
                case TarEntryType.RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    entry.ExtractToFile(destinationPath, overwrite: true);
                    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        File.SetUnixFileMode(destinationPath, UnixFileMode.UserRead | UnixFileMode.UserWrite
                            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead
                            | UnixFileMode.OtherWrite);
                    }

                    break;
                default:
                    // Symlinks/hardlinks/device nodes are never expected in the feed
                    // artifact; skipping them keeps the extraction side-effect free.
                    break;
            }
        }
    }

    /// <summary>The feed archives carry either the classic C# host layout (a bare
    /// <c>MoveBit</c> executable) or the Rivet packaged layout (a <c>RivetHost</c>
    /// executable, possibly inside an app/payload directory). Accept either.</summary>
    private static bool StagingContainsExecutable(string stagingDirectory, string executableName)
    {
        if (File.Exists(Path.Combine(stagingDirectory, executableName)))
        {
            return true;
        }

        var rivetHostName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "RivetHost.exe" : "RivetHost";
        var candidates = new HashSet<string>(StringComparer.Ordinal) { executableName, rivetHostName };
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // macOS bundles name their executable after the app (lowercase).
            candidates.Add(executableName.ToLowerInvariant());
        }

        foreach (var file in Directory.EnumerateFiles(stagingDirectory, "*", SearchOption.AllDirectories))
        {
            if (candidates.Contains(Path.GetFileName(file)))
            {
                return true;
            }
        }

        return false;
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

    private sealed class SignedManifestWrapper
    {
        [JsonPropertyName("payload")]
        public string? Payload { get; init; }

        [JsonPropertyName("schema")]
        public int Schema { get; init; }

        [JsonPropertyName("signature")]
        public SignedManifestSignature? Signature { get; init; }
    }

    private sealed class SignedManifestSignature
    {
        [JsonPropertyName("algorithm")]
        public string? Algorithm { get; init; }

        [JsonPropertyName("key_id")]
        public string? KeyId { get; init; }

        [JsonPropertyName("value")]
        public string? Value { get; init; }
    }

    // Field names follow rivet/distribution's payload JSON exactly; unknown
    // fields (build, rollout, published_at, arguments, ...) are ignored so the
    // signer can evolve without breaking old verifiers.
    private sealed class ManifestPayload
    {
        [JsonPropertyName("application_id")]
        public string? ApplicationId { get; init; }

        [JsonPropertyName("version")]
        public string? Version { get; init; }

        [JsonPropertyName("channel")]
        public string? Channel { get; init; }

        [JsonPropertyName("minimum_version")]
        public string? MinimumVersion { get; init; }

        [JsonPropertyName("artifacts")]
        public List<ManifestArtifactDto>? Artifacts { get; init; }
    }

    private sealed class ManifestArtifactDto
    {
        [JsonPropertyName("platform")]
        public string? Platform { get; init; }

        [JsonPropertyName("architecture")]
        public string? Architecture { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("sha256")]
        public string? Sha256 { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("installer")]
        public string? Installer { get; init; }
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
