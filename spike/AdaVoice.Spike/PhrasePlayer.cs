using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Spike;

/// <summary>
/// One playing phrase: reads from a pre-decoded float array (48 kHz mono),
/// supports stop with a 10 ms linear fade-out (design 06 §1, hot-path).
/// MixingSampleProvider removes the input when Read returns less than asked.
/// </summary>
public class PhraseSampleProvider : ISampleProvider
{
    private const int FadeSamples = 480; // 10 ms @ 48 kHz

    private readonly float[] _data;
    private int _position;
    private volatile bool _stopRequested;
    private int _fadeRemaining = -1;
    private long _triggerTimestamp;
    private bool _firstReadReported;

    public PhraseSampleProvider(float[] data, string name)
    {
        _data = data;
        Name = name;
        _triggerTimestamp = Stopwatch.GetTimestamp();
    }

    public string Name { get; }

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

    public void Stop() => _stopRequested = true;

    public int Read(float[] buffer, int offset, int count)
    {
        if (!_firstReadReported)
        {
            _firstReadReported = true;
            var ms = (Stopwatch.GetTimestamp() - _triggerTimestamp) * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"  [latency] trigger -> first mixer read: {ms:F1} ms (app-side, excludes render buffer + cable)");
        }

        if (_stopRequested && _fadeRemaining < 0)
            _fadeRemaining = FadeSamples;

        var available = _data.Length - _position;
        if (_fadeRemaining >= 0)
            available = Math.Min(available, _fadeRemaining);
        var toCopy = Math.Min(count, available);

        for (var i = 0; i < toCopy; i++)
        {
            var sample = _data[_position + i];
            if (_fadeRemaining >= 0)
                sample *= (_fadeRemaining - i) / (float)FadeSamples;
            buffer[offset + i] = sample;
        }

        _position += toCopy;
        if (_fadeRemaining >= 0)
            _fadeRemaining -= toCopy;

        return toCopy; // < count signals end-of-input to the mixer
    }
}

/// <summary>
/// Loads WAV files and pre-decodes them to 48 kHz mono float arrays in RAM
/// (production does this on a background thread at startup; the spike loads
/// synchronously). Generates test tones if the phrase directory is empty.
/// </summary>
public static class PhraseCache
{
    public static List<(string Name, float[] Data)> Load(string dir)
    {
        Directory.CreateDirectory(dir);
        var files = Directory.GetFiles(dir, "*.wav").OrderBy(f => f).ToList();
        if (files.Count == 0)
        {
            Console.WriteLine($"No WAVs in {dir} — generating test signals (replace with real recorded phrases for the AGC test).");
            GenerateTestSignals(dir);
            files = Directory.GetFiles(dir, "*.wav").OrderBy(f => f).ToList();
        }

        var phrases = new List<(string, float[])>();
        foreach (var file in files.Take(9))
        {
            using var reader = new AudioFileReader(file);
            ISampleProvider sp = reader;
            if (sp.WaveFormat.Channels == 2)
                sp = sp.ToMono(0.5f, 0.5f);
            if (sp.WaveFormat.SampleRate != 48000)
                sp = new WdlResamplingSampleProvider(sp, 48000);

            var samples = new List<float>();
            var buffer = new float[48000];
            int read;
            while ((read = sp.Read(buffer, 0, buffer.Length)) > 0)
                samples.AddRange(buffer.Take(read));
            phrases.Add((Path.GetFileName(file), samples.ToArray()));
        }
        return phrases;
    }

    private static void GenerateTestSignals(string dir)
    {
        const int rate = 48000;

        // 1: 440 Hz tone, 1 s — basic audibility / level check
        WriteWav(Path.Combine(dir, "1-tone-440hz.wav"),
            Synth(rate, 1.0, t => 0.5 * Math.Sin(2 * Math.PI * 440 * t)));

        // 2: 300->3000 Hz sweep, 2 s — speech-band frequency response through AGC/NS
        WriteWav(Path.Combine(dir, "2-sweep-300-3000hz.wav"),
            Synth(rate, 2.0, t =>
            {
                var f = 300 * Math.Pow(10, t / 2.0); // log sweep over 1 decade
                return 0.5 * Math.Sin(2 * Math.PI * f * t);
            }));

        // 3: amplitude-modulated bursts, 2 s — exposes AGC pumping between bursts
        WriteWav(Path.Combine(dir, "3-bursts-am.wav"),
            Synth(rate, 2.0, t =>
            {
                var envelope = (Math.Sin(2 * Math.PI * 3 * t) > 0) ? 1.0 : 0.0;
                return 0.5 * envelope * Math.Sin(2 * Math.PI * 800 * t);
            }));
    }

    private static float[] Synth(int rate, double seconds, Func<double, double> f)
    {
        var data = new float[(int)(rate * seconds)];
        for (var i = 0; i < data.Length; i++)
            data[i] = (float)f((double)i / rate);
        return data;
    }

    private static void WriteWav(string path, float[] data)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));
        writer.WriteSamples(data, 0, data.Length);
    }
}
