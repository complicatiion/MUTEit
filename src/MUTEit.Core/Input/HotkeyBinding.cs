using System.Globalization;
using System.Text.Json.Serialization;
using MUTEit.Core.Localization;

namespace MUTEit.Core.Input;

/// <summary>Modifier keys of a hotkey (left and right are treated alike).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>Windows virtual-key codes used by the hotkey engine.</summary>
public static class Vk
{
    public const int LButton = 0x01;
    public const int RButton = 0x02;
    public const int MButton = 0x04;
    public const int XButton1 = 0x05;
    public const int XButton2 = 0x06;
    public const int Back = 0x08;
    public const int Tab = 0x09;
    public const int Return = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;
    public const int Pause = 0x13;
    public const int Capital = 0x14;
    public const int Escape = 0x1B;
    public const int Space = 0x20;
    public const int Delete = 0x2E;
    public const int M = 0x4D;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int F1 = 0x70;
    public const int F24 = 0x87;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;

    /// <summary>Unassigned key code: sent as a dummy key so a suppressed Win / Alt combination does not open the Start menu / menu bar.</summary>
    public const int Unassigned = 0xE8;

    public static bool IsMouse(int vk) => vk is LButton or RButton or MButton or XButton1 or XButton2;

    /// <summary>Modifier flag of a key (none for normal keys).</summary>
    public static HotkeyModifiers ModifierOf(int vk) => vk switch
    {
        Control or LControl or RControl => HotkeyModifiers.Control,
        Menu or LMenu or RMenu => HotkeyModifiers.Alt,
        Shift or LShift or RShift => HotkeyModifiers.Shift,
        LWin or RWin => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    public static bool IsModifier(int vk) => ModifierOf(vk) != HotkeyModifiers.None;
}

/// <summary>
/// A hotkey: one main key (keyboard virtual-key code or mouse button) plus modifiers.
/// A modifier can be the main key itself, e.g. "Right Ctrl" for push-to-talk.
/// </summary>
public sealed record HotkeyBinding
{
    [JsonConstructor]
    public HotkeyBinding(int key, HotkeyModifiers modifiers = HotkeyModifiers.None)
    {
        Key = key;

        // The main key's own modifier is never part of the required modifiers.
        Modifiers = modifiers & ~Vk.ModifierOf(key);
    }

    /// <summary>Default: Ctrl+Alt+Shift+M (rarely used, not AltGr+M = µ on German keyboards).</summary>
    public static HotkeyBinding Default => new(Vk.M, HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift);

    public int Key { get; }

    public HotkeyModifiers Modifiers { get; }

    [JsonIgnore]
    public bool IsMouse => Vk.IsMouse(Key);

    /// <summary>Left / right mouse buttons alone would block normal clicking; they are not allowed.</summary>
    [JsonIgnore]
    public bool IsValid => Key is > 0 and < 0xFF && Key is not (Vk.LButton or Vk.RButton);

    /// <summary>
    /// Optional resolver for layout-dependent key names (OEM keys such as "Ü" or "#"), set by the Windows layer.
    /// </summary>
    public static Func<int, string?>? KeyNameResolver { get; set; }

    /// <summary>"Ctrl + Alt + Shift + M", "Strg rechts", "Maustaste 4 (zurück)" …</summary>
    public string Display()
    {
        var parts = ModifierNames(Modifiers);
        parts.Add(KeyName(Key));
        return string.Join(" + ", parts);
    }

    /// <summary>Modifiers held while recording: "Ctrl + Shift + …".</summary>
    public static string DisplayPartial(HotkeyModifiers modifiers)
    {
        var parts = ModifierNames(modifiers);
        parts.Add("…");
        return string.Join(" + ", parts);
    }

    private static List<string> ModifierNames(HotkeyModifiers modifiers)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add(Loc.T("KeyCtrl"));
        }

        if (modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add(Loc.T("KeyAlt"));
        }

        if (modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add(Loc.T("KeyShift"));
        }

        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add(Loc.T("KeyWin"));
        }

        return parts;
    }

    public override string ToString() => Display();

    /// <summary>Display name of a single key.</summary>
    public static string KeyName(int vk)
    {
        switch (vk)
        {
            case Vk.MButton: return Loc.T("KeyMouseMiddle");
            case Vk.XButton1: return Loc.T("KeyMouseX1");
            case Vk.XButton2: return Loc.T("KeyMouseX2");
            case Vk.LControl: return Loc.T("KeyLCtrl");
            case Vk.RControl: return Loc.T("KeyRCtrl");
            case Vk.Control: return Loc.T("KeyCtrl");
            case Vk.LMenu: return Loc.T("KeyLAlt");
            case Vk.RMenu: return Loc.T("KeyRAlt");
            case Vk.Menu: return Loc.T("KeyAlt");
            case Vk.LShift: return Loc.T("KeyLShift");
            case Vk.RShift: return Loc.T("KeyRShift");
            case Vk.Shift: return Loc.T("KeyShift");
            case Vk.LWin: return Loc.T("KeyLWin");
            case Vk.RWin: return Loc.T("KeyRWin");
            case Vk.Back: return Loc.T("KeyBackspace");
            case Vk.Tab: return Loc.T("KeyTab");
            case Vk.Return: return Loc.T("KeyEnter");
            case Vk.Pause: return Loc.T("KeyPause");
            case Vk.Capital: return Loc.T("KeyCapsLock");
            case Vk.Escape: return Loc.T("KeyEscape");
            case Vk.Space: return Loc.T("KeySpace");
            case 0x21: return Loc.T("KeyPageUp");
            case 0x22: return Loc.T("KeyPageDown");
            case 0x23: return Loc.T("KeyEnd");
            case 0x24: return Loc.T("KeyHome");
            case 0x25: return Loc.T("KeyLeft");
            case 0x26: return Loc.T("KeyUp");
            case 0x27: return Loc.T("KeyRight");
            case 0x28: return Loc.T("KeyDown");
            case 0x2C: return Loc.T("KeyPrint");
            case 0x2D: return Loc.T("KeyInsert");
            case Vk.Delete: return Loc.T("KeyDelete");
            case 0x5D: return Loc.T("KeyApps");
            case 0x6A: return "Num *";
            case 0x6B: return "Num +";
            case 0x6D: return "Num -";
            case 0x6E: return Loc.T("KeyNumDecimal");
            case 0x6F: return "Num /";
            case 0x90: return Loc.T("KeyNumLock");
            case 0x91: return Loc.T("KeyScrollLock");
            case 0xAD: return Loc.T("KeyVolumeMute");
            case 0xAE: return Loc.T("KeyVolumeDown");
            case 0xAF: return Loc.T("KeyVolumeUp");
            case 0xB0: return Loc.T("KeyMediaNext");
            case 0xB1: return Loc.T("KeyMediaPrev");
            case 0xB2: return Loc.T("KeyMediaStop");
            case 0xB3: return Loc.T("KeyMediaPlay");
        }

        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
        {
            return ((char)vk).ToString();
        }

        if (vk is >= Vk.F1 and <= Vk.F24)
        {
            return "F" + (vk - Vk.F1 + 1).ToString(CultureInfo.InvariantCulture);
        }

        if (vk is >= 0x60 and <= 0x69)
        {
            return "Num " + (vk - 0x60).ToString(CultureInfo.InvariantCulture);
        }

        if (KeyNameResolver?.Invoke(vk) is { Length: > 0 } resolved)
        {
            return resolved;
        }

        return Loc.F("KeyCodeFmt", vk.ToString("X2", CultureInfo.InvariantCulture));
    }
}
