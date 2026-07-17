namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Which physical devices the engine should use. The WPF app builds this with its defaults
/// (<c>App.xaml.cs</c>); the console dev harness fills it from <c>--mic</c>/<c>--cable</c> args
/// (<c>Program.cs</c>). The setup wizard runs environment checks and calibration but does not feed
/// device names back into a running host — there is no path for that yet. The alarm always uses
/// the system default output, so it is not configurable here.
/// </summary>
public sealed record WasapiAudioOptions
{
    /// <summary>The microphone to capture. Null means the default communications mic.</summary>
    public string? MicName { get; init; }

    /// <summary>The render device Chrome reads as its mic, matched by name substring.</summary>
    public string CableName { get; init; } = "CABLE Input";
}
