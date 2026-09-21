using System;
using System.IO;
using System.Text.Json;
using MoveBit.Models;

namespace MoveBit.Services;

/// <summary>Loads and saves <see cref="ReminderConfig"/> as JSON in the user config directory.</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _directory;

    public ConfigStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "movebit");
    }

    private string ConfigPath => Path.Combine(_directory, "config.json");

    public ReminderConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var config = JsonSerializer.Deserialize<ReminderConfig>(File.ReadAllText(ConfigPath));
                if (config is not null)
                {
                    config.SitReminderMinutes = Clamp(config.SitReminderMinutes, 10, 240);
                    config.WaterReminderMinutes = Clamp(config.WaterReminderMinutes, 5, 180);
                    config.AwayResetMinutes = Clamp(config.AwayResetMinutes, 1, 60);
                    config.BreakDurationMinutes = Clamp(config.BreakDurationMinutes, 1, 30);
                    config.SkipAfterSeconds = Clamp(config.SkipAfterSeconds, 0, 120);
                    config.MicroBreakIntervalMinutes = Clamp(config.MicroBreakIntervalMinutes, 10, 60);
                    config.MicroBreakDurationSeconds = Clamp(config.MicroBreakDurationSeconds, 10, 60);
                    return config;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable config: fall through to defaults rather than crash the tray app.
        }

        return new ReminderConfig();
    }

    public void Save(ReminderConfig config)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Config persistence is best-effort; reminders keep working with in-memory settings.
        }
    }

    public override string ToString() => ConfigPath;

    private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
}
