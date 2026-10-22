using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using PixlReplay.Settings;

namespace PixlReplay.App;

/// <summary>
/// PixlReplay's mark: a rounded plate in the window's brown, a ring of three arcs and a dot in the theme color. Drawn for the
/// settings window and for app.ico (<c>PixlReplay.exe --write-icon</c>).
/// </summary>
public static class AppIconArt
{
    public static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    /// <summary>
    /// The ring's arcs (start, sweep; degrees clockwise from 3 o'clock), measured from the logo in the window's sidebar
    /// (an 18 px circle with StrokeDashArray "6 1.4"), so the icons look the same.
    /// </summary>
    static readonly (float Start, float Sweep)[] Arcs = [(354.5f, 101.5f), (118.5f, 95.5f), (236.5f, 95.5f)];

    /// <summary>The ring, its centre line on <paramref name="box"/>.</summary>
    public static void DrawRing(Graphics g, Pen pen, RectangleF box)
    {
        foreach (var (start, sweep) in Arcs)
            g.DrawArc(pen, box, start, sweep);
    }

    public static Bitmap Draw(int size, uint accent)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);
        float d = size * 0.44f, w = size - 1;
        using (var plate = GdiShapes.Rounded(new RectangleF(0, 0, w, w), d / 2))
        {
            using var fill = new SolidBrush(Opaque(ThemeColors.Background));
            g.FillPath(fill, plate);
        }
        Color color = Opaque(accent);
        float margin = size * 0.2f;
        using (var ring = new Pen(color, Math.Max(1.5f, size * 0.09f)))
            DrawRing(g, ring, new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin));
        float dot = size * 0.24f;
        using (var brush = new SolidBrush(color))
            g.FillEllipse(brush, (size - dot) / 2, (size - dot) / 2, dot, dot);
        return bmp;
    }

    /// <summary>An .ico file (PNG frames) with the mark at every size in <see cref="Sizes"/>.</summary>
    public static byte[] Ico(uint accent = ThemeColors.Accent)
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
