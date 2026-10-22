using System.Globalization;

namespace PixlReplay.Ui.Edit;

/// <summary>Colours as "#RRGGBB" and as hue (0..360), saturation and value (0..1): the colour picker's arithmetic.</summary>
public static class ColourMath
{
    public static (double H, double S, double V) ToHsv(string hex)
    {
        if (!TryParse(hex, out string clean))
            clean = "#FFFFFF";
        double r = Convert.ToInt32(clean.Substring(1, 2), 16) / 255.0;
        double g = Convert.ToInt32(clean.Substring(3, 2), 16) / 255.0;
        double b = Convert.ToInt32(clean.Substring(5, 2), 16) / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r)
                h = 60 * (((g - b) / d) % 6);
            else if (max == g)
                h = 60 * ((b - r) / d + 2);
            else
                h = 60 * ((r - g) / d + 4);
            if (h < 0)
                h += 360;
        }
        return (h, max > 0 ? d / max : 0, max);
    }

    public static string ToHex(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return $"#{Channel(r + m)}{Channel(g + m)}{Channel(b + m)}";
    }

    static string Channel(double value) => ((int)Math.Round(Math.Clamp(value, 0, 1) * 255)).ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>"#RRGGBB" from what someone typed (the hash optional, any case, spaces around); false for anything else.</summary>
    public static bool TryParse(string? text, out string hex)
    {
        hex = "";
        string s = (text ?? "").Trim().TrimStart('#');
        if (s.Length != 6 || !s.All(Uri.IsHexDigit))
            return false;
        hex = "#" + s.ToUpperInvariant();
        return true;
    }
}
