using System.Runtime.InteropServices;

namespace MUTEit.App;

/// <summary>Win32 calls of the Windows Forms windows (overlay, taskbar switch, icons).</summary>
internal static class NativeApp
{
    public const int WM_SIZE = 0x0005;
    public const int WM_CLOSE = 0x0010;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int MA_NOACTIVATE = 3;
    public const int SIZE_RESTORED = 0;
    public const int SIZE_MAXIMIZED = 2;
    public const int SC_RESTORE = 0xF120;
    public const int SC_MAXIMIZE = 0xF030;
    public const int SC_CLOSE = 0xF060;
    public const int SC_MOVE = 0xF010;
    public const int SC_SIZE = 0xF000;

    public const int SW_SHOWMINNOACTIVE = 7;
    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;

    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_APPWINDOW = 0x00040000;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;

        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Cx;
        public int Cy;

        public SIZE(int cx, int cy)
        {
            Cx = cx;
            Cy = cy;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>DPI of the monitor at a screen point (96 when unknown).</summary>
    public static int DpiAt(int x, int y)
    {
        try
        {
            var monitor = MonitorFromPoint(new POINT(x, y), MONITOR_DEFAULTTONEAREST);
            return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 && dpi > 0 ? (int)dpi : 96;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return 96;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc,
        ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);
}
