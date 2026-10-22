using System.Buffers.Binary;
using System.Diagnostics;
using PixlReplay.Engine.Share;

namespace PixlReplay.Tests;

/// <summary>Copying part of a clip as it is: no conversion, so it takes about as long as the disk needs.</summary>
public static class ClipCutterTests
{
    static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Share");

    static string Temp(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "pixlreplay-cut-tests");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.Delete(path);
        return path;
    }

    /// <summary>Top-level MP4 boxes in file order.</summary>
    static List<string> Boxes(string path)
    {
        var boxes = new List<string>();
        using var f = File.OpenRead(path);
        Span<byte> head = stackalloc byte[16];
        while (f.Position + 8 <= f.Length)
        {
            long at = f.Position;
            f.ReadExactly(head[..8]);
            long size = BinaryPrimitives.ReadUInt32BigEndian(head);
            if (size == 1)
            {
                f.ReadExactly(head[8..16]);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(head[8..16]);
            }
            else if (size == 0)
                size = f.Length - at;
            boxes.Add(System.Text.Encoding.ASCII.GetString(head[4..8]));
            f.Position = at + size;
        }
        return boxes;
    }

    [Test]
    public static void A_part_is_copied_as_it_is_from_the_key_frame_before_its_start()
    {
        TestFfmpeg.Load();
        foreach (var (clip, codec) in new[] { ("clip-h264.mp4", "h264"), ("clip-av1-2tracks.mp4", "av1") })
        {
            string output = Temp("cut-" + clip);
            var result = ClipCutter.Cut(Path.Combine(Fixtures, clip), output, 2.4, 4.0, null, CancellationToken.None);
            var o = ClipTranscoder.Probe(result.Path);
            Assert.Equal((codec, "aac", 1), (o.VideoCodec, o.AudioCodec, o.AudioStreams), clip + ": same codec, the full mix only");
            Assert.True(o.Duration >= 1.55 && o.Duration <= 4.05, $"{clip}: from a key frame at or before 2.4 s to 4.0 s, got {o.Duration:F2} s");
            Assert.Equal((640, 360), (o.Width, o.Height), clip + ": same picture");
            var boxes = Boxes(result.Path);
            Assert.True(boxes.IndexOf("moov") < boxes.IndexOf("mdat"), clip + ": plays while it downloads (" + string.Join(",", boxes) + ")");
            Assert.Equal(new FileInfo(result.Path).Length, result.Bytes, clip + ": size");
        }
    }

    [Test]
    public static void Cutting_without_converting_is_much_faster_than_converting()
    {
        TestFfmpeg.Load();
        string source = Path.Combine(Fixtures, "clip-av1-2tracks.mp4");
        var cut = Stopwatch.StartNew();
        ClipCutter.Cut(source, Temp("fast.mp4"), 0.5, 5.5, null, CancellationToken.None);
        cut.Stop();
        var convert = Stopwatch.StartNew();
        ClipTranscoder.Transcode(source, Temp("slow.mp4"), new SharePlan(0.5, 5, 640, 360, null, 60, 1_500_000), long.MaxValue, null, CancellationToken.None);
        convert.Stop();
        Assert.True(cut.Elapsed < convert.Elapsed / 3, $"cut {cut.ElapsedMilliseconds} ms, convert {convert.ElapsedMilliseconds} ms");
    }

    [Test]
    public static void Probe_tells_the_clips_file_size()
    {
        TestFfmpeg.Load();
        string clip = Path.Combine(Fixtures, "clip-h264.mp4");
        Assert.Equal(new FileInfo(clip).Length, ClipTranscoder.Probe(clip).Bytes, "bytes");
    }
}
