using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTEit.Core.Audio;
using MUTEit.Core.Input;
using MUTEit.Core.Ipc;
using MUTEit.Core.Localization;
using MUTEit.Core.Settings;
using MUTEit.Platform.Windows;

namespace MUTEit.App.SettingsUi;

public enum SettingsPage
{
    Microphone,
    Hotkey,
    Sounds,
    Display,
    General,
    About,
}

/// <summary>A hint on the microphone page (warning or information).</summary>
public sealed record Notice(string Text, bool IsWarning);

/// <summary>Entry of a device list (microphones, output devices).</summary>
/// <param name="IsMissing">The configured device is not connected (entry only shows it).</param>
public sealed record DeviceOption(string Value, string Name, bool IsMissing = false);

/// <summary>File and confirmation dialogs of the main window.</summary>
public interface IDialogs
{
    Task<string?> SaveFileAsync(string title, string suggestedName);

    Task<string?> OpenFileAsync(string title, string filterName, IReadOnlyList<string> patterns);

    Task<bool> ConfirmAsync(string title, string text, string okText);

    /// <summary>Theme / Mica switched: apply the window background again.</summary>
    void RefreshBackdrop();
}

/// <summary>State of a result pill (accent / red / grey).</summary>
public enum PillState
{
    None,
    Ok,
    Error,
    Busy,
}

/// <summary>
/// View model of the settings window.
/// <list type="bullet">
/// <item>Every change is saved at once (read-modify-write) and the tray is told to reload – no Save button.</item>
/// <item>Mute / unmute and the microphone level go to the tray over the named pipe (the tray owns the microphone).</item>
/// <item>The tray is polled every second; changes made in the tray menu are picked up from the settings file.</item>
/// </list>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _store = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveLater = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _volumeLater = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly List<Action<MuteitSettings>> _pendingChanges = [];
    private DateTime _fileStamp;
    private DateTime _volumeEditedAt;
    private bool _loading;
    private TrayStatus? _status;

    public SettingsViewModel()
    {
        Settings = _store.Load();
        Loc.SetLanguage(Settings.Language);
        HotkeyBinding.KeyNameResolver = InputHook.KeyName;
        _fileStamp = FileStamp();
        RefreshDeviceLists();
        LoadFrom(Settings);

        Loc.Changed += RefreshTexts;
        _poll.Tick += async (_, _) =>
        {
            await RefreshStatusAsync();
            PickUpExternalChanges();
        };
        _saveLater.Tick += (_, _) => FlushPendingChanges();
        _volumeLater.Tick += async (_, _) =>
        {
            _volumeLater.Stop();
            await TrayClient.SendAsync(new TrayRequest(TrayCommand.SetVolume, (int)Math.Round(MicVolume)));
        };
        _recordTimer.Tick += (_, _) => HandleRecorder(_recorder?.OnTimer(Environment.TickCount64) ?? RecorderOutcome.Continue);

        InitGeneral();
        _poll.Start();
        _ = RefreshStatusAsync();
    }

    /// <summary>Current settings (as last loaded or saved).</summary>
    public MuteitSettings Settings { get; private set; }

    public IDialogs? Dialogs { get; set; }

    // ================================================================== Navigation

    [ObservableProperty]
    private SettingsPage _page = SettingsPage.Microphone;

    public bool IsMicrophonePage { get => Page == SettingsPage.Microphone; set => Go(value, SettingsPage.Microphone); }

    public bool IsHotkeyPage { get => Page == SettingsPage.Hotkey; set => Go(value, SettingsPage.Hotkey); }

    public bool IsSoundsPage { get => Page == SettingsPage.Sounds; set => Go(value, SettingsPage.Sounds); }

    public bool IsDisplayPage { get => Page == SettingsPage.Display; set => Go(value, SettingsPage.Display); }

    public bool IsGeneralPage { get => Page == SettingsPage.General; set => Go(value, SettingsPage.General); }

    public bool IsAboutPage { get => Page == SettingsPage.About; set => Go(value, SettingsPage.About); }

    public string PageTitle => Loc.T(Page switch
    {
        SettingsPage.Hotkey => "NavHotkey",
        SettingsPage.Sounds => "NavSounds",
        SettingsPage.Display => "NavDisplay",
        SettingsPage.General => "NavGeneral",
        SettingsPage.About => "NavAbout",
        _ => "NavMicrophone",
    });

    private void Go(bool value, SettingsPage page)
    {
        if (value)
        {
            Page = page;
        }
    }

    partial void OnPageChanged(SettingsPage value)
    {
        OnPropertyChanged(nameof(IsMicrophonePage));
        OnPropertyChanged(nameof(IsHotkeyPage));
        OnPropertyChanged(nameof(IsSoundsPage));
        OnPropertyChanged(nameof(IsDisplayPage));
        OnPropertyChanged(nameof(IsGeneralPage));
        OnPropertyChanged(nameof(IsAboutPage));
        OnPropertyChanged(nameof(PageTitle));
        CancelRecording();

        if (value is SettingsPage.Microphone or SettingsPage.Sounds)
        {
            RefreshDeviceLists();
        }

        if (value == SettingsPage.General)
        {
            Autostart = Platform.Windows.Autostart.IsEnabled();
        }
    }

    // ================================================================== Saving

    /// <summary>Writes a change right away and lets the tray reload.</summary>
    private void Save(Action<MuteitSettings> change)
    {
        if (_loading)
        {
            return;
        }

        FlushPendingChanges();
        Settings = _store.Update(change);
        _fileStamp = FileStamp();
        _ = TrayClient.SendAsync(new TrayRequest(TrayCommand.Reload));
        RefreshNotices();
    }

    /// <summary>Slider changes: collected and written once the slider rests for a moment.</summary>
    private void SaveLater(Action<MuteitSettings> change)
    {
        if (_loading)
        {
            return;
        }

        _pendingChanges.Add(change);
        _saveLater.Stop();
        _saveLater.Start();
    }

    private void FlushPendingChanges()
    {
        _saveLater.Stop();
        if (_pendingChanges.Count == 0)
        {
            return;
        }

        var changes = _pendingChanges.ToList();
        _pendingChanges.Clear();
        Settings = _store.Update(s => changes.ForEach(c => c(s)));
        _fileStamp = FileStamp();
        _ = TrayClient.SendAsync(new TrayRequest(TrayCommand.Reload));
    }

    private DateTime FileStamp()
    {
        try
        {
            return File.Exists(_store.FilePath) ? File.GetLastWriteTimeUtc(_store.FilePath) : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    /// <summary>Changed in the tray menu (mode, microphone, overlay, taskbar): show the new values.</summary>
    private void PickUpExternalChanges()
    {
        if (_pendingChanges.Count > 0 || IsRecording)
        {
            return;
        }

        var stamp = FileStamp();
        if (stamp == _fileStamp)
        {
            return;
        }

        _fileStamp = stamp;
        Settings = _store.Load();
        LoadFrom(Settings);
    }

    /// <summary>Puts all values of <paramref name="s"/> into the controls without saving them back.</summary>
    private void LoadFrom(MuteitSettings s)
    {
        _loading = true;
        try
        {
            SelectedMicrophone = MicOptions.FirstOrDefault(o => o.Value == s.Microphone) ?? AddMissingMicrophone(s);
            InitialStateIndex = (int)s.InitialState;
            ExitStateIndex = (int)s.ExitState;

            Mode = s.Mode;
            IgnoreModifiers = s.IgnoreModifiers;
            SuppressHotkey = s.SuppressHotkey;
            HotkeyText = HotkeyDisplay(s.Hotkey);
            OnPropertyChanged(nameof(HasHotkey));

            LoadSounds(s);
            LoadDisplay(s);
            LoadGeneral(s);
        }
        finally
        {
            _loading = false;
        }

        RefreshNotices();
    }

    // ================================================================== Microphone page

    [ObservableProperty]
    private bool _trayRunning = true;

    [ObservableProperty]
    private string _stateHeadline = string.Empty;

    [ObservableProperty]
    private string _stateDetail = string.Empty;

    [ObservableProperty]
    private string _activeMicrophone = string.Empty;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isUnavailable;

    [ObservableProperty]
    private double _micVolume;

    [ObservableProperty]
    private bool _canSetVolume;

    [ObservableProperty]
    private DeviceOption? _selectedMicrophone;

    [ObservableProperty]
    private int _initialStateIndex;

    [ObservableProperty]
    private int _exitStateIndex;

    public ObservableCollection<DeviceOption> MicOptions { get; } = [];

    public ObservableCollection<Notice> Notices { get; } = [];

    public bool IsLive => TrayRunning && !IsMuted && !IsUnavailable;

    public string ToggleText => Loc.T(IsMuted ? "BtnUnmute" : "BtnMute");

    public bool CanToggle => TrayRunning && !IsUnavailable;

    public string MicVolumeText => CanSetVolume ? $"{(int)Math.Round(MicVolume)} %" : "–";

    public IReadOnlyList<string> InitialStateNames => [Loc.T("InitDoNotChange"), Loc.T("InitMute"), Loc.T("InitUnmute")];

    public IReadOnlyList<string> ExitStateNames => [Loc.T("ExitKeep"), Loc.T("ExitUnmute"), Loc.T("ExitMute")];

    partial void OnIsMutedChanged(bool value) => StateFlagsChanged();

    partial void OnIsUnavailableChanged(bool value) => StateFlagsChanged();

    partial void OnTrayRunningChanged(bool value) => StateFlagsChanged();

    partial void OnCanSetVolumeChanged(bool value) => OnPropertyChanged(nameof(MicVolumeText));

    private void StateFlagsChanged()
    {
        OnPropertyChanged(nameof(IsLive));
        OnPropertyChanged(nameof(ToggleText));
        OnPropertyChanged(nameof(CanToggle));
    }

    partial void OnMicVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(MicVolumeText));
        if (_applyingStatus || _loading)
        {
            return;
        }

        // Live level: sent to the tray shortly after the slider moved (not stored in the settings).
        _volumeEditedAt = DateTime.UtcNow;
        _volumeLater.Stop();
        _volumeLater.Start();
    }

    partial void OnSelectedMicrophoneChanged(DeviceOption? value)
    {
        if (value is null || value.IsMissing)
        {
            return;
        }

        Save(s =>
        {
            s.Microphone = value.Value;
            s.MicrophoneName = DeviceSelection.IsSpecific(value.Value) ? value.Name : null;
        });
    }

    partial void OnInitialStateIndexChanged(int value)
    {
        // -1: the combo box lost its selection while the item texts were replaced (language switch).
        if (value >= 0)
        {
            Save(s => s.InitialState = (InitialMicState)Math.Clamp(value, 0, 2));
        }
    }

    partial void OnExitStateIndexChanged(int value)
    {
        if (value >= 0)
        {
            Save(s => s.ExitState = (ExitMicState)Math.Clamp(value, 0, 2));
        }
    }

    [RelayCommand]
    private async Task ToggleMicAsync() => ApplyStatus((await TrayClient.SendAsync(new TrayRequest(TrayCommand.Toggle)))?.Status);

    [RelayCommand]
    private void RefreshDevices() => RefreshDeviceLists();

    [RelayCommand]
    private async Task StartTrayAsync()
    {
        Program.Start(string.Empty);
        await Task.Delay(1200);
        await RefreshStatusAsync();
    }

    /// <summary>Reads the microphones and output devices again (selection stays).</summary>
    private void RefreshDeviceLists()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            SelectedMicrophone = null;
            MicOptions.Clear();
            MicOptions.Add(new DeviceOption(DeviceSelection.DefaultCommunications, Loc.T("MicDefaultComm")));
            MicOptions.Add(new DeviceOption(DeviceSelection.DefaultConsole, Loc.T("MicDefault")));
            MicOptions.Add(new DeviceOption(DeviceSelection.All, Loc.T("MicAll")));
            foreach (var device in AudioDevices.Microphones())
            {
                MicOptions.Add(new DeviceOption(device.Id, device.Name));
            }

            SelectedMicrophone = MicOptions.FirstOrDefault(o => o.Value == Settings.Microphone) ?? AddMissingMicrophone(Settings);

            SelectedOutput = null;
            OutputOptions.Clear();
            OutputOptions.Add(new DeviceOption(DeviceSelection.DefaultOutput, Loc.T("OutDefault")));
            foreach (var device in AudioDevices.Outputs())
            {
                OutputOptions.Add(new DeviceOption(device.Id, device.Name));
            }

            SelectedOutput = OutputOptions.FirstOrDefault(o => o.Value == Settings.OutputDevice) ?? AddMissingOutput(Settings);
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    /// <summary>The chosen microphone is unplugged: keep it in the list, marked as not connected.</summary>
    private DeviceOption AddMissingMicrophone(MuteitSettings s)
    {
        var option = new DeviceOption(s.Microphone, Loc.F("MicMissingFmt", s.MicrophoneName ?? "?"), IsMissing: true);
        MicOptions.Add(option);
        return option;
    }

    private DeviceOption AddMissingOutput(MuteitSettings s)
    {
        var option = new DeviceOption(s.OutputDevice, Loc.F("MicMissingFmt", s.OutputDeviceName ?? "?"), IsMissing: true);
        OutputOptions.Add(option);
        return option;
    }

    private bool _applyingStatus;

    private async Task RefreshStatusAsync()
    {
        var response = await TrayClient.SendAsync(new TrayRequest(TrayCommand.Status));
        ApplyStatus(response?.Status);
    }

    private void ApplyStatus(TrayStatus? status)
    {
        _status = status;
        _applyingStatus = true;
        try
        {
            TrayRunning = status is not null;
            if (status is null)
            {
                StateHeadline = Loc.T("StTrayOff");
                StateDetail = Loc.T("TrayNotRunning");
                IsMuted = false;
                IsUnavailable = true;
                CanSetVolume = false;
                RefreshNotices();
                return;
            }

            IsMuted = status.State is MicState.Muted or MicState.Mixed;
            IsUnavailable = status.State == MicState.Unavailable;
            StateHeadline = status.State switch
            {
                MicState.Muted => Loc.T("StMuted"),
                MicState.Unmuted => Loc.T("StLive"),
                MicState.Mixed => Loc.T("StMixed"),
                _ => Loc.T("StUnavailable"),
            };

            StateDetail = Names.Mode(status.Mode);
            ActiveMicrophone = status.DeviceName is { Length: > 0 } name ? name : Loc.T("StUnavailable");
            UpdateScreens(status.Screens);

            CanSetVolume = status.VolumePercent is not null;

            // Do not fight the user while the slider is being moved.
            if (status.VolumePercent is { } percent && DateTime.UtcNow - _volumeEditedAt > TimeSpan.FromSeconds(1.5))
            {
                MicVolume = percent;
            }
        }
        finally
        {
            _applyingStatus = false;
        }

        RefreshNotices();
    }

    private static Notice Warn(string text) => new(Loc.T("NoticeWarnPrefix") + text, true);

    private static Notice Info(string text) => new(Loc.T("NoticeInfoPrefix") + text, false);

    private void RefreshNotices()
    {
        var notices = BuildNotices();

        // Only touch the list when something changed (the tray is polled every second).
        if (notices.SequenceEqual(Notices))
        {
            return;
        }

        Notices.Clear();
        foreach (var notice in notices)
        {
            Notices.Add(notice);
        }
    }

    private List<Notice> BuildNotices()
    {
        var list = new List<Notice>();
        var s = Settings;
        var status = _status;
        if (status is null)
        {
            return list;
        }

        if (status.State == MicState.Unavailable)
        {
            list.Add(Warn(DeviceSelection.IsSpecific(s.Microphone)
                ? Loc.F("NoticeMicMissingFmt", s.MicrophoneName ?? "?")
                : Loc.T("NoticeNoMic")));
        }

        if (status.State == MicState.Mixed)
        {
            list.Add(Info(Loc.T("NoticeMixed")));
        }

        if (s.Hotkey is not null && !status.HotkeyActive && !status.HotkeySuspended)
        {
            list.Add(Warn(Loc.T("NoticeHotkeyInactive")));
        }

        if (s.Mode != MuteMode.Toggle && s.ExitState == ExitMicState.Keep)
        {
            list.Add(Info(Loc.T(s.Mode == MuteMode.PushToTalk ? "NoticePttExit" : "NoticePtmExit")));
        }

        if (status.SoundError is { } sound)
        {
            list.Add(Warn(sound));
        }

        if (status.IconError is { } icon)
        {
            list.Add(Warn(icon));
        }

        if (status.AccentFellBack)
        {
            list.Add(Info(Loc.T("NoticeAccentFallback")));
        }

        return list;
    }

    // ================================================================== Hotkey page

    private readonly DispatcherTimer _recordTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private InputHook? _recordHook;
    private HotkeyRecorder? _recorder;

    [ObservableProperty]
    private MuteMode _mode;

    [ObservableProperty]
    private bool _ignoreModifiers;

    [ObservableProperty]
    private bool _suppressHotkey;

    [ObservableProperty]
    private string _hotkeyText = string.Empty;

    [ObservableProperty]
    private bool _isRecording;

    public bool IsModeToggle { get => Mode == MuteMode.Toggle; set => SetMode(value, MuteMode.Toggle); }

    public bool IsModePtt { get => Mode == MuteMode.PushToTalk; set => SetMode(value, MuteMode.PushToTalk); }

    public bool IsModePtm { get => Mode == MuteMode.PushToMute; set => SetMode(value, MuteMode.PushToMute); }

    public string RecorderHint => Loc.T(IsRecording ? "RecorderRecording" : "RecorderClick");

    public bool HasHotkey => Settings.Hotkey is not null;

    private void SetMode(bool value, MuteMode mode)
    {
        if (value)
        {
            Mode = mode;
        }
    }

    partial void OnModeChanged(MuteMode value)
    {
        OnPropertyChanged(nameof(IsModeToggle));
        OnPropertyChanged(nameof(IsModePtt));
        OnPropertyChanged(nameof(IsModePtm));
        Save(s => s.Mode = value);
    }

    partial void OnIgnoreModifiersChanged(bool value) => Save(s => s.IgnoreModifiers = value);

    partial void OnSuppressHotkeyChanged(bool value) => Save(s => s.SuppressHotkey = value);

    partial void OnIsRecordingChanged(bool value) => OnPropertyChanged(nameof(RecorderHint));

    /// <summary>Click on the hotkey box: start recording (a second click cancels).</summary>
    [RelayCommand]
    private async Task RecordHotkeyAsync()
    {
        if (IsRecording)
        {
            CancelRecording();
            return;
        }

        // The tray must not react to (or swallow) the old hotkey while a new one is pressed.
        await TrayClient.SendAsync(new TrayRequest(TrayCommand.SuspendHotkey));
        _recorder = new HotkeyRecorder();
        _recordHook = new InputHook
        {
            // Every key goes to the box while recording (also Alt, Win, Esc …) and is swallowed.
            Key = (vk, down) =>
            {
                var outcome = _recorder?.OnKey(vk, down, Environment.TickCount64) ?? RecorderOutcome.Continue;
                Dispatcher.UIThread.Post(() => HandleRecorder(outcome));
                return true;
            },
            MouseButton = (vk, down) =>
            {
                var outcome = _recorder?.OnMouse(vk, down, Environment.TickCount64) ?? RecorderOutcome.Continue;
                Dispatcher.UIThread.Post(() => HandleRecorder(outcome));
                return true;
            },
        };

        try
        {
            _recordHook.Install(mouse: true);
        }
        catch (Win32Exception)
        {
            _recordHook.Dispose();
            _recordHook = null;
            await TrayClient.SendAsync(new TrayRequest(TrayCommand.ResumeHotkey));
            return;
        }

        IsRecording = true;
        HotkeyText = Loc.T("RecorderPress");
        _recordTimer.Start();
    }

    [RelayCommand]
    private void ClearHotkey()
    {
        CancelRecording();
        Save(s => s.Hotkey = null);
        HotkeyText = HotkeyDisplay(null);
        OnPropertyChanged(nameof(HasHotkey));
    }

    [RelayCommand]
    private void DefaultHotkey()
    {
        CancelRecording();
        Save(s => s.Hotkey = HotkeyBinding.Default);
        HotkeyText = HotkeyDisplay(HotkeyBinding.Default);
        OnPropertyChanged(nameof(HasHotkey));
    }

    /// <summary>Window lost the focus, page changed or window closes: stop recording, keep the old hotkey.</summary>
    public void CancelRecording()
    {
        if (!IsRecording)
        {
            return;
        }

        StopRecording();
        HotkeyText = HotkeyDisplay(Settings.Hotkey);
    }

    private void HandleRecorder(RecorderOutcome outcome)
    {
        if (!IsRecording || _recorder is null)
        {
            return;
        }

        switch (outcome)
        {
            case RecorderOutcome.Assigned when _recorder.Result is { } binding:
                StopRecording();
                Save(s => s.Hotkey = binding);
                HotkeyText = HotkeyDisplay(binding);
                OnPropertyChanged(nameof(HasHotkey));
                break;
            case RecorderOutcome.Cleared:
                StopRecording();
                Save(s => s.Hotkey = null);
                HotkeyText = HotkeyDisplay(null);
                OnPropertyChanged(nameof(HasHotkey));
                break;
            default:
                var held = _recorder.HeldModifiers;
                HotkeyText = held == HotkeyModifiers.None
                    ? Loc.T(_recorder.HasPendingClear ? "RecorderPendingClear" : "RecorderPress")
                    : HotkeyBinding.DisplayPartial(held);
                break;
        }
    }

    private void StopRecording()
    {
        _recordTimer.Stop();
        _recordHook?.Dispose();
        _recordHook = null;
        _recorder = null;
        IsRecording = false;
        _ = TrayClient.SendAsync(new TrayRequest(TrayCommand.ResumeHotkey));
    }

    private static string HotkeyDisplay(HotkeyBinding? binding) => binding?.Display() ?? Loc.T("HotkeyNone");

    // ================================================================== Shutdown and helpers

    /// <summary>Window closes: stop recording (gives the tray its hotkey back) and write pending slider values.</summary>
    public void Shutdown()
    {
        _poll.Stop();
        if (IsRecording)
        {
            _recordTimer.Stop();
            _recordHook?.Dispose();
            _recordHook = null;
            IsRecording = false;
            TrayClient.Send(new TrayRequest(TrayCommand.ResumeHotkey), 1000);
        }

        if (_pendingChanges.Count > 0)
        {
            FlushPendingChanges();
            TrayClient.Send(new TrayRequest(TrayCommand.Reload), 1000);
        }
    }

    public static string Version =>
        typeof(SettingsViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.1";

    private void RefreshTexts()
    {
        foreach (var language in Languages)
        {
            language.Refresh();
        }

        RefreshDeviceLists();
        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(ToggleText));
        OnPropertyChanged(nameof(InitialStateNames));
        OnPropertyChanged(nameof(ExitStateNames));
        OnPropertyChanged(nameof(OverlayVisibilityNames));
        OnPropertyChanged(nameof(OverlayStyleNames));
        OnPropertyChanged(nameof(ThemeNames));
        OnPropertyChanged(nameof(RecorderHint));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(SoundVolumeText));
        OnPropertyChanged(nameof(MicVolumeText));

        // ComboBoxes keep their index when the item texts are replaced.
        var wasLoading = _loading;
        _loading = true;
        try
        {
            InitialStateIndex = (int)Settings.InitialState;
            ExitStateIndex = (int)Settings.ExitState;
            OverlayVisibilityIndex = (int)Settings.OverlayVisibility;
            OverlayStyleIndex = (int)Settings.OverlayStyle;
            ThemeIndex = (int)Settings.Theme;
        }
        finally
        {
            _loading = wasLoading;
        }

        HotkeyText = HotkeyDisplay(Settings.Hotkey);
        LoadSounds(Settings);
        LoadDisplay(Settings);
        ApplyStatus(_status);
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // nothing to open
        }
    }
}
