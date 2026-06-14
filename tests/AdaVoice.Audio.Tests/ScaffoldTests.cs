using AdaVoice.Audio.Engine;

namespace AdaVoice.Audio.Tests;

/// <summary>
/// A simple test used until the first real feature (the live path) is added. It keeps
/// CI green from the start. It also proves that the test project can use the core project.
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
