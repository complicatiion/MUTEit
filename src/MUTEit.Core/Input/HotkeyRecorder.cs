namespace MUTEit.Core.Input;

public enum RecorderOutcome
{
    /// <summary>Still recording.</summary>
    Continue,

    /// <summary><see cref="HotkeyRecorder.Result"/> holds the new hotkey.</summary>
    Assigned,

    /// <summary>The hotkey was removed (Backspace, Delete or Esc pressed once).</summary>
    Cleared,
}

/// <summary>
/// Hotkey box of the settings window ("Press keys…"). Fed with low-level keyboard / mouse events.
/// <list type="bullet">
/// <item>Modifiers + key, a key alone, or a mouse button (middle, back, forward) with or without modifiers.</item>
/// <item>A modifier alone (e.g. Right Ctrl for push-to-talk): press and release it without another key.</item>
/// <item>Backspace, Delete or Esc once: remove the hotkey. Twice within <see cref="DoublePressMs"/> ms: use that key as hotkey.</item>
/// </list>
/// </summary>
public sealed class HotkeyRecorder
{
    public const int DoublePressMs = 300;

    private readonly List<int> _chord = [];
    private bool _chordHadKey;
    private int? _pendingKey;
    private long _pendingSince;

    public HotkeyBinding? Result { get; private set; }

    /// <summary>A single Backspace / Delete / Esc waits for a possible second press.</summary>
    public bool HasPendingClear => _pendingKey is not null;

    /// <summary>Modifiers held right now (live text "Ctrl + Shift + …").</summary>
    public HotkeyModifiers HeldModifiers
    {
        get
        {
            var flags = HotkeyModifiers.None;
            foreach (var vk in _chord)
            {
                flags |= Vk.ModifierOf(vk);
            }

            return flags;
        }
    }

    public RecorderOutcome OnKey(int vk, bool down, long timestampMs)
    {
        if (Vk.IsModifier(vk))
        {
            if (down)
            {
                if (!_chord.Contains(vk))
                {
                    _chord.Add(vk);
                }

                return RecorderOutcome.Continue;
            }

            // Modifier released without any other key: the modifier (combination) itself is the hotkey.
            if (!_chordHadKey && _chord.Contains(vk) && _pendingKey is null)
            {
                var main = _chord[^1];
                var others = HotkeyModifiers.None;
                foreach (var other in _chord)
                {
                    if (other != main)
                    {
                        others |= Vk.ModifierOf(other);
                    }
                }

                return Assign(new HotkeyBinding(main, others));
            }

            _chord.Remove(vk);
            if (_chord.Count == 0)
            {
                _chordHadKey = false;
            }

            return RecorderOutcome.Continue;
        }

        if (!down)
        {
            return RecorderOutcome.Continue;
        }

        _chordHadKey = true;
        var modifiers = HeldModifiers;
        if (vk is Vk.Escape or Vk.Back or Vk.Delete && modifiers == HotkeyModifiers.None)
        {
            if (_pendingKey == vk && timestampMs - _pendingSince <= DoublePressMs)
            {
                return Assign(new HotkeyBinding(vk));
            }

            _pendingKey = vk;
            _pendingSince = timestampMs;
            return RecorderOutcome.Continue;
        }

        return Assign(new HotkeyBinding(vk, modifiers));
    }

    public RecorderOutcome OnMouse(int vk, bool down, long timestampMs)
    {
        if (!down || vk is not (Vk.MButton or Vk.XButton1 or Vk.XButton2))
        {
            return RecorderOutcome.Continue;
        }

        _chordHadKey = true;
        return Assign(new HotkeyBinding(vk, HeldModifiers));
    }

    /// <summary>Called by a short timer: a single Backspace / Delete / Esc becomes "remove" after <see cref="DoublePressMs"/> ms.</summary>
    public RecorderOutcome OnTimer(long timestampMs)
    {
        if (_pendingKey is not null && timestampMs - _pendingSince > DoublePressMs)
        {
            _pendingKey = null;
            Result = null;
            Reset();
            return RecorderOutcome.Cleared;
        }

        return RecorderOutcome.Continue;
    }

    private RecorderOutcome Assign(HotkeyBinding binding)
    {
        Result = binding;
        _pendingKey = null;
        Reset();
        return RecorderOutcome.Assigned;
    }

    private void Reset()
    {
        _chord.Clear();
        _chordHadKey = false;
    }
}
