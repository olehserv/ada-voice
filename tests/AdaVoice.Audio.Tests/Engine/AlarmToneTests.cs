using AdaVoice.Audio.Engine;
using AdaVoice.Audio.Tests.Fakes;

namespace AdaVoice.Audio.Tests.Engine;

public class AlarmToneTests
{
    [Fact]
    public void Produces_a_repeating_non_silent_signal()
    {
        var tone = new AlarmTone(TestAudio.EngineFormat);

        var buffer = new float[48_000]; // 1 second
        var read = tone.Read(buffer, 0, buffer.Length);

        Assert.Equal(buffer.Length, read);                 // never ends
        Assert.Contains(buffer, s => Math.Abs(s) > 0.1f);  // is audible
        Assert.Contains(buffer, s => s == 0f);             // beeps (has gaps), not a flat tone
    }
}
