using AdaVoice.Audio.Abstractions;

namespace AdaVoice.Audio.Engine;

/// <summary>
/// Creates audio devices on demand so the engine can rebuild a stream after a failure
/// without referencing WASAPI. The real implementation lives in the Wasapi project; tests
/// provide a fake. Throws <see cref="AudioDeviceException"/> on failure.
/// </summary>
public interface IAudioDeviceFactory
{
    IAudioCaptureDevice CreateCapture(DeviceRole role);
    IAudioRenderDevice CreateRender(DeviceRole role);
}
