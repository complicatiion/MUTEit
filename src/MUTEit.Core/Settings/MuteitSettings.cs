using MUTEit.Core.Input;

namespace MUTEit.Core.Settings;

/// <summary>How the hotkey works.</summary>
public enum MuteMode
{
    /// <summary>Every press switches between muted and unmuted.</summary>
    Toggle,

    /// <summary>Muted by default; unmuted only while the hotkey is held.</summary>
    PushToTalk,

    /// <summary>Unmuted by default; muted only while the hotkey is held.</summary>
    PushToMute,
}

/// <summary>Microphone state applied when the tray starts.</summary>
public enum InitialMicState
{
    DoNotChange,
    Mute,
    Unmute,
}

/// <summary>Microphone state left behind when MUTEit quits.</summary>
public enum ExitMicState
{
    Keep,
    Unmute,
    Mute,
}

/// <summary>When the desktop overlay is shown (only while the overlay switch is on).</summary>
public enum OverlayVisibility
{
    Always,
    WhenMuted,
    WhenUnmuted,
}

/// <summary>Where the overlay sits: a corner of a screen, or anywhere (dragged).</summary>
public enum OverlayAnchor
{
    /// <summary>Free position (dragged with the mouse), see <see cref="MuteitSettings.OverlayX"/>.</summary>
    Free,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Symbol of the desktop overlay.</summary>
public enum OverlayStyle
{
    /// <summary>Microphone / crossed-out microphone on a coloured disc (default).</summary>
    Microphone,

    /// <summary>MUTEit logo in the accent colour / red.</summary>
    Logo,
}

/// <summary>Source of the tray, overlay and taskbar icons.</summary>
public enum IconMode
{
    /// <summary>MUTEit logo with state badges.</summary>
    Default,

    /// <summary>One own image; MUTEit adds the state badges.</summary>
    General,

    /// <summary>Two own images, one per state, shown as they are.</summary>
    Dedicated,
}

public enum ThemeMode
{
    Dark,
    Light,

    /// <summary>Follows the Windows app mode.</summary>
    System,
}

/// <summary>Special values of <see cref="MuteitSettings.Microphone"/> and <see cref="MuteitSettings.OutputDevice"/>.</summary>
public static class DeviceSelection
{
    /// <summary>Windows default communications microphone (Teams, Discord, Zoom use it).</summary>
    public const string DefaultCommunications = "default:communications";

    /// <summary>Windows default microphone.</summary>
    public const string DefaultConsole = "default:console";

    /// <summary>Every active microphone at once.</summary>
    public const string All = "all";

    /// <summary>Windows default playback device (notification sounds).</summary>
    public const string DefaultOutput = "default";

    public static bool IsSpecific(string value) =>
        value is not (DefaultCommunications or DefaultConsole or All or DefaultOutput) && !string.IsNullOrWhiteSpace(value);
}

/// <summary>User settings, stored as JSON (see <see cref="SettingsStore"/>).</summary>
public sealed class MuteitSettings
{
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;

    // ------------------------------------------------------------------ Microphone

    /// <summary><see cref="DeviceSelection"/> constant or a Core Audio endpoint id.</summary>
    public string Microphone { get; set; } = DeviceSelection.DefaultCommunications;

    /// <summary>Last known name of a specific microphone (shown while it is unplugged).</summary>
    public string? MicrophoneName { get; set; }

    public InitialMicState InitialState { get; set; } = InitialMicState.DoNotChange;

    public ExitMicState ExitState { get; set; } = ExitMicState.Keep;

    // ------------------------------------------------------------------ Hotkey

    public MuteMode Mode { get; set; } = MuteMode.Toggle;

    /// <summary>Null = no hotkey.</summary>
    public HotkeyBinding? Hotkey { get; set; } = HotkeyBinding.Default;

    /// <summary>Extra Ctrl / Alt / Shift / Win held at the same time do not block the hotkey (games: sprint, crouch).</summary>
    public bool IgnoreModifiers { get; set; }

    /// <summary>The hotkey is swallowed: other applications do not see it.</summary>
    public bool SuppressHotkey { get; set; }

    // ------------------------------------------------------------------ Sounds

    public bool SoundOnMute { get; set; } = true;

    public bool SoundOnUnmute { get; set; } = true;

    /// <summary>Also play the sounds in push-to-talk / push-to-mute (off: they would sound on every press).</summary>
    public bool SoundInPushModes { get; set; }

    /// <summary><see cref="DeviceSelection.DefaultOutput"/> or a Core Audio endpoint id.</summary>
    public string OutputDevice { get; set; } = DeviceSelection.DefaultOutput;

    public string? OutputDeviceName { get; set; }

    /// <summary>0 – 100 %.</summary>
    public int SoundVolume { get; set; } = 50;

    /// <summary>Own sound files (WAV / MP3); null = built-in sound.</summary>
    public string? MuteSoundPath { get; set; }

    public string? UnmuteSoundPath { get; set; }

    // ------------------------------------------------------------------ Overlay and taskbar

    /// <summary>Desktop overlay (off by default).</summary>
    public bool OverlayEnabled { get; set; }

    /// <summary>Default "Always": switching the overlay on shows it right away, whatever the state.</summary>
    public OverlayVisibility OverlayVisibility { get; set; } = OverlayVisibility.Always;

    /// <summary>Edge length in logical pixels (32 – 128).</summary>
    public int OverlaySize { get; set; } = 56;

    /// <summary>10 – 100 %.</summary>
    public int OverlayOpacity { get; set; } = 90;

    public bool OverlayClickToggles { get; set; } = true;

    public OverlayStyle OverlayStyle { get; set; } = OverlayStyle.Microphone;

    /// <summary>Corner of <see cref="OverlayScreen"/>, or <see cref="OverlayAnchor.Free"/> for a dragged position.</summary>
    public OverlayAnchor OverlayAnchor { get; set; } = OverlayAnchor.BottomRight;

    /// <summary>Windows device name of the screen ("\\.\DISPLAY2"); null = primary screen.</summary>
    public string? OverlayScreen { get; set; }

    /// <summary>Top-left corner in physical screen pixels; null = default position (bottom right of the primary screen).</summary>
    public int? OverlayX { get; set; }

    public int? OverlayY { get; set; }

    /// <summary>Taskbar button: a click switches the microphone (off by default).</summary>
    public bool TaskbarButton { get; set; }

    // ------------------------------------------------------------------ Icons

    public IconMode IconMode { get; set; } = IconMode.Default;

    public string? GeneralIconPath { get; set; }

    public string? MutedIconPath { get; set; }

    public string? UnmutedIconPath { get; set; }

    // ------------------------------------------------------------------ Appearance and general

    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    /// <summary>Mica background of the settings window (Windows 11, off by default).</summary>
    public bool Transparency { get; set; }

    /// <summary>Use the Windows accent colour instead of Teal (falls back to Teal for red-like accents).</summary>
    public bool UseWindowsAccent { get; set; }

    /// <summary>Windows notifications (balloon tips: first start, new version). Off by default.</summary>
    public bool Notifications { get; set; }

    /// <summary>"auto", "en" or "de".</summary>
    public string Language { get; set; } = "auto";

    /// <summary>The tray has switched on "Start with Windows" once (first start). Afterwards the user's choice is kept.</summary>
    public bool AutostartInitialized { get; set; }

    /// <summary>Daily check for new releases on GitHub (opt-in, off by default).</summary>
    public bool UpdateCheckEnabled { get; set; }

    public DateTimeOffset? LastUpdateCheck { get; set; }

    public string? LatestVersion { get; set; }

    public string? LatestReleaseUrl { get; set; }

    /// <summary>Version the tray already announced with a balloon tip.</summary>
    public string? NotifiedVersion { get; set; }

    public const int MinOverlaySize = 32;
    public const int MaxOverlaySize = 128;
    public const int MinOverlayOpacity = 10;

    /// <summary>Repairs out-of-range values after loading (hand-edited or older files).</summary>
    public MuteitSettings Normalize()
    {
        // 0.0.1 → 0.0.2: overlay default "When muted" → "Always" (unless the overlay is already in use).
        if (Version < 2 && !OverlayEnabled)
        {
            OverlayVisibility = OverlayVisibility.Always;
        }

        // → 0.0.3: corners; a dragged position stays free.
        if (Version < 3)
        {
            OverlayAnchor = OverlayX is not null && OverlayY is not null ? OverlayAnchor.Free : OverlayAnchor.BottomRight;
        }

        Version = CurrentVersion;
        if (string.IsNullOrWhiteSpace(Microphone))
        {
            Microphone = DeviceSelection.DefaultCommunications;
        }

        if (string.IsNullOrWhiteSpace(OutputDevice)
            || OutputDevice is DeviceSelection.All or DeviceSelection.DefaultCommunications or DeviceSelection.DefaultConsole)
        {
            OutputDevice = DeviceSelection.DefaultOutput;
        }

        Mode = Enum.IsDefined(Mode) ? Mode : MuteMode.Toggle;
        InitialState = Enum.IsDefined(InitialState) ? InitialState : InitialMicState.DoNotChange;
        ExitState = Enum.IsDefined(ExitState) ? ExitState : ExitMicState.Keep;
        OverlayVisibility = Enum.IsDefined(OverlayVisibility) ? OverlayVisibility : OverlayVisibility.Always;
        IconMode = Enum.IsDefined(IconMode) ? IconMode : IconMode.Default;
        Theme = Enum.IsDefined(Theme) ? Theme : ThemeMode.Dark;

        if (Hotkey is not null && !Hotkey.IsValid)
        {
            Hotkey = null;
        }

        SoundVolume = Math.Clamp(SoundVolume, 0, 100);
        OverlaySize = Math.Clamp(OverlaySize, MinOverlaySize, MaxOverlaySize);
        OverlayOpacity = Math.Clamp(OverlayOpacity, MinOverlayOpacity, 100);
        if (OverlayX is null || OverlayY is null)
        {
            OverlayX = null;
            OverlayY = null;
        }

        OverlayStyle = Enum.IsDefined(OverlayStyle) ? OverlayStyle : OverlayStyle.Microphone;
        OverlayAnchor = Enum.IsDefined(OverlayAnchor) ? OverlayAnchor : OverlayAnchor.BottomRight;
        if (OverlayAnchor == OverlayAnchor.Free && OverlayX is null)
        {
            OverlayAnchor = OverlayAnchor.BottomRight;
        }

        if (string.IsNullOrWhiteSpace(OverlayScreen))
        {
            OverlayScreen = null;
        }

        MuteSoundPath = CleanPath(MuteSoundPath);
        UnmuteSoundPath = CleanPath(UnmuteSoundPath);
        GeneralIconPath = CleanPath(GeneralIconPath);
        MutedIconPath = CleanPath(MutedIconPath);
        UnmutedIconPath = CleanPath(UnmutedIconPath);

        // An icon mode without its files falls back to the default icons.
        if (IconMode == IconMode.General && GeneralIconPath is null
            || IconMode == IconMode.Dedicated && (MutedIconPath is null || UnmutedIconPath is null))
        {
            IconMode = IconMode.Default;
        }

        if (Language is not ("auto" or "en" or "de"))
        {
            Language = "auto";
        }

        return this;
    }

    /// <summary>Default state of the microphone in the current mode (PTT: muted, PTM: unmuted, Toggle: none).</summary>
    public bool? BaselineMuted => Mode switch
    {
        MuteMode.PushToTalk => true,
        MuteMode.PushToMute => false,
        _ => null,
    };

    /// <summary>Deep copy.</summary>
    public MuteitSettings Clone() => SettingsStore.Parse(SettingsStore.Serialize(this));

    private static string? CleanPath(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim().Trim('"');
}
