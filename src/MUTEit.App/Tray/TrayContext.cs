using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using MUTEit.Core.Audio;
using MUTEit.Core.Ipc;
using MUTEit.Core.Localization;
using MUTEit.Core.Settings;
using MUTEit.Core.Updates;
using MUTEit.Platform.Windows;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace MUTEit.App.Tray;

/// <summary>
/// The tray: icon (state indicator), left click = mute / unmute, right click = menu with "Settings…" on top.
/// Owns the microphone controller, the hotkey hooks, the sounds, the desktop overlay, the taskbar switch and the pipe
/// server for the settings window.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly SettingsStore _store = new();
    private readonly IconFactory _icons = new();
    private readonly NotifyIcon _notify;
    private readonly ContextMenuStrip _menu;
    private readonly MenuRenderer _renderer;
    private readonly MicrophoneController _mic;
    private readonly HotkeyService _hotkeys;
    private readonly SoundPlayer _sounds = new();
    private readonly TrayPipeServer _pipe;
    private readonly WinFormsTimer _updateTimer = new() { Interval = 2 * 60 * 1000 };
    private readonly WinFormsTimer _refreshTimer = new() { Interval = 1500 };
    private readonly Font _font = new("Segoe UI", 9f);
    private readonly Font _boldFont = new("Segoe UI", 9f, FontStyle.Bold);

    private MuteitSettings _settings;
    private TrayPalette _palette;
    private OverlayWindow? _overlay;
    private string? _overlayKey;
    private TaskbarSwitchWindow? _taskbar;
    private ForegroundTracker? _foreground;
    private string? _taskbarKey;
    private Icon? _taskbarIcon;
    private Icon? _taskbarBadge;
    private bool _jumpListSet;
    private Icon? _trayIcon;
    private string? _trayIconKey;
    private string? _balloonUrl;
    private bool _exiting;
    private bool _exitApplied;
    private bool _disposed;

    private ToolStripMenuItem _settingsItem = null!;
    private ToolStripMenuItem _statusItem = null!;
    private ToolStripMenuItem _toggleItem = null!;
    private ToolStripMenuItem _modeItem = null!;
    private readonly Dictionary<MuteMode, ToolStripMenuItem> _modeItems = [];
    private ToolStripMenuItem _micItem = null!;
    private ToolStripMenuItem _overlayItem = null!;
    private ToolStripMenuItem _overlayOff = null!;
    private readonly Dictionary<OverlayVisibility, ToolStripMenuItem> _overlayItems = [];
    private ToolStripMenuItem _taskbarItem = null!;
    private ToolStripMenuItem _autostartItem = null!;
    private ToolStripMenuItem _exitItem = null!;

    public TrayContext()
    {
        var firstRun = !File.Exists(_store.FilePath);
        _settings = _store.Load();
        Loc.SetLanguage(_settings.Language);
        _palette = TrayPalette.Create(SystemState.IsDark(_settings.Theme), SystemState.Accent(_settings));
        _renderer = new MenuRenderer(_palette);
        _icons.LoadCustom(_settings);

        _menu = BuildMenu();

        // Creating the menu (a Control) installs the Windows Forms synchronisation context.
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _notify = new NotifyIcon { ContextMenuStrip = _menu, Visible = false };
        _notify.MouseClick += OnTrayClick;
        _notify.BalloonTipClicked += (_, _) =>
        {
            if (_balloonUrl is { } url)
            {
                OpenUrl(url);
            }
        };

        _mic = new MicrophoneController(ui);
        _mic.Changed += UpdateUi;
        _mic.Select(_settings.Microphone);

        _hotkeys = new HotkeyService(ui);
        _hotkeys.Pressed += () => Apply(MuteLogic.OnHotkeyPressed(_settings.Mode), MuteSource.Hotkey);
        _hotkeys.Released += () => Apply(MuteLogic.OnHotkeyReleased(_settings.Mode), MuteSource.Hotkey);
        _hotkeys.Apply(_settings);

        _pipe = new TrayPipeServer(ui, Handle);

        // Optional update check: two minutes after the start, then every hour (at most one request per 24 h).
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateTimer.Start();
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            _hotkeys.Reinstall();
            _mic.Refresh();
            _overlayKey = null;
            UpdateUi();
        };

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.SessionEnding += OnSessionEnding;
        Loc.Changed += OnLanguageChanged;

        // Start state: push modes always start in their default state; otherwise the configured initial state.
        Apply(MuteLogic.OnStartup(_settings.InitialState, _settings.Mode), MuteSource.Startup);

        _pipe.Start();
        UpdateUi();
        _notify.Visible = true;

        // "Start with Windows" is on by default: switched on exactly once. Later the user's choice is kept.
        if (!_settings.AutostartInitialized)
        {
            Autostart.Set(true);
            _settings = _store.Update(s => s.AutostartInitialized = true);
            UpdateUi();
        }

        if (firstRun && _settings.Notifications)
        {
            var hotkey = _settings.Hotkey?.Display() ?? Loc.T("HotkeyNone");
            _notify.ShowBalloonTip(9000, Loc.T("FirstRunTitle"), Loc.F("FirstRunTextFmt", hotkey), ToolTipIcon.None);
        }
    }

    public static string CurrentVersion =>
        typeof(TrayContext).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    /// <summary>Crash handler: leave the microphone in the configured exit state.</summary>
    public void EmergencyExit()
    {
        try
        {
            ApplyExitState();
        }
        catch (Exception)
        {
            // nothing left to do
        }
    }

    public static void LogError(Exception ex)
    {
        try
        {
            var dir = SettingsStore.DefaultDirectory();
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "error.log"), $"{DateTimeOffset.Now:O} {ex}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // nothing left to do
        }
    }

    /// <summary>Runs once (Windows Forms disposes the context itself, the host again as a safety net).</summary>
    protected override void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.SessionEnding -= OnSessionEnding;
            Loc.Changed -= OnLanguageChanged;
            _updateTimer.Dispose();
            _refreshTimer.Dispose();
            _pipe.Dispose();
            _hotkeys.Dispose();
            _sounds.Dispose();
            _mic.Dispose();
            _foreground?.Dispose();
            _overlay?.Dispose();
            _taskbar?.Dispose();
            _taskbarIcon?.Dispose();
            _taskbarBadge?.Dispose();
            _notify.Visible = false;
            _notify.Dispose();
            _trayIcon?.Dispose();
            _menu.Dispose();
            _icons.Dispose();
            _font.Dispose();
            _boldFont.Dispose();
        }

        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ Microphone

    /// <summary>Executes a mute action; plays the confirmation sound when the state really changed.</summary>
    private void Apply(MuteAction action, MuteSource source)
    {
        var before = _mic.State;
        if (MuteLogic.Target(action, before) is not bool muted)
        {
            return;
        }

        var already = before == (muted ? MicState.Muted : MicState.Unmuted);
        if (!_mic.SetMuted(muted) || already)
        {
            return;
        }

        if (MuteLogic.ShouldPlaySound(_settings, muted, source))
        {
            _sounds.Play(muted, _settings);
        }
    }

    private void ApplyExitState()
    {
        if (_exitApplied)
        {
            return;
        }

        _exitApplied = true;
        Apply(MuteLogic.OnExit(_settings.ExitState), MuteSource.Exit);
    }

    // ------------------------------------------------------------------ Menu

    private ContextMenuStrip BuildMenu()
    {
        var menu = NewDropDown(new ContextMenuStrip());

        _settingsItem = Item(string.Empty, "settings", (_, _) => OpenSettings());
        _settingsItem.Font = _boldFont;

        _statusItem = Item(string.Empty, "mic", null);
        _statusItem.Enabled = false;
        _statusItem.Tag = new MenuTag(Muted: true);

        _toggleItem = Item(string.Empty, "mic-off", (_, _) => Apply(MuteAction.Toggle, MuteSource.Tray));

        _modeItem = Item(string.Empty, "mode", null);
        var modeChildren = new List<ToolStripItem>();
        foreach (var mode in Enum.GetValues<MuteMode>())
        {
            var m = mode;
            var item = Item(string.Empty, null, (_, _) => ChangeMode(m));
            _modeItems[mode] = item;
            modeChildren.Add(item);
        }

        AddSubItems(_modeItem, [.. modeChildren]);

        // Microphone list: filled when the submenu opens (devices come and go).
        _micItem = Item(string.Empty, "device", null);
        NewDropDown((ToolStripDropDownMenu)_micItem.DropDown);
        _micItem.DropDownItems.Add(new ToolStripMenuItem("…"));
        _micItem.DropDown.Opening += (_, _) =>
        {
            ScaleImages(_micItem.DropDown);
            FillMicrophones();
        };

        _overlayItem = Item(string.Empty, "overlay", null);
        _overlayOff = Item(string.Empty, null, (_, _) => ChangeSettings(s => s.OverlayEnabled = false));
        var overlayChildren = new List<ToolStripItem> { _overlayOff, new ToolStripSeparator() };
        foreach (var visibility in Enum.GetValues<OverlayVisibility>())
        {
            var v = visibility;
            var item = Item(string.Empty, null, (_, _) => ChangeSettings(s =>
            {
                s.OverlayEnabled = true;
                s.OverlayVisibility = v;
            }));
            _overlayItems[visibility] = item;
            overlayChildren.Add(item);
        }

        AddSubItems(_overlayItem, [.. overlayChildren]);

        _taskbarItem = Item(string.Empty, "taskbar", (_, _) => ChangeSettings(s => s.TaskbarButton = !s.TaskbarButton));

        _autostartItem = Item(string.Empty, "autostart", (_, _) =>
        {
            Autostart.Set(!Autostart.IsEnabled());
            _settings = _store.Update(s => s.AutostartInitialized = true);
            UpdateUi();
        });

        _exitItem = Item(string.Empty, "exit", (_, _) => ExitTray());

        menu.Items.AddRange(new ToolStripItem[]
        {
            _settingsItem,
            _statusItem,
            new ToolStripSeparator(),
            _toggleItem,
            _modeItem,
            _micItem,
            _overlayItem,
            _taskbarItem,
            new ToolStripSeparator(),
            _autostartItem,
            new ToolStripSeparator(),
            _exitItem,
        });

        menu.Opening += (_, _) =>
        {
            ScaleImages(menu);
            UpdateUi();
        };
        return menu;
    }

    private T NewDropDown<T>(T dropDown)
        where T : ToolStripDropDown
    {
        dropDown.Renderer = _renderer;
        dropDown.Font = _font;
        dropDown.Padding = new Padding(2, 4, 2, 4);
        ScaleImages(dropDown);
        dropDown.DropShadowEnabled = true;
        if (dropDown is ToolStripDropDownMenu m)
        {
            m.ShowImageMargin = true;
            m.ShowCheckMargin = false;
        }

        dropDown.Opened += (_, _) => SystemState.RoundCorners(dropDown.Handle);
        return dropDown;
    }

    private ToolStripMenuItem Item(string text, string? image, EventHandler? onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            Padding = new Padding(2, 4, 2, 4),
            Image = image is null ? null : MenuImage(image),
        };
        if (onClick is not null)
        {
            item.Click += onClick;
        }

        return item;
    }

    private void AddSubItems(ToolStripMenuItem parent, params ToolStripItem[] children)
    {
        NewDropDown((ToolStripDropDownMenu)parent.DropDown);
        parent.DropDownItems.AddRange(children);
        parent.DropDown.Opening += (_, _) => ScaleImages(parent.DropDown);
    }

    /// <summary>Menu images are 32 px; show them at 16 logical pixels of the current DPI.</summary>
    private static void ScaleImages(ToolStrip strip)
    {
        var px = Math.Max(16, (int)Math.Round(16 * strip.DeviceDpi / 96.0));
        strip.ImageScalingSize = new Size(px, px);
    }

    private Image MenuImage(string name, bool accent = false) =>
        _icons.MenuImage(name, accent ? _palette.Accent : _palette.TextMuted);

    private void SetImage(ToolStripMenuItem item, string name, bool accent = false) => item.Image = MenuImage(name, accent);

    private void SetCheck(ToolStripMenuItem item, bool isChecked, string? image = null)
    {
        item.Checked = isChecked;
        item.Image = image is not null ? MenuImage(image, isChecked) : isChecked ? MenuImage("check", true) : null;
    }

    private void FillMicrophones()
    {
        foreach (var old in _micItem.DropDownItems.Cast<ToolStripItem>().ToList())
        {
            old.Dispose();
        }

        _micItem.DropDownItems.Clear();
        var selection = _settings.Microphone;

        void Add(string text, string value)
        {
            var item = Item(text, null, (_, _) => ChangeSettings(s =>
            {
                s.Microphone = value;
                s.MicrophoneName = DeviceSelection.IsSpecific(value) ? text : null;
            }));
            SetCheck(item, selection == value);
            _micItem.DropDownItems.Add(item);
        }

        Add(Loc.T("MicDefaultComm"), DeviceSelection.DefaultCommunications);
        Add(Loc.T("MicDefault"), DeviceSelection.DefaultConsole);
        Add(Loc.T("MicAll"), DeviceSelection.All);
        _micItem.DropDownItems.Add(new ToolStripSeparator());

        var devices = AudioDevices.Microphones();
        foreach (var device in devices)
        {
            Add(device.Name, device.Id);
        }

        // Selected microphone unplugged: still listed (greyed) so the user sees what is configured.
        if (DeviceSelection.IsSpecific(selection) && devices.All(d => d.Id != selection))
        {
            var missing = Item(Loc.F("MicMissingFmt", _settings.MicrophoneName ?? "?"), null, null);
            missing.Enabled = false;
            SetCheck(missing, true);
            _micItem.DropDownItems.Add(missing);
        }

        if (devices.Count == 0)
        {
            var none = Item(Loc.T("MicNone"), null, null);
            none.Enabled = false;
            _micItem.DropDownItems.Add(none);
        }
    }

    // ------------------------------------------------------------------ UI state

    private void UpdateUi()
    {
        if (_exiting || _disposed)
        {
            return;
        }

        var state = _mic.State;
        var muted = state is MicState.Muted or MicState.Mixed;

        _settingsItem.Text = Loc.T("TrayOpenSettings");
        SetImage(_settingsItem, "settings", true);

        _statusItem.Text = StatusText(state);
        SetImage(_statusItem, muted ? "mic-off" : "mic");

        _toggleItem.Text = Loc.T(muted ? "TrayUnmute" : "TrayMute");
        _toggleItem.Enabled = state != MicState.Unavailable;
        SetImage(_toggleItem, muted ? "mic" : "mic-off", muted);

        _modeItem.Text = Loc.T("TrayMode");
        SetImage(_modeItem, "mode");
        foreach (var (mode, item) in _modeItems)
        {
            item.Text = Names.Mode(mode);
            SetCheck(item, _settings.Mode == mode);
        }

        _micItem.Text = Loc.T("TrayMicrophone");
        SetImage(_micItem, "device");

        _overlayItem.Text = Loc.T("TrayOverlay");
        SetImage(_overlayItem, "overlay", _settings.OverlayEnabled);
        _overlayOff.Text = Loc.T("OverlayOff");
        SetCheck(_overlayOff, !_settings.OverlayEnabled);
        foreach (var (visibility, item) in _overlayItems)
        {
            item.Text = Names.Overlay(visibility);
            SetCheck(item, _settings.OverlayEnabled && _settings.OverlayVisibility == visibility);
        }

        _taskbarItem.Text = Loc.T("TrayTaskbar");
        SetCheck(_taskbarItem, _settings.TaskbarButton, "taskbar");

        _autostartItem.Text = Loc.T("TrayAutostart");
        SetCheck(_autostartItem, Autostart.IsEnabled(), "autostart");
        _exitItem.Text = Loc.T("TrayExit");
        SetImage(_exitItem, "exit");

        UpdateTrayIcon(state);
        UpdateOverlay(state);
        UpdateTaskbar(state);

        var tooltip = Loc.F("TooltipFmt", StatusText(state));
        _notify.Text = tooltip.Length > 63 ? tooltip[..62] + "…" : tooltip;
    }

    private void UpdateTrayIcon(MicState state)
    {
        var size = SystemInformation.SmallIconSize.Width;
        var light = SystemState.IsTaskbarLight();
        var iconState = IconFactory.StateOf(state);
        var key = $"{iconState}|{light}|{size}|{_palette.Accent.ToArgb()}|{_settings.IconMode}|{_settings.GeneralIconPath}|{_settings.MutedIconPath}|{_settings.UnmutedIconPath}";
        if (key == _trayIconKey)
        {
            return;
        }

        var old = _trayIcon;
        _trayIcon = _icons.StateIcon(iconState, _palette, light, size);
        _notify.Icon = _trayIcon;
        old?.Dispose();
        _trayIconKey = key;
    }

    private string StatusText(MicState state)
    {
        var name = _mic.DeviceName ?? _settings.MicrophoneName ?? string.Empty;
        var text = state switch
        {
            MicState.Muted => Loc.T("StMuted"),
            MicState.Unmuted => Loc.T("StLive"),
            MicState.Mixed => Loc.T("StMixed"),
            _ => Loc.T("StUnavailable"),
        };

        if (_settings.Microphone == DeviceSelection.All && state != MicState.Unavailable)
        {
            return $"{text} · {Loc.F("AllDevicesFmt", _mic.DeviceCount)}";
        }

        return state == MicState.Unavailable || name.Length == 0 ? text : $"{text} · {name}";
    }

    // ------------------------------------------------------------------ Overlay

    private void UpdateOverlay(MicState state)
    {
        // While the overlay is dragged it keeps its image and position; "Moved" redraws it afterwards.
        if (_overlay is { IsDragging: true })
        {
            return;
        }

        if (!MuteLogic.OverlayVisible(_settings, state))
        {
            _overlay?.HideOverlay();
            return;
        }

        if (_overlay is null)
        {
            _overlay = new OverlayWindow();
            _overlay.Clicked += () => Apply(MuteAction.Toggle, MuteSource.Overlay);
            _overlay.Moved += point =>
            {
                _settings = _store.Update(s =>
                {
                    s.OverlayX = point.X;
                    s.OverlayY = point.Y;
                    s.OverlayAnchor = OverlayAnchor.Free;
                });
                _overlayKey = null;
                UpdateUi();
            };
            _overlay.DpiChangedTo += () =>
            {
                _overlayKey = null;
                UpdateUi();
            };
        }

        _overlay.ClickEnabled = _settings.OverlayClickToggles;
        var location = OverlayLocation(out var dpi);
        var size = (int)Math.Round(_settings.OverlaySize * dpi / 96.0);
        var iconState = IconFactory.StateOf(state);
        var key = $"{iconState}|{size}|{_settings.OverlayOpacity}|{_palette.Accent.ToArgb()}|{_settings.IconMode}|{_settings.GeneralIconPath}|{_settings.MutedIconPath}|{_settings.UnmutedIconPath}|{location}|{_settings.OverlayStyle}";
        if (key != _overlayKey)
        {
            _overlay.Location = location;
            _overlay.SetImage(_icons.OverlayBitmap(iconState, _palette, size, _settings.OverlayStyle), _settings.OverlayOpacity);
            _overlayKey = key;
        }

        _overlay.ShowOverlay();
    }

    /// <summary>
    /// Corner of the chosen screen (work area, 24 px margin), or the dragged position if it is still on a screen.
    /// A missing screen falls back to the primary one.
    /// </summary>
    private Point OverlayLocation(out int dpi)
    {
        if (_settings.OverlayAnchor == OverlayAnchor.Free && _settings.OverlayX is { } x && _settings.OverlayY is { } y)
        {
            dpi = NativeApp.DpiAt(x, y);
            var size = (int)Math.Round(_settings.OverlaySize * dpi / 96.0);
            var center = new Point(x + (size / 2), y + (size / 2));
            if (Screen.AllScreens.Any(s => s.WorkingArea.Contains(center)))
            {
                return new Point(x, y);
            }
        }

        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.OverlayScreen)
                     ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
        var area = screen.WorkingArea;
        dpi = NativeApp.DpiAt(area.Left + (area.Width / 2), area.Top + (area.Height / 2));
        var px = (int)Math.Round(_settings.OverlaySize * dpi / 96.0);
        var margin = (int)Math.Round(24 * dpi / 96.0);
        var left = area.Left + margin;
        var right = area.Right - px - margin;
        var top = area.Top + margin;
        var bottom = area.Bottom - px - margin;
        return _settings.OverlayAnchor switch
        {
            OverlayAnchor.TopLeft => new Point(left, top),
            OverlayAnchor.TopRight => new Point(right, top),
            OverlayAnchor.BottomLeft => new Point(left, bottom),
            _ => new Point(right, bottom),
        };
    }

    /// <summary>Screens for the settings window ("Screen 1 · 1920 × 1080", primary first).</summary>
    private static List<ScreenInfo> ScreenList()
    {
        var screens = Screen.AllScreens;
        var list = new List<ScreenInfo>(screens.Length);
        for (var i = 0; i < screens.Length; i++)
        {
            var b = screens[i].Bounds;
            var name = Loc.F("ScreenFmt", i + 1, b.Width, b.Height) + (screens[i].Primary ? " · " + Loc.T("ScreenPrimary") : string.Empty);
            list.Add(new ScreenInfo(screens[i].DeviceName, name, screens[i].Primary));
        }

        return list.OrderByDescending(s => s.IsPrimary).ToList();
    }

    // ------------------------------------------------------------------ Taskbar switch

    private void UpdateTaskbar(MicState state)
    {
        if (!_settings.TaskbarButton)
        {
            if (_taskbar is { IsHandleCreated: true })
            {
                Shell.SetOverlayIcon(_taskbar.Handle, IntPtr.Zero, null);
                _taskbar.Withdraw();
                _taskbarKey = null;
            }

            if (_jumpListSet)
            {
                Shell.SetJumpList(Shell.TaskbarSwitchAppId, Program.ExePath, []);
                _jumpListSet = false;
            }

            _foreground?.Dispose();
            _foreground = null;
            return;
        }

        if (_taskbar is null)
        {
            _taskbar = new TaskbarSwitchWindow();
            _taskbar.Clicked += () =>
            {
                Apply(MuteAction.Toggle, MuteSource.Taskbar);

                // Hand the focus back to the game / call window that had it before the click.
                _taskbar?.BeginInvoke(new Action(() => _foreground?.RestoreFocus()));
            };
            _taskbar.CloseRequested += () => ChangeSettings(s => s.TaskbarButton = false);
            _taskbar.TaskbarRecreated += () =>
            {
                _taskbarKey = null;
                UpdateUi();
            };
        }

        _foreground ??= new ForegroundTracker();
        _taskbar.Present();
        if (!_jumpListSet)
        {
            SetJumpList();
        }

        var iconState = IconFactory.StateOf(state);
        var size = SystemInformation.IconSize.Width;
        var light = SystemState.IsTaskbarLight();
        var title = Loc.F("TaskbarTitleFmt", StatusText(state));
        var key = $"{iconState}|{size}|{light}|{_palette.Accent.ToArgb()}|{title}|{_settings.IconMode}|{_settings.GeneralIconPath}|{_settings.MutedIconPath}|{_settings.UnmutedIconPath}";
        if (key == _taskbarKey)
        {
            return;
        }

        _taskbar.Text = title;
        var oldIcon = _taskbarIcon;
        _taskbarIcon = _icons.StateIcon(iconState, _palette, light, size);
        _taskbar.Icon = _taskbarIcon;
        oldIcon?.Dispose();

        // Badge: red (with slash) = muted, accent = live. Visible also on a pinned button.
        var oldBadge = _taskbarBadge;
        _taskbarBadge = _icons.BadgeIcon(iconState, _palette);
        Shell.SetOverlayIcon(_taskbar.Handle, _taskbarBadge.Handle, StatusText(state));
        oldBadge?.Dispose();
        _taskbarKey = key;
    }

    private void SetJumpList()
    {
        Shell.SetJumpList(Shell.TaskbarSwitchAppId, Program.ExePath,
        [
            (Loc.T("JumpSettings"), "--settings"),
            (Loc.T("JumpExit"), "--exit"),
        ]);
        _jumpListSet = true;
    }

    // ------------------------------------------------------------------ Actions

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Apply(MuteAction.Toggle, MuteSource.Tray);
        }
    }

    private void ChangeMode(MuteMode mode)
    {
        ChangeSettings(s => s.Mode = mode);
        Apply(MuteLogic.Baseline(mode), MuteSource.Mode);
    }

    private void ChangeSettings(Action<MuteitSettings> change)
    {
        var before = _settings;
        _settings = _store.Update(change);
        ApplySettings(before);
    }

    private void Reload()
    {
        var before = _settings;
        _settings = _store.Load();
        Loc.SetLanguage(_settings.Language);
        ApplySettings(before);
        if (before.Mode != _settings.Mode)
        {
            Apply(MuteLogic.Baseline(_settings.Mode), MuteSource.Mode);
        }
    }

    /// <summary>Applies changed settings to every part of the tray.</summary>
    private void ApplySettings(MuteitSettings before)
    {
        _mic.Select(_settings.Microphone);
        _hotkeys.Apply(_settings);
        if (before.MuteSoundPath != _settings.MuteSoundPath || before.UnmuteSoundPath != _settings.UnmuteSoundPath
            || before.OutputDevice != _settings.OutputDevice)
        {
            _sounds.ClearCache();
        }

        if (before.IconMode != _settings.IconMode || before.GeneralIconPath != _settings.GeneralIconPath
            || before.MutedIconPath != _settings.MutedIconPath || before.UnmutedIconPath != _settings.UnmutedIconPath)
        {
            _icons.LoadCustom(_settings);
        }

        if (before.Theme != _settings.Theme || before.UseWindowsAccent != _settings.UseWindowsAccent)
        {
            RefreshPalette();
        }

        _overlayKey = null;
        UpdateUi();
    }

    private void RefreshPalette()
    {
        _palette = TrayPalette.Create(SystemState.IsDark(_settings.Theme), SystemState.Accent(_settings));
        _renderer.Palette = _palette;
        _icons.ClearCache();
        _trayIconKey = null;
        _overlayKey = null;
        _taskbarKey = null;
    }

    private void OnLanguageChanged()
    {
        if (_jumpListSet)
        {
            SetJumpList();
        }

        _taskbarKey = null;
        UpdateUi();
    }

    private static void OpenSettings()
    {
        // Already open: bring it to the front.
        if (EventWaitHandle.TryOpenExisting(Program.SettingsShowEventName, out var show))
        {
            // The settings process may then take the foreground (otherwise Windows only flashes its button).
            Shell.AllowAnyForeground();
            using (show)
            {
                show.Set();
            }

            return;
        }

        Program.Start("--settings");
    }

    private void ExitTray()
    {
        if (_exiting)
        {
            return;
        }

        ApplyExitState();
        _exiting = true;
        _overlay?.HideOverlay();
        if (_taskbar is { IsHandleCreated: true })
        {
            Shell.SetOverlayIcon(_taskbar.Handle, IntPtr.Zero, null);
            _taskbar.Withdraw();
        }

        _notify.Visible = false;
        ExitThread();
    }

    /// <summary>Opt-in: at most once per 24 h; a new release is announced once with a balloon tip.</summary>
    private async Task CheckForUpdateAsync()
    {
        _updateTimer.Interval = 60 * 60 * 1000;
        if (!_settings.UpdateCheckEnabled || (_settings.LastUpdateCheck is { } last && DateTimeOffset.Now - last < TimeSpan.FromHours(24)))
        {
            return;
        }

        var result = await UpdateChecker.CheckAsync(CurrentVersion);
        if (_disposed)
        {
            return;
        }

        var announce = false;
        _settings = _store.Update(s =>
        {
            s.LastUpdateCheck = DateTimeOffset.Now;
            if (result.Outcome is UpdateCheckOutcome.UpToDate or UpdateCheckOutcome.UpdateAvailable)
            {
                s.LatestVersion = result.LatestVersion;
                s.LatestReleaseUrl = result.ReleaseUrl;
            }

            if (result.Outcome == UpdateCheckOutcome.UpdateAvailable && s.NotifiedVersion != result.LatestVersion)
            {
                s.NotifiedVersion = result.LatestVersion;
                announce = true;
            }
        });

        if (announce && _settings.Notifications)
        {
            _balloonUrl = result.ReleaseUrl ?? UpdateChecker.ReleasesPage;
            _notify.ShowBalloonTip(10000, Loc.F("UpdateBalloonTitleFmt", result.LatestVersion), Loc.T("UpdateBalloonText"), ToolTipIcon.Info);
        }
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogError(ex);
        }
    }

    // ------------------------------------------------------------------ Pipe

    private TrayResponse Handle(TrayRequest request)
    {
        switch (request.Command)
        {
            case TrayCommand.Status:
                break;
            case TrayCommand.Reload:
            case TrayCommand.UpdateCheck:
                Reload();
                break;
            case TrayCommand.Toggle:
                Apply(MuteAction.Toggle, MuteSource.SettingsWindow);
                break;
            case TrayCommand.Mute:
                Apply(MuteAction.Mute, MuteSource.SettingsWindow);
                break;
            case TrayCommand.Unmute:
                Apply(MuteAction.Unmute, MuteSource.SettingsWindow);
                break;
            case TrayCommand.SetVolume when request.Value is { } percent:
                _mic.SetVolume(Math.Clamp(percent, 0, 100));
                break;
            case TrayCommand.SuspendHotkey:
                _hotkeys.Suspend(true);
                break;
            case TrayCommand.ResumeHotkey:
                _hotkeys.Suspend(false);
                break;
            case TrayCommand.TestSound:
                _sounds.Play(request.Value == 1, _settings);
                break;
            case TrayCommand.ResetOverlay:
                ChangeSettings(s =>
                {
                    s.OverlayX = null;
                    s.OverlayY = null;
                    s.OverlayAnchor = OverlayAnchor.BottomRight;
                    s.OverlayScreen = null;
                });
                break;
            case TrayCommand.ShowSettings:
                OpenSettings();
                break;
            case TrayCommand.Exit:
                // Apply the exit state, answer, then quit.
                ApplyExitState();
                var timer = new WinFormsTimer { Interval = 300 };
                timer.Tick += (_, _) =>
                {
                    timer.Dispose();
                    ExitTray();
                };
                timer.Start();
                return new TrayResponse(true);
            default:
                return new TrayResponse(false, Error: "unknown command");
        }

        return new TrayResponse(true, Status());
    }

    private TrayStatus Status() => new()
    {
        State = _mic.State,
        VolumePercent = _mic.VolumePercent,
        DeviceName = _settings.Microphone == DeviceSelection.All && _mic.DeviceCount > 0
            ? Loc.F("AllDevicesFmt", _mic.DeviceCount)
            : _mic.DeviceName,
        DeviceCount = _mic.DeviceCount,
        Mode = _settings.Mode,
        HotkeyActive = _hotkeys.IsActive,
        HotkeySuspended = _hotkeys.IsSuspended,
        HotkeyHeld = _hotkeys.IsHeld,
        OverlayVisible = MuteLogic.OverlayVisible(_settings, _mic.State),
        TaskbarButtonVisible = _settings.TaskbarButton,
        SoundError = _sounds.Error,
        IconError = _icons.Error ?? _hotkeys.Error,
        AccentFellBack = SystemState.Accent(_settings).FellBack,
        Screens = ScreenList(),
    };

    // ------------------------------------------------------------------ System events

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Color)
        {
            // Taskbar / app theme or accent colour changed.
            RefreshPalette();
            UpdateUi();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _refreshTimer.Start();

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _refreshTimer.Start();
        }
    }

    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect
            or SessionSwitchReason.SessionLogon)
        {
            _refreshTimer.Start();
        }
    }

    private void OnSessionEnding(object? sender, SessionEndingEventArgs e) => ApplyExitState();
}
