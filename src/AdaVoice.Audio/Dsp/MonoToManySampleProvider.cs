using NAudio.Wave;

namespace AdaVoice.Audio.Dsp;

/// <summary>
/// Up-mixes mono to any channel count by replicating the sample across all channels. NAudio
/// only ships mono→stereo; the DEGRADED alarm plays to the system default output, which can be
/// a 6/8-channel surround device — without this adapter the alarm could never sound there.
/// </summary>
internal sealed class MonoToManySampleProvider(ISampleProvider source, int targetChannels) : ISampleProvider
{
    private float[] _monoBuffer = [];

    public WaveFormat WaveFormat { get; } =
        WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, targetChannels);

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = WaveFormat.Channels;
        var frames = count / channels;
        if (_monoBuffer.Length < frames)
            _monoBuffer = new float[frames];

        var readFrames = source.Read(_monoBuffer, 0, frames);
        for (var frame = 0; frame < readFrames; frame++)
            for (var channel = 0; channel < channels; channel++)
                buffer[offset + frame * channels + channel] = _monoBuffer[frame];

        return readFrames * channels;
    }
}
