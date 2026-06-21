using AdaVoice.Audio.Abstractions;
using AdaVoice.Audio.Dsp;
using AdaVoice.Audio.Wasapi.Interop;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// NAudio also has a DeviceState type; use ours for the seam state.
using DeviceState = AdaVoice.Audio.Abstractions.DeviceState;

namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Production render seam. Wraps NAudio's <see cref="WasapiOut"/> so the audio core can
/// send audio to a real device (the cable or a headphone monitor) through
/// <see cref="IAudioRenderDevice"/>. After the stream starts, it opts the session out of
/// Windows communications ducking (design 06 §1).
/// </summary>
/// <remarks>
/// This is where the render seam meets real WASAPI. <see cref="WasapiOut"/> pulls from the
/// source on its own render thread. This push/pull point was validated on real hardware in
/// Phase 1 step 4 (live mic-to-cable passthrough with ducking).
/// </remarks>
public sealed class WasapiRenderDevice : IAudioRenderDevice
{
    private readonly MMDevice _device;
    private readonly bool _optOutOfDucking;
    private readonly int _latencyMs;
    private WasapiOut? _output;

    public WasapiRenderDevice(MMDevice device, bool optOutOfDucking = true, int latencyMs = 20)
    {
        _device = device;
        _optOutOfDucking = optOutOfDucking;
        _latencyMs = latencyMs;
        Format = device.AudioClient.MixFormat;
    }

    /// <summary>The device's own format. The source is matched to this before it is sent.</summary>
    public WaveFormat Format { get; }

    public DeviceState State { get; private set; } = DeviceState.Stopped;

    /// <summary>
    /// Set if the ducking opt-out failed. This is not fatal: the fallback is the Windows
    /// "Communications" sound setting ("Do nothing").
    /// </summary>
    public Exception? DuckingOptOutError { get; private set; }

    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public void Init(ISampleProvider source)
    {
        if (source.WaveFormat.SampleRate != Format.SampleRate)
            throw new NotSupportedException(
                $"Source is {source.WaveFormat.SampleRate} Hz but the device is {Format.SampleRate} Hz. " +
                "Set the cable to 48 kHz (a wizard check later).");

        var matched = ChannelAdapter.Match(source, Format.Channels);

        _output = new WasapiOut(_device, AudioClientShareMode.Shared, useEventSync: true, _latencyMs);
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(matched.ToWaveProvider());
    }

    public void Start()
    {
        if (_output is null)
            throw new InvalidOperationException("Call Init before Start.");

        _output.Play();
        SetState(DeviceState.Running);

        // Apply AFTER Play: the ducking preference takes effect on stream start.
        if (_optOutOfDucking)
        {
            try { DuckingOptOut.Apply(_device.ID); }
            catch (Exception ex) { DuckingOptOutError = ex; }
        }
    }

    public void Stop() => _output?.Stop();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) =>
        SetState(e.Exception is null ? DeviceState.Stopped : DeviceState.Faulted, e.Exception);

    public void Dispose()
    {
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Dispose();
        }

        // The seam owns the MMDevice it was handed: the factory resolves a fresh one on every
        // rebuild, so this must be released or a flapping device slowly leaks COM objects.
        _device.Dispose();
    }

    private void SetState(DeviceState state, Exception? error = null)
    {
        State = state;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(state, error));
    }
}
