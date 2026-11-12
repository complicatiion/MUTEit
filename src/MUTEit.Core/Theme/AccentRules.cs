using System.Globalization;

namespace MUTEit.Core.Theme;

/// <summary>sRGB colour without alpha.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";

    public static Rgb FromHex(string hex)
    {
        var h = hex.Trim().TrimStart('#');
        if (h.Length == 8)
        {
            h = h[2..]; // #AARRGGBB
        }

        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            throw new FormatException("Invalid colour: " + hex);
        }

        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    /// <summary>Relative luminance (WCAG 2.x).</summary>
    public double Luminance
    {
        get
        {
            static double Channel(byte c)
            {
                var s = c / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(R)) + (0.7152 * Channel(G)) + (0.0722 * Channel(B));
        }
    }

    public static double Contrast(Rgb a, Rgb b)
    {
        var la = a.Luminance;
        var lb = b.Luminance;
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>Linear mix: 0 = this, 1 = <paramref name="other"/>.</summary>
    public Rgb Mix(Rgb other, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        byte M(byte a, byte b) => (byte)Math.Round(a + ((b - a) * amount));
        return new Rgb(M(R, other.R), M(G, other.G), M(B, other.B));
    }

    /// <summary>Hue 0 – 360, saturation and value 0 – 1.</summary>
    public (double Hue, double Saturation, double Value) Hsv
    {
        get
        {
            double r = R / 255.0, g = G / 255.0, b = B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            double hue = 0;
            if (delta > 0)
            {
                if (max == r)
                {
                    hue = 60 * (((g - b) / delta) % 6);
                }
                else if (max == g)
                {
                    hue = 60 * (((b - r) / delta) + 2);
                }
                else
                {
                    hue = 60 * (((r - g) / delta) + 4);
                }
            }

            if (hue < 0)
            {
                hue += 360;
            }

            return (hue, max == 0 ? 0 : delta / max, max);
        }
    }
}

/// <summary>The derived accent colours of one theme.</summary>
public sealed record AccentSet(Rgb Accent, Rgb Hover, Rgb Pressed, Rgb Soft, Rgb OnAccent, bool FellBack);

/// <summary>
/// Accent colour: Teal by default; optionally the Windows accent. Red is reserved for "muted", so a red-like Windows
/// accent falls back to Teal. The accent is lightened (dark theme) or darkened (light theme) until it reaches 4.5 : 1
/// against the window background.
/// </summary>
public static class AccentRules
{
    public static readonly Rgb TealDark = Rgb.FromHex("#4FD1C5");
    public static readonly Rgb TealLight = Rgb.FromHex("#0F766E");
    public static readonly Rgb DarkBackground = Rgb.FromHex("#1A1D23");
    public static readonly Rgb LightBackground = Rgb.FromHex("#F3F4F6");
    public static readonly Rgb MutedRed = Rgb.FromHex("#E5484D");

    private static readonly Rgb White = new(255, 255, 255);
    private static readonly Rgb Ink = Rgb.FromHex("#0F1A1A");

    /// <summary>Hue 330° – 30° with visible saturation: would be confused with the red "muted" colour.</summary>
    public static bool IsRedLike(Rgb color)
    {
        var (hue, saturation, value) = color.Hsv;
        return saturation >= 0.3 && value >= 0.25 && (hue >= 330 || hue <= 30);
    }

    public static AccentSet Resolve(Rgb? windowsAccent, bool useWindowsAccent, bool dark)
    {
        var fellBack = false;
        Rgb baseColor;
        if (useWindowsAccent && windowsAccent is { } win)
        {
            if (IsRedLike(win))
            {
                baseColor = dark ? TealDark : TealLight;
                fellBack = true;
            }
            else
            {
                baseColor = EnsureContrast(win, dark ? DarkBackground : LightBackground, 4.5);
            }
        }
        else
        {
            baseColor = dark ? TealDark : TealLight;
        }

        return Derive(baseColor, dark, fellBack);
    }

    public static AccentSet Derive(Rgb accent, bool dark, bool fellBack = false)
    {
        var background = dark ? DarkBackground : LightBackground;
        var hover = dark ? accent.Mix(White, 0.18) : accent.Mix(Ink, 0.15);
        var pressed = dark ? accent.Mix(Ink, 0.15) : accent.Mix(Ink, 0.28);
        var soft = background.Mix(accent, dark ? 0.16 : 0.12);

        // Text on the accent: whichever of ink / white reads better.
        var onAccent = Rgb.Contrast(Ink, accent) >= Rgb.Contrast(White, accent) ? Ink : White;
        return new AccentSet(accent, hover, pressed, soft, onAccent, fellBack);
    }

    /// <summary>Moves the colour towards white (dark background) or black (light background) until the ratio is reached.</summary>
    public static Rgb EnsureContrast(Rgb color, Rgb background, double ratio)
    {
        var target = background.Luminance < 0.5 ? White : new Rgb(0, 0, 0);
        var current = color;
        for (var i = 0; i < 20 && Rgb.Contrast(current, background) < ratio; i++)
        {
            current = current.Mix(target, 0.08);
        }

        return current;
    }
}
