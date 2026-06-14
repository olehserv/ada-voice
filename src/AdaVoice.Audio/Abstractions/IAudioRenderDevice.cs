using NAudio.Wave;

namespace AdaVoice.Audio.Abstractions;

/// <summary>
/// Seam over a render endpoint — CABLE Input / headphone monitor in production, an
/// in-memory collector in tests. The engine hands the device the mixed sample source;
/// the device pulls from it on its own clock and writes to hardware.
/// </summary>
/// <remarks>
/// PROVISIONAL contract. Design 08 §1 sketched the render seam as <c>int Read(float[])</c>.
/// This <see cref="Init"/>-a-source shape maps directly onto <c>WasapiOut.Init(ISampleProvider)</c>
/// and the spike. The exact pull contract is validated against real WASAPI in Phase 1
/// step 4 — before the engine is built on top of it — so this may still change.
/// </remarks>
public interface IAudioRenderDevice : IDisposable
{
    WaveFormat Format { get; }

    DeviceState State { get; }

    event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    /// <summary>Supply the sample source the device renders. Call before <see cref="Start"/>.</summary>
    void Init(ISampleProvider source);

    void Start();
    void Stop();
}
