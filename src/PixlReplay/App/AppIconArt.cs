using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PixlReplay.Settings;

namespace PixlReplay.App;

/// <summary>
/// Pixl Replay's mark: the Pixl family's geometric P, with a play triangle where Pixl has its diamond. The app icon puts it
/// on a rounded plate in the window's background; the tray colors the whole letter by state. Drawn for app.ico
/// (<c>PixlReplay.exe --write-icon</c>), the window and the tray, so no image files ship with the app.
/// </summary>
public static class AppIconArt
{
    public static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    /// <summary>The box the letter is drawn in (the points below are in it).</summary>
    public const float ArtWidth = 328, ArtHeight = 420;

    /// <summary>The P's stem, cut on a slant at its foot.</summary>
    public static readonly PointF[] Stem = [new(1, 1), new(94, 1), new(94, 326), new(1, 419)];

    /// <summary>The P's bowl: apart from the stem at the top (a slot), joined to it under the counter, its corners cut.</summary>
    public static readonly PointF[] Bowl =
    [
        new(126, 1), new(246, 1), new(327, 82), new(327, 178), new(246, 261), new(94, 261), new(94, 176),
        new(208, 176), new(235, 149), new(235, 114), new(208, 87), new(126, 87),
    ];

    /// <summary>The play mark in the counter, where Pixl has its diamond.</summary>
    public static readonly PointF[] Play = [new(146, 101), new(146, 161), new(198, 131)];

    /// <summary>The letter (stem and bowl) scaled into <paramref name="box"/>, keeping its shape and centred.</summary>
    public static GraphicsPath Letter(RectangleF box)
    {
        var path = new GraphicsPath(FillMode.Winding);
        path.AddPolygon(Fit(Stem, box));
        path.AddPolygon(Fit(Bowl, box));
        return path;
    }

    /// <summary>Points from the art box into <paramref name="box"/>, the same scale on both sides, centred.</summary>
    public static PointF[] Fit(PointF[] points, RectangleF box)
    {
        float scale = Math.Min(box.Width / ArtWidth, box.Height / ArtHeight);
        float x0 = box.X + (box.Width - ArtWidth * scale) / 2, y0 = box.Y + (box.Height - ArtHeight * scale) / 2;
        return points.Select(p => new PointF(x0 + p.X * scale, y0 + p.Y * scale)).ToArray();
    }

    /// <summary>
    /// The app icon: a tile in <paramref name="accent"/> with the letter and the play mark in white (dark on a light
    /// accent). Pixl's own icon is a white letter on dark: the colored tile tells the two apart on the taskbar.
    /// </summary>
    public static Bitmap Draw(int size, uint accent)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        float w = size - 1;
        using (var plate = GdiShapes.Rounded(new RectangleF(0, 0, w, w), size * 0.22f))
        {
            using var fill = new SolidBrush(Opaque(accent));
            g.FillPath(fill, plate);
        }
        // Small icons get a bigger letter: the slot and the counter would close up otherwise.
        float h = size * (size <= 24 ? 0.78f : 0.66f);
        var box = new RectangleF((size - h) / 2, (size - h) / 2, h, h);
        using var ink = new SolidBrush(Opaque(InkOn(accent)));
        using (var letter = Letter(box))
            g.FillPath(ink, letter);
        g.FillPolygon(ink, Fit(Play, box));
        return bmp;
    }

    /// <summary>The letter's color on a tile of <paramref name="plate"/>: white, or near-black on a light tile.</summary>
    public static uint InkOn(uint plate) => ThemeColors.Luminance(plate) > 0.45 ? 0x16161Au : 0xFFFFFFu;

    /// <summary>
    /// The install wizard's side picture: the mark above the name ("Pixl" in the text color, "Replay" in the accent), on
    /// the window's background.
    /// </summary>
    public static Bitmap Wizard(int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(Opaque(ThemeColors.Background));
        float letter = height * 0.17f;
        var box = new RectangleF((width - letter) / 2, height * 0.33f, letter, letter);
        using (var path = Letter(box))
        using (var ink = new SolidBrush(Opaque(ThemeColors.Text)))
            g.FillPath(ink, path);
        using (var mark = new SolidBrush(Opaque(ThemeColors.Accent)))
            g.FillPolygon(mark, Fit(Play, box));

        using var bold = new Font("Segoe UI Semibold", height * 0.046f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var light = new Font("Segoe UI Light", height * 0.046f, FontStyle.Regular, GraphicsUnit.Pixel);
        var format = StringFormat.GenericTypographic;
        float w1 = g.MeasureString("Pixl", bold, 1000, format).Width, gap = height * 0.014f;
        float w2 = g.MeasureString("Replay", light, 1000, format).Width;
        float x = (width - w1 - gap - w2) / 2, y = box.Bottom + height * 0.05f;
        using (var ink = new SolidBrush(Opaque(ThemeColors.Text)))
            g.DrawString("Pixl", bold, ink, x, y, format);
        using (var accent = new SolidBrush(Opaque(ThemeColors.Accent)))
            g.DrawString("Replay", light, accent, x + w1 + gap, y, format);
        return bmp;
    }

    /// <summary>An .ico file (PNG frames) with the mark at every size in <see cref="Sizes"/>.</summary>
    public static byte[] Ico() => Ico(ThemeColors.Accent);

    public static byte[] Ico(uint accent)
    {
        var frames = Sizes.Select(s =>
        {
            using var bmp = Draw(s, accent);
            using var png = new MemoryStream();
            bmp.Save(png, ImageFormat.Png);
            return png.ToArray();
        }).ToList();
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)Sizes.Length);
        int offset = 6 + 16 * Sizes.Length;
        for (int i = 0; i < Sizes.Length; i++)
        {
            byte side = (byte)(Sizes[i] >= 256 ? 0 : Sizes[i]);
            w.Write(side);
            w.Write(side);
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write((uint)frames[i].Length);
            w.Write((uint)offset);
            offset += frames[i].Length;
        }
        foreach (var f in frames)
            w.Write(f);
        w.Flush();
        return ms.ToArray();
    }

    static Color Opaque(uint rgb) => Color.FromArgb(255, (int)(rgb >> 16) & 0xFF, (int)(rgb >> 8) & 0xFF, (int)rgb & 0xFF);
}
