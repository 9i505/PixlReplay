using System.Windows;
using PixlReplay.Engine.Edit;

namespace PixlReplay.Ui.Edit;

/// <summary>
/// Where the timeline puts things: the row names on the left, a ruler on top, the clip's parts, then a row per kind of
/// item. Every row is one bar tall; items at the same time share their row in layers, one over another, and only then
/// does a row grow, each bar comfortably tall, until the timeline would pass <c>maxHeight</c> — then the bars get
/// thinner, so nothing ever scrolls up or down. Zoomed in, the clip is wider than the lane and <c>scroll</c> says how far
/// it is moved sideways.
/// </summary>
public sealed class TimelineLayout
{
    public const double HeadWidth = 108, RulerHeight = 20, BottomPad = 4, EndPad = 10, EdgePx = 6;
    /// <summary>The space between two parts of the clip: wide enough to see where one ends and the next begins.</summary>
    public const double PartGap = 10;
    /// <summary>A row's padding above and below its bars, the gap between layers, and a bar's height (comfortable, least,
    /// and the tallest it grows to in a tall editor).</summary>
    public const double RowPad = 3, LayerGap = 2, BarHeight = 20, MinBarHeight = 7, TallBarHeight = 44;
    /// <summary>The most layers a row splits into (more items at once share the last one).</summary>
    public const int MaxLayers = 6;

    readonly List<List<(ItemBar Bar, int Layer)>> _rows = [];
    readonly List<int> _layers = [];

    /// <param name="fillHeight">The height the timeline is offered (a share of a taller editor): bars grow to fill it, up to
    /// <see cref="TallBarHeight"/>; below it they stay comfortable.</param>
    public TimelineLayout(EditProject project, IReadOnlyList<TimelineRow> rows, double width, double zoom = 1, double scroll = 0,
        double maxHeight = double.PositiveInfinity, double fillHeight = 0)
    {
        Project = project;
        Rows = rows;
        Width = width;
        Zoom = Math.Max(1, zoom);
        Scroll = scroll;
        Duration = project.OutputDuration;
        var all = TimelineMath.ItemBars(project);
        double fixedPart = RulerHeight + BottomPad;
        int layers = 0;
        foreach (var row in rows)
        {
            // A layer per set of overlapping items; one for the clip's parts, and one when the row is empty.
            var placed = Place(all.Where(b => EditorViewModel.RowOf(b.Item) == row));
            int count = placed.Count == 0 ? 1 : placed.Max(p => p.Layer) + 1;
            _rows.Add(placed);
            _layers.Add(count);
            fixedPart += 2 * RowPad + (count - 1) * LayerGap;
            layers += count;
        }
        double grown = Math.Clamp((fillHeight - fixedPart) / layers, BarHeight, TallBarHeight);
        Bar = Math.Clamp((maxHeight - fixedPart) / layers, MinBarHeight, grown);
    }

    /// <summary>Each bar in the first layer free at its start (earliest first, the longer first at the same start).</summary>
    static List<(ItemBar Bar, int Layer)> Place(IEnumerable<ItemBar> bars)
    {
        var placed = new List<(ItemBar, int)>();
        var ends = new List<double>();
        foreach (var bar in bars.OrderBy(b => b.Start).ThenByDescending(b => b.End))
        {
            int layer = ends.FindIndex(end => end <= bar.Start + 1e-9);
            if (layer < 0)
            {
                layer = Math.Min(ends.Count, MaxLayers - 1);
                if (layer == ends.Count)
                    ends.Add(bar.End);
            }
            ends[layer] = Math.Max(ends[layer], bar.End);
            placed.Add((bar, layer));
        }
        return placed;
    }

    public EditProject Project { get; }
    public IReadOnlyList<TimelineRow> Rows { get; }
    public double Width { get; }
    public double Zoom { get; }
    public double Scroll { get; }
    /// <summary>A bar's height in every item row.</summary>
    public double Bar { get; }

    public double Height => RowTop(Rows.Count) + BottomPad;
    public double LaneWidth => Math.Max(1, Width - HeadWidth - EndPad);
    public double ContentWidth => LaneWidth * Zoom;
    public double MaxScroll => ContentWidth - LaneWidth;
    double Duration { get; }

    public double XOf(double output) => HeadWidth + (Duration <= 0 ? 0 : output / Duration * ContentWidth) - Scroll;

    public double OutputAt(double x) => Duration <= 0 ? 0 : Math.Clamp((x - HeadWidth + Scroll) / ContentWidth, 0, 1) * Duration;

    public double RowTop(int row)
    {
        double top = RulerHeight;
        for (int i = 0; i < row; i++)
            top += RowHeightOf(i);
        return top;
    }

    public double RowHeightOf(int row) => 2 * RowPad + _layers[row] * Bar + (_layers[row] - 1) * LayerGap;

    /// <summary>The row at a height: -1 the ruler, the row's index, or Rows.Count below them.</summary>
    public int RowAt(double y)
    {
        if (y < RulerHeight)
            return -1;
        for (int i = 0; i < Rows.Count; i++)
            if (y < RowTop(i) + RowHeightOf(i))
                return i;
        return Rows.Count;
    }

    /// <summary>The kept parts' boxes in the Video row, <see cref="PartGap"/> between one and the next (a cut sits in its middle).</summary>
    public IReadOnlyList<Rect> PartBoxes()
    {
        var pieces = Project.Pieces();
        var boxes = new Rect[pieces.Count];
        double top = RowTop(0) + RowPad, at = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            double end = at + pieces[i].Span.Length / pieces[i].Factor;
            double left = XOf(at) + (i > 0 ? PartGap / 2 : 0), right = XOf(end) - (i < pieces.Count - 1 ? PartGap / 2 : 0);
            boxes[i] = new Rect(left, top, Math.Max(1, right - left), Bar);
            at = end;
        }
        return boxes;
    }

    public Rect PartBox(int part) => PartBoxes()[part];

    public int PartAt(double x)
    {
        var boxes = PartBoxes();
        for (int i = 0; i < boxes.Count; i++)
            if (x >= boxes[i].Left - PartGap && x <= boxes[i].Right + PartGap)
                return i;
        return -1;
    }

    /// <summary>The bars of an item row, each with its box (in its layer).</summary>
    public IEnumerable<(ItemBar Bar, Rect Box)> Bars(int row)
    {
        if (row < 0 || row >= Rows.Count)
            return [];
        double top = RowTop(row) + RowPad;
        return _rows[row].Select(p =>
        {
            double x0 = XOf(p.Bar.Start), x1 = XOf(p.Bar.End);
            return (p.Bar, new Rect(x0, top + p.Layer * (Bar + LayerGap), Math.Max(4, x1 - x0), Bar));
        }).ToList();
    }

    /// <summary>The items whose bars a box (dragged over the timeline) touches.</summary>
    public IReadOnlyList<EditItem> ItemsIn(Rect box) =>
        [.. Enumerable.Range(0, Rows.Count).SelectMany(Bars).Where(b => b.Box.IntersectsWith(box)).Select(b => b.Bar.Item)];

    /// <summary>The bar under a point (the last drawn wins), and whether the point is on its start or end.</summary>
    public (ItemBar Bar, bool Edge, bool StartEdge)? BarAt(Point at)
    {
        int row = RowAt(at.Y);
        var bars = Bars(row).ToList();
        for (int i = bars.Count - 1; i >= 0; i--)
        {
            var box = bars[i].Box;
            var grab = box;
            grab.Inflate(EdgePx / 2, LayerGap / 2);
            if (!grab.Contains(at))
                continue;
            bool startEdge = Math.Abs(at.X - box.Left) <= EdgePx, endEdge = Math.Abs(at.X - box.Right) <= EdgePx;
            return (bars[i].Bar, startEdge || endEdge, startEdge && (!endEdge || Math.Abs(at.X - box.Left) < Math.Abs(at.X - box.Right)));
        }
        return null;
    }

    /// <summary>How far to scroll so <paramref name="output"/> is on screen: as it is when it shows, else near the far side.</summary>
    public static double ScrollToShow(TimelineLayout l, double output)
    {
        double x = l.Duration <= 0 ? 0 : output / l.Duration * l.ContentWidth; // in the content
        double scroll = l.Scroll;
        if (x > scroll + l.LaneWidth)
            scroll = x - l.LaneWidth * 0.9;
        else if (x < scroll)
            scroll = x - l.LaneWidth * 0.1;
        return Math.Clamp(scroll, 0, Math.Max(0, l.MaxScroll));
    }
}
