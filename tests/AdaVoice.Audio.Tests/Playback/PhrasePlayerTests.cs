using AdaVoice.Audio.Playback;
using AdaVoice.Audio.Tests.Fakes;
using NAudio.Wave.SampleProviders;

namespace AdaVoice.Audio.Tests.Playback;

public class PhrasePlayerTests
{
    private static MixingSampleProvider NewMixer() =>
        new(TestAudio.EngineFormat) { ReadFully = true };

    [Fact]
    public void Phrase_audio_is_mixed_into_the_output()
    {
        var samples = TestAudio.Sine(440, sampleCount: 300);
        var mixer = NewMixer();
        using var player = new PhrasePlayer(mixer, new DuckSpy());

        player.Play(new Phrase("a", samples));
        var buffer = new float[300];
        var read = mixer.Read(buffer, 0, 300);

        Assert.Equal(300, read);
        AssertClose(samples, buffer);
    }

    [Fact]
    public void Playing_ducks_the_mic_and_un_ducks_when_the_phrase_ends()
    {
        var spy = new DuckSpy();
        var mixer = NewMixer();
        using var player = new PhrasePlayer(mixer, spy);

        player.Play(new Phrase("a", TestAudio.Sine(440, 300)));
        Assert.True(spy.LastGain < 1f);            // ducked on play
        Assert.Equal("a", player.ActivePhraseId);

        DrainUntilDone(mixer, player);

        Assert.Null(player.ActivePhraseId);
        Assert.Equal(1f, spy.LastGain);            // un-ducked on end
    }

    [Fact]
    public void New_trigger_replaces_the_current_phrase()
    {
        var spy = new DuckSpy();
        var mixer = NewMixer();
        using var player = new PhrasePlayer(mixer, spy);

        player.Play(new Phrase("a", Enumerable.Repeat(0.5f, 48_000).ToArray())); // long
        player.Play(new Phrase("b", TestAudio.Sine(440, 300)));

        Assert.Equal("b", player.ActivePhraseId);
        Assert.DoesNotContain(spy.Calls, c => c.Gain == 1f); // stayed ducked across the swap

        DrainUntilDone(mixer, player);
        Assert.Null(player.ActivePhraseId);
        Assert.Equal(1f, spy.LastGain);
    }

    [Fact]
    public void Ignore_mode_keeps_the_first_phrase()
    {
        var spy = new DuckSpy();
        var mixer = NewMixer();
        using var player = new PhrasePlayer(mixer, spy,
            new PhrasePlayerOptions { ReplaceOnRetrigger = false });

        player.Play(new Phrase("a", Enumerable.Repeat(0.5f, 48_000).ToArray()));
        player.Play(new Phrase("b", TestAudio.Sine(440, 300)));

        Assert.Equal("a", player.ActivePhraseId);
        Assert.Single(spy.Calls); // only the first play ducked
    }

    [Fact]
    public void Stop_fades_out_and_un_ducks()
    {
        var spy = new DuckSpy();
        var mixer = NewMixer();
        using var player = new PhrasePlayer(mixer, spy);
        player.Play(new Phrase("a", Enumerable.Repeat(1f, 48_000).ToArray()));

        player.Stop();
        DrainUntilDone(mixer, player);

        Assert.Null(player.ActivePhraseId);
        Assert.Equal(1f, spy.LastGain);
    }

    private static void DrainUntilDone(MixingSampleProvider mixer, PhrasePlayer player, int maxReads = 4000)
    {
        var buffer = new float[480];
        for (var i = 0; i < maxReads && player.ActivePhraseId is not null; i++)
            mixer.Read(buffer, 0, buffer.Length);
    }

    private static void AssertClose(float[] expected, float[] actual, float tolerance = 1e-6f)
    {
        Assert.Equal(expected.Length, actual.Length);
        for (var i = 0; i < expected.Length; i++)
            Assert.True(Math.Abs(expected[i] - actual[i]) <= tolerance, $"sample {i}");
    }
}
