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
    public static void Save(string path, float[] samples)
    {
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
