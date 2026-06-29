namespace AdaVoice.App.Services;

/// <summary>OS seam for a single global hotkey. The real impl wraps Win32 RegisterHotKey; a fake
/// drives tests. Registering again replaces any previous registration.</summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>Try to claim the hotkey system-wide. Returns false if the OS rejects it (conflict).</summary>
    bool TryRegister(HotkeyDef def);

    /// <summary>Raised when the currently-registered hotkey is pressed.</summary>
    event EventHandler? Pressed;
}
