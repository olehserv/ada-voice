namespace AdaVoice.App.Services;

/// <summary>
/// Owns the stop-hotkey policy: try Pause, then Ctrl+F12, and keep the first the OS accepts. Raises
/// <see cref="StopRequested"/> when the registered hotkey is pressed. Knows nothing about phrases —
/// the window wires StopRequested to the stop action.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    // MOD_NOREPEAT keeps a held key from firing repeatedly; VK_PAUSE = 0x13, VK_F12 = 0x7B.
    private const uint ModNoRepeat = 0x4000;
    private const uint ModControl = 0x0002;

    private static readonly HotkeyDef[] Candidates =
    [
        new(ModNoRepeat, 0x13, "Pause"),
        new(ModControl | ModNoRepeat, 0x7B, "Ctrl+F12"),
    ];

    private readonly IHotkeyRegistrar _registrar;

    public HotkeyService(IHotkeyRegistrar registrar)
    {
        _registrar = registrar;
        _registrar.Pressed += (_, _) => StopRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The label of the hotkey currently registered, or null if none could be.</summary>
    public string? ActiveHotkey { get; private set; }

    /// <summary>Raised when the registered hotkey is pressed.</summary>
    public event EventHandler? StopRequested;

    /// <summary>Try each candidate in order; keep the first the OS accepts. False if all are taken.</summary>
    public bool Register()
    {
        foreach (var def in Candidates)
        {
            if (_registrar.TryRegister(def))
            {
                ActiveHotkey = def.Display;
                return true;
            }
        }

        ActiveHotkey = null;
        return false;
    }

    public void Dispose() => _registrar.Dispose();
}
