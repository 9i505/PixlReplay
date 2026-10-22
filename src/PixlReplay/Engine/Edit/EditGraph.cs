using System.Globalization;
using System.Text;

namespace PixlReplay.Engine.Edit;

/// <summary>A drawn item's transparent picture, only as big as the item, and where its top-left goes in the frame.</summary>
public sealed record Overlay(string Path, int X, int Y);

/// <summary>
/// An edit as FFmpeg filter-graph text for the export. Video (<c>[vin]</c> → <c>[vout]</c>): the colour look, blurred
/// boxes and drawn items (transparent PNGs read by <c>movie</c>) laid over in their source times, then the kept pieces
/// trimmed, re-timed for their speed and joined, at the clip's frame rate. Audio (<c>[aN]</c> → <c>[aout]</c>): game and
/// mic at their levels (or the mix of a one-track clip), cut and re-timed the same way (tempo, not pitch).
/// </summary>
/// <param name="AudioStreams">The audio streams (0-based) the graph reads, as inputs <c>[aN]</c>.</param>
public sealed record EditGraph(string Video, string Audio, IReadOnlyList<int> AudioStreams)
{
    /// <summary>The first source moment the edit keeps: the export starts reading near it, not at the clip's start.</summary>
    public double From { get; init; }

    /// <summary>The last source moment the edit keeps: the export stops reading soon after it.</summary>
    public double To { get; init; } = double.PositiveInfinity;

    /// <summary>
    /// The sound tracks the graph gives, in the MP4's order: <c>[aout]</c> (the mix), and for PixlReplay's clips also
    /// <c>[agame]</c> and <c>[amic]</c>, so an edited clip keeps the clip's layout and can be edited again.
    /// </summary>
    public IReadOnlyList<string> AudioOutputs { get; init; } = ["aout"];

    /// <summary>The preview's blur (radius 20 on a 720-line picture), kept at the clip's size.</summary>
    const double BlurPerLine = 20 / 720.0;

    public static EditGraph Build(EditProject p, int width, int height, double fps, int audioStreams,
        IReadOnlyDictionary<Guid, Overlay> overlayPngs)
    {
        var pieces = p.Pieces();
        string audio = AudioGraph(p, pieces, audioStreams, out var streams, out var outputs);
        return new EditGraph(VideoGraph(p, pieces, width, height, fps, overlayPngs), audio, streams)
        {
            From = pieces.Min(piece => piece.Span.Start),
            To = pieces.Max(piece => piece.Span.End),
            AudioOutputs = outputs,
        };
    }

    static string VideoGraph(EditProject p, IReadOnlyList<(ClipSpan Span, double Factor)> pieces, int width, int height, double fps,
        IReadOnlyDictionary<Guid, Overlay> overlayPngs)
    {
        var g = new StringBuilder();
        string current = "vin";
        int n = 0;
        string Next() => $"v{n++}";

        if (pieces.All(piece => piece.Span.Look == null))
        {
            if (ColourLooks.Filter(p.Look, p.LookStrength) is { } look)
            {
                string next = Next();
                g.Append($"[{current}]{look}[{next}];");
                current = next;
            }
        }
        else
        {
            // Parts with their own looks: each look on only while its parts play (before texts and drawings, which keep
            // their colours).
            foreach (var group in pieces.GroupBy(piece => p.LookAt((piece.Span.Start + piece.Span.End) / 2)))
            {
                if (ColourLooks.Filter(group.Key.Look, group.Key.Strength) is not { } look)
                    continue;
                string enable = Enable(group.Select(piece => piece.Span))!;
                string next = Next();
                g.Append($"[{current}]{string.Join(",", look.Split(',').Select(f => $"{f}:enable='{enable}'"))}[{next}];");
                current = next;
            }
        }

        foreach (var item in p.Items)
        {
            string? enable = Enable(p.Visible(item));
            if (enable == null)
                continue; // all of it is cut out
            if (item is BlurItem blur)
            {
                var (x, y, w, h) = Pixels(blur.Box, width, height);
                string baseLabel = Next(), source = Next(), blurred = Next(), next = Next();
                g.Append($"[{current}]split[{baseLabel}][{source}];");
                // FFmpeg's boxblur wants a radius under half of a plane's shorter side, and the colour planes are half
                // size (4:2:0); the planar format keeps NV12 (graphics-card decode) colours from being blurred together.
                int radius = Math.Max(1, (int)Math.Round(BlurPerLine * height));
                int side = Math.Min(w, h);
                int luma = Math.Clamp(side / 2 - 1, 0, radius), chroma = Math.Clamp((side + 1) / 2 / 2 - 1, 0, radius);
                g.Append($"[{source}]crop={w}:{h}:{x}:{y},format=yuv420p,");
                g.Append($"boxblur=luma_radius={luma}:luma_power=2:chroma_radius={chroma}:chroma_power=2[{blurred}];");
                g.Append($"[{baseLabel}][{blurred}]overlay={x}:{y}:enable='{enable}'[{next}];");
                current = next;
            }
            else if (overlayPngs.TryGetValue(item.Id, out var png))
            {
                string picture = Next(), next = Next();
                g.Append($"movie={MoviePath(png.Path)}[{picture}];");
                g.Append($"[{current}][{picture}]overlay={png.X}:{png.Y}:enable='{enable}'[{next}];");
                current = next;
            }
        }

        string Piece((ClipSpan Span, double Factor) piece) =>
            $"trim=start={N(piece.Span.Start)}:end={N(piece.Span.End)},setpts=(PTS-STARTPTS)/{N(piece.Factor)}";

        if (pieces.Count == 1)
        {
            g.Append($"[{current}]{Piece(pieces[0])},fps={N(fps)}[vout]");
            return g.ToString();
        }
        var outs = Enumerable.Range(0, pieces.Count).Select(_ => Next()).ToList();
        g.Append($"[{current}]split={pieces.Count}{string.Concat(outs.Select(o => $"[{o}]"))};");
        var parts = new List<string>();
        for (int i = 0; i < pieces.Count; i++)
        {
            string part = Next();
            g.Append($"[{outs[i]}]{Piece(pieces[i])}[{part}];");
            parts.Add(part);
        }
        g.Append($"{string.Concat(parts.Select(o => $"[{o}]"))}concat=n={pieces.Count}:v=1:a=0,fps={N(fps)}[vout]");
        return g.ToString();
    }

    static string AudioGraph(EditProject p, IReadOnlyList<(ClipSpan Span, double Factor)> pieces, int audioStreams, out IReadOnlyList<int> streams,
        out IReadOnlyList<string> outputs)
    {
        outputs = ["aout"];
        if (audioStreams <= 0)
        {
            streams = [];
            outputs = [];
            return "";
        }
        var g = new StringBuilder();
        var s = p.Sound;
        int n = pieces.Count;
        // Each track split into one copy per piece (a single piece reads the track itself).
        IReadOnlyList<string> Copies(string track, string prefix)
        {
            if (n == 1)
                return [track];
            var copies = Enumerable.Range(0, n).Select(i => $"{prefix}{i}").ToList();
            g.Append($"[{track}]asplit={n}{string.Concat(copies.Select(c => $"[{c}]"))};");
            return copies;
        }
        string Trim(ClipSpan span) => $"atrim=start={N(span.Start)}:end={N(span.End)},asetpts=PTS-STARTPTS";
        string Tempo(double factor) => Math.Abs(factor - 1) < 1e-9 ? "" : "," + TempoChain(factor);
        var outs = Enumerable.Range(0, n).Select(i => n == 1 ? "aout" : $"ap{i}").ToList();

        if (audioStreams >= 3)
        {
            // PixlReplay's clips: track 1 is the mix, 2 the game, 3 the mic; each piece at the clip's level times its own.
            // The edit keeps that layout: the mix, and the game and the mic on their own, cut and set the same way.
            streams = [1, 2];
            outputs = ["aout", "agame", "amic"];
            var game = Copies("a1", "g");
            var mic = Copies("a2", "m");
            var gameOuts = Enumerable.Range(0, n).Select(i => n == 1 ? "agame" : $"gq{i}").ToList();
            var micOuts = Enumerable.Range(0, n).Select(i => n == 1 ? "amic" : $"mq{i}").ToList();
            for (int i = 0; i < n; i++)
            {
                var span = pieces[i].Span;
                double gameLevel = s.GameMuted ? 0 : s.Game * span.Game, micLevel = s.MicMuted ? 0 : s.Mic * span.Mic;
                string tempo = Tempo(pieces[i].Factor);
                g.Append($"[{game[i]}]{Trim(span)},volume={N(gameLevel)},asplit=2[gp{i}][go{i}];");
                g.Append($"[{mic[i]}]{Trim(span)},volume={N(micLevel)},asplit=2[mp{i}][mo{i}];");
                g.Append($"[gp{i}][mp{i}]amix=inputs=2:normalize=0{tempo}[{outs[i]}];");
                g.Append($"[go{i}]anull{tempo}[{gameOuts[i]}];");
                g.Append($"[mo{i}]anull{tempo}[{micOuts[i]}];");
            }
            if (n == 1)
                return g.ToString().TrimEnd(';');
            g.Append($"{string.Concat(outs.Select(o => $"[{o}]"))}concat=n={n}:v=0:a=1[aout];");
            g.Append($"{string.Concat(gameOuts.Select(o => $"[{o}]"))}concat=n={n}:v=0:a=1[agame];");
            g.Append($"{string.Concat(micOuts.Select(o => $"[{o}]"))}concat=n={n}:v=0:a=1[amic]");
            return g.ToString();
        }
        else
        {
            streams = [0];
            var mix = Copies("a0", "s");
            for (int i = 0; i < n; i++)
            {
                var span = pieces[i].Span;
                g.Append($"[{mix[i]}]{Trim(span)},volume={N(s.All * span.Game)}{Tempo(pieces[i].Factor)}[{outs[i]}];");
            }
        }
        if (n == 1)
            return g.ToString().TrimEnd(';');
        g.Append($"{string.Concat(outs.Select(o => $"[{o}]"))}concat=n={n}:v=0:a=1[aout]");
        return g.ToString();
    }

    /// <summary>atempo takes 0.5..100 per filter: slower than half speed is a chain of halves.</summary>
    internal static string TempoChain(double factor)
    {
        var parts = new List<string>();
        double left = factor;
        while (left < 0.5 - 1e-9)
        {
            parts.Add("atempo=0.5");
            left /= 0.5;
        }
        parts.Add($"atempo={N(left)}");
        return string.Join(",", parts);
    }

    static string? Enable(IEnumerable<ClipSpan> visible)
    {
        var parts = visible.Select(v => $"between(t,{N(v.Start)},{N(v.End)})").ToList();
        return parts.Count == 0 ? null : string.Join("+", parts);
    }

    /// <summary>A box in whole pixels, even sizes (what the encoders want), inside the picture.</summary>
    static (int X, int Y, int W, int H) Pixels(System.Windows.Rect box, int width, int height)
    {
        int x = Math.Clamp((int)Math.Round(box.X * width), 0, width - 2);
        int y = Math.Clamp((int)Math.Round(box.Y * height), 0, height - 2);
        int w = Math.Clamp((int)Math.Round(box.Width * width), 2, width - x) & ~1;
        int h = Math.Clamp((int)Math.Round(box.Height * height), 2, height - y) & ~1;
        return (x, y, Math.Max(w, 2), Math.Max(h, 2));
    }

    /// <summary>
    /// A file for the <c>movie</c> source: forward slashes, the drive colon escaped for the option parser, quoted for
    /// the graph parser (the files are PixlReplay's own temporary ones, so they never contain a quote).
    /// </summary>
    static string MoviePath(string path) => "'" + path.Replace('\\', '/').Replace(":", "\\:") + "'";

    static string N(double value) => Math.Round(value, 6).ToString("0.######", CultureInfo.InvariantCulture);
}
