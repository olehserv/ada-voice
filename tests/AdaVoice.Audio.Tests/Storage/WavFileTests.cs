using AdaVoice.Audio;
using AdaVoice.Audio.Storage;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave;

namespace AdaVoice.Audio.Tests.Storage;

public class WavFileTests
{
    [Fact]
    public void Save_then_read_roundtrips_the_samples()
    {
        var samples = TestAudio.Sine(440, 4800, amplitude: 0.5);
        var path = TempWavPath();
        try
        {
            WavFile.Save(path, samples);

            var read = ReadAll(path);
            Assert.Equal(samples.Length, read.Length);
            for (var i = 0; i < samples.Length; i++)
                Assert.True(Math.Abs(samples[i] - read[i]) < 1e-3, $"sample {i}");
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void Saved_file_is_48k_16bit_mono()
    {
        var path = TempWavPath();
        try
        {
            WavFile.Save(path, TestAudio.Sine(440, 480, 0.5));

            using var reader = new WaveFileReader(path);
            Assert.Equal(AudioFormats.SampleRate, reader.WaveFormat.SampleRate);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
            Assert.Equal(1, reader.WaveFormat.Channels);
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void No_temp_file_is_left_behind_after_a_successful_save()
    {
        var path = TempWavPath();
        try
        {
            WavFile.Save(path, TestAudio.Sine(440, 480, 0.5));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { Cleanup(path); }
    }

    [Fact]
    public void A_failed_save_leaves_no_final_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "adavoice-no-such-dir-" + Guid.NewGuid().ToString("N"), "x.wav");

        Assert.ThrowsAny<Exception>(() => WavFile.Save(path, TestAudio.Sine(440, 480, 0.5)));
        Assert.False(File.Exists(path));
    }

    private static string TempWavPath() =>
        Path.Combine(Path.GetTempPath(), "adavoice-" + Guid.NewGuid().ToString("N") + ".wav");

    private static float[] ReadAll(string path)
    {
        using var reader = new WaveFileReader(path);
        var provider = reader.ToSampleProvider();
        var all = new List<float>();
        var buffer = new float[4096];
        int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0)
            all.AddRange(buffer[..n]);
        return all.ToArray();
    }

    private static void Cleanup(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
    }
}
