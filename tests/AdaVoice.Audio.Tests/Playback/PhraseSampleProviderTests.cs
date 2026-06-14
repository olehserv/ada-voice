using AdaVoice.Audio.Playback;
using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests.Playback;

public class PhraseSampleProviderTests
{
    [Fact]
    public void Plays_all_samples_then_returns_zero()
    {
        var phrase = new PhraseSampleProvider([0.1f, 0.2f, 0.3f], TestAudio.EngineFormat, "a");

        var buffer = new float[8];
        var read = phrase.Read(buffer, 0, 8);

        Assert.Equal(3, read);
        Assert.Equal([0.1f, 0.2f, 0.3f], buffer[..3]);
        Assert.Equal(0, phrase.Read(buffer, 0, 8));
        Assert.True(phrase.IsFinished);
    }

    [Fact]
    public void Stop_fades_out_smoothly_then_ends()
    {
        var data = Enumerable.Repeat(1f, 48_000).ToArray();
        var phrase = new PhraseSampleProvider(data, TestAudio.EngineFormat, "a");
        phrase.Read(new float[100], 0, 100); // play at full for a moment

        phrase.Stop(fadeMs: 10);
        var fadeSamples = 10 * TestAudio.SampleRate / 1000; // 480
        var fade = new float[fadeSamples];
        var read = phrase.Read(fade, 0, fadeSamples);

        Assert.Equal(fadeSamples, read);
        Assert.Equal(1f, fade[0], 3);                      // starts at full: no jump
        Assert.True(fade[^1] < 0.01f);                     // ends near zero
        for (var i = 1; i < fade.Length; i++)
        {
            Assert.True(fade[i] <= fade[i - 1]);           // never rises
            Assert.True(fade[i - 1] - fade[i] <= 0.0025f); // small steps: no click
        }

        Assert.Equal(0, phrase.Read(fade, 0, fadeSamples));
        Assert.True(phrase.IsFinished);
    }
}
