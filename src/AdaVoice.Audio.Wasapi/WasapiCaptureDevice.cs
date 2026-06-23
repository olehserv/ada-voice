using AdaVoice.Audio.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

// NAudio also has a DeviceState type; use ours for the seam state.
using DeviceState = AdaVoice.Audio.Abstractions.DeviceState;

namespace AdaVoice.Audio.Wasapi;

/// <summary>
/// Production capture seam. Wraps NAudio's <see cref="WasapiCapture"/> so the audio core
/// can use a real microphone through <see cref="IAudioCaptureDevice"/>. This layer is the
/// only place that touches WASAPI; the core never sees these types.
/// </summary>
public sealed class WasapiCaptureDevice : IAudioCaptureDevice
{
    private readonly WasapiCapture _capture;
    private readonly MMDevice _device;

    public WasapiCaptureDevice(MMDevice device, int latencyMs = 20)
    {
        _device = device;
        _capture = new WasapiCapture(device, useEventSync: true, audioBufferMillisecondsLength: latencyMs);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
    }

    public WaveFormat Format => _capture.WaveFormat;
    public DeviceState State { get; private set; } = DeviceState.Stopped;

    public event EventHandler<CaptureBufferEventArgs>? DataAvailable;
    public event EventHandler<DeviceStateChangedEventArgs>? StateChanged;

    public void Start()
    {
        _capture.StartRecording();
        SetState(DeviceState.Running);
    }

    public void Stop() => _capture.StopRecording();

    private void OnDataAvailable(object? sender, WaveInEventArgs e) =>
        DataAvailable?.Invoke(this, new CaptureBufferEventArgs(e.Buffer, e.BytesRecorded));

    private void OnRecordingStopped(object? sender, StoppedEventArgs e) =>
        // An exception here means the device was lost or the driver failed.
        SetState(e.Exception is null ? DeviceState.Stopped : DeviceState.Faulted, e.Exception);

    public void Dispose()
    {
        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.Dispose();

        // The seam owns the MMDevice it was handed (the factory resolves a fresh one per rebuild),
        // so release it here to avoid a slow COM leak.
        _device.Dispose();
    }

    private void SetState(DeviceState state, Exception? error = null)
    {
        State = state;
        StateChanged?.Invoke(this, new DeviceStateChangedEventArgs(state, error));
    }
}
