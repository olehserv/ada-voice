using AdaVoice.Audio.Engine;

namespace AdaVoice.Audio.Tests;

/// <summary>
/// Placeholder until the first real slice (live path) lands. Exists so CI has a green
/// suite from day one and to prove the test project references the core correctly.
/// </summary>
public class ScaffoldTests
{
    [Fact]
    public void EngineState_has_the_four_states_from_design_06()
    {
        Assert.Equal(
            [EngineState.Stopped, EngineState.Live, EngineState.OffAir, EngineState.Degraded],
            Enum.GetValues<EngineState>());
    }
}
