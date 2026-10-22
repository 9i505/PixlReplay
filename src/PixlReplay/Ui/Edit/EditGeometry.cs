using System.Windows;
using PixlReplay.Engine.Edit;

namespace PixlReplay.Ui.Edit;

public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>
/// Where things are on the preview. Items keep 0..1 coordinates of the picture; the preview shows the picture centred
/// and scaled to fit (bars on the sides or above and below), so a point on screen is first placed on the picture.
/// </summary>
public static class EditGeometry
{
    /// <summary>The smallest box (a share of each side) a resize or a new box can make.</summary>
    public const double MinSize = 0.03;

    /// <summary>A new text, drawing, shape or blur shows this long from the playhead.</summary>
    public const double NewItemSeconds = 5;

    /// <summary>Where a picture of <paramref name="aspect"/> (width / height) sits in a view of <paramref name="view"/>.</summary>
    public static Rect PictureRect(Size view, double aspect)
    {
        if (view.Width <= 0 || view.Height <= 0 || aspect <= 0)
            return new Rect(0, 0, Math.Max(0, view.Width), Math.Max(0, view.Height));
        double width = view.Width, height = width / aspect;
        if (height > view.Height)
        {
            height = view.Height;
            width = height * aspect;
        }
        return new Rect((view.Width - width) / 2, (view.Height - height) / 2, width, height);
    }

    /// <summary>A point of the view as 0..1 of the picture, kept on the picture.</summary>
    public static Point Normalize(Point view, Rect picture) => picture.Width <= 0 || picture.Height <= 0
        ? new Point(0, 0)
        : new Point(Math.Clamp((view.X - picture.X) / picture.Width, 0, 1), Math.Clamp((view.Y - picture.Y) / picture.Height, 0, 1));

    public static Point ToPixels(Point normalized, Rect picture) =>
        new(picture.X + normalized.X * picture.Width, picture.Y + normalized.Y * picture.Height);

    public static Rect ToPixels(Rect normalized, Rect picture) =>
        new(picture.X + normalized.X * picture.Width, picture.Y + normalized.Y * picture.Height,
            normalized.Width * picture.Width, normalized.Height * picture.Height);

    /// <summary>The box an item covers, in 0..1 of the picture.</summary>
    public static Rect Bounds(EditItem item) => item switch
    {
        TextItem t => t.Box,
        BlurItem b => b.Box,
        ShapeItem s => new Rect(s.From, s.To),
        StrokeItem s when s.AllPoints.Any() => new Rect(
            new Point(s.AllPoints.Min(p => p.X), s.AllPoints.Min(p => p.Y)),
            new Point(s.AllPoints.Max(p => p.X), s.AllPoints.Max(p => p.Y))),
        _ => Rect.Empty,
    };

    /// <summary>The item under a point (the last drawn wins), with a margin for thin lines.</summary>
    public static EditItem? HitTest(IReadOnlyList<EditItem> items, Point at, double margin)
    {
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var box = Bounds(items[i]);
            if (box.IsEmpty)
                continue;
            box.Inflate(margin, margin);
            if (box.Contains(at))
                return items[i];
        }
        return null;
    }

    /// <summary>The box with one corner dragged to <paramref name="to"/>; the opposite corner stays, all inside the picture.</summary>
    public static Rect Resize(Rect box, Corner corner, Point to)
    {
        double x = Math.Clamp(to.X, 0, 1), y = Math.Clamp(to.Y, 0, 1);
        double left = box.Left, top = box.Top, right = box.Right, bottom = box.Bottom;
        switch (corner)
        {
            case Corner.TopLeft:
                left = Math.Min(x, right - MinSize);
                top = Math.Min(y, bottom - MinSize);
                break;
            case Corner.TopRight:
                right = Math.Max(x, left + MinSize);
                top = Math.Min(y, bottom - MinSize);
                break;
            case Corner.BottomLeft:
                left = Math.Min(x, right - MinSize);
                bottom = Math.Max(y, top + MinSize);
                break;
            default:
                right = Math.Max(x, left + MinSize);
                bottom = Math.Max(y, top + MinSize);
                break;
        }
        left = Math.Max(0, left);
        top = Math.Max(0, top);
        right = Math.Min(1, right);
        bottom = Math.Min(1, bottom);
        return new Rect(left, top, Math.Max(MinSize, right - left), Math.Max(MinSize, bottom - top));
    }

    /// <summary>The item moved by <paramref name="delta"/> (0..1 units), kept whole on the picture.</summary>
    public static EditItem Move(EditItem item, Vector delta)
    {
        var box = Bounds(item);
        if (box.IsEmpty)
            return item;
        double dx = Math.Clamp(delta.X, -box.Left, 1 - box.Right), dy = Math.Clamp(delta.Y, -box.Top, 1 - box.Bottom);
        var d = new Vector(dx, dy);
        return item switch
        {
            TextItem t => t with { Box = Offset(t.Box, d) },
            BlurItem b => b with { Box = Offset(b.Box, d) },
            ShapeItem s => s with { From = s.From + d, To = s.To + d },
            StrokeItem s => s with { Strokes = s.Strokes.Select(l => (IReadOnlyList<Point>)l.Select(p => p + d).ToList()).ToList() },
            _ => item,
        };
    }

    /// <summary>The item fitted into <paramref name="box"/> (shapes and drawings scale with it).</summary>
    public static EditItem WithBounds(EditItem item, Rect box)
    {
        var old = Bounds(item);
        Point Map(Point p) => old.Width <= 0 || old.Height <= 0
            ? new Point(box.X + box.Width / 2, box.Y + box.Height / 2)
            : new Point(box.X + (p.X - old.X) / old.Width * box.Width, box.Y + (p.Y - old.Y) / old.Height * box.Height);
        return item switch
        {
            TextItem t => t with { Box = box, SizeRel = old.Height > 0 ? t.SizeRel * box.Height / old.Height : t.SizeRel },
            BlurItem b => b with { Box = box },
            ShapeItem s => s with { From = Map(s.From), To = Map(s.To) },
            StrokeItem s => s with { Strokes = s.Strokes.Select(l => (IReadOnlyList<Point>)l.Select(Map).ToList()).ToList() },
            _ => item,
        };
    }

    /// <summary>A new item's time: from the playhead, five seconds, stopping at the clip's end (a screenshot: none).</summary>
    public static (double Start, double End) NewItemTime(EditProject p, double playheadOutput)
    {
        if (p.Duration <= 0)
            return (0, 0);
        double start = p.ToSource(playheadOutput) ?? 0;
        return (start, Math.Min(p.Duration, start + NewItemSeconds));
    }

    /// <summary>Whether an item shows at a source time (a screenshot's items always show).</summary>
    public static bool ShowsAt(EditProject p, EditItem item, double source) =>
        p.Duration <= 0 || (source >= item.Start && source <= item.End);

    static Rect Offset(Rect r, Vector d) => new(r.X + d.X, r.Y + d.Y, r.Width, r.Height);
}
