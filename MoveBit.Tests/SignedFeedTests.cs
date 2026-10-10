using System;
using System.Security.Cryptography;
using System.Text.Json;
using MoveBit.Services;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Xunit;

namespace MoveBit.Tests;

/// <summary>
/// The signed-feed verifier: wrapper parsing, Ed25519 verification over the
/// exact payload bytes, the key-id pin, and artifact selection. Wrappers are
/// built and signed here with an ephemeral test keypair — the same wrapper
/// shape rivet/distribution's write-signed-manifest emits.
/// </summary>
public sealed class SignedFeedTests
{
    // Fixed 32-byte seed (Ed25519 private key) for deterministic tests; the
    // matching public key is derived, never hard-coded.
    private static readonly byte[] TestSeed =
    {
        0x4c, 0x0f, 0x11, 0x2a, 0x9d, 0x8e, 0x71, 0x33,
        0x5c, 0xb4, 0x26, 0xaf, 0xe1, 0x90, 0xd7, 0x48,
        0x0b, 0x63, 0xf2, 0x85, 0x1e, 0xc9, 0xa6, 0x04,
        0x39, 0x7d, 0xbb, 0x50, 0xcc, 0x2f, 0x18, 0x97,
    };

    private static readonly byte[] OtherSeed =
    {
        0x01, 0x24, 0x47, 0x6a, 0x8d, 0xb0, 0xd3, 0xf6,
        0x19, 0x3c, 0x5f, 0x82, 0xa5, 0xc8, 0xeb, 0x0e,
        0x31, 0x54, 0x77, 0x9a, 0xbd, 0xe0, 0x03, 0x26,
        0x49, 0x6c, 0x8f, 0xb2, 0xd5, 0xf8, 0x1b, 0x3e,
    };

    private static byte[] PublicKeyOf(byte[] seed)
    {
        var publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(seed, publicKey);
        return publicKey;
    }

    private static byte[] Sign(byte[] seed, byte[] message)
    {
        var signature = new byte[Ed25519.SignatureSize];
        Ed25519.Sign(seed, message, signature);
        return signature;
    }

    /// <summary>An inner manifest with the four artifacts the release pipeline
    /// publishes, in rivet/distribution's payload JSON shape.</summary>
    private static string BuildPayloadJson(
        string applicationId = "site.jrtx.movebit",
        string version = "2.0.0",
        string channel = "stable",
        string minimumVersion = "0.0.0")
    {
        var digest = new string('a', 64);
        return $$"""
            {"application_id":"{{applicationId}}","artifacts":[
              {"architecture":"arm64","arguments":[],"installer":"zip","platform":"macos","sha256":"{{digest}}","size":100,"url":"https://github.com/turinglambdaai/movebit/releases/download/v{{version}}/movebit-{{version}}-macos-arm64.zip"},
              {"architecture":"x64","arguments":[],"installer":"zip","platform":"macos","sha256":"{{digest}}","size":101,"url":"https://github.com/turinglambdaai/movebit/releases/download/v{{version}}/movebit-{{version}}-macos-x64.zip"},
              {"architecture":"x64","arguments":[],"installer":"zip","platform":"windows","sha256":"{{digest}}","size":102,"url":"https://github.com/turinglambdaai/movebit/releases/download/v{{version}}/movebit-{{version}}-windows-x64.zip"},
              {"architecture":"x64","arguments":[],"installer":"tar-gz","platform":"linux","sha256":"{{digest}}","size":103,"url":"https://github.com/turinglambdaai/movebit/releases/download/v{{version}}/movebit-{{version}}-linux-x64.tar.gz"}
            ],"build":5,"channel":"{{channel}}","minimum_version":"{{minimumVersion}}","previous_version":null,"published_at":"2026-10-10T00:00:00Z","rollback_allowed":true,"rollout":100,"schema":1,"version":"{{version}}"}
            """;
    }

    private static string BuildWrapperJson(
        byte[] seed,
        byte[] payload,
        string keyId = "movebit-2026-10",
        string algorithm = "ed25519",
        int schema = 1,
        string? payloadOverride = null,
        string? signatureOverride = null)
    {
        var signature = signatureOverride ?? Convert.ToBase64String(Sign(seed, payload));
        return JsonSerializer.Serialize(new
        {
            payload = payloadOverride ?? Convert.ToBase64String(payload),
            schema,
            signature = new
            {
                algorithm,
                key_id = keyId,
                value = signature,
            },
        });
    }

    private static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);

    [Fact]
    public void TryReadSignedFeed_accepts_well_formed_wrapper()
    {
        var payload = Utf8(BuildPayloadJson(version: "2.0.0"));
        var wrapper = BuildWrapperJson(TestSeed, payload);

        var ok = UpdateService.TryReadSignedFeed(wrapper, PublicKeyOf(TestSeed), "movebit-2026-10", out var manifest);

        Assert.True(ok);
        Assert.NotNull(manifest);
        Assert.Equal("2.0.0", manifest!.Version.ToString(3));
        Assert.Equal("site.jrtx.movebit", manifest.ApplicationId);
        Assert.Equal("stable", manifest.Channel);
        Assert.Equal(4, manifest.Artifacts.Count);
    }

    [Fact]
    public void TryReadSignedFeed_rejects_tampered_payload()
    {
        var payload = Utf8(BuildPayloadJson(version: "2.0.0"));
        var signedWrapper = BuildWrapperJson(TestSeed, payload);

        // Rebuild the wrapper with a payload that differs from the signed bytes
        // (version bumped after signing — the classic feed swap).
        var tamperedPayload = Utf8(BuildPayloadJson(version: "9.9.9"));
        var tamperedWrapper = BuildWrapperJson(
            TestSeed, payload, payloadOverride: Convert.ToBase64String(tamperedPayload));

        Assert.NotEqual(signedWrapper, tamperedWrapper);
        Assert.False(UpdateService.TryReadSignedFeed(
            tamperedWrapper, PublicKeyOf(TestSeed), "movebit-2026-10", out _));
    }

    [Fact]
    public void TryReadSignedFeed_rejects_signature_from_another_key()
    {
        var payload = Utf8(BuildPayloadJson());
        var wrapper = BuildWrapperJson(OtherSeed, payload);

        Assert.False(UpdateService.TryReadSignedFeed(
            wrapper, PublicKeyOf(TestSeed), "movebit-2026-10", out _));
    }

    [Fact]
    public void TryReadSignedFeed_rejects_flipped_signature_bit()
    {
        var payload = Utf8(BuildPayloadJson());
        var signature = Sign(TestSeed, payload);
        signature[0] ^= 0x01;
        var wrapper = BuildWrapperJson(TestSeed, payload, signatureOverride: Convert.ToBase64String(signature));

        Assert.False(UpdateService.TryReadSignedFeed(
            wrapper, PublicKeyOf(TestSeed), "movebit-2026-10", out _));
    }

    [Fact]
    public void TryReadSignedFeed_rejects_key_id_mismatch()
    {
        var payload = Utf8(BuildPayloadJson());

        foreach (var keyId in new[] { "taskly-2026-10", "podlens-2026-10", "", "movebit-2026-11" })
        {
            var wrapper = BuildWrapperJson(TestSeed, payload, keyId: keyId);
            Assert.False(UpdateService.TryReadSignedFeed(
                wrapper, PublicKeyOf(TestSeed), "movebit-2026-10", out _));
        }
    }

    [Fact]
    public void TryReadSignedFeed_rejects_unknown_algorithm()
    {
        var payload = Utf8(BuildPayloadJson());
        var wrapper = BuildWrapperJson(TestSeed, payload, algorithm: "rsa-pss");

        Assert.False(UpdateService.TryReadSignedFeed(
            wrapper, PublicKeyOf(TestSeed), "movebit-2026-10", out _));
    }

    [Fact]
    public void TryReadSignedFeed_rejects_unknown_schema()
    {
        var payload = Utf8(BuildPayloadJson());

        foreach (var schema in new[] { 0, 2 })
        {
            var wrapper = BuildWrapperJson(TestSeed, payload, schema: schema);
            Assert.False(UpdateService.TryReadSignedFeed(
                wrapper, PublicKeyOf(TestSeed), "movebit-2026-10", out _));
        }
    }

    [Fact]
    public void TryReadSignedFeed_rejects_malformed_inputs()
    {
        var publicKey = PublicKeyOf(TestSeed);
        var payload = Utf8(BuildPayloadJson());

        Assert.False(UpdateService.TryReadSignedFeed("not json", publicKey, "movebit-2026-10", out _));
        Assert.False(UpdateService.TryReadSignedFeed("{}", publicKey, "movebit-2026-10", out _));
        Assert.False(UpdateService.TryReadSignedFeed(
            BuildWrapperJson(TestSeed, payload, payloadOverride: "!!!not-base64!!!"),
            publicKey, "movebit-2026-10", out _));
    }

    [Fact]
    public void TryReadSignedFeed_rejects_foreign_application_or_channel()
    {
        var publicKey = PublicKeyOf(TestSeed);

        var foreignApp = Utf8(BuildPayloadJson(applicationId: "site.jrtx.taskly"));
        Assert.False(UpdateService.TryReadSignedFeed(
            BuildWrapperJson(TestSeed, foreignApp), publicKey, "movebit-2026-10", out _));

        var betaChannel = Utf8(BuildPayloadJson(channel: "beta"));
        Assert.False(UpdateService.TryReadSignedFeed(
            BuildWrapperJson(TestSeed, betaChannel), publicKey, "movebit-2026-10", out _));
    }

    [Fact]
    public void TryReadSignedFeed_rejects_http_artifact_url()
    {
        var payloadJson = BuildPayloadJson().Replace(
            "https://github.com", "http://github.com.example.invalid");
        var wrapper = BuildWrapperJson(TestSeed, Utf8(payloadJson));

        Assert.False(UpdateService.TryReadSignedFeed(
            wrapper, PublicKeyOf(TestSeed), "movebit-2026-10", out _));
    }

    [Fact]
    public void TryReadSignedFeed_rejects_garbage_payload_fields()
    {
        var publicKey = PublicKeyOf(TestSeed);

        var badVersion = Utf8("""{"application_id":"site.jrtx.movebit","version":"not-a-version","channel":"stable","artifacts":[]}""");
        Assert.False(UpdateService.TryReadSignedFeed(
            BuildWrapperJson(TestSeed, badVersion), publicKey, "movebit-2026-10", out _));

        var noArtifacts = Utf8("""{"application_id":"site.jrtx.movebit","version":"2.0.0","channel":"stable","artifacts":[]}""");
        Assert.False(UpdateService.TryReadSignedFeed(
            BuildWrapperJson(TestSeed, noArtifacts), publicKey, "movebit-2026-10", out _));

        var badDigest = Utf8(BuildPayloadJson().Replace(new string('a', 64), new string('g', 64)));
        Assert.False(UpdateService.TryReadSignedFeed(
            BuildWrapperJson(TestSeed, badDigest), publicKey, "movebit-2026-10", out _));
    }

    [Fact]
    public void TryReadSignedFeed_rejects_wrong_length_keys_and_signatures()
    {
        var payload = Utf8(BuildPayloadJson());
        var fullKey = PublicKeyOf(TestSeed);

        // 31-byte key: unusable, fail closed rather than throw.
        Assert.False(UpdateService.TryReadSignedFeed(
            BuildWrapperJson(TestSeed, payload), fullKey[..31], "movebit-2026-10", out _));

        // Truncated signature inside an otherwise valid wrapper.
        var signature = Convert.ToBase64String(Sign(TestSeed, payload));
        var truncated = Convert.ToBase64String(Convert.FromBase64String(signature)[..63]);
        Assert.False(UpdateService.TryReadSignedFeed(
            BuildWrapperJson(TestSeed, payload, signatureOverride: truncated),
            fullKey, "movebit-2026-10", out _));
    }

    [Fact]
    public void TryReadSignedFeed_rejects_a_real_rivet_wrapper_signed_by_an_unknown_key()
    {
        // The v1.6.0 release wrapper (public asset), structurally perfect but
        // signed with the pre-verifier release key: the verifier must refuse it
        // because its signature does not match the pinned key.
        var wrapper = ActualRivetWrapper;
        Assert.False(UpdateService.TryReadSignedFeed(
            wrapper, PublicKeyOf(TestSeed), "movebit-2026-10", out _));
    }

    [Fact]
    public void Pinned_feed_constants_are_intact()
    {
        Assert.Equal(32, UpdateService.PinnedFeedPublicKey.Length);
        Assert.Equal("movebit-2026-10", UpdateService.PinnedFeedKeyId);
        Assert.Equal(1, UpdateService.FeedSchemaVersion);
        Assert.Equal("site.jrtx.movebit", UpdateService.FeedApplicationId);
        Assert.Equal("stable", UpdateService.FeedChannel);
    }

    [Fact]
    public void SelectArtifact_matches_platform_and_architecture()
    {
        var payload = Utf8(BuildPayloadJson());
        Assert.True(UpdateService.TryReadSignedFeed(
            BuildWrapperJson(TestSeed, payload), PublicKeyOf(TestSeed), "movebit-2026-10", out var manifest));
        Assert.NotNull(manifest);

        var windows = UpdateService.SelectArtifact(manifest!, "windows", "x64");
        Assert.NotNull(windows);
        Assert.Equal("zip", windows!.Installer);
        Assert.EndsWith("movebit-2.0.0-windows-x64.zip", windows.Url.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(102, windows.Size);

        var macos = UpdateService.SelectArtifact(manifest!, "macos", "arm64");
        Assert.NotNull(macos);
        Assert.EndsWith("movebit-2.0.0-macos-arm64.zip", macos!.Url.AbsoluteUri, StringComparison.Ordinal);

        // Match is case-insensitive; the feed serves lowercase.
        Assert.Same(windows, UpdateService.SelectArtifact(manifest!, "Windows", "X64"));

        // Nothing published for this combination — callers must refuse, not guess.
        Assert.Null(UpdateService.SelectArtifact(manifest!, "linux", "arm64"));
        Assert.Null(UpdateService.SelectArtifact(manifest!, "freebsd", "x64"));
    }

    /// <summary>The exact wrapper the v1.6.0 release pipeline published
    /// (downloaded from releases/latest/download/update-manifest.json).</summary>
    private const string ActualRivetWrapper = """
        {"payload":"eyJhcHBsaWNhdGlvbl9pZCI6InNpdGUuanJ0eC5tb3ZlYml0IiwiYXJ0aWZhY3RzIjpbeyJhcmNoaXRlY3R1cmUiOiJhcm02NCIsImFyZ3VtZW50cyI6W10sImluc3RhbGxlciI6InppcCIsInBsYXRmb3JtIjoibWFjb3MiLCJzaGEyNTYiOiIwYjEzMjU0ZTk2YmM1NDE4MzI3MjVjOTJjNWU1NGNjNjE3OTU3ZDMxMmQ5MzBkMGM3MzJlZGJkYmZlYzNlZTYzIiwic2l6ZSI6MjQyMzQyMDksInVybCI6Imh0dHBzOi8vZ2l0aHViLmNvbS90dXJpbmdsYW1iZGFhaS9tb3ZlYml0L3JlbGVhc2VzL2Rvd25sb2FkL3YxLjYuMC9tb3ZlYml0LTEuNi4wLW1hY29zLWFybTY0LnppcCJ9LHsiYXJjaGl0ZWN0dXJlIjoieDY0IiwiYXJndW1lbnRzIjpbXSwiaW5zdGFsbGVyIjoiemlwIiwicGxhdGZvcm0iOiJtYWNvcyIsInNoYTI1NiI6IjIyOWU1ZDU1YWM4NmU3Njc1ODNkMDhkMjY0NTMyZjhlNjAxMzQyMzM4MDFiZGY2YTM0MGI3YzE1MzBkNmE3NjMiLCJzaXplIjoyNTAzMjc5OSwidXJsIjoiaHR0cHM6Ly9naXRodWIuY29tL3R1cmluZ2xhbWJkYWFpL21vdmViaXQvcmVsZWFzZXMvZG93bmxvYWQvdjEuNi4wL21vdmViaXQtMS42LjAtbWFjb3MteDY0LnppcCJ9LHsiYXJjaGl0ZWN0dXJlIjoieDY0IiwiYXJndW1lbnRzIjpbXSwiaW5zdGFsbGVyIjoiemlwIiwicGxhdGZvcm0iOiJ3aW5kb3dzIiwic2hhMjU2IjoiYjk3NzZlNWRiOGZmNGI3OGI3NTQzNjdkZjMyZTU5YjliMjljNTE1N2YyMjRhODRkMjM0YTM5MmZkODI0YWFkNyIsInNpemUiOjg2OTgwOTM5LCJ1cmwiOiJodHRwczovL2dpdGh1Yi5jb20vdHVyaW5nbGFtYmRhYWkvbW92ZWJpdC9yZWxlYXNlcy9kb3dubG9hZC92MS42LjAvbW92ZWJpdC0xLjYuMC13aW5kb3dzLXg2NC56aXAifSx7ImFyY2hpdGVjdHVyZSI6Ing2NCIsImFyZ3VtZW50cyI6W10sImluc3RhbGxlciI6InRhci1neiIsInBsYXRmb3JtIjoibGludXgiLCJzaGEyNTYiOiJmMTM4Y2NmYWMyNGUzNTVlNjY4MGI5N2UzYjBlNGU2ZWMyMmE5NGRhMTc0YjNjZGJlNzUxMjFlODE5MGFhMGExIiwic2l6ZSI6MTU3Njc3NjUsInVybCI6Imh0dHBzOi8vZ2l0aHViLmNvbS90dXJpbmdsYW1iZGFhaS9tb3ZlYml0L3JlbGVhc2VzL2Rvd25sb2FkL3YxLjYuMC9tb3ZlYml0LTEuNi4wLWxpbnV4LXg2NC50YXIuZ3oifV0sImJ1aWxkIjo0LCJjaGFubmVsIjoic3RhYmxlIiwibWluaW11bV92ZXJzaW9uIjoiMC4wLjAiLCJwcmV2aW91c192ZXJzaW9uIjpudWxsLCJwdWJsaXNoZWRfYXQiOiIyMDI2LTEwLTA5VDE5OjA1OjU2WiIsInJvbGxiYWNrX2FsbG93ZWQiOnRydWUsInJvbGxvdXQiOjEwMCwic2NoZW1hIjoxLCJ2ZXJzaW9uIjoiMS42LjAifQ==","schema":1,"signature":{"algorithm":"ed25519","key_id":"movebit-2026-10","value":"W4zv9x6KBt0W6TcvOjL88hboAolrgrtL+SFx+86hC/bmCZ12uRG2mw3KuyRlA0Cx844BxWHjwHxdyVO7/YGzDw=="}}
        """;
}
