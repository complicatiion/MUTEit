using System.Runtime.InteropServices;

namespace MUTEit.Platform.Windows;

// Classic COM interop declarations (BuiltInComInteropSupport). Every interface lists all methods of its vtable in
// the documented order – including inherited ones – because the runtime maps them by position.

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;

    public PROPERTYKEY(Guid formatId, uint propertyId)
    {
        fmtid = formatId;
        pid = propertyId;
    }
}

/// <summary>PROPVARIANT (24 bytes on x64). Only VT_LPWSTR and VT_BOOL are used by MUTEit.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct PROPVARIANT
{
    public const ushort VT_EMPTY = 0;
    public const ushort VT_BOOL = 11;
    public const ushort VT_LPWSTR = 31;

    [FieldOffset(0)]
    public ushort vt;

    [FieldOffset(8)]
    public IntPtr pointerValue;

    [FieldOffset(8)]
    public short boolValue;

    public static PROPVARIANT FromString(string value) => new() { vt = VT_LPWSTR, pointerValue = Marshal.StringToCoTaskMemUni(value) };

    public static PROPVARIANT FromBool(bool value) => new() { vt = VT_BOOL, boolValue = (short)(value ? -1 : 0) };

    public readonly string? AsString() => vt == VT_LPWSTR && pointerValue != IntPtr.Zero ? Marshal.PtrToStringUni(pointerValue) : null;
}

internal static class PropertyKeys
{
    /// <summary>PKEY_Device_FriendlyName ("Microphone (Rode NT-USB)").</summary>
    public static readonly PROPERTYKEY DeviceFriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

    private static readonly Guid AppUserModel = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");

    public static readonly PROPERTYKEY AppUserModelRelaunchCommand = new(AppUserModel, 2);
    public static readonly PROPERTYKEY AppUserModelRelaunchIconResource = new(AppUserModel, 3);
    public static readonly PROPERTYKEY AppUserModelRelaunchDisplayNameResource = new(AppUserModel, 4);
    public static readonly PROPERTYKEY AppUserModelId = new(AppUserModel, 5);

    /// <summary>PKEY_Title (jump list task names).</summary>
    public static readonly PROPERTYKEY Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
}

[ComImport]
[Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint count);

    void GetAt(uint index, out PROPERTYKEY key);

    void GetValue(ref PROPERTYKEY key, out PROPVARIANT value);

    void SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);

    void Commit();
}

/// <summary>
/// Creates COM objects by CLSID. No [ComImport] coclass types on purpose: the runtime caches the managed type per
/// CLSID, so a second coclass with the same CLSID in another assembly would get MUTEit's type back and fail with an
/// InvalidCastException.
/// </summary>
internal static class ComClass
{
    public static readonly Guid MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public static readonly Guid TaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    public static readonly Guid DestinationList = new("77f10cf0-3db5-4966-b520-b7c54fd35ed6");

    public static readonly Guid EnumerableObjectCollection = new("2d3468c1-36a7-43b6-ac24-d3f02fd9607a");

    public static readonly Guid ShellLink = new("00021401-0000-0000-C000-000000000046");

    public static T Create<T>(Guid clsid)
        where T : class
    {
        var type = Type.GetTypeFromCLSID(clsid, throwOnError: true)!;
        return (T)Activator.CreateInstance(type)!;
    }
}

// ---------------------------------------------------------------------- Core Audio

internal enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2,
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2,
}

internal static class DeviceStates
{
    public const uint Active = 0x1;
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    void EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

    void GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);

    void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

    void RegisterEndpointNotificationCallback(IMMNotificationClient client);

    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    void GetCount(out uint count);

    void Item(uint index, out IMMDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    void Activate(ref Guid iid, uint clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

    void OpenPropertyStore(uint access, out IPropertyStore properties);

    void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

    void GetState(out uint state);
}

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    void RegisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    void UnregisterControlChangeNotify(IAudioEndpointVolumeCallback notify);

    void GetChannelCount(out uint channelCount);

    void SetMasterVolumeLevel(float levelDb, ref Guid eventContext);

    void SetMasterVolumeLevelScalar(float level, ref Guid eventContext);

    void GetMasterVolumeLevel(out float levelDb);

    void GetMasterVolumeLevelScalar(out float level);

    void SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);

    void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);

    void GetChannelVolumeLevel(uint channel, out float levelDb);

    void GetChannelVolumeLevelScalar(uint channel, out float level);

    void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);

    void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);

    void GetVolumeStepInfo(out uint step, out uint stepCount);

    void VolumeStepUp(ref Guid eventContext);

    void VolumeStepDown(ref Guid eventContext);

    void QueryHardwareSupport(out uint hardwareSupportMask);

    void GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
}

[ComImport]
[Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolumeCallback
{
    [PreserveSig]
    int OnNotify(IntPtr notifyData);
}

[ComImport]
[Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMNotificationClient
{
    [PreserveSig]
    int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);

    [PreserveSig]
    int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);

    [PreserveSig]
    int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);

    [PreserveSig]
    int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PROPERTYKEY key);
}

/// <summary>AUDIO_VOLUME_NOTIFICATION_DATA without the variable channel array.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AUDIO_VOLUME_NOTIFICATION_DATA
{
    public Guid guidEventContext;
    public int bMuted;
    public float fMasterVolume;
    public uint nChannels;
}

// ---------------------------------------------------------------------- Shell: taskbar and jump list

[ComImport]
[Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList3
{
    // ITaskbarList
    void HrInit();

    void AddTab(IntPtr hwnd);

    void DeleteTab(IntPtr hwnd);

    void ActivateTab(IntPtr hwnd);

    void SetActiveAlt(IntPtr hwnd);

    // ITaskbarList2
    void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

    // ITaskbarList3
    void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);

    void SetProgressState(IntPtr hwnd, int flags);

    void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);

    void UnregisterTab(IntPtr hwndTab);

    void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);

    void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);

    void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);

    void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);

    void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);

    void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string? description);

    void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string? tip);

    void SetThumbnailClip(IntPtr hwnd, IntPtr clip);
}

[ComImport]
[Guid("6332debf-87b5-4670-90c0-5e57b408a49e")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ICustomDestinationList
{
    void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    void BeginList(out uint minSlots, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removedItems);

    void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);

    void AppendKnownCategory(int category);

    void AddUserTasks(IObjectArray tasks);

    void CommitList();

    void GetRemovedDestinations(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object removedItems);

    void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string? appId);

    void AbortList();
}

[ComImport]
[Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectArray
{
    void GetCount(out uint count);

    void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);
}

[ComImport]
[Guid("5632b1a4-e38a-400a-928a-d4cd63230295")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IObjectCollection
{
    // IObjectArray
    void GetCount(out uint count);

    void GetAt(uint index, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object item);

    // IObjectCollection
    void AddObject([MarshalAs(UnmanagedType.IUnknown)] object item);

    void AddFromArray(IObjectArray source);

    void RemoveObjectAt(uint index);

    void Clear();
}

[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int maxPath, IntPtr findData, uint flags);

    void GetIDList(out IntPtr idList);

    void SetIDList(IntPtr idList);

    void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

    void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder dir, int maxPath);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

    void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder args, int maxPath);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

    void GetHotkey(out short hotkey);

    void SetHotkey(short hotkey);

    void GetShowCmd(out int showCmd);

    void SetShowCmd(int showCmd);

    void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder iconPath, int maxIconPath, out int iconIndex);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

    void Resolve(IntPtr hwnd, uint flags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
}

// ---------------------------------------------------------------------- WASAPI playback (notification sounds)

[ComImport]
[Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    void Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);

    void GetBufferSize(out uint bufferFrames);

    void GetStreamLatency(out long latency);

    void GetCurrentPadding(out uint paddingFrames);

    [PreserveSig]
    int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

    void GetMixFormat(out IntPtr deviceFormat);

    void GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

    void Start();

    void Stop();

    void Reset();

    void SetEventHandle(IntPtr eventHandle);

    void GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport]
[Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    void GetBuffer(uint framesRequested, out IntPtr data);

    void ReleaseBuffer(uint framesWritten, uint flags);
}
