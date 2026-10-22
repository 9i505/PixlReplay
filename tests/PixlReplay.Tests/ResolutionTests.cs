using PixlReplay.Engine;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

public static class ResolutionTests
{
    [Test]
    public static void Options_on_a_1440p_monitor_stop_below_native()
    {
        Assert.Equal("Native,P1080,P720", string.Join(",", ResolutionOptions.For(1440)), "options");
    }

    [Test]
    public static void Options_on_a_4k_monitor_include_1440p()
    {
        Assert.Equal("Native,P1440,P1080,P720", string.Join(",", ResolutionOptions.For(2160)), "options");
    }

    [Test]
    public static void Options_on_a_1080p_monitor()
    {
        Assert.Equal("Native,P720", string.Join(",", ResolutionOptions.For(1080)), "options");
    }

    [Test]
    public static void Saved_4k_on_a_smaller_monitor_falls_back_to_native()
    {
        Assert.Equal((2560, 1440), VideoMath.OutputSize(2560, 1440, OutputResolution.P2160), "size");
        Assert.Equal((3840, 2160), VideoMath.OutputSize(3840, 2160, OutputResolution.P2160), "4k native");
    }

    [Test]
    public static void Default_1440p_downscales_4k_and_keeps_1080p()
    {
        Assert.Equal((2560, 1440), VideoMath.OutputSize(3840, 2160, OutputResolution.P1440), "4k -> 1440p");
        Assert.Equal((1920, 1080), VideoMath.OutputSize(1920, 1080, OutputResolution.P1440), "1080p stays");
    }
}
