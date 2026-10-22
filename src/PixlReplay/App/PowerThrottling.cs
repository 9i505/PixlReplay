using System.Runtime.InteropServices;

namespace PixlReplay.App;

/// <summary>Opts the process out of Windows EcoQoS so encoding threads keep full speed in the background.</summary>
public static class PowerThrottling
{
    const int ProcessPowerThrottling = 4;
    const uint ExecutionSpeed = 0x1, IgnoreTimerResolution = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    struct State
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    public static bool OptOut()
    {
        var state = new State { Version = 1, ControlMask = ExecutionSpeed | IgnoreTimerResolution, StateMask = 0 };
        return SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, Marshal.SizeOf<State>());
    }

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetProcessInformation(IntPtr process, int infoClass, ref State info, int size);
}
