namespace PixlReplay.Settings;

/// <summary>
/// The theme the user chose on the Look page: three colors (the window's background, its text and the accent) and every
/// shade the window, the tray and the toasts draw with, derived from them. Starts as Pixl Replay's own theme (a dark
/// graphite window with a coral-red accent) until <see cref="Use"/> gets the user's.
/// </summary>
public static class ThemeColors
{
    public static uint Accent { get; private set; } = LookSettings.Parse(LookSettings.DefaultAccent);
    public static uint Background { get; private set; } = LookSettings.Parse(LookSettings.DefaultBackground);
    public static uint Text { get; private set; } = LookSettings.Parse(LookSettings.DefaultText);

    /// <summary>The user's colors (from settings.json) for everything drawn from now on.</summary>
    public static void Use(LookSettings look)
    {
        Accent = LookSettings.Parse(look.Accent);
        Background = LookSettings.Parse(look.Background);
        Text = LookSettings.Parse(look.Text);
    }

    /// <summary>A light background (the user picked one): shadows and fields go darker instead of lighter.</summary>
    public static bool IsLight => Luminance(Background) > 0.35;

    /// <summary>Text and icons on the accent: white or the background's darkest, whichever reads better.</summary>
    public static uint OnAccent
    {
        get
        {
            uint dark = Mix(IsLight ? Text : Background, 0x000000, 0.35);
            return Contrast(Accent, 0xFFFFFF) >= Contrast(Accent, dark) ? 0xFFFFFF : dark;
        }
    }

    /// <summary>Mouse-over shade: 15% toward white.</summary>
    public static uint Hover => Mix(Accent, 0xFFFFFF, 0.15);

    /// <summary>Selected-row tint: 13% of the accent over the window background.</summary>
    public static uint Soft => Mix(Background, Accent, 0.13);

    // The window's shades: steps from the background toward the text, so any pair of colors keeps its contrast.
    public static uint Surface => Mix(Background, Text, 0.055);
    public static uint CardHover => Mix(Background, Text, 0.10);
    public static uint Stroke => Mix(Background, Text, 0.13);
    public static uint StrokeStrong => Mix(Background, Text, 0.22);
    /// <summary>The outline of a field or an outlined button: 3:1 against the cards.</summary>
    public static uint ControlStroke => Mix(Background, Text, 0.45);
    public static uint NavBg => IsLight ? Mix(Background, Text, 0.035) : Mix(Background, 0x000000, 0.16);
    public static uint FieldFill => IsLight ? Mix(Background, 0xFFFFFF, 0.6) : Mix(Background, 0x000000, 0.10);
    public static uint FieldHover => Mix(FieldFill, Text, 0.04);
    public static uint ItemSelected => Mix(Background, Text, 0.17);
    public static uint TextSecondary => Mix(Text, Background, 0.33);
    public static uint Idle => Mix(Text, Background, 0.38);

    public const uint Success = 0x2ED47A;
    public const uint Warning = 0xFFB020;

    public static uint Mix(uint from, uint to, double amount)
    {
        uint Channel(int shift)
        {
            double a = (from >> shift) & 0xFF, b = (to >> shift) & 0xFF;
            return (uint)Math.Round(a + (b - a) * amount) << shift;
        }
        return Channel(16) | Channel(8) | Channel(0);
    }

    /// <summary>WCAG relative luminance.</summary>
    public static double Luminance(uint rgb)
    {
        double Linear(int shift)
        {
            double c = ((rgb >> shift) & 0xFF) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(16) + 0.7152 * Linear(8) + 0.0722 * Linear(0);
    }

    public static double Contrast(uint a, uint b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
