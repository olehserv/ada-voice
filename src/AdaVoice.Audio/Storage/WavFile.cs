using NAudio.Wave;

namespace AdaVoice.Audio.Storage;

/// <summary>
/// Saves recorded mono samples as a 16-bit PCM / 48 kHz WAV file (design 06 §3 / 04 §2). Writes to a
/// temp file first and then atomically moves it into place, so a crash mid-write can never leave a
/// half-written final file. The take is saved raw — per-phrase loudness gain is metadata applied at
/// playback, not baked in here.
/// </summary>
public static class WavFile
{
    /// <summary>Read a WAV file into engine-format float samples (the saved files are 48 kHz mono).</summary>
    public static float[] Load(string path)
    {
        using var reader = new WaveFileReader(path);
        var provider = reader.ToSampleProvider();
        var all = new List<float>();
        var buffer = new float[4096];
        int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0)
            all.AddRange(buffer.AsSpan(0, n));

        return [.. all];
    }

    public static void Save(string path, float[] samples)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory); // e.g. the audio/ folder on first save

        var tmp = path + ".tmp";
        try
        {
            using (var writer = new WaveFileWriter(tmp, new WaveFormat(AudioFormats.SampleRate, bits: 16, channels: 1)))
            {
                foreach (var s in samples)
                    writer.WriteSample(s);
            }

            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup; the original failure is what matters.
        }
    }
}
