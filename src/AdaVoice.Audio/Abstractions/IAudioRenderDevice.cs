using NAudio.Wave;

namespace AdaVoice.Audio.Abstractions;

/// <summary>
/// One output for audio. In the real app this is CABLE Input or the headphone monitor.
/// In tests it can be an object that stores the audio in memory.
/// The engine gives the device the mixed audio source. The device reads from this
/// source at its own speed and sends it to the hardware.
/// </summary>
/// <remarks>
/// This contract was validated against real WASAPI in Phase 1, step 4. Design 08 §1 first
/// described the output seam as <c>int Read(float[])</c>. We use an <see cref="Init"/>
/// method that takes a source instead, because it matches <c>WasapiOut.Init(...)</c> and
/// the spike. A real mic-to-cable passthrough through this seam worked, so the engine can
/// rely on it.
/// </remarks>
public interface IAudioRenderDevice : IDisposable
{
    WaveFormat Format { get; }

    DeviceState State { get; }

    event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    /// <summary>Set the audio source that the device will play. Call this before <see cref="Start"/>.</summary>
    void Init(ISampleProvider source);

    void Start();
    void Stop();
}
