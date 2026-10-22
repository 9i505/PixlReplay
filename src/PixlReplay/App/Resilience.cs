using System.Runtime.InteropServices;
using PixlReplay.Engine;

namespace PixlReplay.App;

/// <summary>Keeps the engine alive through errors.</summary>
public static class Resilience
{
    const int RestartNoPatch = 4, RestartNoReboot = 8;

    /// <summary>Logs unexpected exceptions; UI-thread exceptions do not end the process.</summary>
    public static void InstallHandlers()
    {
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, e) => ObsLog.Error("ui thread: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ObsLog.Error("fatal: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ObsLog.Warn("task: " + e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>Asks Windows to relaunch the engine if it crashes (after it has run for 60 s).</summary>
    public static void RegisterRestart(string arguments) => RegisterApplicationRestart(arguments, RestartNoPatch | RestartNoReboot);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern int RegisterApplicationRestart(string commandLine, int flags);
}
