namespace AdaVoice.Audio.Engine;

/// <summary>Which device a factory request or fault refers to.</summary>
public enum DeviceRole
{
    /// <summary>The hardware microphone (capture).</summary>
    Mic,

    /// <summary>The virtual cable input (render) Chrome uses as its mic.</summary>
    Cable,

    /// <summary>The system default output, used only to sound the DEGRADED alarm.</summary>
    Alarm,
}
