using System.Text.Json;

namespace EchoDeck.Engine;

/// <summary>Persisted settings: device selections (by ID) + effect toggles.</summary>
public sealed class EngineConfig
{
    public string SchemaVersion { get; set; } = "1";

    public string? InputDeviceId { get; set; }
    public string? FarEndDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }

    public bool Aec { get; set; } = true;
    public bool NoiseRemoval { get; set; }
    public bool RoomEchoRemoval { get; set; }

    public bool ShowDisabledDevices { get; set; }
    public bool ShowDisconnectedDevices { get; set; }

    /// <summary>Optional override for the NVIDIA SDK install path.</summary>
    public string? SdkDir { get; set; }
}

/// <summary>Loads/saves <see cref="EngineConfig"/> as JSON in %APPDATA%\EchoDeck.</summary>
public static class ConfigStore
{
    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EchoDeck");

    public static string FilePath => Path.Combine(Directory, "config.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static EngineConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<EngineConfig>(File.ReadAllText(FilePath)) ?? new EngineConfig();
        }
        catch { /* corrupt/unreadable → defaults */ }
        return new EngineConfig();
    }

    public static void Save(EngineConfig config)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, Options));
            File.Move(tmp, FilePath, overwrite: true); // atomic-ish replace
        }
        catch { /* best effort */ }
    }
}
