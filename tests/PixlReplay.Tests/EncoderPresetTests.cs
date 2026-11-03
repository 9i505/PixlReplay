using PixlReplay.Engine;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

public static class EncoderPresetTests
{
    [Test]
    public static void Av1_ultra_1440p60_uses_reference_values()
    {
        var p = EncoderPresets.Plan(VideoCodec.Av1, QualityPreset.Ultra, 2560, 1440, 60, 22, 40, nvencAvailable: true);
        Assert.Equal("obs_nvenc_av1_tex", p.EncoderId, "encoder");
        Assert.Equal("CQVBR", p.RateControl, "rate control");
        Assert.Equal(22, p.Cq, "cq");
        Assert.Equal(60000, p.MaxBitrateKbps, "max bitrate");
        Assert.Equal(1, p.KeyintSec, "keyint");
        Assert.Equal("p7", p.Preset, "preset: the slowest, sharpest one while the card has room");
        Assert.Equal(true, p.Best, "best settings");
        Assert.Equal(2, p.BFrames, "b-frames");
        var busy = EncoderPresets.Plan(VideoCodec.Av1, QualityPreset.Ultra, 3840, 2160, 120, 22, 40, nvencAvailable: true);
        Assert.Equal("p1", busy.Preset, "4K120 (as many pixels a second as 1080p480): the fastest preset");
        Assert.Equal(false, busy.Best, "4K120: no best settings");
        var balanced = EncoderPresets.Plan(VideoCodec.Av1, QualityPreset.Balanced, 1920, 1080, 60, 22, 40, nvencAvailable: true);
        Assert.Equal("p5", balanced.Preset, "Balanced keeps the chosen preset");
    }

    [Test]
    public static void High_frame_rates_get_a_preset_that_keeps_up()
    {
        string At(int w, int h, int fps, string chosen = "p7") =>
            EncoderPresets.Plan(VideoCodec.H264, QualityPreset.Ultra, w, h, fps, 22, 40, nvencAvailable: true, chosen).Preset;
        Assert.Equal("p7", At(1920, 1080, 165), "1080p165: the best settings");
        Assert.Equal("p5", At(1920, 1080, 240), "1080p240: p5 at most");
        Assert.Equal("p3", At(1920, 1080, 240, "p3"), "a faster preset the user chose stays");
        Assert.Equal("p2", At(1920, 1080, 360), "1080p360: p2");
        Assert.Equal("p1", At(1920, 1080, 500), "1080p500: p1");
        Assert.Equal("p1", At(1280, 720, 1000), "720p1000: p1");
        Assert.Equal(600, EncoderPresets.MaxFps(1920, 1080), "1080p: 600 fps at most");
        Assert.True(EncoderPresets.MaxFps(1280, 720) >= 1000, "720p: 1000 fps");
        Assert.Equal(337, EncoderPresets.MaxFps(2560, 1440), "1440p: about 340");
    }

    [Test]
    public static void Hevc_scales_cq_to_51_range_and_bitrate_by_1_15()
    {
        var p = EncoderPresets.Plan(VideoCodec.Hevc, QualityPreset.Ultra, 2560, 1440, 60, 22, 40, nvencAvailable: true);
        Assert.Equal("obs_nvenc_hevc_tex", p.EncoderId, "encoder");
        Assert.Equal(18, p.Cq, "cq");
        Assert.Equal(69000, p.MaxBitrateKbps, "max bitrate");
    }

    [Test]
    public static void H264_high_1080p60_scales_by_pixel_rate()
    {
        var p = EncoderPresets.Plan(VideoCodec.H264, QualityPreset.High, 1920, 1080, 60, 22, 40, nvencAvailable: true);
        Assert.Equal("obs_nvenc_h264_tex", p.EncoderId, "encoder");
        Assert.Equal(21, p.Cq, "cq");
        double expected = 40000 * 1.5 * Math.Pow(1920.0 * 1080 / (2560.0 * 1440), 0.75);
        Assert.Near(expected, p.MaxBitrateKbps, expected * 0.01, "max bitrate");
    }

    [Test]
    public static void Custom_uses_user_values_without_scaling()
    {
        var p = EncoderPresets.Plan(VideoCodec.Av1, QualityPreset.Custom, 1920, 1080, 60, 30, 25, nvencAvailable: true);
        Assert.Equal(30, p.Cq, "cq");
        Assert.Equal(25000, p.MaxBitrateKbps, "max bitrate");
    }

    [Test]
    public static void Small_light_output_keeps_a_4_mbps_floor()
    {
        var p = EncoderPresets.Plan(VideoCodec.Av1, QualityPreset.Light, 1280, 720, 30, 22, 40, nvencAvailable: true);
        Assert.Equal(4000, p.MaxBitrateKbps, "max bitrate");
    }

    [Test]
    public static void Without_nvenc_falls_back_to_x264()
    {
        var p = EncoderPresets.Plan(VideoCodec.Av1, QualityPreset.Ultra, 2560, 1440, 60, 22, 40, nvencAvailable: false);
        Assert.Equal("obs_x264", p.EncoderId, "encoder");
        Assert.Equal("CRF", p.RateControl, "rate control");
        Assert.Equal("veryfast", p.Preset, "preset");
    }

    [Test]
    public static void Nvenc_preset_comes_from_settings()
    {
        var p = EncoderPresets.Plan(VideoCodec.Av1, QualityPreset.Ultra, 2560, 1440, 60, 22, 40, nvencAvailable: true, nvencPreset: "p7");
        Assert.Equal("p7", p.Preset, "preset");
    }

    [Test]
    public static void Buffer_estimate_for_60_mbps_over_5_minutes()
    {
        Assert.Equal(2146, EncoderPresets.EstimateBufferMb(60000, 300), "MB");
    }
}
