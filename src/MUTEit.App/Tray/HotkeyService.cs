using System.ComponentModel;
using MUTEit.Core.Input;
using MUTEit.Core.Settings;
using MUTEit.Platform.Windows;

namespace MUTEit.App.Tray;

/// <summary>
/// Global hotkey of the tray: low-level hooks (<see cref="InputHook"/>) + <see cref="HotkeyMatcher"/>.
/// The hook callback only decides (match, swallow) and posts the action to the UI thread.
/// </summary>
internal sealed class HotkeyService : IDisposable
{
    private readonly SynchronizationContext _ui;
    private readonly InputHook _hook = new();
    private readonly HotkeyMatcher _matcher = new();

    /// <summary>Safety net: a settings window that crashed while recording never sends "resume".</summary>
    private readonly System.Windows.Forms.Timer _suspendTimeout = new() { Interval = 60_000 };
    private bool _suspended;
    private bool _enabled;
    private bool _mouse;

    public HotkeyService(SynchronizationContext ui)
    {
        _ui = ui;
        _hook.Key = OnKey;
        _hook.MouseButton = OnMouseButton;
        _suspendTimeout.Tick += (_, _) => Suspend(false);
        HotkeyBinding.KeyNameResolver = InputHook.KeyName;
    }

    public event Action? Pressed;

    public event Action? Released;

    public bool IsActive => _enabled && _hook.IsInstalled && !_suspended;

    public bool IsSuspended => _suspended;

    /// <summary>Hotkey held down right now.</summary>
    public bool IsHeld => _matcher.IsActive;

    /// <summary>Last error when installing the hook, null = fine.</summary>
    public string? Error { get; private set; }

    public void Apply(MuteitSettings settings)
    {
        var wasHeld = _matcher.IsActive;
        _matcher.Configure(settings.Hotkey, settings.IgnoreModifiers, settings.SuppressHotkey);
        _enabled = settings.Hotkey is { IsValid: true };
        _mouse = settings.Hotkey?.IsMouse == true;
        Install();
        if (wasHeld)
        {
            Released?.Invoke();
        }
    }

    /// <summary>The settings window records a new hotkey: stop reacting and swallowing.</summary>
    public void Suspend(bool suspend)
    {
        _suspended = suspend;
        _suspendTimeout.Stop();
        if (suspend)
        {
            _suspendTimeout.Start();
        }

        if (suspend && _matcher.Reset())
        {
            Released?.Invoke();
        }
    }

    /// <summary>
    /// After resume from sleep / session unlock: install the hooks again (Windows may have dropped them) and forget held
    /// keys (their key-up may never have arrived).
    /// </summary>
    public void Reinstall()
    {
        if (_matcher.Reset())
        {
            Released?.Invoke();
        }

        Install();
    }

    public void Dispose()
    {
        _suspendTimeout.Dispose();
        _hook.Dispose();
        HotkeyBinding.KeyNameResolver = null;
    }

    private void Install()
    {
        if (!_enabled)
        {
            _hook.Uninstall();
            Error = null;
            return;
        }

        try
        {
            _hook.Install(_mouse);
            Error = null;
        }
        catch (Win32Exception ex)
        {
            Error = ex.Message;
        }
    }

    private bool OnKey(int vk, bool down)
    {
        if (_suspended || !_enabled)
        {
            return false;
        }

        return Dispatch(_matcher.OnKey(vk, down));
    }

    private bool OnMouseButton(int vk, bool down)
    {
        if (_suspended || !_enabled)
        {
            return false;
        }

        return Dispatch(_matcher.OnMouse(vk, down));
    }

    private bool Dispatch(MatchResult result)
    {
        switch (result.Event)
        {
            case HotkeyEvent.Pressed:
                _ui.Post(_ => Pressed?.Invoke(), null);
                break;
            case HotkeyEvent.Released:
                _ui.Post(_ => Released?.Invoke(), null);
                break;
        }

        if (result.SendDummyKey)
        {
            // Not from inside the hook callback: SendInput there would re-enter the hook.
            _ui.Post(_ => InputHook.SendDummyKey(), null);
        }

        return result.Suppress;
    }
}
