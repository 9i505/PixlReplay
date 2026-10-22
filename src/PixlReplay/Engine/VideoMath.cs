using PixlReplay.Settings;

namespace PixlReplay.Engine;

public static class VideoMath
{
    /// <summary>Output size for a target height, keeping aspect, even dimensions, never upscaling.</summary>
    public static (int W, int H) OutputSize(int baseW, int baseH, OutputResolution resolution)
    {
        int targetH = resolution switch
        {
            OutputResolution.P2160 => 2160,
            OutputResolution.P1440 => 1440,
            OutputResolution.P1080 => 1080,
            OutputResolution.P720 => 720,
            _ => baseH,
        };
        if (targetH >= baseH)
            return (baseW, baseH);

        int w = (int)Math.Round(baseW * (double)targetH / baseH);
        return (w & ~1, targetH & ~1);
    }

    /// <summary>The monitor with <paramref name="id"/>, else the primary, else the first.</summary>
    public static MonitorInfo? PickMonitor(IReadOnlyList<MonitorInfo> monitors, string? id) =>
        monitors.FirstOrDefault(m => m.Id == id)
        ?? monitors.FirstOrDefault(m => m.Primary)
        ?? monitors.FirstOrDefault();
}
