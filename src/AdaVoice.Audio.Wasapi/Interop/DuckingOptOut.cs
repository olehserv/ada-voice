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
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        object? managerObj = null;
        IAudioSessionControl2? control = null;

        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(renderDeviceId, out device));

            var iidSessionManager = typeof(IAudioSessionManager).GUID;
            const int CLSCTX_ALL = 0x17;
            Marshal.ThrowExceptionForHR(device.Activate(ref iidSessionManager, CLSCTX_ALL, IntPtr.Zero, out managerObj));
            var manager = (IAudioSessionManager)managerObj;

            // A null/empty session GUID means the default session for this process on this
            // device — which is where NAudio's WasapiOut renders.
            var sessionGuid = Guid.Empty;
            Marshal.ThrowExceptionForHR(manager.GetAudioSessionControl(ref sessionGuid, 0, out control));
            Marshal.ThrowExceptionForHR(control.SetDuckingPreference(true));
        }
        finally
        {
            // Release the COM objects we created, newest first. They are RCWs; without this
            // they linger until GC finalizes them. `manager` is the same RCW as `managerObj`,
            // so it is released once via `managerObj`.
            if (control is not null) Marshal.ReleaseComObject(control);
            if (managerObj is not null) Marshal.ReleaseComObject(managerObj);
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

// Every method carries [PreserveSig]: we declare `int` returns and check the HRESULT ourselves
// via Marshal.ThrowExceptionForHR. Without it the marshaler rewrites each signature (HRESULT
// hidden, `int` return remapped to an extra retval argument), so the native call sites no longer
// match the real vtable and the returned "HRESULT" is garbage the callee never wrote.
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object iface);
}

[ComImport, Guid("BFA971F1-4D5E-40BB-935E-967039BFBEE4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager
{
    // Declared to return IAudioSessionControl2: the marshaler asks the returned
    // IAudioSessionControl* for the v2 interface, which WASAPI sessions implement.
    [PreserveSig] int GetAudioSessionControl(ref Guid sessionGuid, int streamFlags, out IAudioSessionControl2 sessionControl);
    [PreserveSig] int GetSimpleAudioVolume(ref Guid sessionGuid, int streamFlags, out IntPtr audioVolume);
}

[ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl members first (the vtable order matters).
    [PreserveSig] int GetState(out int state);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
    [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
    [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string value, ref Guid eventContext);
    [PreserveSig] int GetGroupingParam(out Guid param);
    [PreserveSig] int SetGroupingParam(ref Guid param, ref Guid eventContext);
    [PreserveSig] int RegisterAudioSessionNotification(IntPtr client);
    [PreserveSig] int UnregisterAudioSessionNotification(IntPtr client);
    // IAudioSessionControl2 members.
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetProcessId(out uint pid);
    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}
