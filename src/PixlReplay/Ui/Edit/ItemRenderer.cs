using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixlReplay.Engine.Edit;
using TextEffect = PixlReplay.Engine.Edit.TextEffect;

namespace PixlReplay.Ui.Edit;

/// <summary>
/// Draws an edit's items. The editor's preview and the export use this same drawing (the export as one transparent PNG
/// per item at the clip's size), so what is seen is what is saved. Blur boxes are not drawn here: FFmpeg blurs them.
/// </summary>
public static class ItemRenderer
{
    static readonly Brush Backing = Freeze(new SolidColorBrush(Color.FromArgb(0xB8, 0x14, 0x10, 0x10)));

    public static void Draw(DrawingContext dc, EditItem item, Size frame)
    {
        switch (item)
        {
            case TextItem text:
                DrawText(dc, text, frame);
                break;
            case StrokeItem drawing:
                foreach (var stroke in drawing.Strokes.Where(s => s.Count > 0))
                    DrawStroke(dc, stroke, drawing.ColorHex, drawing.ThicknessRel, frame);
                break;
            case ShapeItem shape:
                DrawShape(dc, shape, frame);
                break;
        }
    }

    /// <summary>
    /// One item on a transparent picture just big enough for it (drawn at the clip's size), written as PNG into
    /// <paramref name="folder"/>, with where it goes in the frame.
    /// </summary>
    public static Overlay RenderPng(EditItem item, int width, int height, string folder)
    {
        Directory.CreateDirectory(folder);
        var frame = new Size(width, height);
        var measure = new DrawingVisual();
        using (var dc = measure.RenderOpen())
            Draw(dc, item, frame);
        var bounds = measure.ContentBounds;
        int x0 = 0, y0 = 0, x1 = 2, y1 = 2;
        if (!bounds.IsEmpty)
        {
            // A pixel of room for anti-aliasing, kept inside the frame.
            x0 = Math.Clamp((int)Math.Floor(bounds.Left) - 1, 0, width - 1);
            y0 = Math.Clamp((int)Math.Floor(bounds.Top) - 1, 0, height - 1);
            x1 = Math.Clamp((int)Math.Ceiling(bounds.Right) + 1, x0 + 1, width);
            y1 = Math.Clamp((int)Math.Ceiling(bounds.Bottom) + 1, y0 + 1, height);
        }
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-x0, -y0));
            Draw(dc, item, frame);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(x1 - x0, y1 - y0, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.Combine(folder, item.Id.ToString("N") + ".png");
        using (var file = File.Create(path))
            encoder.Save(file);
        return new Overlay(path, x0, y0);
    }

    /// <summary>The text laid out at its size (its box is fitted to this by the editor).</summary>
    public static FormattedText Layout(TextItem text, Size frame) =>
        new(text.Text.Length == 0 ? " " : text.Text, CultureInfo.CurrentUICulture,
            IsRightToLeft(text.Text) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, TextFonts.For(text.Font).Typeface,
            Math.Max(1, text.SizeRel * frame.Height), Brush(text.ColorHex), 1.0);

    /// <summary>The box a text needs at its size, with room around it, as shares of the picture (at most all of it).</summary>
    public static Size BoxSize(TextItem text, Size frame)
    {
        var layout = Layout(text with { Text = text.Text.Length == 0 ? "Aa" : text.Text }, frame);
        double pad = text.SizeRel * frame.Height * 0.5;
        return new Size(Math.Min(1, (layout.WidthIncludingTrailingWhitespace + pad * 2) / frame.Width),
            Math.Min(1, (layout.Height + pad) / frame.Height));
    }

    static void DrawText(DrawingContext dc, TextItem text, Size frame)
    {
        var box = Scale(text.Box, frame);
        if (text.Effect == TextEffect.Box)
            dc.DrawRoundedRectangle(Backing, null, box, box.Height * 0.18, box.Height * 0.18);
        else if (text.Effect == TextEffect.Pill)
            dc.DrawRoundedRectangle(Backing, null, box, box.Height / 2, box.Height / 2);
        var layout = Layout(text, frame);
        layout.TextAlignment = TextAlignment.Center;
        layout.MaxTextWidth = Math.Max(1, box.Width);
        // Both directions lay out inside MaxTextWidth from the box's left edge (Arabic is placed right-to-left within it).
        var origin = new Point(box.Left, box.Top + (box.Height - layout.Height) / 2);
        double size = Math.Max(1, text.SizeRel * frame.Height);
        // Dark round light letters, light round dark ones.
        var edge = IsLight(text.ColorHex) ? Colors.Black : Colors.White;
        switch (text.Effect)
        {
            case TextEffect.Shadow:
                double offset = Math.Max(1, size * 0.07);
                dc.DrawGeometry(Freeze(new SolidColorBrush(Color.FromArgb(0xD8, edge.R, edge.G, edge.B))), null,
                    layout.BuildGeometry(new Point(origin.X + offset, origin.Y + offset)));
                break;
            case TextEffect.Outline:
                var pen = new Pen(Freeze(new SolidColorBrush(edge)), Math.Max(1, size * 0.14)) { LineJoin = PenLineJoin.Round };
                pen.Freeze();
                dc.DrawGeometry(null, pen, layout.BuildGeometry(origin));
                break;
            case TextEffect.Glow:
                Halo(dc, layout.BuildGeometry(origin), Colour(text.ColorHex), size, 0.6);
                break;
            case TextEffect.Neon:
            {
                // A tube of light: the colour's halo around letters lit from inside, edged in the colour.
                var colour = Colour(text.ColorHex);
                var letters = layout.BuildGeometry(origin);
                Halo(dc, letters, colour, size, 1);
                var tube = new Pen(Freeze(new SolidColorBrush(colour)), Math.Max(1, size * 0.05)) { LineJoin = PenLineJoin.Round };
                tube.Freeze();
                dc.DrawGeometry(Freeze(new SolidColorBrush(Lighten(colour, 0.6))), tube, letters);
                return;
            }
        }
        dc.DrawText(layout, origin);
    }

    /// <summary>
    /// A soft light around the letters in their colour: strokes of widening width, each fainter. Drawn, not an effect,
    /// so the export's pictures match the preview exactly.
    /// </summary>
    static void Halo(DrawingContext dc, Geometry letters, Color colour, double size, double strength)
    {
        foreach (var (width, alpha) in new[] { (0.44, 0.10), (0.32, 0.16), (0.21, 0.24), (0.11, 0.34) })
        {
            var pen = new Pen(Freeze(new SolidColorBrush(Color.FromArgb((byte)(255 * alpha * strength), colour.R, colour.G, colour.B))),
                Math.Max(1, size * width)) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, letters);
        }
    }

    static Color Lighten(Color c, double amount) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * amount), (byte)(c.G + (255 - c.G) * amount), (byte)(c.B + (255 - c.B) * amount));

    static Color Colour(string colorHex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(colorHex);
        }
        catch (FormatException)
        {
            return Colors.White;
        }
    }

    static void DrawStroke(DrawingContext dc, IReadOnlyList<Point> stroke, string colorHex, double thicknessRel, Size frame)
    {
        var pen = Pen(colorHex, thicknessRel, frame);
        var points = stroke.Select(p => new Point(p.X * frame.Width, p.Y * frame.Height)).ToList();
        if (points.Count == 1)
        {
            dc.DrawEllipse(pen.Brush, null, points[0], pen.Thickness / 2, pen.Thickness / 2);
            return;
        }
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], false, false);
            g.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    static void DrawShape(DrawingContext dc, ShapeItem shape, Size frame)
    {
        var pen = Pen(shape.ColorHex, shape.ThicknessRel, frame);
        var from = new Point(shape.From.X * frame.Width, shape.From.Y * frame.Height);
        var to = new Point(shape.To.X * frame.Width, shape.To.Y * frame.Height);
        switch (shape.Kind)
        {
            case ShapeKind.Rectangle:
                dc.DrawRectangle(null, pen, new Rect(from, to));
                break;
            case ShapeKind.Ellipse:
                var r = new Rect(from, to);
                dc.DrawEllipse(null, pen, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), r.Width / 2, r.Height / 2);
                break;
            default:
                DrawArrow(dc, pen, from, to);
                break;
        }
    }

    /// <summary>A line with a filled head at <paramref name="to"/>, about five line widths long.</summary>
    static void DrawArrow(DrawingContext dc, Pen pen, Point from, Point to)
    {
        var direction = to - from;
        if (direction.Length < 1e-6)
            return;
        direction.Normalize();
        double length = Math.Max(12, pen.Thickness * 5), half = length * 0.6;
        var baseCentre = to - direction * length;
        var side = new Vector(-direction.Y, direction.X) * half;
        dc.DrawLine(pen, from, baseCentre);
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(to, true, true);
            g.PolyLineTo([baseCentre + side, baseCentre - side], true, true);
        }
        head.Freeze();
        dc.DrawGeometry(pen.Brush, null, head);
    }

    static Pen Pen(string colorHex, double thicknessRel, Size frame)
    {
        var pen = new Pen(Brush(colorHex), Math.Max(1, thicknessRel * frame.Height))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();
        return pen;
    }

    static Brush Brush(string colorHex)
    {
        try
        {
            return Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex)));
        }
        catch (FormatException)
        {
            return Brushes.White;
        }
    }

    static Rect Scale(Rect box, Size frame) => new(box.X * frame.Width, box.Y * frame.Height, box.Width * frame.Width, box.Height * frame.Height);

    static bool IsLight(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 110;
    }

    static bool IsRightToLeft(string text) => text.Any(c => c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= 'ﻼ');

    static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
