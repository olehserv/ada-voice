namespace AdaVoice.App.Services;

/// <summary>A registerable global hotkey: Win32 modifier flags + virtual-key code, plus a label
/// for logging/UI (e.g. "Pause", "Ctrl+F12").</summary>
public sealed record HotkeyDef(uint Modifiers, uint VirtualKey, string Display);
