using System.Windows;
using System.Windows.Media;
using PixlReplay.Settings;

namespace PixlReplay.Ui;

/// <summary>Puts the chosen theme color into the resources that Theme.xaml and the pages reference dynamically.</summary>
public static class ThemeAccent
{
    /// <summary>The theme's accent and its shades, also for the built-in Fluent controls (else they take Windows' accent).</summary>
    public static void Apply(ResourceDictionary resources)
    {
        uint rgb = ThemeColors.Accent;
        resources["Accent"] = Brush(rgb);
        resources["AccentHover"] = Brush(ThemeColors.Hover);
        resources["AccentSoft"] = Brush(ThemeColors.Soft);
        resources["OnAccent"] = Brush(ThemeColors.OnAccent);
        foreach (string key in FluentAccentColors)
            resources[key] = Color(rgb);
        foreach (string key in FluentAccentBrushes)
            resources[key] = Brush(rgb);
    }

    static readonly string[] FluentAccentColors =
    [
        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    static readonly string[] FluentAccentBrushes =
    [
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
        "SliderThumbBackground", "SliderThumbBackgroundPointerOver",
        "ComboBoxItemPillFillBrush", // the bar beside the chosen entry of a drop-down list
    ];

    static System.Windows.Media.Color Color(uint rgb) => System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static SolidColorBrush Brush(uint rgb)
    {
        var b = new SolidColorBrush(Color(rgb));
        b.Freeze();
        return b;
    }
}
