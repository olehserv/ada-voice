using AdaVoice.Audio.Setup;

namespace AdaVoice.Audio.Tests.Setup;

public class EnvironmentChecksTests
{
    private sealed class FakeProbe(AudioEndpointInfo[] outputs, AudioEndpointInfo[] inputs) : IEnvironmentProbe
    {
        public IReadOnlyList<AudioEndpointInfo> Outputs() => outputs;
        public IReadOnlyList<AudioEndpointInfo> Inputs() => inputs;
    }

    private static CheckStatus StatusOf(IReadOnlyList<EnvironmentCheck> checks, EnvironmentCheckKind kind) =>
        checks.First(c => c.Kind == kind).Status;

    private static readonly AudioEndpointInfo Mic = new("Microphone", 48_000, IsDefault: true);

    [Fact]
    public void A_correct_setup_passes_every_check()
    {
        var outputs = new[]
        {
            new AudioEndpointInfo("Speakers", 48_000, IsDefault: true),
            new AudioEndpointInfo("CABLE Input (VB-Audio)", 48_000, IsDefault: false),
        };

        var checks = new EnvironmentChecks(new FakeProbe(outputs, [Mic])).Run("CABLE Input", micName: null);

        Assert.All(checks, c => Assert.Equal(CheckStatus.Pass, c.Status));
    }

    [Fact]
    public void A_missing_cable_fails_the_presence_and_rate_checks()
    {
        var outputs = new[] { new AudioEndpointInfo("Speakers", 48_000, IsDefault: true) };

        var checks = new EnvironmentChecks(new FakeProbe(outputs, [Mic])).Run("CABLE Input", null);

        Assert.Equal(CheckStatus.Fail, StatusOf(checks, EnvironmentCheckKind.CableOutput));
        Assert.Equal(CheckStatus.Fail, StatusOf(checks, EnvironmentCheckKind.CableSampleRate));
    }

    [Fact]
    public void A_cable_at_the_wrong_rate_fails_only_the_rate_check()
    {
        var outputs = new[]
        {
            new AudioEndpointInfo("Speakers", 48_000, IsDefault: true),
            new AudioEndpointInfo("CABLE Input", 44_100, IsDefault: false),
        };

        var checks = new EnvironmentChecks(new FakeProbe(outputs, [Mic])).Run("CABLE Input", null);

        Assert.Equal(CheckStatus.Pass, StatusOf(checks, EnvironmentCheckKind.CableOutput));
        Assert.Equal(CheckStatus.Fail, StatusOf(checks, EnvironmentCheckKind.CableSampleRate));
    }

    [Fact]
    public void The_default_output_being_the_cable_fails()
    {
        var outputs = new[] { new AudioEndpointInfo("CABLE Input", 48_000, IsDefault: true) };

        var checks = new EnvironmentChecks(new FakeProbe(outputs, [Mic])).Run("CABLE Input", null);

        Assert.Equal(CheckStatus.Fail, StatusOf(checks, EnvironmentCheckKind.DefaultOutput));
    }

    [Fact]
    public void A_missing_named_mic_fails_the_mic_check()
    {
        var outputs = new[]
        {
            new AudioEndpointInfo("Speakers", 48_000, IsDefault: true),
            new AudioEndpointInfo("CABLE Input", 48_000, IsDefault: false),
        };
        var inputs = new[] { new AudioEndpointInfo("Built-in mic", 48_000, IsDefault: true) };

        var checks = new EnvironmentChecks(new FakeProbe(outputs, inputs)).Run("CABLE Input", micName: "Yeti");

        Assert.Equal(CheckStatus.Fail, StatusOf(checks, EnvironmentCheckKind.Microphone));
    }
}
