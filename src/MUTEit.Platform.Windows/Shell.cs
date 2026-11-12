using System.Runtime.InteropServices;
using System.Text;

namespace MUTEit.Platform.Windows;

/// <summary>
/// Taskbar integration: AppUserModelIDs (so the taskbar switch and the settings window get separate buttons), overlay
/// badge on the taskbar button and the jump list (right click on the taskbar button).
/// </summary>
public static class Shell
{
    /// <summary>Settings window (and everything else of MUTEit.exe).</summary>
    public const string AppId = "sksdesign.MUTEit";

    /// <summary>The taskbar switch window of the tray: own ID → own taskbar button, never grouped with the settings window.</summary>
    public const string TaskbarSwitchAppId = "sksdesign.MUTEit.TaskbarSwitch";

    private static ITaskbarList3? _taskbar;

    public static void SetProcessAppId(string appId)
    {
        try
        {
            _ = NativeMethods.SetCurrentProcessExplicitAppUserModelID(appId);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // very old Windows
        }
    }

    /// <summary>
    /// Window properties of a taskbar button: own AppUserModelID plus the relaunch command used when the button is
    /// pinned (a pinned button starts <c>MUTEit.exe --taskbar</c>). Set them before the window is shown.
    /// </summary>
    public static void SetWindowAppId(IntPtr hwnd, string appId, string relaunchCommand, string displayName, string iconResource)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (NativeMethods.SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0 || store is null)
        {
            return;
        }

        try
        {
            SetString(store, PropertyKeys.AppUserModelId, appId);
            SetString(store, PropertyKeys.AppUserModelRelaunchCommand, relaunchCommand);
            SetString(store, PropertyKeys.AppUserModelRelaunchDisplayNameResource, displayName);
            SetString(store, PropertyKeys.AppUserModelRelaunchIconResource, iconResource);
            store.Commit();
        }
        catch (COMException)
        {
            // property store refused – the button simply groups by process
        }
        finally
        {
            AudioDevices.Release(store);
        }
    }

    /// <summary>Badge on the taskbar button (null = remove). <paramref name="icon"/> is an HICON owned by the caller.</summary>
    public static void SetOverlayIcon(IntPtr hwnd, IntPtr icon, string? description)
    {
        try
        {
            _taskbar ??= CreateTaskbar();
            _taskbar?.SetOverlayIcon(hwnd, icon, description);
        }
        catch (COMException)
        {
            // Explorer restarted: create the taskbar object again next time.
            _taskbar = null;
        }
    }

    /// <summary>After an Explorer restart ("TaskbarCreated") the taskbar object must be created again.</summary>
    public static void ResetTaskbar()
    {
        if (_taskbar is not null)
        {
            AudioDevices.Release(_taskbar);
            _taskbar = null;
        }
    }

    /// <summary>Jump list tasks of an AppUserModelID (title, arguments). Empty list = remove.</summary>
    public static void SetJumpList(string appId, string exePath, IReadOnlyList<(string Title, string Arguments)> tasks)
    {
        ICustomDestinationList? list = null;
        try
        {
            list = ComClass.Create<ICustomDestinationList>(ComClass.DestinationList);
            list.SetAppID(appId);
            if (tasks.Count == 0)
            {
                list.DeleteList(appId);
                return;
            }

            var arrayIid = typeof(IObjectArray).GUID;
            list.BeginList(out _, ref arrayIid, out var removed);
            AudioDevices.Release(removed);

            var collection = ComClass.Create<IObjectCollection>(ComClass.EnumerableObjectCollection);
            foreach (var (title, arguments) in tasks)
            {
                collection.AddObject(CreateLink(exePath, arguments, title));
            }

            list.AddUserTasks((IObjectArray)collection);
            list.CommitList();
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            try
            {
                list?.AbortList();
            }
            catch (COMException)
            {
                // nothing to abort
            }
        }
        finally
        {
            AudioDevices.Release(list);
        }
    }

    /// <summary>Registered window message Explorer broadcasts after it (re)started the taskbar.</summary>
    public static uint TaskbarCreatedMessage { get; } = NativeMethods.RegisterWindowMessage("TaskbarCreated");

    /// <summary>Sent to a window once its taskbar button exists – only then are overlay badges accepted.</summary>
    public static uint TaskbarButtonCreatedMessage { get; } = NativeMethods.RegisterWindowMessage("TaskbarButtonCreated");

    /// <summary>Lets another MUTEit process (settings window) bring its window to the front.</summary>
    public static void AllowAnyForeground() => NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

    /// <summary>
    /// True when the mouse is over a taskbar (primary or secondary) or the Win key is held (Win + number):
    /// a restore request of the taskbar switch then really came from the taskbar, not from Alt+Tab or Task View.
    /// </summary>
    public static bool IsTaskbarActivation()
    {
        const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_MENU = 0x12;
        static bool Down(int vk) => (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;

        if (Down(VK_MENU))
        {
            return false; // Alt+Tab
        }

        if (Down(VK_LWIN) || Down(VK_RWIN))
        {
            return true; // Win + number
        }

        if (!NativeMethods.GetCursorPos(out var point))
        {
            return false;
        }

        var root = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), NativeMethods.GA_ROOT);
        var name = ClassName(root);
        return name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    internal static string ClassName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = new StringBuilder(256);
        return NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private static ITaskbarList3? CreateTaskbar()
    {
        var taskbar = ComClass.Create<ITaskbarList3>(ComClass.TaskbarList);
        taskbar.HrInit();
        return taskbar;
    }

    private static IShellLinkW CreateLink(string exePath, string arguments, string title)
    {
        var link = ComClass.Create<IShellLinkW>(ComClass.ShellLink);
        link.SetPath(exePath);
        link.SetArguments(arguments);
        link.SetIconLocation(exePath, 0);
        link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? string.Empty);
        var store = (IPropertyStore)link;
        SetString(store, PropertyKeys.Title, title);
        store.Commit();
        return link;
    }

    private static void SetString(IPropertyStore store, PROPERTYKEY key, string value)
    {
        var variant = PROPVARIANT.FromString(value);
        try
        {
            store.SetValue(ref key, ref variant);
        }
        finally
        {
            NativeMethods.PropVariantClear(ref variant);
        }
    }
}

/// <summary>
/// Remembers the last foreground window of another program. After a click on the taskbar switch Windows activates
/// MUTEit; the focus is then handed back to that window, so a game or a call window keeps the keyboard.
/// </summary>
public sealed class ForegroundTracker : IDisposable
{
    private readonly NativeMethods.WinEventProc _proc;
    private readonly uint _ownProcess = (uint)Environment.ProcessId;
    private IntPtr _hook;

    public ForegroundTracker()
    {
        _proc = OnForeground;
        _hook = NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        Remember(NativeMethods.GetForegroundWindow());
    }

    public IntPtr LastExternalWindow { get; private set; }

    /// <summary>Gives the focus back to the last window of another program (if it still exists).</summary>
    public void RestoreFocus()
    {
        var target = LastExternalWindow;
        if (target != IntPtr.Zero && NativeMethods.IsWindow(target) && NativeMethods.IsWindowVisible(target))
        {
            NativeMethods.SetForegroundWindow(target);
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private void OnForeground(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time) => Remember(hwnd);

    private void Remember(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out var process);
        if (process == _ownProcess)
        {
            return;
        }

        // The taskbar itself gets the focus while it is clicked – not a target to return to.
        var name = Shell.ClassName(hwnd);
        if (name is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "XamlExplorerHostIslandWindow")
        {
            return;
        }

        LastExternalWindow = hwnd;
    }
}
