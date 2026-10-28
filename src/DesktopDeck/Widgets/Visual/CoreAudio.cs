using System.Runtime.InteropServices;

namespace DesktopDeck.Widgets;

/// <summary>
/// The system's master volume and mute through Windows Core Audio (MMDevice API + IAudioEndpointVolume), declared
/// here with [ComImport] so no package is needed. Every call opens the current default output device and releases
/// everything it touched, so a device switched elsewhere is picked up on the next read.
/// </summary>
internal static class CoreAudio
{
    /// <summary>What the default output device reports. <see cref="Found"/> is false when there is no output device.</summary>
    internal readonly record struct Reading(bool Found, float Level, bool Muted, string? DeviceName);

    private const int ENotFound = unchecked((int)0x80070490);
    private const int ClsCtxAll = 0x17;

    /// <summary>Reads the master level (0–1), mute and, if asked, the device's friendly name.</summary>
    internal static Reading Read(bool withName)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
            if (hr != 0 || device is null) return new Reading(false, 0, false, null);
            volume = Activate(device);
            if (volume is null) return new Reading(false, 0, false, null);
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out float level));
            Marshal.ThrowExceptionForHR(volume.GetMute(out bool muted));
            return new Reading(true, Math.Clamp(level, 0, 1), muted, withName ? FriendlyName(device) : null);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException or UnauthorizedAccessException) { return new Reading(false, 0, false, null); }
        finally { Release(volume); Release(device); Release(enumerator); }
    }

    /// <summary>Sets the master level (0–1) and, if given, mute. Returns false when there is no output device.</summary>
    internal static bool Write(float? level, bool? mute)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            if (enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device) != 0 || device is null) return false;
            volume = Activate(device);
            if (volume is null) return false;
            var context = Guid.Empty;
            if (level is { } l) Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(Math.Clamp(l, 0f, 1f), ref context));
            if (mute is { } m) Marshal.ThrowExceptionForHR(volume.SetMute(m, ref context));
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException or UnauthorizedAccessException) { return false; }
        finally { Release(volume); Release(device); Release(enumerator); }
    }

    private static IAudioEndpointVolume? Activate(IMMDevice device)
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        int hr = device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object? endpoint);
        if (hr == ENotFound || hr != 0) return null;
        return endpoint as IAudioEndpointVolume;
    }

    private static string? FriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        var value = new PropVariant();
        try
        {
            if (device.OpenPropertyStore(0 /* STGM_READ */, out store) != 0 || store is null) return null;
            var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), PropertyId = 14 };
            if (store.GetValue(ref key, out value) != 0) return null;
            return value.Type == 31 /* VT_LPWSTR */ && value.Pointer != IntPtr.Zero ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return null; }
        finally
        {
            PropVariantClear(ref value);
            Release(store);
        }
    }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    /// <summary>PROPVARIANT: 24 bytes on 64-bit (vt, three reserved words, then a 16-byte union); only strings are read.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorClass { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice? device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice? device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object? endpoint);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore? store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string? id);
        [PreserveSig] int GetState(out int state);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    /// <summary>All 18 methods in vtable order (only a few are used).</summary>
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }
}
