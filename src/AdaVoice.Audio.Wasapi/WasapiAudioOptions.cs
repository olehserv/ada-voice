namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Which physical devices the engine should use. Kept deliberately small for now; the future
/// setup wizard will populate it. The alarm always uses the system default output, so it is not
/// configurable here.
/// </summary>
public sealed record WasapiAudioOptions
{
    /// <summary>The microphone to capture. Null means the default communications mic.</summary>
    public string? MicName { get; init; }

    /// <summary>The render device Chrome reads as its mic, matched by name substring.</summary>
    public string CableName { get; init; } = "CABLE Input";
}
