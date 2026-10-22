using PixlReplay.Settings;

namespace PixlReplay.Engine;

public static class ResolutionOptions
{
    static readonly (OutputResolution Value, int Height)[] Presets =
    [
        (OutputResolution.P2160, 2160), (OutputResolution.P1440, 1440), (OutputResolution.P1080, 1080), (OutputResolution.P720, 720),
    ];

    /// <summary>Native first, then every preset strictly smaller than the monitor (no upscaling choices).</summary>
    public static List<OutputResolution> For(int baseHeight) =>
        [OutputResolution.Native, .. Presets.Where(p => p.Height < baseHeight).Select(p => p.Value)];
}
