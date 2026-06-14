using System.Runtime.InteropServices;

namespace AdaVoice.Audio.Wasapi.Interop;

/// <summary>
/// Opts our render session out of Windows communications ducking, using
/// IAudioSessionControl2::SetDuckingPreference. NAudio does not wrap this call
/// (design 06 §1). It must be called AFTER the stream has started, because the
/// preference takes effect when the stream starts.
/// </summary>
/// <remarks>
/// Without this, Windows lowers our cable audio the moment Chrome opens a call. Ported
/// from the Phase 0 spike, which is where this COM shim was first proven.
/// </remarks>
internal static class DuckingOptOut
{
    public static void Apply(string renderDeviceId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        Marshal.ThrowExceptionForHR(enumerator.GetDevice(renderDeviceId, out var device));

        var iidSessionManager = typeof(IAudioSessionManager).GUID;
        const int CLSCTX_ALL = 0x17;
        Marshal.ThrowExceptionForHR(device.Activate(ref iidSessionManager, CLSCTX_ALL, IntPtr.Zero, out var managerObj));
        var manager = (IAudioSessionManager)managerObj;

        // A null/empty session GUID means the default session for this process on this
        // device — which is where NAudio's WasapiOut renders.
        var sessionGuid = Guid.Empty;
        Marshal.ThrowExceptionForHR(manager.GetAudioSessionControl(ref sessionGuid, 0, out var control));
        Marshal.ThrowExceptionForHR(control.SetDuckingPreference(true));
    }
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager
{
    // Declared to return IAudioSessionControl2: the marshaler asks the returned
    // IAudioSessionControl* for the v2 interface, which WASAPI sessions implement.
    int GetAudioSessionControl(ref Guid sessionGuid, int streamFlags, out IAudioSessionControl2 sessionControl);
    int GetSimpleAudioVolume(ref Guid sessionGuid, int streamFlags, out IntPtr audioVolume);
}

[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl members first (the vtable order matters).
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    int GetGroupingParam(out Guid param);
    int SetGroupingParam(ref Guid param, ref Guid eventContext);
    int RegisterAudioSessionNotification(IntPtr client);
    int UnregisterAudioSessionNotification(IntPtr client);
    // IAudioSessionControl2 members.
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetProcessId(out uint pid);
    int IsSystemSoundsSession();
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}
