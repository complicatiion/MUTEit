using Microsoft.Win32;
using MUTEit.Core.Settings;
using MUTEit.Core.Theme;

namespace MUTEit.Platform.Windows;

/// <summary>Read-only checks of the Windows environment (theme, accent, version).</summary>
public static class SystemState
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Windows 11 (build 22000+): Mica, rounded corners.</summary>
    public static bool IsWindows11 => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    /// <summary>Light taskbar ("Choose your default Windows mode" = Light) → dark tray glyph.</summary>
    public static bool IsTaskbarLight() => ReadDword(PersonalizeKey, "SystemUsesLightTheme") == 1;

    /// <summary>Light app mode ("Choose your default app mode" = Light).</summary>
    public static bool IsAppsLight() => ReadDword(PersonalizeKey, "AppsUseLightTheme") == 1;

    /// <summary>Resolves <see cref="ThemeMode.System"/> to dark / light.</summary>
    public static bool IsDark(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => false,
        ThemeMode.System => !IsAppsLight(),
        _ => true,
    };

    /// <summary>Windows accent colour (DWM "AccentColor", stored as 0xAABBGGRR); null when not readable.</summary>
    public static Rgb? WindowsAccent()
    {
        if (ReadDword(@"Software\Microsoft\Windows\DWM", "AccentColor") is not { } abgr)
        {
            return null;
        }

        var value = unchecked((uint)abgr);
        return new Rgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
    }

    /// <summary>Accent colours for the current settings (Teal, or the Windows accent unless it is red-like).</summary>
    public static AccentSet Accent(MuteitSettings settings) =>
        AccentRules.Resolve(WindowsAccent(), settings.UseWindowsAccent, IsDark(settings.Theme));

    /// <summary>Rounded corners for a popup window (tray menu) on Windows 11; ignored on Windows 10.</summary>
    public static void RoundCorners(IntPtr hwnd, bool small = false)
    {
        if (hwnd == IntPtr.Zero || !IsWindows11)
        {
            return;
        }

        var preference = small ? NativeMethods.DWMWCP_ROUNDSMALL : NativeMethods.DWMWCP_ROUND;
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    private static int? ReadDword(string key, string name)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(key);
            return k?.GetValue(name) is int value ? value : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
