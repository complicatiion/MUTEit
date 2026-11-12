using System.Windows.Forms;
using MUTEit.Platform.Windows;

namespace MUTEit.App.Tray;

/// <summary>Tray mode: Windows Forms message loop without a main window.</summary>
internal static class TrayHost
{
    public static int Run()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        // Own AppUserModelID for the process; the taskbar switch window gets a second one (see TaskbarSwitchWindow).
        Shell.SetProcessAppId(Shell.AppId);

        TrayContext? context = null;

        // UI thread: log and keep running. Anything fatal: apply the exit state of the microphone first.
        Application.ThreadException += (_, e) => TrayContext.LogError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            context?.EmergencyExit();
            if (e.ExceptionObject is Exception ex)
            {
                TrayContext.LogError(ex);
            }
        };

        context = new TrayContext();
        try
        {
            // Windows Forms disposes the context itself when ExitThread ends the message loop.
            Application.Run(context);
        }
        finally
        {
            // Safety net (e.g. the loop ended by an exception); TrayContext.Dispose is idempotent.
            context.Dispose();
        }

        return 0;
    }
}
