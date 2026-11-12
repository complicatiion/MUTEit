using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MUTEit.Core.Audio;
using MUTEit.Core.Localization;
using MUTEit.Core.Settings;
using MUTEit.Core.Theme;

namespace MUTEit.App.Tray;

/// <summary>Visual state of the icons ("All devices" with mixed states counts as muted – the safe reading).</summary>
internal enum IconState
{
    Unmuted,
    Muted,
    Unavailable,
}

/// <summary>
/// Draws every state icon at runtime from the logo masks, so they follow the accent colour (Teal or Windows accent),
/// the theme and the DPI:
/// <list type="bullet">
/// <item>Tray / taskbar window: "M" glyph + state dot (accent = live, red = muted).</item>
/// <item>Taskbar overlay badge (16 px).</item>
/// <item>Desktop overlay: microphone / crossed-out microphone on a coloured disc (default) or the circle logo in accent / red,
/// on a dark backdrop visible on any wallpaper.</item>
/// <item>Custom icons: one own image + badges, or two own images as they are.</item>
/// </list>
/// </summary>
internal sealed class IconFactory : IDisposable
{
    private readonly Bitmap _logoMask = LoadEmbedded("logo-mask.png");
    private readonly Bitmap _glyphMask = LoadEmbedded("glyph-mask.png");
    private readonly Bitmap _micMask = LoadEmbedded("overlay.mic.png");
    private readonly Bitmap _micOffMask = LoadEmbedded("overlay.mic-off.png");
    private readonly Dictionary<(string Name, int Argb), Bitmap> _menuImages = [];

    /// <summary>Images of the previous theme: still shown by open menus until they are rebuilt – disposed one change later.</summary>
    private readonly List<Bitmap> _retired = [];
    private Bitmap? _general;
    private Bitmap? _muted;
    private Bitmap? _unmuted;
    private IconMode _mode = IconMode.Default;

    /// <summary>Last problem with the custom icon files (shown in the settings window), null = fine.</summary>
    public string? Error { get; private set; }

    public static IconState StateOf(MicState state) => state switch
    {
        MicState.Unmuted => IconState.Unmuted,
        MicState.Unavailable => IconState.Unavailable,
        _ => IconState.Muted,
    };

    /// <summary>Loads the custom icon files of the settings; falls back to the default icons on any error.</summary>
    public void LoadCustom(MuteitSettings settings)
    {
        DisposeCustom();
        Error = null;
        _mode = settings.IconMode;
        try
        {
            switch (settings.IconMode)
            {
                case IconMode.General:
                    _general = LoadFile(settings.GeneralIconPath!);
                    break;
                case IconMode.Dedicated:
                    _muted = LoadFile(settings.MutedIconPath!);
                    _unmuted = LoadFile(settings.UnmutedIconPath!);
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException or ExternalException)
        {
            Error = Loc.F("IconLoadFailedFmt", ex.Message);
            DisposeCustom();
            _mode = IconMode.Default;
        }
    }

    /// <summary>Tray icon (and taskbar window icon) of <paramref name="size"/> px.</summary>
    public Icon StateIcon(IconState state, TrayPalette palette, bool lightTaskbar, int size)
    {
        using var bitmap = StateBitmap(state, palette, lightTaskbar, size);
        return ToIcon(bitmap);
    }

    public Bitmap StateBitmap(IconState state, TrayPalette palette, bool lightTaskbar, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        Prepare(g);

        if (_mode == IconMode.Dedicated && _muted is not null && _unmuted is not null)
        {
            DrawFit(g, state == IconState.Unmuted ? _unmuted : _muted, new Rectangle(0, 0, size, size));
            return bitmap;
        }

        if (_mode == IconMode.General && _general is not null)
        {
            DrawFit(g, _general, new Rectangle(0, 0, size, size));
        }
        else
        {
            var glyph = state == IconState.Unavailable
                ? palette.TextDisabled
                : lightTaskbar ? ColorTranslator.FromHtml("#1A1D23") : ColorTranslator.FromHtml("#ECEFF4");
            using var tinted = Tint(_glyphMask, glyph, size);
            g.DrawImage(tinted, 0, 0, size, size);
        }

        var badge = (int)Math.Round(size * 0.5);
        var gap = Math.Max(1, size / 16);
        CutCircle(g, new Rectangle(size - badge - gap, size - badge - gap, badge + (2 * gap), badge + (2 * gap)));

        // Light taskbar: a darker accent, so the "live" dot stands out against white (≥ 3 : 1).
        var badgePalette = lightTaskbar ? palette with { Accent = ReadableOn(palette.Accent, Color.White) } : palette;
        DrawBadge(g, state, badgePalette, new Rectangle(size - badge, size - badge, badge, badge));
        return bitmap;
    }

    /// <summary>Badge for the taskbar button (ITaskbarList3.SetOverlayIcon), 16 px.</summary>
    public Icon BadgeIcon(IconState state, TrayPalette palette)
    {
        using var bitmap = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            Prepare(g);
            DrawBadge(g, state, palette, new Rectangle(1, 1, 14, 14));
        }

        return ToIcon(bitmap);
    }

    /// <summary>Desktop overlay image of <paramref name="size"/> px (premultiplied by the layered window).</summary>
    public Bitmap OverlayBitmap(IconState state, TrayPalette palette, int size, OverlayStyle style)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        Prepare(g);
        var full = new Rectangle(0, 0, size, size);

        if (_mode == IconMode.Dedicated && _muted is not null && _unmuted is not null)
        {
            DrawFit(g, state == IconState.Unmuted ? _unmuted : _muted, full);
            return bitmap;
        }

        // Dark backdrop: readable on white documents and on dark wallpapers alike.
        using (var backdrop = new SolidBrush(Color.FromArgb(200, 0x1A, 0x1D, 0x23)))
        {
            g.FillEllipse(backdrop, full);
        }

        var inset = Math.Max(2, size / 14);
        var inner = Rectangle.Inflate(full, -inset, -inset);
        if (_mode == IconMode.General && _general is not null)
        {
            var image = Rectangle.Inflate(inner, -size / 10, -size / 10);
            DrawFit(g, _general, image);
            var badge = (int)Math.Round(size * 0.38);
            CutCircle(g, new Rectangle(size - badge - 3, size - badge - 3, badge + 3, badge + 3));
            DrawBadge(g, state, palette, new Rectangle(size - badge - 1, size - badge - 1, badge, badge));
            return bitmap;
        }

        var color = StateColor(state, palette);
        if (style == OverlayStyle.Logo)
        {
            // MUTEit logo: accent = live, red = muted (no slash), grey = no microphone.
            using var tinted = Tint(_logoMask, color, inner.Width);
            g.DrawImage(tinted, inner);
            return bitmap;
        }

        // Microphone symbol on a coloured disc: microphone = live, crossed-out microphone = muted.
        using (var disc = new SolidBrush(color))
        {
            g.FillEllipse(disc, inner);
        }

        var glyphSize = (int)Math.Round(inner.Width * 0.58);
        var glyphRect = new Rectangle(inner.X + ((inner.Width - glyphSize) / 2), inner.Y + ((inner.Height - glyphSize) / 2), glyphSize, glyphSize);
        var glyphColor = state == IconState.Unmuted ? palette.OnAccent : Color.White;
        var mask = state == IconState.Unmuted ? _micMask : _micOffMask;
        using (var glyph = Tint(mask, glyphColor, glyphSize))
        {
            g.DrawImage(glyph, glyphRect);
        }

        return bitmap;
    }

    private static Color StateColor(IconState state, TrayPalette palette) => state switch
    {
        IconState.Unmuted => palette.Accent,
        IconState.Muted => palette.Muted,
        _ => palette.TextDisabled,
    };

    /// <summary>Menu image (white mask PNG) tinted with <paramref name="color"/>; cached.</summary>
    public Image MenuImage(string name, Color color)
    {
        var key = (name, color.ToArgb());
        if (!_menuImages.TryGetValue(key, out var image))
        {
            using var mask = LoadEmbedded($"menu.{name}.png");
            image = Tint(mask, color, mask.Width);
            _menuImages[key] = image;
        }

        return image;
    }

    /// <summary>Theme or accent changed: new tinted menu images from now on.</summary>
    public void ClearCache()
    {
        foreach (var image in _retired)
        {
            image.Dispose();
        }

        _retired.Clear();
        _retired.AddRange(_menuImages.Values);
        _menuImages.Clear();
    }

    public void Dispose()
    {
        ClearCache();
        ClearCache();
        DisposeCustom();
        _logoMask.Dispose();
        _glyphMask.Dispose();
        _micMask.Dispose();
        _micOffMask.Dispose();
    }

    /// <summary>Darkens / lightens <paramref name="color"/> until it reaches 3 : 1 against <paramref name="background"/>.</summary>
    private static Color ReadableOn(Color color, Color background)
    {
        var rgb = AccentRules.EnsureContrast(new Rgb(color.R, color.G, color.B), new Rgb(background.R, background.G, background.B), 3.0);
        return Color.FromArgb(rgb.R, rgb.G, rgb.B);
    }

    internal static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeApp.DestroyIcon(handle);
        }
    }

    /// <summary>State dot: accent = live, red = muted, grey = no microphone.</summary>
    private static void DrawBadge(Graphics g, IconState state, TrayPalette palette, Rectangle r)
    {
        using var fill = new SolidBrush(StateColor(state, palette));
        g.FillEllipse(fill, r);
    }

    /// <summary>Clears a circle (transparent ring around the badge).</summary>
    private static void CutCircle(Graphics g, Rectangle r)
    {
        var mode = g.CompositingMode;
        g.CompositingMode = CompositingMode.SourceCopy;
        using var clear = new SolidBrush(Color.Transparent);
        g.FillEllipse(clear, r);
        g.CompositingMode = mode;
    }

    private static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
    }

    /// <summary>White-on-transparent mask → <paramref name="color"/>, scaled to <paramref name="size"/> px.</summary>
    private static Bitmap Tint(Bitmap mask, Color color, int size)
    {
        var result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        Prepare(g);
        var matrix = new ColorMatrix(
        [
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0],
            [0, 0, 0, color.A / 255f, 0],
            [color.R / 255f, color.G / 255f, color.B / 255f, 0, 1],
        ]);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(mask, new Rectangle(0, 0, size, size), 0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel, attributes);
        return result;
    }

    private static void DrawFit(Graphics g, Image image, Rectangle target)
    {
        var scale = Math.Min((float)target.Width / image.Width, (float)target.Height / image.Height);
        var w = image.Width * scale;
        var h = image.Height * scale;
        g.DrawImage(image, target.X + ((target.Width - w) / 2), target.Y + ((target.Height - h) / 2), w, h);
    }

    private static Bitmap LoadFile(string path)
    {
        if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            // Largest frame of the icon.
            using var icon = new Icon(path, 256, 256);
            return icon.ToBitmap();
        }

        using var stream = File.OpenRead(path);
        using var loaded = Image.FromStream(stream);
        return new Bitmap(loaded);
    }

    private static Bitmap LoadEmbedded(string name)
    {
        using var stream = typeof(IconFactory).Assembly.GetManifestResourceStream("MUTEit.Assets." + name)
                           ?? throw new InvalidOperationException("Missing embedded resource: " + name);
        using var loaded = Image.FromStream(stream);
        return new Bitmap(loaded);
    }

    private void DisposeCustom()
    {
        _general?.Dispose();
        _muted?.Dispose();
        _unmuted?.Dispose();
        _general = null;
        _muted = null;
        _unmuted = null;
    }
}
