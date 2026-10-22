using PixlReplay.Engine.Voice;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

public static class VoiceUnitTests
{
    static float[] Sine(double hz, int rate, double seconds, double amp = 0.5) =>
        [.. Enumerable.Range(0, (int)(rate * seconds)).Select(i => (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate)))];

    static double Rms(ReadOnlySpan<float> x)
    {
        double s = 0;
        foreach (float v in x)
            s += v * v;
        return Math.Sqrt(s / Math.Max(1, x.Length));
    }

    static int ZeroCrossings(ReadOnlySpan<float> x)
    {
        int n = 0;
        for (int i = 1; i < x.Length; i++)
            if ((x[i - 1] < 0) != (x[i] < 0))
                n++;
        return n;
    }

    static float[][] RandomSeq(int n, int seed)
    {
        var rnd = new Random(seed);
        return [.. Enumerable.Range(0, n).Select(_ => Enumerable.Range(0, 96).Select(_ => (float)(rnd.NextDouble() * 2 - 1)).ToArray())];
    }

    [Test]
    public static void Resampler_keeps_speech_and_divides_the_rate_by_three()
    {
        var r = new Resampler48To16();
        var input = Sine(1000, 48000, 1.0);
        var output = new List<float>();
        for (int i = 0; i < input.Length; i += 480)   // libobs delivers ~10 ms blocks
            output.AddRange(r.Process(input.AsSpan(i, 480)));
        Assert.Equal(16000, output.Count, "one second in, one second out");
        var tail = output.Skip(1000).ToArray();
        Assert.Near(0.5 / Math.Sqrt(2), Rms(tail), 0.02, "1 kHz passes");
        Assert.Near(2000.0 * tail.Length / 16000, ZeroCrossings(tail), 4, "still 1 kHz");
    }

    [Test]
    public static void Resampler_removes_tones_above_eight_kilohertz()
    {
        var output = new Resampler48To16().Process(Sine(12000, 48000, 1.0));
        Assert.True(Rms(output.AsSpan(1000)) < 0.5 / Math.Sqrt(2) * 0.03, "12 kHz attenuated by at least 30 dB");
    }

    [Test]
    public static void Wsola_changes_length_but_not_pitch()
    {
        var x = Sine(220, 16000, 1.0);
        foreach (double rate in new[] { 0.9, 1.1 })
        {
            var y = Wsola.Stretch(x, rate);
            Assert.Near(16000 / rate, y.Length, 2, $"length at {rate}");
            var middle = y.AsSpan(800, y.Length - 1600);
            Assert.Near(220, ZeroCrossings(middle) / 2.0 / (middle.Length / 16000.0), 6, $"pitch at {rate}");
        }
    }

    [Test]
    public static void Matcher_finds_the_phrase_inside_a_longer_sequence()
    {
        var phrase = RandomSeq(15, 1);
        var query = RandomSeq(10, 2).Concat(phrase).Concat(RandomSeq(10, 3)).ToArray();
        Assert.Near(0, PhraseMatcher.Cost(phrase, query), 1e-6, "exact copy inside");
        Assert.True(PhraseMatcher.Cost(phrase, RandomSeq(35, 4)) > 0.5, "unrelated sequence costs a lot");
        Assert.True(double.IsPositiveInfinity(PhraseMatcher.Cost(phrase, RandomSeq(5, 9))), "far too short to hold the phrase");
    }

    [Test]
    public static void Matcher_tolerates_speaking_faster_or_slower()
    {
        var phrase = RandomSeq(16, 5);
        var slower = phrase.SelectMany((f, i) => i % 2 == 0 ? new[] { f, f } : [f]).ToArray();
        var faster = phrase.Where((_, i) => i % 3 != 2).ToArray();
        Assert.True(PhraseMatcher.Cost(phrase, slower) < 0.01, "slower");
        Assert.True(PhraseMatcher.Cost(phrase, faster) < 0.01, "faster");
    }

    [Test]
    public static void Score_is_the_mean_of_the_two_closest_templates()
    {
        var a = RandomSeq(12, 6);
        var b = RandomSeq(12, 7);
        Assert.Near(0, PhraseMatcher.Score([a, a, b], a), 1e-6, "two exact templates");
        Assert.Near(PhraseMatcher.Cost(b, a) / 2, PhraseMatcher.Score([a, b], a), 1e-6, "one exact, one far");
    }

    [Test]
    public static void Match_strength_is_full_well_below_the_threshold_and_empty_far_above()
    {
        Assert.Near(1, PhraseMatcher.Strength(0.03, 0.06), 1e-9, "strong");
        Assert.Near(0.6, PhraseMatcher.Strength(0.06, 0.06), 1e-9, "at the threshold");
        Assert.Near(0, PhraseMatcher.Strength(0.2, 0.06), 1e-9, "none");
    }

    static List<float[]> Feed(VoiceSegmenter seg, int chunks, float prob)
    {
        var found = new List<float[]>();
        var chunk = new float[VoiceSegmenter.Chunk];
        for (int i = 0; i < chunks; i++)
            found.AddRange(seg.Push(chunk, prob));
        return found;
    }

    [Test]
    public static void Segmenter_checks_the_last_three_seconds_when_the_speaker_pauses()
    {
        var seg = new VoiceSegmenter();
        Assert.Equal(0, Feed(seg, 30, 0.05f).Count + Feed(seg, 30, 0.9f).Count, "nothing until the speaker pauses");
        var found = Feed(seg, 30, 0.05f);
        Assert.Equal(1, found.Count, "one segment");
        Assert.Equal((30 + 30 + VoiceSegmenter.HangoverChunks) * VoiceSegmenter.Chunk, found[0].Length, "all audio so far, up to 3 s");
    }

    [Test]
    public static void Segmenter_includes_the_words_before_a_pause()
    {
        var seg = new VoiceSegmenter();
        Feed(seg, 10, 0.05f);
        Feed(seg, 15, 0.9f);
        Assert.Equal(1, Feed(seg, 18, 0.05f).Count, "the first word alone is checked");
        Feed(seg, 15, 0.9f);
        var found = Feed(seg, 30, 0.05f);
        Assert.Equal(1, found.Count, "then the whole phrase");
        Assert.True(found[0].Length >= (15 + 18 + 15) * VoiceSegmenter.Chunk, "both words and the pause");
    }

    [Test]
    public static void Enrollment_segmenter_keeps_a_comma_pause_inside_one_sample()
    {
        var seg = new VoiceSegmenter(VoiceSegmenter.EnrollHangoverChunks, withContext: false);
        int count = Feed(seg, 10, 0.05f).Count + Feed(seg, 15, 0.9f).Count + Feed(seg, 18, 0.05f).Count + Feed(seg, 15, 0.9f).Count;
        var found = Feed(seg, 40, 0.05f);
        Assert.Equal(0, count, "no cut at the pause");
        Assert.Equal(1, found.Count, "one sample");
        int expected = (VoiceSegmenter.PreRollChunks + 15 + 18 + 15 + VoiceSegmenter.EnrollHangoverChunks) * VoiceSegmenter.Chunk;
        Assert.Equal(expected, found[0].Length, "pre-roll + phrase + hangover");
    }

    [Test]
    public static void Segmenter_keeps_a_phrase_with_a_short_pause_together()
    {
        var seg = new VoiceSegmenter();
        int count = Feed(seg, 10, 0.05f).Count + Feed(seg, 10, 0.9f).Count + Feed(seg, 4, 0.05f).Count
                    + Feed(seg, 10, 0.9f).Count + Feed(seg, 30, 0.05f).Count;
        Assert.Equal(1, count, "one segment");
    }

    [Test]
    public static void Segmenter_checks_long_speech_in_three_second_windows()
    {
        var seg = new VoiceSegmenter();
        Feed(seg, 10, 0.05f);
        var during = Feed(seg, 200, 0.9f);
        Assert.Equal(4, during.Count, "a window at 3 s, then one every second");
        Assert.True(during.All(w => w.Length == VoiceSegmenter.WindowChunks * VoiceSegmenter.Chunk), "each window is 3 s");
        Assert.Equal(1, Feed(seg, 30, 0.05f).Count, "the end of speech is checked too");
    }

    [Test]
    public static void Smart_save_is_off_by_default_with_normal_sensitivity()
    {
        var s = new AppSettings();
        Assert.Equal(false, s.Voice.Enabled, "off");
        Assert.Equal(VoiceSensitivity.Normal, s.Voice.Sensitivity, "normal");
        Assert.Near(0.060, VoiceSettings.Threshold(VoiceSensitivity.Normal), 1e-9, "normal threshold from the spike");
        Assert.True(VoiceSettings.Threshold(VoiceSensitivity.Strict) < VoiceSettings.Threshold(VoiceSensitivity.Easy), "strict is stricter");
    }

    [Test]
    public static void Threshold_follows_how_much_the_users_own_samples_vary()
    {
        Assert.Near(0.060, VoiceSettings.Threshold(VoiceSensitivity.Normal, spread: 0.02), 1e-9, "steady voice keeps the floor");
        // A real voice (2026-10-12): samples 0.045-0.052 apart, attempts 0.060-0.081, normal talk 0.114+.
        double normal = VoiceSettings.Threshold(VoiceSensitivity.Normal, spread: 0.052);
        Assert.True(normal > 0.085 && normal < 0.105, $"normal {normal:F3} sits between the attempts and normal talk");
        double strict = VoiceSettings.Threshold(VoiceSensitivity.Strict, 0.052), easy = VoiceSettings.Threshold(VoiceSensitivity.Easy, 0.052);
        Assert.True(strict < normal && normal < easy, "strict < normal < easy");
        Assert.True(VoiceSettings.Threshold(VoiceSensitivity.Easy, 0.2) <= VoiceSettings.MaxThreshold, "never so loose that talk matches");
        // A second real recording varied more (spread 0.064); the cap must still keep clear of normal talk (0.114).
        Assert.True(VoiceSettings.Threshold(VoiceSensitivity.Easy, 0.064) <= 0.100, "every sensitivity keeps a margin below normal talk");
    }

    [Test]
    public static void Turning_smart_save_on_rebuilds_but_sensitivity_does_not()
    {
        var before = new AppSettings();
        AppSettings Changed(Action<AppSettings> change)
        {
            var s = SettingsStore.Deserialize(SettingsStore.Serialize(before));
            change(s);
            return s;
        }
        Assert.Equal(true, SettingsDiff.NeedsRebuild(before, Changed(s => s.Voice.Enabled = true)), "listening needs the mic source");
        Assert.Equal(false, SettingsDiff.NeedsRebuild(before, Changed(s =>
        {
            s.Voice.Sensitivity = VoiceSensitivity.Strict;
            s.Voice.HintDismissed = true;
        })), "sensitivity is live");
    }
}
