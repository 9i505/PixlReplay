using System.Windows;
using System.Windows.Media;

namespace PixlReplay.Ui.Edit;

/// <param name="Key">What a text item keeps (<see cref="Engine.Edit.TextItem.Font"/>).</param>
public sealed record TextFont(string Key, string Label, FontFamily Family, FontWeight Weight)
{
    public Typeface Typeface { get; } = new(Family, FontStyles.Normal, Weight, FontStretches.Normal);
}

/// <summary>
/// The text tool's fonts: Windows' Segoe UI, and nine free fonts that write Arabic and English (SIL Open Font License,
/// from Google Fonts) shipped in fonts\ beside PixlReplay.exe.
/// </summary>
public static class TextFonts
{
    public const string Default = "segoe";

    static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "fonts") + Path.DirectorySeparatorChar;

    public static IReadOnlyList<TextFont> All { get; } =
    [
        new(Default, "Segoe UI", new FontFamily("Segoe UI Variable Display, Segoe UI, Tahoma"), FontWeights.SemiBold),
        Shipped("almarai", "Almarai", FontWeights.ExtraBold),
        Shipped("lalezar", "Lalezar", FontWeights.Normal),
        Shipped("rakkas", "Rakkas", FontWeights.Normal),
        Shipped("ruqaa", "Aref Ruqaa", FontWeights.Bold),
        Shipped("cairo", "Cairo", FontWeights.Bold),
        Shipped("tajawal", "Tajawal", FontWeights.Bold),
        Shipped("changa", "Changa", FontWeights.SemiBold),
        Shipped("reemkufi", "Reem Kufi", FontWeights.Normal),
        Shipped("lemonada", "Lemonada", FontWeights.SemiBold),
    ];

    /// <summary>The font for <paramref name="key"/>; an unknown one is the default font.</summary>
    public static TextFont For(string? key) => All.FirstOrDefault(f => f.Key == key) ?? All[0];

    static TextFont Shipped(string key, string family, FontWeight weight) =>
        new(key, family, new FontFamily(new Uri(Folder), "./#" + family), weight);
}
