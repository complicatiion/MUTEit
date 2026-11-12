using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using MUTEit.Core.Input;

namespace MUTEit.Platform.Windows;

/// <summary>
/// Global low-level keyboard and mouse hooks (WH_KEYBOARD_LL / WH_MOUSE_LL). Needed for push-to-talk (key up),
/// mouse buttons, "ignore modifiers" and swallowing the hotkey – RegisterHotKey can do none of these.
/// <para>Install it on a thread with a message loop (tray UI thread, Avalonia UI thread). The callbacks run on that
/// thread and must return quickly: Windows silently removes a hook that is too slow (LowLevelHooksTimeout), so
/// handlers only decide and post the real work.</para>
/// </summary>
public sealed class InputHook : IDisposable
{
    private readonly NativeMethods.LowLevelProc _keyboardProc;
    private readonly NativeMethods.LowLevelProc _mouseProc;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    public InputHook()
    {
        // Delegates are kept in fields so the garbage collector never frees them while Windows still calls them.
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    /// <summary>Keyboard event: (virtual key with left / right distinction, down) → true = swallow.</summary>
    public Func<int, bool, bool>? Key { get; set; }

    /// <summary>Mouse button event (middle, X1, X2): (virtual key, down) → true = swallow. Only called while <see cref="WantMouse"/>.</summary>
    public Func<int, bool, bool>? MouseButton { get; set; }

    public bool IsInstalled => _keyboardHook != IntPtr.Zero;

    /// <summary>Mouse hook only when a mouse button is used (it sees every mouse move otherwise).</summary>
    public bool WantMouse { get; private set; }

    /// <summary>Installs (or reinstalls) the hooks. Throws <see cref="Win32Exception"/> when Windows refuses.</summary>
    public void Install(bool mouse)
    {
        Uninstall();
        var module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        WantMouse = mouse;
        if (mouse)
        {
            _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _mouseProc, module, 0);
            if (_mouseHook == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                Uninstall();
                throw new Win32Exception(error);
            }
        }
    }

    public void Uninstall()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        WantMouse = false;
    }

    public void Dispose() => Uninstall();

    /// <summary>
    /// Sends an unassigned key (0xE8) down and up. After a swallowed Win / Alt combination this keeps Windows from opening
    /// the Start menu or an application from activating its menu bar when Win / Alt is released.
    /// </summary>
    public static void SendDummyKey()
    {
        var inputs = new NativeMethods.INPUT[2];
        inputs[0].type = NativeMethods.INPUT_KEYBOARD;
        inputs[0].u.ki.wVk = Vk.Unassigned;
        inputs[1].type = NativeMethods.INPUT_KEYBOARD;
        inputs[1].u.ki.wVk = Vk.Unassigned;
        inputs[1].u.ki.dwFlags = NativeMethods.KEYEVENTF_KEYUP;
        NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    /// <summary>Layout-dependent name of a key ("Ü", "#", "^") for OEM keys; null when Windows has none.</summary>
    public static string? KeyName(int vk)
    {
        var scan = NativeMethods.MapVirtualKey((uint)vk, NativeMethods.MAPVK_VK_TO_VSC);
        if (scan == 0)
        {
            return null;
        }

        var buffer = new StringBuilder(64);
        return NativeMethods.GetKeyNameText((int)(scan << 16), buffer, buffer.Capacity) > 0 ? buffer.ToString() : null;
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Key is { } handler)
        {
            var message = (int)wParam;
            var down = message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN;
            var up = message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
            if (down || up)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (data.vkCode != Vk.Unassigned)
                {
                    try
                    {
                        if (handler((int)data.vkCode, down))
                        {
                            return 1;
                        }
                    }
                    catch (Exception)
                    {
                        // never let an exception escape into Windows
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && MouseButton is { } handler)
        {
            var message = (int)wParam;
            int? vk = null;
            var down = false;
            switch (message)
            {
                case NativeMethods.WM_MBUTTONDOWN:
                    vk = Vk.MButton;
                    down = true;
                    break;
                case NativeMethods.WM_MBUTTONUP:
                    vk = Vk.MButton;
                    break;
                case NativeMethods.WM_XBUTTONDOWN:
                case NativeMethods.WM_XBUTTONUP:
                    var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    var button = (int)(data.mouseData >> 16);
                    vk = button == NativeMethods.XBUTTON2 ? Vk.XButton2 : Vk.XButton1;
                    down = message == NativeMethods.WM_XBUTTONDOWN;
                    break;
            }

            if (vk is { } key)
            {
                try
                {
                    if (handler(key, down))
                    {
                        return 1;
                    }
                }
                catch (Exception)
                {
                    // never let an exception escape into Windows
                }
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }
}
