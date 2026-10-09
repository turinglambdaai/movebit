using System;
using System.IO;
using System.Security.Cryptography;
using MoveBit.Services;
using Xunit;

namespace MoveBit.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _directory;

    public UpdateServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "movebit-update-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort test cleanup.
        }
    }

    [Theory]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    [InlineData("v0.9.9", "1.0.0", false)]
    [InlineData("not-a-version", "1.0.0", false)]
    public void IsNewerRelease_compares_normalized_tags(string tag, string current, bool expected)
    {
        Assert.Equal(expected, UpdateService.IsNewerRelease(tag, Version.Parse(current)));
    }

    [Theory]
    [InlineData("movebit-1.6.0-windows-x64.zip", "windows", "x64", ".zip", true)]
    [InlineData("movebit-1.6.0-windows-x64.zip.sha256", "windows", "x64", ".zip", false)]
    [InlineData("movebit-1.6.0-windows-x64.msi", "windows", "x64", ".zip", false)]
    [InlineData("movebit-1.6.0-linux-x64.zip", "linux", "x64", ".tar.gz", false)]
    [InlineData("movebit-1.6.0-linux-x64.tar.gz", "linux", "x64", ".tar.gz", true)]
    [InlineData("movebit-1.6.0-macos-arm64.zip", "macos", "arm64", ".zip", true)]
    [InlineData("movebit-1.6.0-macos-x64.zip", "macos", "x64", ".zip", true)]
    [InlineData("movebit-1.6.0-macos-arm64.zip", "macos", "x64", ".zip", false)]
    [InlineData("movebit-1.5.0-macos-arm64.zip", "macos", "arm64", ".zip", true)]
    [InlineData("MoveBit-windows-x64.zip", "windows", "x64", ".zip", false)]
    [InlineData("movebit-1.6.0-windows-x64.backup.zip", "windows", "x64", ".zip", false)]
    [InlineData("movebit-1.6-windows-x64.zip", "windows", "x64", ".zip", false)]
    [InlineData("SHA256SUMS", "windows", "x64", ".zip", false)]
    public void MatchesPlatformAsset_accepts_only_published_archive_names(
        string assetName, string os, string arch, string extension, bool expected)
    {
        Assert.Equal(expected, UpdateService.MatchesPlatformAsset(assetName, os, arch, extension));
    }

    [Fact]
    public void ChecksumMatches_accepts_sha256sum_sidecar()
    {
        var path = Path.Combine(_directory, "package.zip");
        File.WriteAllText(path, "movebit update payload");
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

        Assert.True(UpdateService.ChecksumMatches(path, $"{digest}  movebit-1.6.0-windows-x64.zip\n"));
    }

    [Fact]
    public void ChecksumMatches_rejects_tampering()
    {
        var path = Path.Combine(_directory, "package.zip");
        File.WriteAllText(path, "original payload");
        var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        File.WriteAllText(path, "modified payload");

        Assert.False(UpdateService.ChecksumMatches(path, $"{digest}  movebit-1.6.0-windows-x64.zip\n"));
        Assert.False(UpdateService.ChecksumMatches(path, "not-a-checksum"));
    }
}
