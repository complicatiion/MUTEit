using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MUTEit.Core.Ipc;
using MUTEit.Core.Localization;
using MUTEit.Core.Settings;
using MUTEit.Core.Updates;
using MUTEit.Platform.Windows;

namespace MUTEit.App.SettingsUi;

/// <summary>Sound source of one state: null = built-in sound, otherwise a WAV path.</summary>
public sealed record SoundOption(string? Path, string Name);

/// <summary>Pages Sounds, Display, General and About.</summary>
public sealed partial class SettingsViewModel
{
    private static readonly string[] SoundPatterns = ["*.wav"];
    private static readonly string[] ImagePatterns = ["*.png", "*.ico", "*.jpg", "*.jpeg", "*.bmp"];

    // ================================================================== Sounds page

    [ObservableProperty]
    private bool _soundOnMute;

    [ObservableProperty]
    private bool _soundOnUnmute;

    [ObservableProperty]
    private bool _soundInPushModes;

    [ObservableProperty]
    private double _soundVolume;

    [ObservableProperty]
    private DeviceOption? _selectedOutput;

    [ObservableProperty]
    private SoundOption? _selectedMuteSound;

    [ObservableProperty]
    private SoundOption? _selectedUnmuteSound;

    public ObservableCollection<SoundOption> MuteSoundOptions { get; } = [];

    public ObservableCollection<SoundOption> UnmuteSoundOptions { get; } = [];

    public ObservableCollection<DeviceOption> OutputOptions { get; } = [];

    public string SoundVolumeText => $"{(int)Math.Round(SoundVolume)} %";

    private void LoadSounds(MuteitSettings s)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            SoundOnMute = s.SoundOnMute;
            SoundOnUnmute = s.SoundOnUnmute;
            SoundInPushModes = s.SoundInPushModes;
            SoundVolume = s.SoundVolume;
            SelectedOutput = OutputOptions.FirstOrDefault(o => o.Value == s.OutputDevice) ?? SelectedOutput;
            SelectedMuteSound = null;
            SelectedUnmuteSound = null;
            FillSoundOptions(MuteSoundOptions, s.MuteSoundPath);
            SelectedMuteSound = MuteSoundOptions.First(o => o.Path == s.MuteSoundPath);
            FillSoundOptions(UnmuteSoundOptions, s.UnmuteSoundPath);
            SelectedUnmuteSound = UnmuteSoundOptions.First(o => o.Path == s.UnmuteSoundPath);
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    partial void OnSoundOnMuteChanged(bool value) => Save(s => s.SoundOnMute = value);

    partial void OnSoundOnUnmuteChanged(bool value) => Save(s => s.SoundOnUnmute = value);

    partial void OnSoundInPushModesChanged(bool value) => Save(s => s.SoundInPushModes = value);

    partial void OnSoundVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(SoundVolumeText));
        SaveLater(s => s.SoundVolume = (int)Math.Round(value));
    }

    partial void OnSelectedOutputChanged(DeviceOption? value)
    {
        if (value is null || value.IsMissing)
        {
            return;
        }

        Save(s =>
        {
            s.OutputDevice = value.Value;
            s.OutputDeviceName = DeviceSelection.IsSpecific(value.Value) ? value.Name : null;
        });
    }

    [RelayCommand]
    private Task TestMuteSoundAsync() => TestSoundAsync(true);

    [RelayCommand]
    private Task TestUnmuteSoundAsync() => TestSoundAsync(false);

    private async Task TestSoundAsync(bool muted)
    {
        FlushPendingChanges();
        await TrayClient.SendAsync(new TrayRequest(TrayCommand.Reload));
        await TrayClient.SendAsync(new TrayRequest(TrayCommand.TestSound, muted ? 1 : 0));
        await Task.Delay(400);
        await RefreshStatusAsync();
    }

    partial void OnSelectedMuteSoundChanged(SoundOption? value)
    {
        if (value is not null)
        {
            Save(s => s.MuteSoundPath = value.Path);
        }
    }

    partial void OnSelectedUnmuteSoundChanged(SoundOption? value)
    {
        if (value is not null)
        {
            Save(s => s.UnmuteSoundPath = value.Path);
        }
    }

    [RelayCommand]
    private async Task LoadMuteSoundAsync()
    {
        if (await PickAsync("PickSoundTitle", "SoundFiles", SoundPatterns) is { } path)
        {
            Save(s => s.MuteSoundPath = path);
            LoadSounds(Settings);
        }
    }

    [RelayCommand]
    private async Task LoadUnmuteSoundAsync()
    {
        if (await PickAsync("PickSoundTitle", "SoundFiles", SoundPatterns) is { } path)
        {
            Save(s => s.UnmuteSoundPath = path);
            LoadSounds(Settings);
        }
    }

    /// <summary>Built-in sound, all Windows sounds (%WINDIR%\Media\*.wav) and – if chosen – the own file.</summary>
    private static void FillSoundOptions(ObservableCollection<SoundOption> options, string? current)
    {
        options.Clear();
        options.Add(new SoundOption(null, Loc.T("SoundBuiltIn")));
        foreach (var file in WindowsSounds.Value)
        {
            options.Add(new SoundOption(file, Path.GetFileNameWithoutExtension(file)));
        }

        if (current is not null && options.All(o => !string.Equals(o.Path, current, StringComparison.OrdinalIgnoreCase)))
        {
            options.Add(new SoundOption(current, Path.GetFileName(current)));
        }

        // Same path, maybe different spelling: select the stored one.
        var match = options.FirstOrDefault(o => string.Equals(o.Path, current, StringComparison.OrdinalIgnoreCase));
        if (match is not null && match.Path != current)
        {
            options[options.IndexOf(match)] = match with { Path = current };
        }
    }

    private static readonly Lazy<List<string>> WindowsSounds = new(() =>
    {
        try
        {
            var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
            return Directory.Exists(media)
                ? Directory.GetFiles(media, "*.wav").OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    });

    // ================================================================== Display page (overlay, taskbar, icons)

    [ObservableProperty]
    private bool _overlayEnabled;

    [ObservableProperty]
    private int _overlayVisibilityIndex;

    [ObservableProperty]
    private double _overlaySize;

    [ObservableProperty]
    private double _overlayOpacity;

    [ObservableProperty]
    private bool _overlayClickToggles;

    [ObservableProperty]
    private bool _taskbarButton;

    [ObservableProperty]
    private OverlayAnchor _overlayAnchor;

    [ObservableProperty]
    private int _overlayStyleIndex;

    [ObservableProperty]
    private DeviceOption? _selectedScreen;

    public ObservableCollection<DeviceOption> ScreenOptions { get; } = [];

    public IReadOnlyList<string> OverlayStyleNames => [Loc.T("OverlayStyleMic"), Loc.T("OverlayStyleLogo")];

    public bool IsAnchorTopLeft => OverlayAnchor == OverlayAnchor.TopLeft;

    public bool IsAnchorTopRight => OverlayAnchor == OverlayAnchor.TopRight;

    public bool IsAnchorBottomLeft => OverlayAnchor == OverlayAnchor.BottomLeft;

    public bool IsAnchorBottomRight => OverlayAnchor == OverlayAnchor.BottomRight;

    public bool IsAnchorFree => OverlayAnchor == OverlayAnchor.Free;

    partial void OnOverlayAnchorChanged(OverlayAnchor value)
    {
        OnPropertyChanged(nameof(IsAnchorTopLeft));
        OnPropertyChanged(nameof(IsAnchorTopRight));
        OnPropertyChanged(nameof(IsAnchorBottomLeft));
        OnPropertyChanged(nameof(IsAnchorBottomRight));
        OnPropertyChanged(nameof(IsAnchorFree));
    }

    /// <summary>Corner dot clicked: the overlay jumps to that corner of the chosen screen.</summary>
    [RelayCommand]
    private void SetAnchor(string corner)
    {
        if (!Enum.TryParse<OverlayAnchor>(corner, out var anchor) || anchor == OverlayAnchor.Free)
        {
            return;
        }

        OverlayAnchor = anchor;
        Save(s => s.OverlayAnchor = anchor);
    }

    partial void OnOverlayStyleIndexChanged(int value)
    {
        if (value >= 0)
        {
            Save(s => s.OverlayStyle = (OverlayStyle)Math.Clamp(value, 0, 1));
        }
    }

    partial void OnSelectedScreenChanged(DeviceOption? value)
    {
        if (value is null || _loading)
        {
            return;
        }

        // Choosing a screen means "put it in a corner there": a free position becomes bottom right.
        if (OverlayAnchor == OverlayAnchor.Free)
        {
            OverlayAnchor = OverlayAnchor.BottomRight;
        }

        var anchor = OverlayAnchor;
        Save(s =>
        {
            s.OverlayScreen = value.Value;
            s.OverlayAnchor = anchor;
        });
    }

    /// <summary>Screens as the tray sees them; refreshed with every status poll (only when the list changed).</summary>
    private void UpdateScreens(IReadOnlyList<MUTEit.Core.Ipc.ScreenInfo> screens)
    {
        var options = screens.Select(s => new DeviceOption(s.Id, s.Name)).ToList();
        if (!options.SequenceEqual(ScreenOptions))
        {
            var wasLoading = _loading;
            _loading = true;
            try
            {
                SelectedScreen = null;
                ScreenOptions.Clear();
                foreach (var option in options)
                {
                    ScreenOptions.Add(option);
                }
            }
            finally
            {
                _loading = wasLoading;
            }
        }

        SelectScreen(Settings);
    }

    private void SelectScreen(MuteitSettings s)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            var primary = ScreenOptions.FirstOrDefault();
            var target = ScreenOptions.FirstOrDefault(o => o.Value == s.OverlayScreen) ?? primary;
            if (!Equals(SelectedScreen, target))
            {
                SelectedScreen = target;
            }
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    [ObservableProperty]
    private Bitmap? _generalPreview;

    [ObservableProperty]
    private Bitmap? _mutedPreview;

    [ObservableProperty]
    private Bitmap? _unmutedPreview;

    [ObservableProperty]
    private string _iconModeText = string.Empty;

    [ObservableProperty]
    private string _iconHint = string.Empty;

    public IReadOnlyList<string> OverlayVisibilityNames =>
    [
        Loc.T("OverlayAlways"), Loc.T("OverlayWhenMuted"), Loc.T("OverlayWhenUnmuted"),
    ];

    public string OverlaySizeText => $"{(int)Math.Round(OverlaySize)} px";

    public string OverlayOpacityText => $"{(int)Math.Round(OverlayOpacity)} %";

    public double OverlayMinSize => MuteitSettings.MinOverlaySize;

    public double OverlayMaxSize => MuteitSettings.MaxOverlaySize;

    public double OverlayMinOpacity => MuteitSettings.MinOverlayOpacity;

    public bool HasCustomIcons => Settings.IconMode != IconMode.Default || Settings.MutedIconPath is not null || Settings.UnmutedIconPath is not null;

    private void LoadDisplay(MuteitSettings s)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            OverlayEnabled = s.OverlayEnabled;
            OverlayVisibilityIndex = (int)s.OverlayVisibility;
            OverlaySize = s.OverlaySize;
            OverlayOpacity = s.OverlayOpacity;
            OverlayClickToggles = s.OverlayClickToggles;
            OverlayAnchor = s.OverlayAnchor;
            OverlayStyleIndex = (int)s.OverlayStyle;
            TaskbarButton = s.TaskbarButton;
        }
        finally
        {
            _loading = wasLoading;
        }

        SelectScreen(s);
        GeneralPreview = s.IconMode == IconMode.General ? LoadPreview(s.GeneralIconPath) : LoadAppPreview();
        MutedPreview = LoadPreview(s.MutedIconPath);
        UnmutedPreview = LoadPreview(s.UnmutedIconPath);
        IconModeText = Loc.T(s.IconMode switch
        {
            IconMode.General => "IconModeGeneral",
            IconMode.Dedicated => "IconModeDedicated",
            _ => "IconModeDefault",
        });
        IconHint = s.IconMode == IconMode.Default && (s.MutedIconPath is null) != (s.UnmutedIconPath is null)
            ? Loc.T("IconSecondMissing")
            : string.Empty;
        OnPropertyChanged(nameof(HasCustomIcons));
    }

    partial void OnOverlayEnabledChanged(bool value) => Save(s => s.OverlayEnabled = value);

    partial void OnOverlayVisibilityIndexChanged(int value)
    {
        if (value >= 0)
        {
            Save(s => s.OverlayVisibility = (OverlayVisibility)Math.Clamp(value, 0, 2));
        }
    }

    partial void OnOverlaySizeChanged(double value)
    {
        OnPropertyChanged(nameof(OverlaySizeText));
        SaveLater(s => s.OverlaySize = (int)Math.Round(value));
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        OnPropertyChanged(nameof(OverlayOpacityText));
        SaveLater(s => s.OverlayOpacity = (int)Math.Round(value));
    }

    partial void OnOverlayClickTogglesChanged(bool value) => Save(s => s.OverlayClickToggles = value);

    partial void OnTaskbarButtonChanged(bool value) => Save(s => s.TaskbarButton = value);

    [RelayCommand]
    private async Task LoadGeneralIconAsync()
    {
        if (await PickAsync("PickIconTitle", "ImageFiles", ImagePatterns) is { } path)
        {
            Save(s =>
            {
                s.GeneralIconPath = path;
                s.IconMode = IconMode.General;
            });
            LoadDisplay(Settings);
        }
    }

    [RelayCommand]
    private async Task LoadMutedIconAsync()
    {
        if (await PickAsync("PickIconTitle", "ImageFiles", ImagePatterns) is { } path)
        {
            Save(s =>
            {
                s.MutedIconPath = path;
                if (s.UnmutedIconPath is not null)
                {
                    s.IconMode = IconMode.Dedicated;
                }
            });
            LoadDisplay(Settings);
        }
    }

    [RelayCommand]
    private async Task LoadUnmutedIconAsync()
    {
        if (await PickAsync("PickIconTitle", "ImageFiles", ImagePatterns) is { } path)
        {
            Save(s =>
            {
                s.UnmutedIconPath = path;
                if (s.MutedIconPath is not null)
                {
                    s.IconMode = IconMode.Dedicated;
                }
            });
            LoadDisplay(Settings);
        }
    }

    [RelayCommand]
    private void ResetIcons()
    {
        Save(s =>
        {
            s.IconMode = IconMode.Default;
            s.GeneralIconPath = null;
            s.MutedIconPath = null;
            s.UnmutedIconPath = null;
        });
        LoadDisplay(Settings);
    }

    private static Bitmap? LoadPreview(string? path)
    {
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null; // unreadable: the tray reports the error on the microphone page
        }
    }

    private static Bitmap? LoadAppPreview()
    {
        using var stream = AssetLoader.Open(new Uri("avares://MUTEit/Assets/app-256.png"));
        return new Bitmap(stream);
    }

    private async Task<string?> PickAsync(string titleKey, string filterKey, IReadOnlyList<string> patterns) =>
        Dialogs is null ? null : await Dialogs.OpenFileAsync(Loc.T(titleKey), Loc.T(filterKey), patterns);

    // ================================================================== General page

    [ObservableProperty]
    private bool _autostart;

    [ObservableProperty]
    private bool _notifications;

    [ObservableProperty]
    private int _languageIndex;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private bool _transparency;

    [ObservableProperty]
    private bool _useWindowsAccent;

    [ObservableProperty]
    private string _accentHint = string.Empty;

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new LanguageOption("LangAuto", null),
        new LanguageOption("LangEn", "avares://MUTEit/Assets/flag-gb.png"),
        new LanguageOption("LangDe", "avares://MUTEit/Assets/flag-de.png"),
    ];

    public IReadOnlyList<string> ThemeNames => [Loc.T("ThemeDark"), Loc.T("ThemeLight"), Loc.T("ThemeSystem")];

    /// <summary>Mica exists on Windows 11 only.</summary>
    public bool CanUseMica => SystemState.IsWindows11;

    public string SettingsFilePath => _store.FilePath;

    public bool IsPortable => SettingsStore.IsPortable;

    private void InitGeneral()
    {
        ShowStoredUpdateResult(Settings);

        // First update check when the window opens (if switched on and older than 24 h).
        if (Settings.UpdateCheckEnabled && (Settings.LastUpdateCheck is not { } last || DateTimeOffset.Now - last > TimeSpan.FromHours(24)))
        {
            _ = CheckUpdateAsync();
        }
    }

    private void LoadGeneral(MuteitSettings s)
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            Autostart = Platform.Windows.Autostart.IsEnabled();
            Notifications = s.Notifications;
            LanguageIndex = s.Language switch { "en" => 1, "de" => 2, _ => 0 };
            ThemeIndex = (int)s.Theme;
            Transparency = s.Transparency;
            UseWindowsAccent = s.UseWindowsAccent;
            UpdateCheckEnabled = s.UpdateCheckEnabled;
        }
        finally
        {
            _loading = wasLoading;
        }

        RefreshAccentHint();
    }

    partial void OnAutostartChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        if (Platform.Windows.Autostart.IsEnabled() != value)
        {
            Platform.Windows.Autostart.Set(value);
        }

        // An explicit choice: the tray must not switch it on again at its first start.
        Save(s => s.AutostartInitialized = true);
    }

    partial void OnNotificationsChanged(bool value) => Save(s => s.Notifications = value);

    partial void OnLanguageIndexChanged(int value)
    {
        if (_loading || value < 0)
        {
            return;
        }

        var language = value switch { 1 => "en", 2 => "de", _ => "auto" };
        Save(s => s.Language = language);
        Loc.SetLanguage(language);
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_loading || value < 0)
        {
            return;
        }

        Save(s => s.Theme = (ThemeMode)Math.Clamp(value, 0, 2));
        ApplyTheme();
    }

    partial void OnTransparencyChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Save(s => s.Transparency = value);
        Dialogs?.RefreshBackdrop();
    }

    partial void OnUseWindowsAccentChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        Save(s => s.UseWindowsAccent = value);
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        ThemeManager.Apply(Settings);
        Dialogs?.RefreshBackdrop();
        RefreshAccentHint();
    }

    private void RefreshAccentHint() =>
        AccentHint = Settings.UseWindowsAccent && SystemState.Accent(Settings).FellBack ? Loc.T("AccentFallbackHint") : string.Empty;

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(_store.Directory);
        Open(_store.Directory);
    }

    // ================================================================== Update check (opt-in)

    [ObservableProperty]
    private bool _updateCheckEnabled;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private PillState _updateState;

    [ObservableProperty]
    private string? _updateUrl;

    [ObservableProperty]
    private bool _updateBusy;

    private bool _updateIsNewer;

    public bool UpdateAvailable => UpdateState == PillState.Ok && UpdateUrl is not null && _updateIsNewer;

    partial void OnUpdateCheckEnabledChanged(bool value) => Save(s => s.UpdateCheckEnabled = value);

    partial void OnUpdateStateChanged(PillState value) => OnPropertyChanged(nameof(UpdateAvailable));

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        UpdateBusy = true;
        UpdateState = PillState.Busy;
        UpdateStatusText = Loc.T("UpdChecking");
        try
        {
            var result = await UpdateChecker.CheckAsync(Version);
            Settings = _store.Update(s =>
            {
                s.LastUpdateCheck = DateTimeOffset.Now;
                if (result.Outcome is UpdateCheckOutcome.UpToDate or UpdateCheckOutcome.UpdateAvailable)
                {
                    s.LatestVersion = result.LatestVersion;
                    s.LatestReleaseUrl = result.ReleaseUrl;
                }
            });
            _fileStamp = FileStamp();
            _ = TrayClient.SendAsync(new TrayRequest(TrayCommand.UpdateCheck));
            ShowUpdateResult(result);
        }
        catch (Exception)
        {
            ShowUpdateResult(new UpdateCheckResult(UpdateCheckOutcome.Failed));
        }
        finally
        {
            UpdateBusy = false;
        }
    }

    [RelayCommand]
    private void OpenRelease() => Open(UpdateUrl ?? UpdateChecker.ReleasesPage);

    private void ShowStoredUpdateResult(MuteitSettings s)
    {
        if (s.LatestVersion is { } latest)
        {
            ShowUpdateResult(new UpdateCheckResult(
                UpdateChecker.IsNewer(latest, Version) ? UpdateCheckOutcome.UpdateAvailable : UpdateCheckOutcome.UpToDate,
                latest,
                s.LatestReleaseUrl));
        }
    }

    private void ShowUpdateResult(UpdateCheckResult result)
    {
        _updateIsNewer = result.Outcome == UpdateCheckOutcome.UpdateAvailable;
        UpdateUrl = result.ReleaseUrl;
        (UpdateStatusText, UpdateState) = result.Outcome switch
        {
            UpdateCheckOutcome.UpdateAvailable => (Loc.F("UpdAvailableFmt", result.LatestVersion), PillState.Ok),
            UpdateCheckOutcome.UpToDate => (Loc.F("UpdCurrentFmt", Version), PillState.Ok),
            UpdateCheckOutcome.NoReleases => (Loc.T("UpdNoReleases"), PillState.Busy),
            _ => (Loc.T("UpdFailed"), PillState.Error),
        };
        OnPropertyChanged(nameof(UpdateAvailable));
    }

    // ================================================================== Export / import / reset

    [ObservableProperty]
    private string _backupMessage = string.Empty;

    [ObservableProperty]
    private PillState _backupState;

    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        if (Dialogs is null)
        {
            return;
        }

        FlushPendingChanges();
        var path = await Dialogs.SaveFileAsync(Loc.T("ExportTitle"), $"muteit-settings-{DateTime.Now:yyyy-MM-dd}.json");
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, SettingsStore.Export(_store.Load()));
            SetBackup(Loc.F("ExportOkFmt", Path.GetFileName(path)), PillState.Ok);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetBackup(Loc.T("ExportFailed"), PillState.Error);
        }
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        if (Dialogs is null)
        {
            return;
        }

        var path = await Dialogs.OpenFileAsync(Loc.T("ImportTitle"), "JSON", ["*.json"]);
        if (path is null)
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            Settings = _store.Import(json);
            AfterReplace();
            SetBackup(Loc.F("ImportOkFmt", Path.GetFileName(path)), PillState.Ok);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or NotSupportedException)
        {
            SetBackup(Loc.T("ImportFailed"), PillState.Error);
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        if (Dialogs is null || !await Dialogs.ConfirmAsync(Loc.T("ResetTitle"), Loc.T("ResetText"), Loc.T("BtnReset")))
        {
            return;
        }

        Settings = _store.Reset();
        AfterReplace();
        SetBackup(Loc.T("ResetOk"), PillState.Ok);
    }

    /// <summary>After import / reset: every page shows the new values, theme and language follow, the tray reloads.</summary>
    private void AfterReplace()
    {
        _fileStamp = FileStamp();
        Loc.SetLanguage(Settings.Language);
        RefreshDeviceLists();
        LoadFrom(Settings);
        ApplyTheme();
        _ = TrayClient.SendAsync(new TrayRequest(TrayCommand.Reload));
    }

    private void SetBackup(string message, PillState state)
    {
        BackupMessage = message;
        BackupState = state;
    }

    // ================================================================== About page

    public string VersionText => Loc.F("VersionFmt", Version);

    public string VersionPill => "v" + Version;

    [RelayCommand]
    private void OpenLicenses() => Open(Path.Combine(AppContext.BaseDirectory, "licenses"));

    [RelayCommand]
    private void OpenWebsite() => Open("https://sksdesign.de");

    [RelayCommand]
    private void OpenGitHub() => Open("https://github.com/complicatiion/MUTEit");
}
