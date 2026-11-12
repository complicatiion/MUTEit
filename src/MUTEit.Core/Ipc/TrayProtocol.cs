using System.Text.Json;
using System.Text.Json.Serialization;
using MUTEit.Core.Audio;
using MUTEit.Core.Settings;

namespace MUTEit.Core.Ipc;

/// <summary>
/// Named pipe between the tray (server) and the settings window, the command line (<c>--toggle</c> …) or a second
/// start of the EXE (client). One JSON line request, one JSON line response per connection. The pipe is restricted to
/// the current user (PipeOptions.CurrentUserOnly) and the current Windows session.
/// </summary>
public static class TrayProtocol
{
    public static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string PipeName(int sessionId) => $"MUTEit.Tray.{Environment.UserName}.{sessionId}";
}

public static class TrayCommand
{
    /// <summary>Returns <see cref="TrayStatus"/>.</summary>
    public const string Status = "status";

    /// <summary>Settings file changed – reload it.</summary>
    public const string Reload = "reload";

    public const string Toggle = "toggle";
    public const string Mute = "mute";
    public const string Unmute = "unmute";

    /// <summary>Microphone level in percent (<see cref="TrayRequest.Value"/>, 0 – 100).</summary>
    public const string SetVolume = "setVolume";

    /// <summary>The settings window records a new hotkey: the tray stops reacting to (and swallowing) the old one.</summary>
    public const string SuspendHotkey = "suspendHotkey";

    public const string ResumeHotkey = "resumeHotkey";

    /// <summary>Plays the mute (<see cref="TrayRequest.Value"/> = 1) or unmute (0) sound on the configured device.</summary>
    public const string TestSound = "testSound";

    /// <summary>Overlay back to its default position.</summary>
    public const string ResetOverlay = "resetOverlay";

    /// <summary>Second start of MUTEit.exe: open the settings window.</summary>
    public const string ShowSettings = "showSettings";

    /// <summary>Apply the exit state of the microphone and quit (installer, update, jump list).</summary>
    public const string Exit = "exit";

    /// <summary>Update check finished in the settings window – refresh the stored result.</summary>
    public const string UpdateCheck = "updateCheck";
}

public sealed record TrayRequest(string Command, int? Value = null);

public sealed record TrayResponse(bool Ok, TrayStatus? Status = null, string? Error = null);

/// <summary>Live state of the tray for the settings window.</summary>
public sealed record TrayStatus
{
    public MicState State { get; init; }

    /// <summary>Microphone level 0 – 100 (null for "All devices" or when unavailable).</summary>
    public int? VolumePercent { get; init; }

    /// <summary>Name of the controlled microphone ("All devices: 3" for all).</summary>
    public string? DeviceName { get; init; }

    public int DeviceCount { get; init; }

    public MuteMode Mode { get; init; }

    /// <summary>Keyboard / mouse hook installed and a hotkey is set.</summary>
    public bool HotkeyActive { get; init; }

    /// <summary>The settings window is recording a hotkey.</summary>
    public bool HotkeySuspended { get; init; }

    /// <summary>The hotkey is held down right now (push-to-talk / push-to-mute).</summary>
    public bool HotkeyHeld { get; init; }

    public bool OverlayVisible { get; init; }

    public bool TaskbarButtonVisible { get; init; }

    /// <summary>Last error of the notification sound (missing file, unknown format, device gone), null = fine.</summary>
    public string? SoundError { get; init; }

    /// <summary>Last error of the custom icons (file missing / unreadable), null = fine.</summary>
    public string? IconError { get; init; }

    /// <summary>The Windows accent is red-like: Teal is used instead.</summary>
    public bool AccentFellBack { get; init; }

    /// <summary>Screens for the overlay position (as Windows Forms sees them in the tray).</summary>
    public IReadOnlyList<ScreenInfo> Screens { get; init; } = [];
}

/// <param name="Id">Windows device name ("\\.\DISPLAY1").</param>
/// <param name="Name">Display text ("Screen 1 · 1920 × 1080").</param>
public sealed record ScreenInfo(string Id, string Name, bool IsPrimary);
