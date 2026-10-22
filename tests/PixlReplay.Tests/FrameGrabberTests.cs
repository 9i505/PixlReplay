using PixlReplay.Engine.Share;

namespace PixlReplay.Tests;

/// <summary>Frames for the trim bar and the preview, read with the FFmpeg that OBS ships.</summary>
public static class FrameGrabberTests
{
    static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Share");

    static double Difference(GrabbedFrame a, GrabbedFrame b)
    {
        long sum = 0;
        for (int i = 0; i < a.Bgra.Length; i += 4)
            sum += Math.Abs(a.Bgra[i] - b.Bgra[i]) + Math.Abs(a.Bgra[i + 1] - b.Bgra[i + 1]) + Math.Abs(a.Bgra[i + 2] - b.Bgra[i + 2]);
        return sum / (a.Bgra.Length / 4.0 * 3);
    }

    /// <summary>
    /// Readers opened and closed again and again (the editor, the player and the share panel each open some) share one
    /// graphics-card device: nothing is left behind (a device made for each reader left two handles every time).
    /// </summary>
    [Test]
    public static void Opening_and_closing_readers_leaves_nothing_behind()
    {
        TestFfmpeg.Load();
        string clip = Path.Combine(Fixtures, "clip-av1-2tracks.mp4");
        for (int i = 0; i < 3; i++)
            using (var warm = new FrameGrabber(clip))
                warm.Grab(1, 160, exact: true);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        int before = self.HandleCount;
        for (int i = 0; i < 20; i++)
            using (var grabber = new FrameGrabber(clip))
                Assert.True(grabber.Grab(1, 160, exact: true) != null, "a frame");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        self.Refresh();
        Assert.True(self.HandleCount - before < 10, $"handles {before} -> {self.HandleCount} after 20 readers");
    }

    [Test]
    public static void The_graphics_card_is_let_go_once_no_reader_uses_it()
    {
        TestFfmpeg.Load();
        TimeSpan keep = HardwareDecoding.KeepIdle;
        HardwareDecoding.KeepIdle = TimeSpan.FromMilliseconds(100);
        try
        {
            string clip = Path.Combine(Fixtures, "clip-av1-2tracks.mp4");
            using (var grabber = new FrameGrabber(clip))
            {
                Assert.True(grabber.Grab(1, 160, exact: true) != null, "a frame");
                Thread.Sleep(300);
                Assert.True(HardwareDecoding.Holding, "kept while a reader is open");
            }
            for (int i = 0; i < 40 && HardwareDecoding.Holding; i++)
                Thread.Sleep(50);
            Assert.True(!HardwareDecoding.Holding, "let go after the last reader closed");
            using (var again = new FrameGrabber(clip))
                Assert.True(again.Grab(1, 160, exact: true) != null, "a new reader still gets a frame");
        }
        finally
        {
            HardwareDecoding.KeepIdle = keep;
        }
    }

    [Test]
    public static void A_step_forward_decodes_on_from_the_last_picture_instead_of_starting_over()
    {
        TestFfmpeg.Load();
        string clip = Path.Combine(Fixtures, "clip-av1-2tracks.mp4"); // key frames every 2 s at 60 fps
        using var grabber = new FrameGrabber(clip);
        using var fresh = new FrameGrabber(clip);
        const double frame = 1 / 60.0;
        Assert.True(grabber.Grab(1.0, 160, exact: true) != null, "a picture");
        long before = grabber.FramesDecoded;
        var stepped = grabber.Grab(1.0 + frame, 160, exact: true);
        Assert.True(stepped != null, "the next picture");
        Assert.True(grabber.FramesDecoded - before <= 2, $"{grabber.FramesDecoded - before} pictures decoded for one step");
        var direct = fresh.Grab(1.0 + frame, 160, exact: true)!;
        Assert.True(stepped!.Bgra.AsSpan().SequenceEqual(direct.Bgra), "the same picture a fresh seek gives");
        before = grabber.FramesDecoded;
        var again = grabber.Grab(1.0 + frame, 160, exact: true)!;
        Assert.True(grabber.FramesDecoded == before, "the same moment again decodes nothing");
        Assert.True(again.Bgra.AsSpan().SequenceEqual(direct.Bgra), "and is the same picture");
        // Far ahead (past the next key frame), backwards, near the end: still the pictures a fresh seek gives.
        foreach (double at in new[] { 3.5, 0.5, 5.9 })
            Assert.True(grabber.Grab(at, 160, exact: true)!.Bgra.AsSpan().SequenceEqual(fresh.Grab(at, 160, exact: true)!.Bgra), $"the picture at {at}");
        // A key frame grab, then exact ones just after it.
        Assert.True(grabber.Grab(2.3, 160, exact: false)!.Bgra.AsSpan().SequenceEqual(fresh.Grab(2.3, 160, exact: false)!.Bgra), "the key frame");
        Assert.True(grabber.Grab(2.3, 160, exact: true)!.Bgra.AsSpan().SequenceEqual(fresh.Grab(2.3, 160, exact: true)!.Bgra), "then the exact picture");
    }

    [Test]
    public static void Frames_come_out_at_the_asked_width_with_the_clips_shape()
    {
        TestFfmpeg.Load();
        foreach (string clip in new[] { "clip-h264.mp4", "clip-av1-2tracks.mp4" })
        {
            using var grabber = new FrameGrabber(Path.Combine(Fixtures, clip));
            var frame = grabber.Grab(2.0, 320, exact: true);
            Assert.True(frame != null, clip + ": a frame");
            Assert.Equal((320, 180), (frame!.Width, frame.Height), clip + ": 16:9 at 320");
            Assert.Equal(320 * 180 * 4, frame.Bgra.Length, clip + ": BGRA");
            Assert.True(frame.Bgra.Where((b, i) => i % 4 != 3).Any(b => b > 40), clip + ": not black");
        }
    }

    [Test]
    public static void Different_times_give_different_pictures_and_the_same_time_the_same_one()
    {
        TestFfmpeg.Load();
        using var grabber = new FrameGrabber(Path.Combine(Fixtures, "clip-h264.mp4"));
        var early = grabber.Grab(1.0, 160, exact: true)!;
        var late = grabber.Grab(4.5, 160, exact: true)!;
        var lateAgain = grabber.Grab(4.5, 160, exact: true)!;
        Assert.True(Difference(early, late) > 3, $"moving picture ({Difference(early, late):F1})");
        Assert.True(Difference(late, lateAgain) < 0.5, $"same frame ({Difference(late, lateAgain):F1})");
    }

    [Test]
    public static void Quick_frames_for_the_trim_bar_and_the_end_of_the_clip_both_work()
    {
        TestFfmpeg.Load();
        using var grabber = new FrameGrabber(Path.Combine(Fixtures, "clip-av1-2tracks.mp4"));
        for (double t = 0.3; t < 6; t += 0.6)
            Assert.True(grabber.Grab(t, 96, exact: false) != null, $"quick frame at {t:F1}");
        Assert.True(grabber.Grab(5.98, 96, exact: true) != null, "the very end");
    }
}
