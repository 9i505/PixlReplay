using PixlReplay.Engine.Edit;

namespace PixlReplay.Tests;

/// <summary>The preview's sound at a part's speed: FFmpeg's atempo, as the export uses (OBS's FFmpeg, made-up tones).</summary>
public static class SoundTempoTests
{
    /// <summary>A second of a 440 Hz tone, 48 kHz stereo 16-bit.</summary>
    static byte[] Tone()
    {
        var pcm = new byte[48_000 * 4];
        for (int i = 0; i < 48_000; i++)
        {
            short v = (short)(16000 * Math.Sin(2 * Math.PI * 440 * i / 48_000.0));
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 4), v);
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 4 + 2), v);
        }
        return pcm;
    }

    static (int Bytes, int Peak) Run(double factor)
    {
        using var tempo = new SoundTempo(factor);
        var tone = Tone();
        var output = new List<byte>();
        for (int at = 0; at < tone.Length; at += 12_000 * 4)
            output.AddRange(tempo.Push(tone.AsSpan(at, Math.Min(12_000 * 4, tone.Length - at))));
        output.AddRange(tempo.Flush());
        var bytes = output.ToArray();
        int peak = 0;
        for (int i = 0; i + 1 < bytes.Length; i += 2)
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(bytes, i)));
        return (bytes.Length, peak);
    }

    [Test]
    public static void Twice_as_fast_is_half_as_long_and_a_quarter_four_times()
    {
        TestFfmpeg.Load();
        var fast = Run(2);
        Assert.Near(48_000 * 4 / 2.0, fast.Bytes, 48_000 * 4 * 0.05, "2x: half a second");
        Assert.True(fast.Peak > 10_000, $"2x still sounds ({fast.Peak})");
        var slow = Run(0.25);
        Assert.Near(48_000 * 4 * 4.0, slow.Bytes, 48_000 * 4 * 4.0 * 0.03, "0.25x: four seconds (the halves chain trims a few ms, as the export does)");
        Assert.True(slow.Peak > 10_000, $"0.25x still sounds ({slow.Peak})");
        Assert.True(Run(4).Bytes is > 45_000 and < 51_000, "4x: a quarter second");
    }
}
