using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using MUTEit.Core.Settings;
using MUTEit.Core.Theme;
using MUTEit.Platform.Windows;

namespace MUTEit.App.SettingsUi;

/// <summary>
/// Theme of the settings window: dark (default) / light / system, accent (Teal or Windows accent), Mica (Windows 11,
/// off by default). The accent brushes are written into the application resources, which take precedence over the
/// Teal defaults of Theme/Colors.axaml; the Fluent palettes get the same accent (switches, sliders, check marks).
/// </summary>
internal static class ThemeManager
{
    public static AccentSet? Current { get; private set; }

    public static bool IsDark { get; private set; } = true;

    public static void Apply(MuteitSettings settings)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        IsDark = SystemState.IsDark(settings.Theme);
        app.RequestedThemeVariant = IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        var accent = SystemState.Accent(settings);
        Current = accent;
        var resources = app.Resources;
        resources["BrushAccent"] = Brush(accent.Accent);
        resources["BrushAccentHover"] = Brush(accent.Hover);
        resources["BrushAccentPressed"] = Brush(accent.Pressed);
        resources["BrushAccentSoft"] = Brush(accent.Soft);
        resources["BrushOnAccent"] = Brush(accent.OnAccent);

        // Fluent controls: knob and check glyph on the accent in the "text on accent" colour (never white on Teal).
        var onAccent = Brush(accent.OnAccent);
        foreach (var key in new[]
                 {
                     "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
                     "RadioButtonCheckGlyphFill", "CheckBoxCheckGlyphForegroundChecked",
                     "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed",
                 })
        {
            resources[key] = onAccent;
        }

        foreach (var fluent in app.Styles.OfType<FluentTheme>())
        {
            foreach (var palette in fluent.Palettes.Values)
            {
                palette.Accent = ToColor(accent.Accent);
            }
        }
    }

    /// <summary>Mica background of a window (Windows 11 only; off by default). Off = solid theme background.</summary>
    public static void ApplyBackdrop(Window window, MuteitSettings settings)
    {
        var mica = settings.Transparency && SystemState.IsWindows11;
        if (mica)
        {
            window.TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.None];
            window.Background = Brushes.Transparent;
            window.Resources["BrushBgSidebar"] = new SolidColorBrush(IsDark ? Color.FromArgb(0x66, 0x15, 0x18, 0x1D) : Color.FromArgb(0x66, 0xEB, 0xED, 0xF0));
        }
        else
        {
            window.TransparencyLevelHint = [WindowTransparencyLevel.None];
            window.ClearValue(Window.BackgroundProperty);
            window.Resources.Remove("BrushBgSidebar");
        }
    }

    private static SolidColorBrush Brush(Rgb color) => new(ToColor(color));

    private static Color ToColor(Rgb color) => Color.FromRgb(color.R, color.G, color.B);
}
