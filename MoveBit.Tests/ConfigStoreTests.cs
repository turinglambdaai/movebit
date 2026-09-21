using System;
using System.IO;
using System.Text.Json;
using MoveBit.Models;
using MoveBit.Services;
using Xunit;

namespace MoveBit.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir;

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "movebit-config-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // temp cleanup is best-effort
        }
    }

    [Fact]
    public void Save_and_load_roundtrip_all_settings()
    {
        var expected = new ReminderConfig
        {
            SitReminderMinutes = 50,
            WaterReminderMinutes = 40,
            AwayResetMinutes = 7,
            ForceBreakEnabled = false,
            BreakDurationMinutes = 4,
            SkipAfterSeconds = 35,
            MicroBreakEnabled = false,
            MicroBreakIntervalMinutes = 25,
            MicroBreakDurationSeconds = 30,
            SoundEnabled = false,
            AutoCheckUpdates = false,
            WelcomeShown = true,
        };

        var store = new ConfigStore(_dir);
        Assert.True(store.TrySave(expected));
        var actual = store.Load();

        Assert.Equal(expected.SitReminderMinutes, actual.SitReminderMinutes);
        Assert.Equal(expected.WaterReminderMinutes, actual.WaterReminderMinutes);
        Assert.Equal(expected.AwayResetMinutes, actual.AwayResetMinutes);
        Assert.Equal(expected.ForceBreakEnabled, actual.ForceBreakEnabled);
        Assert.Equal(expected.BreakDurationMinutes, actual.BreakDurationMinutes);
        Assert.Equal(expected.SkipAfterSeconds, actual.SkipAfterSeconds);
        Assert.Equal(expected.MicroBreakEnabled, actual.MicroBreakEnabled);
        Assert.Equal(expected.MicroBreakIntervalMinutes, actual.MicroBreakIntervalMinutes);
        Assert.Equal(expected.MicroBreakDurationSeconds, actual.MicroBreakDurationSeconds);
        Assert.Equal(expected.SoundEnabled, actual.SoundEnabled);
        Assert.Equal(expected.AutoCheckUpdates, actual.AutoCheckUpdates);
        Assert.Equal(expected.WelcomeShown, actual.WelcomeShown);
        Assert.False(File.Exists(Path.Combine(_dir, "config.json.tmp")));
    }

    [Fact]
    public void TrySave_returns_false_when_config_directory_is_a_file()
    {
        var blockedPath = Path.Combine(_dir, "not-a-directory");
        File.WriteAllText(blockedPath, "block directory creation");

        var store = new ConfigStore(blockedPath);

        Assert.False(store.TrySave(new ReminderConfig()));
    }

    [Fact]
    public void Load_clamps_numeric_values_from_disk()
    {
        var invalid = new ReminderConfig
        {
            SitReminderMinutes = 1,
            WaterReminderMinutes = 999,
            AwayResetMinutes = 0,
            BreakDurationMinutes = 100,
            SkipAfterSeconds = -10,
            MicroBreakIntervalMinutes = 1,
            MicroBreakDurationSeconds = 999,
        };
        File.WriteAllText(Path.Combine(_dir, "config.json"), JsonSerializer.Serialize(invalid));

        var actual = new ConfigStore(_dir).Load();

        Assert.Equal(10, actual.SitReminderMinutes);
        Assert.Equal(180, actual.WaterReminderMinutes);
        Assert.Equal(1, actual.AwayResetMinutes);
        Assert.Equal(30, actual.BreakDurationMinutes);
        Assert.Equal(0, actual.SkipAfterSeconds);
        Assert.Equal(10, actual.MicroBreakIntervalMinutes);
        Assert.Equal(60, actual.MicroBreakDurationSeconds);
        Assert.True(actual.AutoCheckUpdates);
    }

    [Fact]
    public void Corrupt_json_falls_back_to_defaults()
    {
        File.WriteAllText(Path.Combine(_dir, "config.json"), "{ definitely not json");

        var actual = new ConfigStore(_dir).Load();

        Assert.Equal(45, actual.SitReminderMinutes);
        Assert.Equal(30, actual.WaterReminderMinutes);
        Assert.True(actual.ForceBreakEnabled);
        Assert.True(actual.AutoCheckUpdates);
    }
}
