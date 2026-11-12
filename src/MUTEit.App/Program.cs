using System.Diagnostics;
using System.Runtime.CompilerServices;
using MUTEit.Core.Ipc;
using MUTEit.Core.Settings;
using MUTEit.Platform.Windows;

namespace MUTEit.App;

/// <summary>
/// Entry point. One EXE, several modes:
/// <list type="table">
/// <item><term>(no argument), --autostart</term><description>Tray (Windows Forms). A second start opens the settings window of the running tray.</description></item>
/// <item><term>--settings</term><description>Settings window (Avalonia) – own process, ends with the window.</description></item>
/// <item><term>--toggle / --mute / --unmute</term><description>Switch the microphone of the running tray (scripts, Stream Deck, jump list).</description></item>
/// <item><term>--taskbar</term><description>Pinned taskbar switch: switches the taskbar button on and starts the tray if needed.</description></item>
/// <item><term>--register-autostart / --unregister-autostart</term><description>Installer: Run key of the current user; the tray keeps this choice.</description></item>
/// <item><term>--setup overlay=0|1 taskbar=0|1</term><description>Installer: writes the chosen options into the settings of the current user.</description></item>
/// <item><term>--exit</term><description>Installer / update / jump list: ask the running tray to apply the exit state and quit.</description></item>
/// </list>
/// Tray and settings code live in separate classes so that the tray process never loads Avalonia.
/// </summary>
internal static class Program
{
    public const string TrayMutexName = @"Local\MUTEit.Tray";
    public const string SettingsMutexName = @"Local\MUTEit.Settings";
    public const string SettingsShowEventName = @"Local\MUTEit.Settings.Show";

    public const string TaskbarArgument = "--taskbar";

    [STAThread]
    public static int Main(string[] args)
    {
        bool Has(string flag) => args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        if (Has("--register-autostart") || Has("--unregister-autostart"))
        {
            // Installer choice: mark autostart as initialized so the first tray start does not switch it on again.
            var on = Has("--register-autostart");
            var ok = Autostart.Set(on);
            TrySetup(s => s.AutostartInitialized = true);
            return ok ? 0 : 1;
        }

        if (Has("--setup"))
        {
            return TrySetup(s =>
            {
                foreach (var arg in args)
                {
                    var parts = arg.Split('=', 2);
                    if (parts.Length != 2)
                    {
                        continue;
                    }

                    var on = parts[1].Trim() is "1" or "true" or "on";
                    switch (parts[0].Trim().ToLowerInvariant())
                    {
                        case "overlay":
                            s.OverlayEnabled = on;
                            break;
                        case "taskbar":
                            s.TaskbarButton = on;
                            break;
                    }
                }
            }) ? 0 : 1;
        }

        if (Has("--exit"))
        {
            return TrayClient.Send(new TrayRequest(TrayCommand.Exit), 2000) is { Ok: true } ? 0 : 1;
        }

        foreach (var (flag, command) in new[] { ("--toggle", TrayCommand.Toggle), ("--mute", TrayCommand.Mute), ("--unmute", TrayCommand.Unmute) })
        {
            if (Has(flag))
            {
                return TrayClient.Send(new TrayRequest(command), 1500) is { Ok: true } ? 0 : 1;
            }
        }

        if (Has("--settings"))
        {
            return RunSettings(args);
        }

        if (Has(TaskbarArgument))
        {
            // A pinned taskbar switch was clicked while its window does not exist: switch it on (the tray shows it).
            new SettingsStore().Update(s => s.TaskbarButton = true);
            if (TrayClient.Send(new TrayRequest(TrayCommand.Reload), 1000) is not null)
            {
                return 0;
            }
        }

        return RunTray(Has(Autostart.Argument) || Has(TaskbarArgument));
    }

    /// <summary>Installer helper: changes the settings file; never throws.</summary>
    private static bool TrySetup(Action<MuteitSettings> change)
    {
        try
        {
            new SettingsStore().Update(change);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>Path of MUTEit.exe (for child processes, autostart and the jump list).</summary>
    public static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "MUTEit.exe");

    /// <summary>Starts MUTEit.exe with an argument (settings window, tray).</summary>
    public static void Start(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ExePath, arguments) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Tray.TrayContext.LogError(ex);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunTray(bool silentSecondStart)
    {
        using var mutex = new Mutex(true, TrayMutexName, out var createdNew);
        if (!createdNew)
        {
            // Already running: a manual second start opens the settings window; autostart stays silent.
            if (!silentSecondStart)
            {
                TrayClient.Send(new TrayRequest(TrayCommand.ShowSettings), 1500);
            }

            return 0;
        }

        return Tray.TrayHost.Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunSettings(string[] args) => SettingsUi.SettingsHost.Run(args);
}
