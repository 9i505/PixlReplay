using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Kairos.Engine;

/// <summary>
/// The recording ahead of a game on the CPU. libobs keeps its own audio thread ahead (MMCSS "Audio") but not its video
/// threads, so a game that keeps every core busy (a user's 8 threads in ARC Raiders) left the threads that draw, convert
/// and hand the picture to the encoder waiting while the sound went on: clips froze with sound. Measured 2026-10-24,
/// Kairos on two CPUs with busy threads on the same two:
/// <list type="bullet">
/// <item>as before: 29 % of the frames lagged, 32 % skipped (busy threads one level above Kairos);</item>
/// <item>the video threads alone time-critical: 0-3 %, but 12-24 % with busier CPUs: they also wait on Kairos's other
/// threads (the .NET runtime's among them);</item>
/// <item>these and the process above normal: none lagged or skipped in any run, even with busy threads at its level.</item>
/// </list>
/// Both take no administrator. Kairos works a few milliseconds a frame and sleeps the rest, so a game loses no more time
/// than before, only the order changes; the heavy work (Smart Save's model) runs a step lower, at the game's level.
/// </summary>
static class CpuPriority
{
    static readonly string[] VideoThreadNames = ["libobs: graphics thread", "obs gpu encode thread", "video-io: video thread"];

    const int TimeCritical = 15; // THREAD_PRIORITY_TIME_CRITICAL
    const uint QueryLimited = 0x0800, SetLimited = 0x0400; // THREAD_QUERY_LIMITED_INFORMATION, THREAD_SET_LIMITED_INFORMATION

    [DllImport("kernel32.dll")]
    static extern IntPtr OpenThread(uint access, bool inherit, int id);

    [DllImport("kernel32.dll")]
    static extern int GetThreadDescription(IntPtr thread, out IntPtr description);

    [DllImport("kernel32.dll")]
    static extern int GetThreadPriority(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetThreadPriority(IntPtr thread, int priority);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// What the process goes to from <paramref name="now"/>: above normal from normal; null when something else chose
    /// (a test runner started it below normal, the user set it higher).
    /// </summary>
    internal static ProcessPriorityClass? Raised(ProcessPriorityClass now) =>
        now == ProcessPriorityClass.Normal ? ProcessPriorityClass.AboveNormal : null;

    /// <summary>The engine's process above normal (<see cref="Raised"/>); what happened, for the log.</summary>
    public static string RaiseProcess()
    {
        try
        {
            using var self = Process.GetCurrentProcess();
            if (Raised(self.PriorityClass) is not { } raised)
                return $"left {self.PriorityClass}";
            self.PriorityClass = raised;
            return "above normal";
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"refused ({e.Message})";
        }
    }

    /// <summary>
    /// libobs's video threads not raised yet go time-critical, found by the names libobs gives them; logged. An encoder's
    /// start makes its thread anew, so this runs after every output start.
    /// </summary>
    public static void RaiseVideoThreads()
    {
        var raised = new List<string>();
        using var self = Process.GetCurrentProcess();
        foreach (ProcessThread thread in self.Threads)
        {
            IntPtr handle = OpenThread(QueryLimited | SetLimited, false, thread.Id);
            if (handle == IntPtr.Zero)
                continue; // it ended
            try
            {
                if (GetThreadPriority(handle) == TimeCritical || GetThreadDescription(handle, out IntPtr text) < 0)
                    continue;
                string? name = Marshal.PtrToStringUni(text);
                LocalFree(text);
                if (name != null && VideoThreadNames.Contains(name))
                    raised.Add(SetThreadPriority(handle, TimeCritical) ? name : $"{name} refused ({Marshal.GetLastPInvokeError()})");
            }
            finally
            {
                CloseHandle(handle);
            }
        }
        if (raised.Count > 0)
            ObsLog.Info($"cpu priority: time-critical: {string.Join(", ", raised)}");
    }
}
