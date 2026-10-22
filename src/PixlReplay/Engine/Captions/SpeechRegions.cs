using PixlReplay.Engine.Voice;

namespace PixlReplay.Engine.Captions;

/// <summary>
/// Where someone is talking in a sound track, from the speech detector Smart Save already ships (Silero VAD). Whisper
/// hears only these parts: in long quiet stretches it writes things nobody said, and it starts a line where the previous
/// one ended instead of where the talking starts.
/// </summary>
public static class SpeechRegions
{
    /// <summary>Room kept before and after each part (s), so first and last sounds are not cut.</summary>
    public const double Before = 0.25, After = 0.35;
    const float Threshold = 0.5f;
    /// <summary>A pause shorter than this stays inside one part (a breath, a short stop).</summary>
    const double MaxPause = 0.8;
    /// <summary>Less talking than this is a click or a bump, not speech.</summary>
    const double MinSpeech = 0.25;
    const int Chunk = 512; // 32 ms at 16 kHz, what the detector takes

    /// <summary>The spoken parts of 16 kHz mono sound (seconds from its start), with the progress through it.</summary>
    /// <param name="modelsDirectory">Default: PixlReplay's models; only the speech detector is loaded.</param>
    public static IReadOnlyList<(double Start, double End)> Find(float[] samples, CancellationToken ct, IProgress<double>? progress = null,
        string? modelsDirectory = null)
    {
        using var models = new SpeechModels(modelsDirectory ?? SpeechModels.DefaultDirectory, vadOnly: true);
        var state = models.NewVad();
        int count = samples.Length / Chunk, every = Math.Max(1, count / 100);
        var chances = new List<float>(count + 1);
        for (int n = 0; n < count; n++)
        {
            if (n % every == 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(n / (double)count);
            }
            chances.Add(models.SpeechProbability(state, samples.AsSpan(n * Chunk, Chunk)));
        }
        progress?.Report(1);
        return From(chances, Chunk / (double)TrackAudio.Rate);
    }

    /// <summary>The spoken parts from the detector's chance of speech per frame of <paramref name="frame"/> seconds.</summary>
    public static IReadOnlyList<(double Start, double End)> From(IReadOnlyList<float> chances, double frame)
    {
        // Runs of speech frames, joined across short pauses.
        var runs = new List<(int First, int Last, int Speech)>();
        for (int i = 0; i < chances.Count; i++)
        {
            if (chances[i] < Threshold)
                continue;
            if (runs.Count > 0 && (i - runs[^1].Last - 1) * frame < MaxPause)
                runs[^1] = (runs[^1].First, i, runs[^1].Speech + 1);
            else
                runs.Add((i, i, 1));
        }
        double length = chances.Count * frame;
        return runs.Where(r => r.Speech * frame >= MinSpeech)
            .Select(r => (Math.Max(0, r.First * frame - Before), Math.Min(length, (r.Last + 1) * frame + After)))
            .ToList();
    }
}

/// <summary>Only the spoken parts of a track, one after another with a short gap, and the way back to the track's time.</summary>
public sealed class SpeechMap
{
    /// <summary>The quiet put between parts (s): enough for Whisper to end a line there.</summary>
    public const double Gap = 0.3;

    readonly List<(double SpeechAt, double SourceAt, double Length)> _pieces;

    SpeechMap(float[] samples, List<(double, double, double)> pieces)
    {
        Samples = samples;
        _pieces = pieces;
    }

    public float[] Samples { get; }

    /// <summary>The spoken parts one after another in one array, made once at its size (a long recording is big).</summary>
    public static SpeechMap Join(float[] samples, IReadOnlyList<(double Start, double End)> parts)
    {
        int rate = TrackAudio.Rate, gap = (int)(Gap * rate);
        var ranges = new List<(int From, int To)>();
        foreach (var (start, end) in parts)
        {
            int from = Math.Clamp((int)Math.Round(start * rate), 0, samples.Length);
            int to = Math.Clamp((int)Math.Round(end * rate), from, samples.Length);
            if (to > from)
                ranges.Add((from, to));
        }
        long length = ranges.Sum(r => (long)(r.To - r.From)) + (long)Math.Max(0, ranges.Count - 1) * gap;
        var joined = new float[length]; // the gaps are already quiet
        var pieces = new List<(double, double, double)>(ranges.Count);
        int at = 0;
        foreach (var (from, to) in ranges)
        {
            if (at > 0)
                at += gap;
            pieces.Add((at / (double)rate, from / (double)rate, (to - from) / (double)rate));
            Array.Copy(samples, from, joined, at, to - from);
            at += to - from;
        }
        return new SpeechMap(joined, pieces);
    }

    /// <summary>A time in <see cref="Samples"/> as a time in the track (a gap counts as the end of the part before it).</summary>
    public double ToSource(double t)
    {
        if (_pieces.Count == 0)
            return t;
        var piece = _pieces[PieceAt(t)];
        return piece.SourceAt + Math.Clamp(t - piece.SpeechAt, 0, piece.Length);
    }

    /// <summary>
    /// A line's start and end in the track. Whisper often starts a line where the one before it ended, which is the gap
    /// (or the quiet tail) of the part before: a line that starts in a gap, or ends in a later part than it starts, starts
    /// with the part its words are in.
    /// </summary>
    public (double Start, double End) ToSource(double t0, double t1)
    {
        if (_pieces.Count == 0)
            return (t0, Math.Max(t0, t1));
        int first = PieceAt(t0), last = PieceAt(t1);
        if (t0 - _pieces[first].SpeechAt > _pieces[first].Length && first + 1 < _pieces.Count)
            first++;
        first = Math.Max(first, last);
        var piece = _pieces[first];
        double start = piece.SourceAt + Math.Clamp(t0 - piece.SpeechAt, 0, piece.Length);
        return (start, Math.Max(start, ToSource(t1)));
    }

    /// <summary>The last part that starts at or before <paramref name="t"/> (the first part for earlier times).</summary>
    int PieceAt(double t)
    {
        int index = 0;
        for (int i = 0; i < _pieces.Count; i++)
            if (_pieces[i].SpeechAt <= t + 1e-9)
                index = i;
        return index;
    }
}
