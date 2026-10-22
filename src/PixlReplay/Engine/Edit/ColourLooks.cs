using System.Globalization;

namespace PixlReplay.Engine.Edit;

/// <summary>
/// The colour looks as FFmpeg filters. The same text runs on the preview pictures and in the export, so what is seen
/// is what is saved. Strength moves every value from neutral (eq 1, balance 0, no sharpening, full colour) to the look.
/// </summary>
public static class ColourLooks
{
    public static string? Filter(ColourLook look, double strength)
    {
        if (look == ColourLook.None || strength <= 0)
            return null;
        double k = Math.Min(strength, 1);
        string Eq(double full) => N(1 + (full - 1) * k);
        string Mix(double full) => N(full * k);
        return look switch
        {
            ColourLook.Vivid => $"eq=saturation={Eq(1.35)}:contrast={Eq(1.08)}",
            ColourLook.Cinematic => $"eq=contrast={Eq(1.12)}:saturation={Eq(0.85)},colorbalance=rs={Mix(-0.05)}:bs={Mix(0.07)}:rh={Mix(0.06)}:bh={Mix(-0.04)}",
            ColourLook.Warm => $"colorbalance=rs={Mix(0.08)}:gs={Mix(0.02)}:bs={Mix(-0.08)}:rm={Mix(0.05)}:bm={Mix(-0.05)}",
            ColourLook.Cool => $"colorbalance=rs={Mix(-0.07)}:bs={Mix(0.08)}:bm={Mix(0.05)}",
            ColourLook.Sharp => $"unsharp=5:5:{Mix(0.9)}:5:5:0",
            ColourLook.Mono => $"hue=s={N(1 - k)}",
            // Shadows toward teal, highlights toward orange: the film grade.
            ColourLook.Teal => $"colorbalance=rs={Mix(-0.10)}:gs={Mix(0.02)}:bs={Mix(0.14)}:rh={Mix(0.12)}:gh={Mix(0.04)}:bh={Mix(-0.14)},eq=saturation={Eq(1.1)}",
            // Lifted blacks, softened whites, faded warm colours.
            ColourLook.Retro => $"curves=all='0/{Mix(0.07)} 0.5/0.5 1/{N(1 - 0.06 * k)}',eq=saturation={Eq(0.82)}:contrast={Eq(0.95)},colorbalance=rm={Mix(0.05)}:bm={Mix(-0.05)}",
            ColourLook.Golden => $"colorbalance=rm={Mix(0.10)}:gm={Mix(0.04)}:bm={Mix(-0.12)}:rh={Mix(0.08)}:bh={Mix(-0.08)},eq=saturation={Eq(1.1)}:brightness={Mix(0.02)}",
            ColourLook.Night => $"eq=brightness={Mix(-0.05)}:contrast={Eq(1.08)}:saturation={Eq(0.9)},colorbalance=rs={Mix(-0.06)}:bs={Mix(0.14)}:bm={Mix(0.06)}",
            ColourLook.Pop => $"eq=contrast={Eq(1.25)}:saturation={Eq(1.5)},unsharp=5:5:{Mix(0.5)}:5:5:0",
            // Film grain: the same grain every time it is saved (a fixed seed), moving from picture to picture.
            ColourLook.Grain => $"noise=alls={N(Math.Round(16 * k))}:allf=t+u:all_seed=7",
            _ => null,
        };
    }

    static string N(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
