using PixlReplay.Engine;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

public static class VideoMathTests
{
    [Test]
    public static void Native_keeps_base_size()
    {
        Assert.Equal((2560, 1440), VideoMath.OutputSize(2560, 1440, OutputResolution.Native), "size");
    }

    [Test]
    public static void Downscale_keeps_aspect_and_even_width()
    {
        Assert.Equal((1920, 1080), VideoMath.OutputSize(2560, 1440, OutputResolution.P1080), "16:9");
        Assert.Equal((2580, 1080), VideoMath.OutputSize(3440, 1440, OutputResolution.P1080), "21:9");
        Assert.Equal((1280, 720), VideoMath.OutputSize(2560, 1440, OutputResolution.P720), "720p");
    }

    [Test]
    public static void Never_upscales()
    {
        Assert.Equal((1920, 1080), VideoMath.OutputSize(1920, 1080, OutputResolution.P1440), "size");
    }

    static readonly List<MonitorInfo> Monitors =
    [
        new("id-lg", "LG: 2560x1440 @ -2560,0", 2560, 1440, false),
        new("id-asus", "ASUS: 2560x1440 @ 0,0 (Primary Monitor)", 2560, 1440, true),
    ];

    [Test]
    public static void Known_monitor_id_is_used()
    {
        Assert.Equal("id-lg", VideoMath.PickMonitor(Monitors, "id-lg")!.Id, "id");
    }

    [Test]
    public static void Stale_or_empty_monitor_id_uses_primary()
    {
        Assert.Equal("id-asus", VideoMath.PickMonitor(Monitors, "unplugged")!.Id, "stale");
        Assert.Equal("id-asus", VideoMath.PickMonitor(Monitors, "")!.Id, "empty");
    }

    [Test]
    public static void No_monitors_gives_null()
    {
        Assert.Equal(null, VideoMath.PickMonitor([], "x"), "monitor");
    }
}
