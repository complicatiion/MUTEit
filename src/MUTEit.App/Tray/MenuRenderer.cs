using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using MUTEit.Core.Theme;

namespace MUTEit.App.Tray;

/// <summary>Colours of the tray menu, overlay and icons for one theme (dark = default) and accent.</summary>
internal sealed record TrayPalette(
    bool Dark,
    Color Background,
    Color Hover,
    Color Border,
    Color Accent,
    Color AccentSoft,
    Color Text,
    Color TextMuted,
    Color TextDisabled,
    Color Muted,
    Color OnAccent)
{
    public static TrayPalette Create(bool dark, AccentSet accent)
    {
        static Color C(string hex) => ColorTranslator.FromHtml(hex);
        static Color R(Rgb rgb) => Color.FromArgb(rgb.R, rgb.G, rgb.B);

        return dark
            ? new TrayPalette(true, C("#1A1D23"), C("#2A2F38"), C("#343A45"), R(accent.Accent), R(accent.Soft),
                C("#ECEFF4"), C("#A7B0BE"), C("#6B7383"), R(AccentRules.MutedRed), R(accent.OnAccent))
            : new TrayPalette(false, C("#F9FAFB"), C("#E4E7EB"), C("#D5D9E0"), R(accent.Accent), R(accent.Soft),
                C("#1A1D23"), C("#4B5563"), C("#9AA1AC"), R(AccentRules.MutedRed), R(accent.OnAccent));
    }
}

/// <summary>
/// Tray menu in the MUTEit style: rounded hover, icons on the left, check state as an accent frame around the icon
/// (never system blue), muted status line. Dark by default, light with the light theme.
/// </summary>
internal sealed class MenuRenderer : ToolStripProfessionalRenderer
{
    private TrayPalette _palette;

    public MenuRenderer(TrayPalette palette)
        : base(new PaletteColorTable(palette))
    {
        _palette = palette;
        RoundedEdges = true;
    }

    public TrayPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            ((PaletteColorTable)ColorTable).Palette = value;
        }
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_palette.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(_palette.Border);
        var r = e.AffectedBounds;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_palette.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(Point.Empty, e.Item.Size);
        bounds.Inflate(-4, -1);
        using var path = RoundedRect(bounds, 6);
        using var brush = new SolidBrush(_palette.Hover);
        g.FillPath(brush, path);
    }

    /// <summary>Text vertically centred on the whole item height – exactly like the icon.</summary>
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var color = e.Item.Tag is MenuTag { Muted: true }
            ? _palette.TextMuted
            : e.Item.Tag is MenuTag { Danger: true }
                ? _palette.Muted
                : e.Item.Enabled ? _palette.Text : _palette.TextDisabled;
        var bounds = new Rectangle(e.TextRectangle.X, 0, e.TextRectangle.Width, e.Item.Height);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                                      | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, color, flags);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(_palette.Border);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item is { Enabled: true } ? _palette.TextMuted : _palette.TextDisabled;
        base.OnRenderArrow(e);
    }

    /// <summary>Checked item: soft accent tile with accent outline behind the (accent) icon.</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = CenteredImageRect(e.Item, e.ImageRectangle);
        r.Inflate(3, 3);
        using var path = RoundedRect(r, 5);
        using var fill = new SolidBrush(_palette.AccentSoft);
        using var pen = new Pen(_palette.Accent, 1f);
        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Image is null)
        {
            return;
        }

        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(e.Image, CenteredImageRect(e.Item, e.ImageRectangle));
    }

    private static Rectangle CenteredImageRect(ToolStripItem item, Rectangle layout)
    {
        var size = Math.Min(layout.Width, layout.Height);
        if (item.Owner is { } owner)
        {
            size = Math.Max(size, owner.ImageScalingSize.Height);
        }

        size = Math.Min(size, item.Height - 4);
        var x = layout.X + ((layout.Width - size) / 2);
        var y = (item.Height - size) / 2;
        return new Rectangle(x, y, size, size);
    }

    internal static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private sealed class PaletteColorTable(TrayPalette palette) : ProfessionalColorTable
    {
        public TrayPalette Palette { get; set; } = palette;

        public override Color ToolStripDropDownBackground => Palette.Background;
        public override Color ImageMarginGradientBegin => Palette.Background;
        public override Color ImageMarginGradientMiddle => Palette.Background;
        public override Color ImageMarginGradientEnd => Palette.Background;
        public override Color MenuBorder => Palette.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => Palette.Hover;
        public override Color MenuItemSelectedGradientBegin => Palette.Hover;
        public override Color MenuItemSelectedGradientEnd => Palette.Hover;
        public override Color SeparatorDark => Palette.Border;
        public override Color SeparatorLight => Palette.Border;
        public override Color CheckBackground => Palette.AccentSoft;
        public override Color CheckSelectedBackground => Palette.AccentSoft;
        public override Color CheckPressedBackground => Palette.AccentSoft;
    }
}

/// <summary>Extra rendering hints for a menu item.</summary>
internal sealed record MenuTag(bool Muted = false, bool Danger = false);
