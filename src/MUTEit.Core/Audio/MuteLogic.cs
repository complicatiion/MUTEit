using MUTEit.Core.Settings;

namespace MUTEit.Core.Audio;

/// <summary>Combined state of the controlled microphone(s).</summary>
public enum MicState
{
    Unmuted,
    Muted,

    /// <summary>"All devices": some are muted, some are not.</summary>
    Mixed,

    /// <summary>The selected microphone is not connected / no microphone at all.</summary>
    Unavailable,
}

/// <summary>What to do with the microphone.</summary>
public enum MuteAction
{
    None,
    Mute,
    Unmute,
    Toggle,
}

/// <summary>Who changed the state (sounds are only played for MUTEit's own actions).</summary>
public enum MuteSource
{
    Hotkey,
    Tray,
    Overlay,
    Taskbar,
    SettingsWindow,
    CommandLine,
    Startup,
    Mode,
    Exit,
}

/// <summary>Mute modes as pure rules (tested in MUTEit.Core.Tests).</summary>
public static class MuteLogic
{
    public static MuteAction OnHotkeyPressed(MuteMode mode) => mode switch
    {
        MuteMode.PushToTalk => MuteAction.Unmute,
        MuteMode.PushToMute => MuteAction.Mute,
        _ => MuteAction.Toggle,
    };

    public static MuteAction OnHotkeyReleased(MuteMode mode) => mode switch
    {
        MuteMode.PushToTalk => MuteAction.Mute,
        MuteMode.PushToMute => MuteAction.Unmute,
        _ => MuteAction.None,
    };

    /// <summary>State the mode falls back to (PTT: muted, PTM: unmuted); Toggle has none.</summary>
    public static MuteAction Baseline(MuteMode mode) => mode switch
    {
        MuteMode.PushToTalk => MuteAction.Mute,
        MuteMode.PushToMute => MuteAction.Unmute,
        _ => MuteAction.None,
    };

    /// <summary>At start of the tray: the push modes always win over the initial state.</summary>
    public static MuteAction OnStartup(InitialMicState initial, MuteMode mode)
    {
        var baseline = Baseline(mode);
        if (baseline != MuteAction.None)
        {
            return baseline;
        }

        return initial switch
        {
            InitialMicState.Mute => MuteAction.Mute,
            InitialMicState.Unmute => MuteAction.Unmute,
            _ => MuteAction.None,
        };
    }

    public static MuteAction OnExit(ExitMicState exit) => exit switch
    {
        ExitMicState.Mute => MuteAction.Mute,
        ExitMicState.Unmute => MuteAction.Unmute,
        _ => MuteAction.None,
    };

    /// <summary>Target mute value of an action for the current state; null = nothing to do.</summary>
    public static bool? Target(MuteAction action, MicState current) => action switch
    {
        MuteAction.Mute => true,
        MuteAction.Unmute => false,

        // Mixed ("All devices", some muted): a toggle mutes everything first – the safe direction.
        MuteAction.Toggle => current switch
        {
            MicState.Muted => false,
            MicState.Unavailable => null,
            _ => true,
        },
        _ => null,
    };

    /// <summary>Play the confirmation sound for this change?</summary>
    public static bool ShouldPlaySound(MuteitSettings settings, bool muted, MuteSource source)
    {
        if (source is MuteSource.Startup or MuteSource.Exit or MuteSource.Mode)
        {
            return false;
        }

        if (settings.Mode != MuteMode.Toggle && source == MuteSource.Hotkey && !settings.SoundInPushModes)
        {
            return false;
        }

        return muted ? settings.SoundOnMute : settings.SoundOnUnmute;
    }

    /// <summary>Overlay visible for this state?</summary>
    public static bool OverlayVisible(MuteitSettings settings, MicState state)
    {
        if (!settings.OverlayEnabled)
        {
            return false;
        }

        return settings.OverlayVisibility switch
        {
            OverlayVisibility.WhenMuted => state is MicState.Muted or MicState.Mixed,
            OverlayVisibility.WhenUnmuted => state == MicState.Unmuted,
            _ => true,
        };
    }
}
