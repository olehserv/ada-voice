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
    public void Mic_reference_defaults_to_null_and_roundtrips()
    {
        Assert.Null(new JsonSettingsRepository(_root).Load().MicReferenceRms); // uncalibrated

        new JsonSettingsRepository(_root).Save(new Settings { MicReferenceRms = 0.0834 });

        Assert.Equal(0.0834, new JsonSettingsRepository(_root).Load().MicReferenceRms);
    }

    [Fact]
    public void Window_placement_defaults_to_null_and_roundtrips()
    {
        var loaded = new JsonSettingsRepository(_root).Load();
        Assert.Null(loaded.WindowWidth); // never saved yet → use the XAML defaults
        Assert.Null(loaded.WindowLeft);

        new JsonSettingsRepository(_root).Save(
            new Settings { WindowWidth = 500, WindowHeight = 700, WindowLeft = 120, WindowTop = 60 });

        var reloaded = new JsonSettingsRepository(_root).Load();
        Assert.Equal(500, reloaded.WindowWidth);
        Assert.Equal(700, reloaded.WindowHeight);
        Assert.Equal(120, reloaded.WindowLeft);
        Assert.Equal(60, reloaded.WindowTop);
    }

    [Fact]
    public void Wizard_completed_defaults_to_false_and_roundtrips()
    {
        Assert.False(new JsonSettingsRepository(_root).Load().WizardCompleted);

        new JsonSettingsRepository(_root).Save(new Settings { WizardCompleted = true });

        Assert.True(new JsonSettingsRepository(_root).Load().WizardCompleted);
    }

    [Fact]
    public void Always_on_top_defaults_to_true_and_roundtrips()
    {
        Assert.True(new JsonSettingsRepository(_root).Load().AlwaysOnTop);

        new JsonSettingsRepository(_root).Save(new Settings { AlwaysOnTop = false });

        Assert.False(new JsonSettingsRepository(_root).Load().AlwaysOnTop);
    }

    [Fact]
    public void Replace_on_retrigger_defaults_to_true_and_roundtrips()
    {
        Assert.True(new JsonSettingsRepository(_root).Load().ReplaceOnRetrigger);

        new JsonSettingsRepository(_root).Save(new Settings { ReplaceOnRetrigger = false });

        Assert.False(new JsonSettingsRepository(_root).Load().ReplaceOnRetrigger);
    }

    [Fact]
    public void Language_defaults_to_en_and_roundtrips()
    {
        Assert.Equal("en", new JsonSettingsRepository(_root).Load().Language);

        new JsonSettingsRepository(_root).Save(new Settings { Language = "uk" });

        Assert.Equal("uk", new JsonSettingsRepository(_root).Load().Language);
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
    public void A_corrupt_settings_file_falls_back_to_defaults_and_reports_it()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(AdaVoicePaths.SettingsFile(_root), "{ not valid json");

        var repo = new JsonSettingsRepository(_root);
        repo.Load();

        Assert.True(repo.LoadReplacedCorruptFile); // so the operator can be told the calibration was lost
    }

    [Fact]
    public void A_missing_or_clean_settings_file_is_not_reported_as_corrupt()
    {
        var missing = new JsonSettingsRepository(_root);
        missing.Load();
        Assert.False(missing.LoadReplacedCorruptFile);

        missing.Save(new Settings { MicDuckDb = -9 });
        var clean = new JsonSettingsRepository(_root);
        clean.Load();
        Assert.False(clean.LoadReplacedCorruptFile);
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
