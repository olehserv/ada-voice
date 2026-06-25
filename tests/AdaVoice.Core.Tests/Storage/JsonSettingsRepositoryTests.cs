using AdaVoice.Core.Domain;
using AdaVoice.Core.Storage;

namespace AdaVoice.Core.Tests.Storage;

public class JsonSettingsRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "adavoice-set-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Missing_file_loads_defaults()
    {
        var settings = new JsonSettingsRepository(_root).Load();

        Assert.Null(settings.MonitorDeviceName);
        Assert.True(settings.MonitorEnabled);
        Assert.Equal(-12, settings.MicDuckDb);
        Assert.Equal(50, settings.DuckRampMs);
    }

    [Fact]
    public void Save_then_load_roundtrips_the_duck_settings()
    {
        new JsonSettingsRepository(_root).Save(new Settings { MicDuckDb = -18, DuckRampMs = 80 });

        var reloaded = new JsonSettingsRepository(_root).Load();
        Assert.Equal(-18, reloaded.MicDuckDb);
        Assert.Equal(80, reloaded.DuckRampMs);
    }

    [Fact]
    public void Save_then_load_roundtrips_the_monitor_choice()
    {
        var repo = new JsonSettingsRepository(_root);
        repo.Save(new Settings { MonitorDeviceName = "Headphones (USB)", MonitorEnabled = true });

        var reloaded = new JsonSettingsRepository(_root).Load();
        Assert.Equal("Headphones (USB)", reloaded.MonitorDeviceName);
        Assert.True(reloaded.MonitorEnabled);
    }

    [Fact]
    public void Corrupt_file_loads_defaults_without_throwing()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(AdaVoicePaths.SettingsFile(_root), "{ not valid json");

        var settings = new JsonSettingsRepository(_root).Load();

        Assert.Null(settings.MonitorDeviceName);
        Assert.True(settings.MonitorEnabled);
    }

    [Fact]
    public void Empty_file_loads_defaults()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(AdaVoicePaths.SettingsFile(_root), "");

        Assert.True(new JsonSettingsRepository(_root).Load().MonitorEnabled);
    }

    [Fact]
    public void Save_writes_valid_json_and_leaves_no_temp_file()
    {
        var repo = new JsonSettingsRepository(_root);
        repo.Save(new Settings { MonitorDeviceName = "x" });

        var file = AdaVoicePaths.SettingsFile(_root);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(file + ".tmp"));
        Assert.Equal("x", new JsonSettingsRepository(_root).Load().MonitorDeviceName); // parses back
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
