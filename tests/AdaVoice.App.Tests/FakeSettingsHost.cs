using AdaVoice.Host;

namespace AdaVoice.App.Tests;

/// <summary>A test double for <see cref="ISettingsHost"/>: records duck changes and save calls.</summary>
internal sealed class FakeSettingsHost : ISettingsHost
{
    public double MicDuckDb { get; set; } = -12;

    public List<double> SetCalls { get; } = [];
    public int SaveCount { get; private set; }

    public void SetMicDuckDb(double db)
    {
        MicDuckDb = db;
        SetCalls.Add(db);
    }

    public void SaveSettings() => SaveCount++;
}
