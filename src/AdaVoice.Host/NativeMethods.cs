using System.Runtime.InteropServices;

namespace AdaVoice.Host;

public static partial class NativeMethods
{
    /// <summary>
    /// Ask Windows to relaunch this process after a crash or hang (design 03 — the mic-forwarding
    /// process must not stay dead). A null command line means "relaunch with the same arguments".
    /// Public so every host (console and WPF) registers the same way.
    /// </summary>
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegisterApplicationRestart(string? pwzCommandline, int dwFlags);
}
