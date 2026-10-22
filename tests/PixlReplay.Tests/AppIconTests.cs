using PixlReplay.App;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

/// <summary>PixlReplay's mark (the window and taskbar icon, and app.ico): drawn in the theme color on the warm plate.</summary>
public static class AppIconTests
{
    static uint Rgb(System.Drawing.Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    [Test]
    public static void The_mark_is_drawn_in_the_theme_color_on_the_warm_plate()
    {
        using var mark = AppIconArt.Draw(64, 0xF48BA4);
        Assert.Equal(0xF48BA4u, Rgb(mark.GetPixel(32, 32)), "the dot");
        Assert.Equal(ThemeColors.Background, Rgb(mark.GetPixel(32, 4)), "the plate: the window's own brown");
        Assert.Equal(0, (int)mark.GetPixel(0, 0).A, "rounded corner");
        using var red = AppIconArt.Draw(64, 0xFF4A5E);
        Assert.Equal(0xFF4A5Eu, Rgb(red.GetPixel(32, 32)), "another theme color");
    }

    /// <summary>
    /// Walks the ring's centre line (clockwise from 3 o'clock, as the drawing angles go) and returns the middle of each
    /// gap, in degrees.
    /// </summary>
    static List<double> Gaps(System.Drawing.Bitmap mark, double radius, uint ring)
    {
        double c = mark.Width / 2.0; // the drawing's centre (GDI+ puts pixel centres on whole numbers)
        bool On(double a)
        {
            var p = mark.GetPixel((int)Math.Round(c + radius * Math.Cos(a * Math.PI / 180)), (int)Math.Round(c + radius * Math.Sin(a * Math.PI / 180)));
            int Dist(uint rgb) => Math.Abs(p.R - (int)(rgb >> 16 & 0xFF)) + Math.Abs(p.G - (int)(rgb >> 8 & 0xFF)) + Math.Abs(p.B - (int)(rgb & 0xFF));
            return Dist(ring) < Dist(ThemeColors.Background);
        }
        // Start inside an arc so every gap is seen whole.
        double start = Enumerable.Range(0, 720).Select(i => i / 2.0).First(On);
        var gaps = new List<double>();
        double? from = null;
        for (double a = start; a <= start + 360; a += 0.5)
        {
            if (!On(a) && from == null)
                from = a;
            else if (On(a) && from is { } f)
            {
                gaps.Add((f + a) / 2 % 360);
                from = null;
            }
        }
        gaps.Sort();
        return gaps;
    }

    static void HasThreeArcs(List<double> gaps, string what)
    {
        Assert.Equal(3, gaps.Count, $"{what}: three arcs around the dot, as in the window's own logo ({string.Join(", ", gaps)})");
        double[] want = [107, 225, 343];
        for (int i = 0; i < 3; i++)
            Assert.True(Math.Abs(gaps[i] - want[i]) < 4, $"{what}: gap {i + 1} at {gaps[i]:0.#}, the logo's is at {want[i]}");
    }

    [Test]
    public static void The_ring_is_three_arcs_like_the_logo_in_the_window()
    {
        foreach (int size in new[] { 256, 64, 32 })
        {
            using var mark = AppIconArt.Draw(size, ThemeColors.Accent);
            // The ring's centre line: the drawing's margin plus half the pen.
            HasThreeArcs(Gaps(mark, size * 0.3, ThemeColors.Accent), $"app icon {size}");
        }
        foreach (string kind in new[] { "active", "waiting", "paused", "recording", "error", "saved" })
        {
            using var tray = TrayController.DrawBitmap(kind);
            HasThreeArcs(Gaps(tray, 10, TrayController.ColorFor(kind)), $"tray icon '{kind}'");
        }
    }

    [Test]
    public static void The_icon_file_has_every_size_windows_asks_for()
    {
        byte[] ico = AppIconArt.Ico();
        Assert.Equal((0, 1), (BitConverter.ToUInt16(ico, 0), BitConverter.ToUInt16(ico, 2)), "an icon file");
        Assert.Equal(AppIconArt.Sizes.Length, (int)BitConverter.ToUInt16(ico, 4), "frames");
        using var icon = new System.Drawing.Icon(new MemoryStream(ico), 32, 32);
        Assert.Equal(32, icon.Width, "Windows reads it");
    }
}
