using PixlReplay.Engine.Share;

namespace PixlReplay.Tests;

/// <summary>Decoding for the preview player: pictures and sound from a point on, with the FFmpeg that OBS ships.</summary>
public static class ClipPlaybackTests
{
    static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Share");

    /// <summary>
    /// Reads from <paramref name="from"/> the pictures and the sound pieces (start, bytes) before <paramref name="until"/>,
    /// each on its own (they are read apart, whatever the file's layout).
    /// </summary>
    static (List<double> Pictures, List<(double Time, int Bytes)> Sound) Read(ClipPlayback playback, double from, double until)
    {
        playback.Seek(from);
        var pictures = new List<double>();
        while (playback.NextPicture() && playback.PictureTime < until)
            pictures.Add(playback.PictureTime);
        var sound = new List<(double, int)>();
        while (playback.HasAudio && playback.NextSound() && playback.SoundTime < until)
            sound.Add((playback.SoundTime, playback.Sound.Length));
        return (pictures, sound);
    }

    [Test]
    public static void Playing_from_a_point_gives_the_pictures_from_there_in_order_and_sound_that_starts_exactly_there()
    {
        TestFfmpeg.Load();
        foreach (string clip in new[] { "clip-h264.mp4", "clip-av1-2tracks.mp4" })
        {
            using var playback = new ClipPlayback(Path.Combine(Fixtures, clip), 320, 180);
            Assert.True(playback.HasAudio, clip + ": has sound");
            var (pictures, sound) = Read(playback, 2.0, 3.0);
            Assert.Near(2.0, pictures[0], 1.0 / 60 + 0.001, clip + ": first picture at the point");
            Assert.True(pictures.Zip(pictures.Skip(1)).All(p => p.Second > p.First), clip + ": pictures in order");
            Assert.Near(60, pictures.Count, 2, clip + ": a second of 60 fps");
            Assert.Near(2.0, sound[0].Time, 0.001, clip + ": sound starts at the point");
            double seconds = sound.Sum(s => s.Bytes) / (double)(ClipPlayback.SampleRate * ClipPlayback.BytesPerFrame);
            Assert.Near(1.0, seconds, 0.05, clip + ": a second of sound");
        }
    }

    [Test]
    public static void Pictures_come_out_at_the_asked_size_and_show_the_clip()
    {
        TestFfmpeg.Load();
        using var playback = new ClipPlayback(Path.Combine(Fixtures, "clip-av1-2tracks.mp4"), 320, 180);
        byte[] PictureAt(double t)
        {
            playback.Seek(t);
            Assert.True(playback.NextPicture(), $"a picture at {t}");
            var bgra = new byte[320 * 180 * 4];
            playback.CopyPicture(bgra);
            return bgra;
        }
        var early = PictureAt(1.0);
        var late = PictureAt(4.5);
        Assert.True(early.Where((b, i) => i % 4 != 3).Any(b => b > 40), "not black");
        Assert.True(early.Where((b, i) => i % 4 == 3).All(b => b == 255), "opaque");
        double difference = early.Zip(late).Where((p, i) => i % 4 != 3).Average(p => Math.Abs(p.First - p.Second));
        Assert.True(difference > 3, $"a moving picture ({difference:F1})");
    }

    [Test]
    public static void Seeking_again_starts_over_from_the_new_point_and_the_end_is_reported()
    {
        TestFfmpeg.Load();
        using var playback = new ClipPlayback(Path.Combine(Fixtures, "clip-h264.mp4"), 320, 180);
        Read(playback, 1.0, 1.5);
        var (pictures, sound) = Read(playback, 4.0, 4.5);
        Assert.Near(4.0, pictures[0], 1.0 / 60 + 0.001, "first picture after the second seek");
        Assert.Near(4.0, sound[0].Time, 0.001, "sound after the second seek");
        var (tail, _) = Read(playback, 5.5, double.PositiveInfinity);
        Assert.True(tail.Count > 0 && tail[^1] < 6.05, "read to the end of the clip");
    }
}
