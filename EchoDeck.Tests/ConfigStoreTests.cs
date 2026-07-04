using EchoDeck.Engine;
using Xunit;

namespace EchoDeck.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        using var tmp = new TempDir();
        string path = Path.Combine(tmp.Path, "config.json");

        var config = new EngineConfig
        {
            InputDeviceId = "mic-1",
            FarEndDeviceId = "spk-2",
            OutputDeviceId = "cable-3",
            Aec = false,
            NoiseRemoval = true,
            RoomEchoRemoval = true,
            ShowDisabledDevices = true,
            ShowDisconnectedDevices = true,
            SdkDir = @"C:\custom\sdk"
        };

        ConfigStore.SaveTo(path, config);
        EngineConfig loaded = ConfigStore.LoadFrom(path);

        Assert.Equal(config.InputDeviceId, loaded.InputDeviceId);
        Assert.Equal(config.FarEndDeviceId, loaded.FarEndDeviceId);
        Assert.Equal(config.OutputDeviceId, loaded.OutputDeviceId);
        Assert.False(loaded.Aec);
        Assert.True(loaded.NoiseRemoval);
        Assert.True(loaded.RoomEchoRemoval);
        Assert.True(loaded.ShowDisabledDevices);
        Assert.True(loaded.ShowDisconnectedDevices);
        Assert.Equal(config.SdkDir, loaded.SdkDir);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        using var tmp = new TempDir();
        string path = Path.Combine(tmp.Path, "does-not-exist.json");

        EngineConfig loaded = ConfigStore.LoadFrom(path);

        Assert.True(loaded.Aec); // default is AEC on
        Assert.False(loaded.NoiseRemoval);
        Assert.Equal("1", loaded.SchemaVersion);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaultsInsteadOfThrowing()
    {
        using var tmp = new TempDir();
        string path = Path.Combine(tmp.Path, "config.json");
        File.WriteAllText(path, "{ this is not valid json ]");

        EngineConfig loaded = ConfigStore.LoadFrom(path);

        Assert.True(loaded.Aec);
        Assert.Equal("1", loaded.SchemaVersion);
    }

    [Fact]
    public void Save_CreatesParentDirectoryIfMissing()
    {
        using var tmp = new TempDir();
        string path = Path.Combine(tmp.Path, "nested", "sub", "config.json");

        ConfigStore.SaveTo(path, new EngineConfig { InputDeviceId = "x" });

        Assert.True(File.Exists(path));
        Assert.Equal("x", ConfigStore.LoadFrom(path).InputDeviceId);
    }

    [Fact]
    public void Save_LeavesNoTempFileBehind()
    {
        using var tmp = new TempDir();
        string path = Path.Combine(tmp.Path, "config.json");

        ConfigStore.SaveTo(path, new EngineConfig());

        Assert.False(File.Exists(path + ".tmp"), "temp file should be moved over the target, not left behind");
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "echodeck-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }
}
