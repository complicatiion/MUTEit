namespace MUTEit.Core.Input;

public enum HotkeyEvent
{
    None,

    /// <summary>The hotkey went down (first press only – auto-repeat is filtered).</summary>
    Pressed,

    /// <summary>The main key of an active hotkey went up.</summary>
    Released,
}

/// <param name="Event">What happened to the hotkey.</param>
/// <param name="Suppress">Swallow this input event (other applications do not see it).</param>
/// <param name="SendDummyKey">
/// A suppressed press held Win or Alt: the hook should send an unassigned key so that releasing Win / Alt does not open
/// the Start menu or a menu bar.
/// </param>
public readonly record struct MatchResult(HotkeyEvent Event, bool Suppress, bool SendDummyKey = false)
{
    public static readonly MatchResult Nothing = new(HotkeyEvent.None, false);
}

/// <summary>
/// Decides for every low-level keyboard / mouse event whether it belongs to the configured hotkey.
/// Pure logic (no Win32), runs inside the hook callback, so it must stay fast and allocation-free.
/// <list type="bullet">
/// <item>Auto-repeat: only the first key-down of a press reports <see cref="HotkeyEvent.Pressed"/>.</item>
/// <item>Modifiers: exact match, or – with <c>ignoreModifiers</c> – at least the required ones (extra Shift / Ctrl … allowed).</item>
/// <item>Suppress: down, repeats and up of a matched press are swallowed together, never only half of them.</item>
/// </list>
/// </summary>
public sealed class HotkeyMatcher
{
    private readonly HashSet<int> _heldModifiers = [];
    private HotkeyBinding? _binding;
    private bool _ignoreModifiers;
    private bool _suppress;
    private bool _active;
    private bool _suppressing;

    public HotkeyBinding? Binding => _binding;

    /// <summary>True while the hotkey is held down.</summary>
    public bool IsActive => _active;

    public void Configure(HotkeyBinding? binding, bool ignoreModifiers, bool suppress)
    {
        _binding = binding is { IsValid: true } ? binding : null;
        _ignoreModifiers = ignoreModifiers;
        _suppress = suppress;
        _active = false;
        _suppressing = false;
    }

    /// <summary>Forget all held keys (session lock, hook reinstalled). Returns true when an active press was cut off.</summary>
    public bool Reset()
    {
        var wasActive = _active;
        _heldModifiers.Clear();
        _active = false;
        _suppressing = false;
        return wasActive;
    }

    public HotkeyModifiers HeldModifiers
    {
        get
        {
            var flags = HotkeyModifiers.None;
            foreach (var vk in _heldModifiers)
            {
                flags |= Vk.ModifierOf(vk);
            }

            return flags;
        }
    }

    /// <summary>Keyboard event (virtual-key code with left / right distinction, as delivered by WH_KEYBOARD_LL).</summary>
    public MatchResult OnKey(int vk, bool down) => Process(vk, down);

    /// <summary>Mouse button event (<see cref="Vk.MButton"/>, <see cref="Vk.XButton1"/>, <see cref="Vk.XButton2"/>).</summary>
    public MatchResult OnMouse(int vk, bool down) => _binding is { IsMouse: true } ? Process(vk, down) : MatchResult.Nothing;

    private MatchResult Process(int vk, bool down)
    {
        var binding = _binding;
        var isMainKey = binding is not null && IsMainKey(binding.Key, vk);
        var result = MatchResult.Nothing;

        if (isMainKey)
        {
            if (down)
            {
                if (_active)
                {
                    // Auto-repeat of the held hotkey: keep swallowing it if the press was swallowed.
                    result = new MatchResult(HotkeyEvent.None, _suppressing);
                }
                else if (ModifiersMatch(binding!, vk))
                {
                    _active = true;
                    _suppressing = _suppress;
                    var held = HeldModifiers;
                    var dummy = _suppress && (held & (HotkeyModifiers.Win | HotkeyModifiers.Alt)) != 0;
                    result = new MatchResult(HotkeyEvent.Pressed, _suppress, dummy);
                }
            }
            else if (_active)
            {
                _active = false;
                var swallow = _suppressing;
                _suppressing = false;
                result = new MatchResult(HotkeyEvent.Released, swallow);
            }
        }

        // Modifier bookkeeping after the decision: a modifier used as the main key does not count for itself.
        if (Vk.IsModifier(vk))
        {
            if (down)
            {
                _heldModifiers.Add(vk);
            }
            else
            {
                _heldModifiers.Remove(vk);
            }
        }

        return result;
    }

    private bool ModifiersMatch(HotkeyBinding binding, int vk)
    {
        var held = HeldModifiers & ~Vk.ModifierOf(vk);
        var required = binding.Modifiers;
        return _ignoreModifiers ? (held & required) == required : held == required;
    }

    /// <summary>Exact key, or a generic modifier code (Ctrl / Alt / Shift) that matches either side.</summary>
    private static bool IsMainKey(int bindingKey, int vk) => bindingKey == vk || bindingKey switch
    {
        Vk.Control => vk is Vk.LControl or Vk.RControl,
        Vk.Menu => vk is Vk.LMenu or Vk.RMenu,
        Vk.Shift => vk is Vk.LShift or Vk.RShift,
        _ => false,
    };
}
