using PixlReplay.Engine.Edit;

namespace PixlReplay.Ui.Edit;

/// <param name="Start">Edit time (seconds of the edited clip).</param>
public sealed record ItemBar(EditItem Item, double Start, double End);

/// <summary>
/// The timeline's arithmetic: each item's bar. Items keep source times (they stay on the
/// content); the timeline shows the edited clip, so a bar is where its kept part plays.
/// </summary>
public static class TimelineMath
{
    /// <summary>The shortest an item's time can be dragged to.</summary>
    public const double MinItemSeconds = 0.1;

    /// <summary>One bar per item that shows in the edit, from where its first kept part plays to where its last ends.</summary>
    public static IReadOnlyList<ItemBar> ItemBars(EditProject p)
    {
        var bars = new List<ItemBar>();
        foreach (var item in p.Items)
        {
            var visible = p.Visible(item).ToList();
            if (visible.Count == 0)
                continue;
            bars.Add(new ItemBar(item, p.ToOutput(visible[0].Start), p.ToOutput(visible[^1].End)));
        }
        return bars;
    }

    /// <summary>The item with one end of its bar dragged to <paramref name="output"/> (written as source time).</summary>
    public static EditItem WithBarEdge(EditProject p, EditItem item, bool start, double output)
    {
        double source = p.ToSource(Math.Clamp(output, 0, p.OutputDuration)) ?? (start ? item.Start : item.End);
        return start
            ? item with { Start = Math.Min(source, item.End - MinItemSeconds) }
            : item with { End = Math.Max(source, item.Start + MinItemSeconds) };
    }

    /// <summary>
    /// The same end of every item in <paramref name="items"/> moved by as much as <paramref name="hit"/>'s is dragged (its end
    /// to <paramref name="output"/>): several chosen bars lengthen or shorten together.
    /// </summary>
    public static IReadOnlyList<EditItem> WithBarEdges(EditProject p, IReadOnlyList<EditItem> items, EditItem hit, bool start, double output)
    {
        double delta = output - p.ToOutput(start ? hit.Start : hit.End);
        return items.Select(i => WithBarEdge(p, i, start, p.ToOutput(start ? i.Start : i.End) + delta)).ToList();
    }

    /// <summary>
    /// The item moved <paramref name="deltaOutput"/> seconds along the edit, as long as before (in source time) and inside
    /// the clip.
    /// </summary>
    public static EditItem MoveBar(EditProject p, EditItem item, double deltaOutput)
    {
        double length = item.End - item.Start;
        double from = p.ToOutput(item.Start) + deltaOutput;
        double start = p.ToSource(Math.Clamp(from, 0, p.OutputDuration)) ?? item.Start;
        start = Math.Clamp(start, 0, Math.Max(0, p.Duration - length));
        return item with { Start = start, End = start + length };
    }
}

