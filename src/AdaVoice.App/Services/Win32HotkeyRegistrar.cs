using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AdaVoice.App.Services;

/// <summary>
/// Real <see cref="IHotkeyRegistrar"/>: registers a system-wide hotkey on the window's HWND via Win32
/// RegisterHotKey and raises <see cref="Pressed"/> from the WM_HOTKEY message. Re-registering replaces
/// the previous key. Dispose unregisters and removes the message hook.
/// </summary>
public sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
{
    private const int HotkeyId = 0xADA;     // any app-unique id
    private const int WmHotkey = 0x0312;

    private readonly HwndSource _source;
    private bool _registered;

    public Win32HotkeyRegistrar(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd)
            ?? throw new InvalidOperationException("No HwndSource for the window handle.");
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    public bool TryRegister(HotkeyDef def)
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _registered = RegisterHotKey(_source.Handle, HotkeyId, def.Modifiers, def.VirtualKey);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
            UnregisterHotKey(_source.Handle, HotkeyId);
        _source.RemoveHook(WndProc);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
