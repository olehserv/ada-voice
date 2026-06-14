namespace AdaVoice.Audio.Engine;

/// <summary>
/// The states of the audio engine (design 06 §2). One important rule: the engine must
/// never stop in a quiet way. If the mic is not being sent forward, the app must show
/// this clearly to the user.
/// </summary>
public enum EngineState
{
    /// <summary>Not running.</summary>
    Stopped,

    /// <summary>The mic is sent to the cable. Phrases can play.</summary>
    Live,

    /// <summary>The recorder is open. The cable output is paused, so a recording never goes into a call.</summary>
    OffAir,

    /// <summary>A stream failed or a device was lost. The app shows an alarm and tries to rebuild the stream.</summary>
    Degraded,
}
