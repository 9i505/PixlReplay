using PixlReplay.Settings;

namespace PixlReplay.Engine;

public static class VideoMath
{
    /// <summary>Output size for a target height, keeping aspect, even dimensions, never upscaling.</summary>
    public static (int W, int H) OutputSize(int baseW, int baseH, OutputResolution resolution)
    {
        int targetH = ResolutionOptions.Height(resolution, baseH);
        if (targetH >= baseH)
            return (baseW, baseH);

        int w = (int)Math.Round(baseW * (double)targetH / baseH);
        return (w & ~1, targetH & ~1);
    }

    /// <summary>
    /// The frame rate to record at: the one asked for, but never more than the screen shows (a 165 Hz screen gives 165
    /// new pictures a second; 240 would repeat frames, a bigger file that plays worse). Unknown refresh: as asked.
    /// </summary>
    public static int CaptureFps(int wanted, int refreshHz) => refreshHz >= 24 ? Math.Min(wanted, refreshHz) : wanted;

    /// <summary>The refresh rate that limits the recording: the captured screen's, or (a window, anywhere) the fastest screen's.</summary>
    public static int LimitingRefresh(IReadOnlyList<MonitorInfo> monitors, MonitorInfo? captured, CaptureMode mode) =>
        mode == CaptureMode.Window || captured == null
            ? monitors.Select(m => m.RefreshHz).DefaultIfEmpty(0).Max()
            : captured.RefreshHz;

    /// <summary>The monitor with <paramref name="id"/>, else the primary, else the first.</summary>
    public static MonitorInfo? PickMonitor(IReadOnlyList<MonitorInfo> monitors, string? id) =>
        monitors.FirstOrDefault(m => m.Id == id)
        ?? monitors.FirstOrDefault(m => m.Primary)
        ?? monitors.FirstOrDefault();
}
