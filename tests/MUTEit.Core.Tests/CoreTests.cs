using MUTEit.Core.Audio;
using MUTEit.Core.Input;
using MUTEit.Core.Localization;
using MUTEit.Core.Settings;
using MUTEit.Core.Theme;

namespace MUTEit.Core.Tests;

public class HotkeyMatcherTests
{
    private static HotkeyMatcher Matcher(HotkeyBinding binding, bool ignore = false, bool suppress = false)
    {
        var matcher = new HotkeyMatcher();
        matcher.Configure(binding, ignore, suppress);
        return matcher;
    }

    [Fact]
    public void Exact_combination_fires_once_despite_auto_repeat()
    {
        var m = Matcher(new HotkeyBinding(Vk.M, HotkeyModifiers.Control | HotkeyModifiers.Shift));
        m.OnKey(Vk.LControl, true);
        m.OnKey(Vk.LShift, true);
        Assert.Equal(HotkeyEvent.Pressed, m.OnKey(Vk.M, true).Event);
        Assert.Equal(HotkeyEvent.None, m.OnKey(Vk.M, true).Event);
        Assert.Equal(HotkeyEvent.None, m.OnKey(Vk.M, true).Event);
        Assert.Equal(HotkeyEvent.Released, m.OnKey(Vk.M, false).Event);
    }

    [Fact]
    public void Extra_modifier_blocks_unless_ignored()
    {
        var strict = Matcher(new HotkeyBinding(0x70)); // F1
        strict.OnKey(Vk.LShift, true);
        Assert.Equal(HotkeyEvent.None, strict.OnKey(0x70, true).Event);

        var relaxed = Matcher(new HotkeyBinding(0x70), ignore: true);
        relaxed.OnKey(Vk.LShift, true);
        Assert.Equal(HotkeyEvent.Pressed, relaxed.OnKey(0x70, true).Event);
    }

    [Fact]
    public void Ignore_modifiers_still_requires_the_configured_ones()
    {
        var m = Matcher(new HotkeyBinding(Vk.M, HotkeyModifiers.Control), ignore: true);
        Assert.Equal(HotkeyEvent.None, m.OnKey(Vk.M, true).Event);
        m.OnKey(Vk.M, false);
        m.OnKey(Vk.RControl, true);
        m.OnKey(Vk.LShift, true);
        Assert.Equal(HotkeyEvent.Pressed, m.OnKey(Vk.M, true).Event);
    }

    [Fact]
    public void Suppress_swallows_down_repeat_and_up_together()
    {
        var m = Matcher(new HotkeyBinding(0x70), suppress: true);
        Assert.True(m.OnKey(0x70, true).Suppress);
        Assert.True(m.OnKey(0x70, true).Suppress);
        Assert.True(m.OnKey(0x70, false).Suppress);

        // Other keys are never swallowed.
        Assert.False(m.OnKey(0x71, true).Suppress);
    }

    [Fact]
    public void Release_is_reported_even_when_modifiers_were_let_go_first()
    {
        var m = Matcher(new HotkeyBinding(Vk.M, HotkeyModifiers.Control), suppress: true);
        m.OnKey(Vk.LControl, true);
        Assert.Equal(HotkeyEvent.Pressed, m.OnKey(Vk.M, true).Event);
        m.OnKey(Vk.LControl, false);
        var up = m.OnKey(Vk.M, false);
        Assert.Equal(HotkeyEvent.Released, up.Event);
        Assert.True(up.Suppress);
    }

    [Fact]
    public void Modifier_alone_works_as_push_to_talk_key()
    {
        var m = Matcher(new HotkeyBinding(Vk.RControl));
        Assert.Equal(HotkeyEvent.Pressed, m.OnKey(Vk.RControl, true).Event);
        Assert.True(m.IsActive);
        Assert.Equal(HotkeyEvent.Released, m.OnKey(Vk.RControl, false).Event);

        // Left Ctrl is a different key.
        Assert.Equal(HotkeyEvent.None, m.OnKey(Vk.LControl, true).Event);
    }

    [Fact]
    public void Win_or_alt_combination_asks_for_dummy_key_when_suppressed()
    {
        var m = Matcher(new HotkeyBinding(Vk.M, HotkeyModifiers.Win), suppress: true);
        m.OnKey(Vk.LWin, true);
        Assert.True(m.OnKey(Vk.M, true).SendDummyKey);

        var plain = Matcher(new HotkeyBinding(Vk.M, HotkeyModifiers.Control), suppress: true);
        plain.OnKey(Vk.LControl, true);
        Assert.False(plain.OnKey(Vk.M, true).SendDummyKey);
    }

    [Fact]
    public void Mouse_buttons_only_when_bound()
    {
        var mouse = Matcher(new HotkeyBinding(Vk.XButton1));
        Assert.Equal(HotkeyEvent.Pressed, mouse.OnMouse(Vk.XButton1, true).Event);
        Assert.Equal(HotkeyEvent.Released, mouse.OnMouse(Vk.XButton1, false).Event);
        Assert.Equal(HotkeyEvent.None, mouse.OnMouse(Vk.XButton2, true).Event);

        var keyboard = Matcher(HotkeyBinding.Default);
        Assert.Equal(HotkeyEvent.None, keyboard.OnMouse(Vk.XButton1, true).Event);
    }

    [Fact]
    public void Reset_reports_a_cut_off_press()
    {
        var m = Matcher(new HotkeyBinding(0x70));
        m.OnKey(0x70, true);
        Assert.True(m.Reset());
        Assert.False(m.IsActive);
        Assert.False(m.Reset());
    }
}

public class HotkeyRecorderTests
{
    [Fact]
    public void Records_modifiers_and_key()
    {
        var r = new HotkeyRecorder();
        Assert.Equal(RecorderOutcome.Continue, r.OnKey(Vk.LControl, true, 0));
        Assert.Equal(RecorderOutcome.Continue, r.OnKey(Vk.RMenu, true, 5));
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, r.HeldModifiers);
        Assert.Equal(RecorderOutcome.Assigned, r.OnKey(Vk.M, true, 10));
        Assert.Equal(new HotkeyBinding(Vk.M, HotkeyModifiers.Control | HotkeyModifiers.Alt), r.Result);
    }

    [Fact]
    public void Single_modifier_press_and_release_is_the_hotkey()
    {
        var r = new HotkeyRecorder();
        r.OnKey(Vk.RControl, true, 0);
        Assert.Equal(RecorderOutcome.Assigned, r.OnKey(Vk.RControl, false, 100));
        Assert.Equal(new HotkeyBinding(Vk.RControl), r.Result);
    }

    [Fact]
    public void Escape_once_clears_after_the_double_press_time()
    {
        var r = new HotkeyRecorder();
        Assert.Equal(RecorderOutcome.Continue, r.OnKey(Vk.Escape, true, 1000));
        Assert.True(r.HasPendingClear);
        Assert.Equal(RecorderOutcome.Continue, r.OnTimer(1000 + HotkeyRecorder.DoublePressMs));
        Assert.Equal(RecorderOutcome.Cleared, r.OnTimer(1001 + HotkeyRecorder.DoublePressMs));
        Assert.Null(r.Result);
    }

    [Fact]
    public void Escape_twice_assigns_escape()
    {
        var r = new HotkeyRecorder();
        r.OnKey(Vk.Back, true, 1000);
        r.OnKey(Vk.Back, false, 1050);
        Assert.Equal(RecorderOutcome.Assigned, r.OnKey(Vk.Back, true, 1200));
        Assert.Equal(new HotkeyBinding(Vk.Back), r.Result);
    }

    [Fact]
    public void Delete_with_modifier_is_a_normal_hotkey()
    {
        var r = new HotkeyRecorder();
        r.OnKey(Vk.LShift, true, 0);
        Assert.Equal(RecorderOutcome.Assigned, r.OnKey(Vk.Delete, true, 10));
        Assert.Equal(new HotkeyBinding(Vk.Delete, HotkeyModifiers.Shift), r.Result);
    }

    [Fact]
    public void Mouse_side_button_with_modifier()
    {
        var r = new HotkeyRecorder();
        r.OnKey(Vk.LControl, true, 0);
        Assert.Equal(RecorderOutcome.Continue, r.OnMouse(Vk.LButton, true, 5));
        Assert.Equal(RecorderOutcome.Assigned, r.OnMouse(Vk.XButton2, true, 10));
        Assert.Equal(new HotkeyBinding(Vk.XButton2, HotkeyModifiers.Control), r.Result);
    }
}

public class HotkeyBindingTests
{
    [Fact]
    public void Main_key_modifier_is_not_a_required_modifier()
    {
        var binding = new HotkeyBinding(Vk.RControl, HotkeyModifiers.Control | HotkeyModifiers.Shift);
        Assert.Equal(HotkeyModifiers.Shift, binding.Modifiers);
    }

    [Fact]
    public void Left_and_right_mouse_buttons_are_invalid()
    {
        Assert.False(new HotkeyBinding(Vk.LButton).IsValid);
        Assert.False(new HotkeyBinding(Vk.RButton, HotkeyModifiers.Control).IsValid);
        Assert.True(new HotkeyBinding(Vk.MButton).IsValid);
    }

    [Fact]
    public void Display_is_localised()
    {
        Loc.SetLanguage("de");
        try
        {
            Assert.Equal("Strg + Alt + Umschalt + M", HotkeyBinding.Default.Display());
            Assert.Equal("Strg rechts", new HotkeyBinding(Vk.RControl).Display());
            Assert.Equal("Strg + Maustaste 4 (zurück)", new HotkeyBinding(Vk.XButton1, HotkeyModifiers.Control).Display());
        }
        finally
        {
            Loc.SetLanguage("en");
        }

        Assert.Equal("Ctrl + F12", new HotkeyBinding(0x7B, HotkeyModifiers.Control).Display());
        Assert.Equal("Ctrl + Shift + …", HotkeyBinding.DisplayPartial(HotkeyModifiers.Control | HotkeyModifiers.Shift));
    }
}

public class MuteLogicTests
{
    [Theory]
    [InlineData(MuteMode.Toggle, MuteAction.Toggle, MuteAction.None)]
    [InlineData(MuteMode.PushToTalk, MuteAction.Unmute, MuteAction.Mute)]
    [InlineData(MuteMode.PushToMute, MuteAction.Mute, MuteAction.Unmute)]
    public void Hotkey_press_and_release(MuteMode mode, MuteAction pressed, MuteAction released)
    {
        Assert.Equal(pressed, MuteLogic.OnHotkeyPressed(mode));
        Assert.Equal(released, MuteLogic.OnHotkeyReleased(mode));
    }

    [Fact]
    public void Push_modes_win_over_the_initial_state()
    {
        Assert.Equal(MuteAction.Mute, MuteLogic.OnStartup(InitialMicState.Unmute, MuteMode.PushToTalk));
        Assert.Equal(MuteAction.Unmute, MuteLogic.OnStartup(InitialMicState.Mute, MuteMode.PushToMute));
        Assert.Equal(MuteAction.None, MuteLogic.OnStartup(InitialMicState.DoNotChange, MuteMode.Toggle));
        Assert.Equal(MuteAction.Mute, MuteLogic.OnStartup(InitialMicState.Mute, MuteMode.Toggle));
    }

    [Fact]
    public void Toggle_on_mixed_state_mutes_everything()
    {
        Assert.True(MuteLogic.Target(MuteAction.Toggle, MicState.Mixed));
        Assert.True(MuteLogic.Target(MuteAction.Toggle, MicState.Unmuted));
        Assert.False(MuteLogic.Target(MuteAction.Toggle, MicState.Muted));
        Assert.Null(MuteLogic.Target(MuteAction.Toggle, MicState.Unavailable));
        Assert.Null(MuteLogic.Target(MuteAction.None, MicState.Muted));
    }

    [Fact]
    public void No_sound_on_every_push_press_unless_enabled()
    {
        var s = new MuteitSettings { Mode = MuteMode.PushToTalk };
        Assert.False(MuteLogic.ShouldPlaySound(s, false, MuteSource.Hotkey));
        Assert.True(MuteLogic.ShouldPlaySound(s, false, MuteSource.Tray));
        s.SoundInPushModes = true;
        Assert.True(MuteLogic.ShouldPlaySound(s, false, MuteSource.Hotkey));
        Assert.False(MuteLogic.ShouldPlaySound(s, true, MuteSource.Startup));
        s.SoundOnMute = false;
        Assert.False(MuteLogic.ShouldPlaySound(s, true, MuteSource.Tray));
    }

    [Fact]
    public void Overlay_is_off_by_default_and_follows_the_visibility()
    {
        var s = new MuteitSettings();
        Assert.False(MuteLogic.OverlayVisible(s, MicState.Muted));
        s.OverlayEnabled = true;
        Assert.True(MuteLogic.OverlayVisible(s, MicState.Unmuted));
        s.OverlayVisibility = OverlayVisibility.WhenMuted;
        Assert.True(MuteLogic.OverlayVisible(s, MicState.Muted));
        Assert.True(MuteLogic.OverlayVisible(s, MicState.Mixed));
        Assert.False(MuteLogic.OverlayVisible(s, MicState.Unmuted));
        s.OverlayVisibility = OverlayVisibility.WhenUnmuted;
        Assert.True(MuteLogic.OverlayVisible(s, MicState.Unmuted));
        s.OverlayVisibility = OverlayVisibility.Always;
        Assert.True(MuteLogic.OverlayVisible(s, MicState.Unavailable));
    }
}

public class AccentRulesTests
{
    [Theory]
    [InlineData("#E81123", true)] // Windows red
    [InlineData("#C30052", true)] // Windows raspberry
    [InlineData("#FF8C00", false)] // dark orange: hue 33°, just outside the red window
    [InlineData("#0078D4", false)] // Windows blue
    [InlineData("#4FD1C5", false)]
    [InlineData("#767676", false)] // grey
    public void Red_like_accents_are_detected(string hex, bool redLike)
    {
        Assert.Equal(redLike, AccentRules.IsRedLike(Rgb.FromHex(hex)));
    }

    [Fact]
    public void Default_is_teal_and_red_windows_accent_falls_back()
    {
        var teal = AccentRules.Resolve(null, useWindowsAccent: false, dark: true);
        Assert.Equal("#4FD1C5", teal.Accent.Hex);
        Assert.False(teal.FellBack);

        var red = AccentRules.Resolve(Rgb.FromHex("#E81123"), useWindowsAccent: true, dark: true);
        Assert.Equal("#4FD1C5", red.Accent.Hex);
        Assert.True(red.FellBack);

        Assert.Equal("#0F766E", AccentRules.Resolve(null, false, dark: false).Accent.Hex);
    }

    [Fact]
    public void Windows_accent_reaches_aa_contrast()
    {
        var dark = AccentRules.Resolve(Rgb.FromHex("#0063B1"), useWindowsAccent: true, dark: true);
        Assert.True(Rgb.Contrast(dark.Accent, AccentRules.DarkBackground) >= 4.5);

        var light = AccentRules.Resolve(Rgb.FromHex("#60CDFF"), useWindowsAccent: true, dark: false);
        Assert.True(Rgb.Contrast(light.Accent, AccentRules.LightBackground) >= 4.5);
    }

    [Fact]
    public void Text_on_accent_is_readable()
    {
        foreach (var dark in new[] { true, false })
        {
            var set = AccentRules.Resolve(null, false, dark);
            Assert.True(Rgb.Contrast(set.Accent, set.OnAccent) >= 4.5);
        }
    }
}

public class SettingsTests
{
    [Fact]
    public void Defaults_match_the_specification()
    {
        var s = new MuteitSettings().Normalize();
        Assert.Equal(DeviceSelection.DefaultCommunications, s.Microphone);
        Assert.Equal(MuteMode.Toggle, s.Mode);
        Assert.Equal(HotkeyBinding.Default, s.Hotkey);
        Assert.False(s.OverlayEnabled);
        Assert.False(s.TaskbarButton);
        Assert.False(s.Transparency);
        Assert.False(s.UseWindowsAccent);
        Assert.Equal(ThemeMode.Dark, s.Theme);
        Assert.False(s.UpdateCheckEnabled);
        Assert.False(s.Notifications);
        Assert.Equal(OverlayVisibility.Always, s.OverlayVisibility);
        Assert.Equal(OverlayStyle.Microphone, s.OverlayStyle);
        Assert.Equal(OverlayAnchor.BottomRight, s.OverlayAnchor);
        Assert.Null(s.OverlayScreen);
    }

    [Fact]
    public void Version_2_files_get_a_corner_or_keep_the_free_position()
    {
        var corner = SettingsStore.Parse("{\"Version\":2,\"OverlayEnabled\":true}");
        Assert.Equal(OverlayAnchor.BottomRight, corner.OverlayAnchor);
        Assert.Equal(OverlayStyle.Microphone, corner.OverlayStyle);

        var free = SettingsStore.Parse("{\"Version\":2,\"OverlayX\":40,\"OverlayY\":60}");
        Assert.Equal(OverlayAnchor.Free, free.OverlayAnchor);
        Assert.Equal(40, free.OverlayX);
        Assert.Equal(MuteitSettings.CurrentVersion, free.Version);
    }

    [Fact]
    public void Free_anchor_without_position_falls_back_to_a_corner()
    {
        var s = new MuteitSettings { OverlayAnchor = OverlayAnchor.Free, OverlayScreen = "  ", OverlayStyle = (OverlayStyle)9 }.Normalize();
        Assert.Equal(OverlayAnchor.BottomRight, s.OverlayAnchor);
        Assert.Null(s.OverlayScreen);
        Assert.Equal(OverlayStyle.Microphone, s.OverlayStyle);
    }

    [Fact]
    public void Json_round_trip_keeps_hotkey_and_enums()
    {
        var s = new MuteitSettings
        {
            Hotkey = new HotkeyBinding(Vk.XButton2, HotkeyModifiers.Control | HotkeyModifiers.Win),
            Mode = MuteMode.PushToTalk,
            OverlayEnabled = true,
            OverlayVisibility = OverlayVisibility.Always,
            Theme = ThemeMode.Light,
        };

        var json = SettingsStore.Serialize(s);
        Assert.Contains("\"PushToTalk\"", json, StringComparison.Ordinal);
        var back = SettingsStore.Parse(json);
        Assert.Equal(s.Hotkey, back.Hotkey);
        Assert.Equal(MuteMode.PushToTalk, back.Mode);
        Assert.Equal(OverlayVisibility.Always, back.OverlayVisibility);
        Assert.Equal(ThemeMode.Light, back.Theme);
    }

    [Fact]
    public void Normalize_repairs_values()
    {
        var s = new MuteitSettings
        {
            SoundVolume = 300,
            OverlaySize = 4,
            OverlayOpacity = 0,
            OverlayX = 10,
            OverlayY = null,
            IconMode = IconMode.Dedicated,
            MutedIconPath = "  ",
            OutputDevice = DeviceSelection.All,
            Language = "fr",
            Hotkey = new HotkeyBinding(Vk.LButton),
        }.Normalize();

        Assert.Equal(100, s.SoundVolume);
        Assert.Equal(MuteitSettings.MinOverlaySize, s.OverlaySize);
        Assert.Equal(MuteitSettings.MinOverlayOpacity, s.OverlayOpacity);
        Assert.Null(s.OverlayX);
        Assert.Equal(IconMode.Default, s.IconMode);
        Assert.Equal(DeviceSelection.DefaultOutput, s.OutputDevice);
        Assert.Equal("auto", s.Language);
        Assert.Null(s.Hotkey);
    }

    [Fact]
    public void Store_update_is_read_modify_write()
    {
        var dir = Path.Combine(Path.GetTempPath(), "muteit-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(dir);
            store.Update(s => s.Mode = MuteMode.PushToMute);
            store.Update(s => s.OverlayEnabled = true);
            var loaded = store.Load();
            Assert.Equal(MuteMode.PushToMute, loaded.Mode);
            Assert.True(loaded.OverlayEnabled);

            var exported = SettingsStore.Export(store.Update(s => s.OverlayX = s.OverlayY = 100));
            Assert.Null(SettingsStore.Parse(exported).OverlayX);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Every_string_has_both_languages()
    {
        foreach (var (key, (en, de)) in Strings.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(en), key);
            Assert.False(string.IsNullOrWhiteSpace(de), key);
        }
    }
}
