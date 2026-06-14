namespace AdaVoice.Audio.Engine;

/// <summary>
/// Engine lifecycle states (design 06 §2). The cardinal rule: the engine must never be
/// silently dead — every state where the mic is not being forwarded is loudly surfaced.
/// </summary>
public enum EngineState
{
    /// <summary>Not running.</summary>
    Stopped,

    /// <summary>Forwarding mic to the cable; phrases can play.</summary>
    Live,

    /// <summary>Recorder open — cable output paused so a take never reaches a call.</summary>
    OffAir,

    /// <summary>A stream failed or a device was lost — alarm raised, rebuild in progress.</summary>
    Degraded,
}
