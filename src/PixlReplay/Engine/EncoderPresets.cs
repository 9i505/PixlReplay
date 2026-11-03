using PixlReplay.Settings;

namespace PixlReplay.Engine;

/// <param name="Cq">NVENC target quality (CQVBR), or CRF for x264.</param>
/// <param name="Best">Ultra or High with room on the card: the encoder's slowest, best settings (see EncoderCatalog).</param>
public sealed record EncoderPlan(
    string EncoderId,
    string RateControl,
    int Cq,
    int MaxBitrateKbps,
    int KeyintSec,
    string Preset,
    int BFrames,
    bool Best = false);

public static class EncoderPresets
{
    // Reference point for the presets: 2560x1440 at 60 fps, AV1.
    const double ReferencePixelRate = 2560.0 * 1440 * 60;
    const int MinBitrateKbps = 4000;
    const int MaxBitrateKbps = 300_000;

    /// <summary>
    /// Up to this many pixels a second (1080p at 170 fps) the encoders keep up with their best settings on any card that
    /// has them; above it they stay at the chosen preset so no frame is skipped.
    /// </summary>
    const double BestPixelRate = 1920.0 * 1080 * 170;

    /// <summary>
    /// Above the best settings' reach, the slowest NVENC preset that still takes every frame: measured on an RTX 5080
    /// Laptop recording a game drawing 400-1100 fps (2026-11-03): 1080p240 at p5, 1080p360 at p2, 1080p500 and 600 at p1,
    /// 720p1000 at p1, each with no frame skipped. A slower one falls behind all at once (1080p360 at p5: 9 frames in 10
    /// repeated, about 30 real frames a second).
    /// </summary>
    static readonly (double PixelRate, string Preset)[] Keeps =
    [
        (1920.0 * 1080 * 240, "p5"),
        (1920.0 * 1080 * 360, "p2"),
    ];

    /// <summary>
    /// The most pixels a second NVENC keeps up with (1080p600; 1080p1000 at p1 repeated 97 % of its frames). Above it the
    /// frame rate is held to this (<see cref="MaxFps"/>).
    /// </summary>
    public const double MaxPixelRate = 1920.0 * 1080 * 600;

    /// <summary>The highest frame rate NVENC keeps up with at <paramref name="width"/> x <paramref name="height"/>.</summary>
    public static int MaxFps(int width, int height) => (int)(MaxPixelRate / ((double)width * height));

    /// <summary>The user's preset, or a faster one when the pixel rate needs it (<see cref="Keeps"/>).</summary>
    static string KeepingUp(string preset, double pixelRate)
    {
        string fastest = Keeps.FirstOrDefault(k => pixelRate <= k.PixelRate).Preset ?? "p1";
        return Level(preset) > Level(fastest) ? fastest : preset;
        static int Level(string p) => p is ['p', >= '1' and <= '7'] ? p[1] - '0' : 5;
    }

    static (int Cq, int Kbps) Reference(QualityPreset q) => q switch
    {
        QualityPreset.Ultra => (22, 60000),
        QualityPreset.High => (26, 40000),
        QualityPreset.Balanced => (30, 25000),
        _ => (34, 15000),
    };

    public static EncoderPlan Plan(VideoCodec codec, QualityPreset q, int width, int height, int fps,
        int customCq, int customMbps, bool nvencAvailable, string nvencPreset = "p5")
    {
        int av1Cq;
        int kbps;
        if (q == QualityPreset.Custom)
        {
            av1Cq = customCq;
            kbps = customMbps * 1000;
        }
        else
        {
            var r = Reference(q);
            double scale = Math.Pow(width * (double)height * fps / ReferencePixelRate, 0.75);
            double codecFactor = codec switch { VideoCodec.Hevc => 1.15, VideoCodec.H264 => 1.5, _ => 1.0 };
            av1Cq = r.Cq;
            kbps = (int)Math.Round(r.Kbps * codecFactor * scale);
        }

        kbps = Math.Clamp(kbps, MinBitrateKbps, MaxBitrateKbps);
        // NVENC AV1 quality runs 1-63, HEVC/H.264 run 1-51.
        int cq = codec == VideoCodec.Av1 ? av1Cq : (int)Math.Round(av1Cq * 51.0 / 63.0, MidpointRounding.AwayFromZero);
        cq = Math.Clamp(cq, 1, codec == VideoCodec.Av1 ? 63 : 51);

        if (!nvencAvailable)
            return new EncoderPlan("obs_x264", "CRF", Math.Clamp((int)Math.Round(av1Cq * 51.0 / 63.0), 1, 51),
                kbps, 1, "veryfast", 2);

        string id = codec switch
        {
            VideoCodec.Hevc => "obs_nvenc_hevc_tex",
            VideoCodec.H264 => "obs_nvenc_h264_tex",
            _ => "obs_nvenc_av1_tex",
        };
        // Ultra and High: the sharpest picture the card makes at this bitrate, when it has the time for it.
        double pixelRate = width * (double)height * fps;
        bool best = q is QualityPreset.Ultra or QualityPreset.High && pixelRate <= BestPixelRate;
        string preset = best ? (q == QualityPreset.Ultra ? "p7" : "p6") : KeepingUp(nvencPreset, pixelRate);
        return new EncoderPlan(id, "CQVBR", cq, kbps, 1, preset, 2, best);
    }

    /// <summary>Worst-case encoded size in MiB of <paramref name="seconds"/> at the max bitrate.</summary>
    public static int EstimateBufferMb(int maxBitrateKbps, int seconds) =>
        (int)Math.Ceiling(maxBitrateKbps * 1000.0 * seconds / 8 / (1024 * 1024));
}
