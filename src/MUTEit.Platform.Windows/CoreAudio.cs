using System.Runtime.InteropServices;

namespace MUTEit.Platform.Windows;

/// <summary>An active audio endpoint.</summary>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault, bool IsDefaultCommunications);

/// <summary>Lists microphones and playback devices (Core Audio, active endpoints only).</summary>
public static class AudioDevices
{
    public static IReadOnlyList<AudioDeviceInfo> Microphones() => List(EDataFlow.eCapture);

    public static IReadOnlyList<AudioDeviceInfo> Outputs() => List(EDataFlow.eRender);

    private static List<AudioDeviceInfo> List(EDataFlow flow)
    {
        var result = new List<AudioDeviceInfo>();
        try
        {
            var enumerator = Enumerator ?? throw new COMException("Core Audio not available");
            var defaultId = DefaultId(enumerator, flow, flow == EDataFlow.eRender ? ERole.eMultimedia : ERole.eConsole);
            var commId = DefaultId(enumerator, flow, ERole.eCommunications);
            enumerator.EnumAudioEndpoints(flow, DeviceStates.Active, out var collection);
            try
            {
                collection.GetCount(out var count);
                for (uint i = 0; i < count; i++)
                {
                    collection.Item(i, out var device);
                    try
                    {
                        device.GetId(out var id);
                        result.Add(new AudioDeviceInfo(id, FriendlyName(device) ?? id, id == defaultId, id == commId));
                    }
                    finally
                    {
                        Release(device);
                    }
                }
            }
            finally
            {
                Release(collection);
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            // No audio service / no devices: empty list.
        }
        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IMMDeviceEnumerator? _enumerator;
    private static bool _enumeratorFailed;

    /// <summary>
    /// One device enumerator per process, never released. Windows may hand out the same enumerator object for every
    /// CoCreateInstance; the runtime then shares one wrapper, and releasing it in one place would break every other
    /// user (device list, microphone controller, sound output).
    /// </summary>
    internal static IMMDeviceEnumerator? Enumerator
    {
        get
        {
            if (_enumerator is null && !_enumeratorFailed)
            {
                try
                {
                    _enumerator = ComClass.Create<IMMDeviceEnumerator>(ComClass.MMDeviceEnumerator);
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
                {
                    _enumeratorFailed = true;
                }
            }

            return _enumerator;
        }
    }

    internal static string? DefaultId(IMMDeviceEnumerator enumerator, EDataFlow flow, ERole role)
    {
        try
        {
            enumerator.GetDefaultAudioEndpoint(flow, role, out var device);
            try
            {
                device.GetId(out var id);
                return id;
            }
            finally
            {
                Release(device);
            }
        }
        catch (COMException)
        {
            return null; // E_NOTFOUND: no device of this kind
        }
    }

    internal static string? FriendlyName(IMMDevice device)
    {
        try
        {
            device.OpenPropertyStore(0 /* STGM_READ */, out var store);
            try
            {
                var key = PropertyKeys.DeviceFriendlyName;
                store.GetValue(ref key, out var value);
                try
                {
                    return value.AsString();
                }
                finally
                {
                    NativeMethods.PropVariantClear(ref value);
                }
            }
            finally
            {
                Release(store);
            }
        }
        catch (COMException)
        {
            return null;
        }
    }

    internal static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            Marshal.ReleaseComObject(com);
        }
    }
}
