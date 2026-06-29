using AdaVoice.App.Services;

namespace AdaVoice.App.Tests;

/// <summary>Test double: records which defs were attempted, fails the ones named in FailFor, and can
/// simulate a key press.</summary>
internal sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
{
    public List<string> Attempts { get; } = [];
    public HashSet<string> FailFor { get; } = [];

    public event EventHandler? Pressed;

    public bool TryRegister(HotkeyDef def)
    {
        Attempts.Add(def.Display);
        return !FailFor.Contains(def.Display);
    }

    public void SimulatePress() => Pressed?.Invoke(this, EventArgs.Empty);

    public void Dispose() { }
}
