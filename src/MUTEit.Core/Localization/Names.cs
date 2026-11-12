using MUTEit.Core.Settings;

namespace MUTEit.Core.Localization;

/// <summary>Display names of enum values (tray menu and settings window).</summary>
public static class Names
{
    public static string Mode(MuteMode mode) => Loc.T(mode switch
    {
        MuteMode.PushToTalk => "ModePtt",
        MuteMode.PushToMute => "ModePtm",
        _ => "ModeToggle",
    });

    public static string Overlay(OverlayVisibility visibility) => Loc.T(visibility switch
    {
        OverlayVisibility.Always => "OverlayAlways",
        OverlayVisibility.WhenUnmuted => "OverlayWhenUnmuted",
        _ => "OverlayWhenMuted",
    });
}
