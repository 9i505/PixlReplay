using System.Runtime.InteropServices;
using System.Text;

namespace PixlReplay.App;

public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom);

/// <summary>Reports whether a fullscreen app (usually a game) is in the foreground, with a grace period on exit.</summary>
public sealed class GameWatcher : IDisposable
{
    static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd" };
    static readonly TimeSpan Grace = TimeSpan.FromSeconds(15);

    readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    DateTime _lastSeenUtc = DateTime.MinValue;

    public bool GameActive { get; private set; }
    public event Action<bool>? Changed;

    public GameWatcher()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    public static bool IsFullscreen(ScreenRect window, ScreenRect monitor, string className) =>
        !ShellClasses.Contains(className) &&
        window.Left <= monitor.Left && window.Top <= monitor.Top &&
        window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;

    void Poll()
    {
        bool now = ForegroundIsFullscreen();
        if (now)
            _lastSeenUtc = DateTime.UtcNow;
        bool active = now || DateTime.UtcNow - _lastSeenUtc < Grace;
        if (active != GameActive)
        {
            GameActive = active;
            Changed?.Invoke(active);
        }
    }

    static bool ForegroundIsFullscreen()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT r))
            return false;
        IntPtr mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(mon, ref info))
            return false;
        var cls = new StringBuilder(64);
        GetClassName(hwnd, cls, cls.Capacity);
        return IsFullscreen(new ScreenRect(r.Left, r.Top, r.Right, r.Bottom),
            new ScreenRect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom), cls.ToString());
    }

    public void Dispose() => _timer.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
}
