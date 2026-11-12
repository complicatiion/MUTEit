using System.Runtime.InteropServices;
using MUTEit.Core.Audio;
using MUTEit.Core.Settings;

namespace MUTEit.Platform.Windows;

/// <summary>
/// Controls the selected microphone(s) through Core Audio (IAudioEndpointVolume) and keeps in sync with changes made
/// by other programs (Teams, Discord, the Windows sound settings, hardware buttons) and with plugged / unplugged devices.
/// <para>Create and use it on the UI thread (STA). Core Audio calls the callbacks on its own threads; they are posted to
/// the UI thread, and <see cref="Changed"/> is always raised there.</para>
/// </summary>
public sealed class MicrophoneController : IDisposable
{
    private static Guid _volumeIid = typeof(IAudioEndpointVolume).GUID;

    /// <summary>Event context of MUTEit's own changes, so notifications can tell them apart from other programs.</summary>
    private static Guid _context = new("2B3C6A41-6D1E-4D7A-9E55-6F2A1C0D7E31");

    private readonly SynchronizationContext _ui;
    private readonly List<Endpoint> _endpoints = [];
    private readonly IMMDeviceEnumerator? _enumerator;
    private readonly DeviceWatcher? _watcher;
    private string _selection = DeviceSelection.DefaultCommunications;
    private bool _rebindQueued;
    private bool _disposed;

    public MicrophoneController(SynchronizationContext ui)
    {
        _ui = ui;
        _enumerator = AudioDevices.Enumerator;
        if (_enumerator is not null)
        {
            try
            {
                _watcher = new DeviceWatcher(this);
                _enumerator.RegisterEndpointNotificationCallback(_watcher);
            }
            catch (COMException)
            {
                _watcher = null; // no hot-plug updates, everything else works
            }
        }
    }

    /// <summary>State, level or device list changed (UI thread).</summary>
    public event Action? Changed;

    public MicState State
    {
        get
        {
            if (_endpoints.Count == 0)
            {
                return MicState.Unavailable;
            }

            var muted = _endpoints.Count(e => e.Muted);
            return muted == 0 ? MicState.Unmuted : muted == _endpoints.Count ? MicState.Muted : MicState.Mixed;
        }
    }

    /// <summary>Level 0 – 100 of the single controlled microphone; null for "All devices" or none.</summary>
    public int? VolumePercent => _endpoints.Count == 1 && _selection != DeviceSelection.All
        ? (int)Math.Round(_endpoints[0].Level * 100)
        : null;

    public int DeviceCount => _endpoints.Count;

    /// <summary>Name of the controlled microphone (first one for "All devices").</summary>
    public string? DeviceName => _endpoints.Count > 0 ? _endpoints[0].Name : null;

    public string? DeviceId => _endpoints.Count > 0 ? _endpoints[0].Id : null;

    /// <summary>Chooses the microphone(s): a <see cref="DeviceSelection"/> constant or an endpoint id.</summary>
    public void Select(string selection)
    {
        if (_selection == selection && _endpoints.Count > 0)
        {
            return;
        }

        _selection = selection;
        Rebind();
    }

    /// <summary>Mutes or unmutes all controlled endpoints. Returns false when there is no microphone.</summary>
    public bool SetMuted(bool muted)
    {
        var any = false;
        foreach (var endpoint in _endpoints)
        {
            try
            {
                endpoint.Volume.SetMute(muted, ref _context);
                endpoint.Muted = muted;
                any = true;
            }
            catch (COMException)
            {
                // device vanished in the meantime – the watcher rebinds
            }
        }

        if (any)
        {
            Changed?.Invoke();
        }

        return any;
    }

    /// <summary>Level of the single controlled microphone (0 – 100). Ignored for "All devices".</summary>
    public void SetVolume(int percent)
    {
        if (VolumePercent is null)
        {
            return;
        }

        var level = Math.Clamp(percent, 0, 100) / 100f;
        try
        {
            _endpoints[0].Volume.SetMasterVolumeLevelScalar(level, ref _context);
            _endpoints[0].Level = level;
            Changed?.Invoke();
        }
        catch (COMException)
        {
            // device vanished in the meantime
        }
    }

    /// <summary>Re-reads the device list and states (resume from sleep, session unlock).</summary>
    public void Refresh() => Rebind();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseEndpoints();
        if (_enumerator is not null)
        {
            try
            {
                if (_watcher is not null)
                {
                    _enumerator.UnregisterEndpointNotificationCallback(_watcher);
                }
            }
            catch (COMException)
            {
                // audio service gone
            }

            // The shared enumerator itself is never released (see AudioDevices.Enumerator).
        }
    }

    private void Rebind()
    {
        if (_disposed)
        {
            return;
        }

        ReleaseEndpoints();
        if (_enumerator is null)
        {
            Changed?.Invoke();
            return;
        }

        foreach (var id in ResolveIds())
        {
            try
            {
                _enumerator.GetDevice(id, out var device);
                try
                {
                    device.Activate(ref _volumeIid, 0x17 /* CLSCTX_ALL */, IntPtr.Zero, out var instance);
                    var volume = (IAudioEndpointVolume)instance;
                    var endpoint = new Endpoint(id, AudioDevices.FriendlyName(device) ?? id, volume);
                    volume.GetMute(out var muted);
                    volume.GetMasterVolumeLevelScalar(out var level);
                    endpoint.Muted = muted;
                    endpoint.Level = level;
                    endpoint.Callback = new VolumeCallback(this, endpoint);
                    volume.RegisterControlChangeNotify(endpoint.Callback);
                    _endpoints.Add(endpoint);
                }
                finally
                {
                    AudioDevices.Release(device);
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException)
            {
                // unplugged between listing and opening
            }
        }

        Changed?.Invoke();
    }

    private IEnumerable<string> ResolveIds()
    {
        if (_enumerator is null)
        {
            return [];
        }

        switch (_selection)
        {
            case DeviceSelection.All:
                return AudioDevices.Microphones().Select(d => d.Id);
            case DeviceSelection.DefaultConsole:
                return AudioDevices.DefaultId(_enumerator, EDataFlow.eCapture, ERole.eConsole) is { } console ? [console] : [];
            case DeviceSelection.DefaultCommunications:
                return AudioDevices.DefaultId(_enumerator, EDataFlow.eCapture, ERole.eCommunications) is { } comm ? [comm] : [];
            default:
                // A specific device: only while it is active (unplugged = unavailable, not another microphone).
                return AudioDevices.Microphones().Any(d => d.Id == _selection) ? [_selection] : [];
        }
    }

    private void ReleaseEndpoints()
    {
        foreach (var endpoint in _endpoints)
        {
            try
            {
                if (endpoint.Callback is not null)
                {
                    endpoint.Volume.UnregisterControlChangeNotify(endpoint.Callback);
                }
            }
            catch (COMException)
            {
                // device already gone
            }

            AudioDevices.Release(endpoint.Volume);
        }

        _endpoints.Clear();
    }

    /// <summary>Device added / removed / default changed: rebind once, a moment later (Windows sends bursts).</summary>
    private void QueueRebind()
    {
        if (_rebindQueued || _disposed)
        {
            return;
        }

        _rebindQueued = true;
        _ = Task.Delay(400).ContinueWith(_ => _ui.Post(_ =>
        {
            _rebindQueued = false;
            Rebind();
        }, null), TaskScheduler.Default);
    }

    private void OnVolumeNotify(Endpoint endpoint, bool muted, float level)
    {
        _ui.Post(_ =>
        {
            if (_disposed || !_endpoints.Contains(endpoint))
            {
                return;
            }

            if (endpoint.Muted == muted && Math.Abs(endpoint.Level - level) < 0.001f)
            {
                return;
            }

            endpoint.Muted = muted;
            endpoint.Level = level;
            Changed?.Invoke();
        }, null);
    }

    private sealed class Endpoint(string id, string name, IAudioEndpointVolume volume)
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public IAudioEndpointVolume Volume { get; } = volume;

        public VolumeCallback? Callback { get; set; }

        public bool Muted { get; set; }

        public float Level { get; set; }
    }

    private sealed class VolumeCallback(MicrophoneController owner, Endpoint endpoint) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notifyData)
        {
            if (notifyData != IntPtr.Zero)
            {
                var data = Marshal.PtrToStructure<AUDIO_VOLUME_NOTIFICATION_DATA>(notifyData);
                owner.OnVolumeNotify(endpoint, data.bMuted != 0, data.fMasterVolume);
            }

            return 0;
        }
    }

    private sealed class DeviceWatcher(MicrophoneController owner) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState) => Queue();

        public int OnDeviceAdded(string deviceId) => Queue();

        public int OnDeviceRemoved(string deviceId) => Queue();

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) =>
            flow == EDataFlow.eCapture ? Queue() : 0;

        public int OnPropertyValueChanged(string deviceId, PROPERTYKEY key) => 0;

        private int Queue()
        {
            owner._ui.Post(_ => owner.QueueRebind(), null);
            return 0;
        }
    }
}
