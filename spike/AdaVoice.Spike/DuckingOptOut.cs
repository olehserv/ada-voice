using System.Runtime.InteropServices;

namespace AdaVoice.Spike;

/// <summary>
/// Opts our render session out of Windows communications ducking via
/// IAudioSessionControl2::SetDuckingPreference. NAudio does not wrap this
/// (design 06 §1). Must be called AFTER the stream has started — the
/// preference takes effect on stream (re)start.
/// </summary>
public static class DuckingOptOut
{
    public static void Apply(string renderDeviceId)
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        Marshal.ThrowExceptionForHR(enumerator.GetDevice(renderDeviceId, out var device));

        var iidSessionManager = typeof(IAudioSessionManager).GUID;
        const int CLSCTX_ALL = 0x17;
        Marshal.ThrowExceptionForHR(device.Activate(ref iidSessionManager, CLSCTX_ALL, IntPtr.Zero, out var managerObj));
        var manager = (IAudioSessionManager)managerObj;

        // Session GUID null/empty = the default session for the calling process
        // on this device — which is where NAudio's WasapiOut renders.
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
    // Declared as IAudioSessionControl2: the marshaler QIs the returned
    // IAudioSessionControl* for the v2 interface, which WASAPI sessions implement.
    int GetAudioSessionControl(ref Guid sessionGuid, int streamFlags, out IAudioSessionControl2 sessionControl);
    int GetSimpleAudioVolume(ref Guid sessionGuid, int streamFlags, out IntPtr audioVolume);
}

[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl (vtable order matters)
    int GetState(out int state);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    int GetGroupingParam(out Guid param);
    int SetGroupingParam(ref Guid param, ref Guid eventContext);
    int RegisterAudioSessionNotification(IntPtr client);
    int UnregisterAudioSessionNotification(IntPtr client);
    // IAudioSessionControl2
    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    int GetProcessId(out uint pid);
    int IsSystemSoundsSession();
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}
