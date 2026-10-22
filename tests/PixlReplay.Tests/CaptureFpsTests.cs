using PixlReplay.Engine;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

/// <summary>A recording never runs faster than the screen it captures: more would only repeat frames.</summary>
public static class CaptureFpsTests
{
    static readonly List<MonitorInfo> Screens =
    [
        new("a", "A: 1920x1080 @ 0,0 (Primary Monitor)", 1920, 1080, true, 165),
        new("b", "B: 2560x1600 @ -2560,4", 2560, 1600, false, 240),
    ];

    [Test]
    public static void The_frame_rate_is_held_to_the_captured_screen()
    {
        Assert.Equal(165, VideoMath.CaptureFps(240, 165), "240 asked on a 165 Hz screen");
        Assert.Equal(60, VideoMath.CaptureFps(60, 165), "less than the screen: as asked");
        Assert.Equal(240, VideoMath.CaptureFps(240, 0), "refresh unknown: as asked");
    }

    [Test]
    public static void A_window_follows_the_fastest_screen_and_a_screen_its_own()
    {
        Assert.Equal(165, VideoMath.LimitingRefresh(Screens, Screens[0], CaptureMode.Screen), "the main screen");
        Assert.Equal(240, VideoMath.LimitingRefresh(Screens, Screens[1], CaptureMode.Game), "a game on the fast screen");
        Assert.Equal(240, VideoMath.LimitingRefresh(Screens, Screens[0], CaptureMode.Window), "a window may be on either");
    }

    [Test]
    public static void This_pcs_screens_report_their_refresh_rate()
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        int known = screens.Count(s => DisplayRefresh.At(s.Bounds.X, s.Bounds.Y) >= 24);
        Assert.True(known >= 1, $"at least one of {screens.Length} screens says its refresh rate");
    }
}
