using System.Drawing;
using System.Windows.Forms;
using MUTEit.Platform.Windows;

namespace MUTEit.App.Tray;

/// <summary>
/// Taskbar switch: a window that only exists as a taskbar button and stays minimized for ever.
/// <list type="bullet">
/// <item>A click on the button makes Windows send <c>SC_RESTORE</c>; it is intercepted, the microphone is switched and
/// the window stays minimized. Win + number works the same way.</item>
/// <item>Restore requests that do not come from the taskbar (Alt+Tab, Task View) are ignored.</item>
/// <item>Own AppUserModelID: never grouped with the settings window; a pinned button starts <c>MUTEit.exe --taskbar</c>.</item>
/// <item>"Close window" on the taskbar only switches the taskbar button off (event <see cref="CloseRequested"/>).</item>
/// </list>
/// </summary>
internal sealed class TaskbarSwitchWindow : Form
{
    private bool _presented;

    public TaskbarSwitchWindow()
    {
        Text = "MUTEit";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(240, 80);
        WindowState = FormWindowState.Minimized;
    }

    /// <summary>The taskbar button was clicked (or Win + number).</summary>
    public event Action? Clicked;

    /// <summary>"Close window" on the taskbar: the user wants the button gone.</summary>
    public event Action? CloseRequested;

    /// <summary>Taskbar button (re)created – Explorer restart or the button was shown: badge and icon have to be set again.</summary>
    public event Action? TaskbarRecreated;

    /// <summary>Creates the button (minimized, without activating anything).</summary>
    public void Present()
    {
        if (!IsHandleCreated)
        {
            CreateHandle();
            var exe = Program.ExePath;
            Shell.SetWindowAppId(Handle, Shell.TaskbarSwitchAppId, $"\"{exe}\" {Program.TaskbarArgument}", "MUTEit", $"{exe},0");
        }

        if (!_presented)
        {
            NativeApp.ShowWindow(Handle, NativeApp.SW_SHOWMINNOACTIVE);
            _presented = true;
        }
    }

    /// <summary>Removes the taskbar button.</summary>
    public void Withdraw()
    {
        if (IsHandleCreated)
        {
            NativeApp.ShowWindow(Handle, NativeApp.SW_HIDE);
        }

        _presented = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= NativeApp.WS_EX_APPWINDOW;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeApp.WM_SYSCOMMAND)
        {
            var command = (int)m.WParam & 0xFFF0;
            switch (command)
            {
                case NativeApp.SC_RESTORE:
                case NativeApp.SC_MAXIMIZE:
                    if (Shell.IsTaskbarActivation())
                    {
                        Clicked?.Invoke();
                    }

                    m.Result = IntPtr.Zero;
                    return;
                case NativeApp.SC_CLOSE:
                    CloseRequested?.Invoke();
                    m.Result = IntPtr.Zero;
                    return;
                case NativeApp.SC_MOVE:
                case NativeApp.SC_SIZE:
                    m.Result = IntPtr.Zero;
                    return;
            }
        }
        else if (m.Msg == NativeApp.WM_SIZE && _presented && (int)m.WParam is NativeApp.SIZE_RESTORED or NativeApp.SIZE_MAXIMIZED)
        {
            // Restored by some other way (e.g. ShowWindow from a tool): minimize again right away, without switching.
            BeginInvoke(new Action(() => NativeApp.ShowWindow(Handle, NativeApp.SW_SHOWMINNOACTIVE)));
        }
        else if (m.Msg == (int)Shell.TaskbarCreatedMessage && Shell.TaskbarCreatedMessage != 0)
        {
            Shell.ResetTaskbar();
            BeginInvoke(new Action(() => TaskbarRecreated?.Invoke()));
        }
        else if (m.Msg == (int)Shell.TaskbarButtonCreatedMessage && Shell.TaskbarButtonCreatedMessage != 0)
        {
            // The button exists now: badge and icon can be set (earlier calls are ignored by Windows).
            BeginInvoke(new Action(() => TaskbarRecreated?.Invoke()));
        }

        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            CloseRequested?.Invoke();
            return;
        }

        if (e.CloseReason == CloseReason.TaskManagerClosing)
        {
            // Task Manager ends the whole process anyway; the window itself stays until then.
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }
}
