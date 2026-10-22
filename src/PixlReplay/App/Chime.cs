using System.Media;

namespace PixlReplay.App;

/// <summary>The save sound: two soft low "bubble" pops, synthesized at startup (no sound files shipped).</summary>
public static class Chime
{
    const int SampleRate = 48000;
    const double Seconds = 0.30;
    const double TargetPeak = 0.30;

    static readonly Lazy<byte[]> Cached = new(Build);
    static SoundPlayer? _player;

    public static byte[] Wav() => Cached.Value;

    /// <summary>Plays asynchronously on the default output device.</summary>
    public static void Play()
    {
        _player ??= new SoundPlayer(new MemoryStream(Wav()));
        _player.Stream!.Position = 0;
        _player.Play();
    }

    static byte[] Build()
    {
        int n = (int)(SampleRate * Seconds);
        var mix = new double[n];
        AddPop(mix, 260, 1.0, 0.000);
        AddPop(mix, 390, 0.7, 0.075);

        return ToWav(mix, SampleRate, TargetPeak);
    }

    /// <summary>16-bit mono WAV of <paramref name="mix"/>, scaled to <paramref name="targetPeak"/>, the last 30 ms faded out.</summary>
    public static byte[] ToWav(double[] mix, int sampleRate, double targetPeak)
    {
        int n = mix.Length;
        double peak = mix.Max(Math.Abs);
        int fade = sampleRate * 30 / 1000;
        var pcm = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            double v = mix[i] / peak * targetPeak;
            if (i > n - fade)
                v *= (n - i) / (double)fade;
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), (short)Math.Round(v * short.MaxValue));
        }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + pcm.Length);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);    // byte rate
        w.Write((short)2);          // block align
        w.Write((short)16);         // bits per sample
        w.Write("data"u8);
        w.Write(pcm.Length);
        w.Write(pcm);
        w.Flush();
        return ms.ToArray();
    }

    /// <summary>A sine that glides from twice <paramref name="baseFreq"/> down to it, with a 3 ms attack and a fast decay.</summary>
    static void AddPop(double[] mix, double baseFreq, double amplitude, double start)
    {
        int first = (int)(start * SampleRate);
        double phase = 0;
        for (int i = first; i < mix.Length; i++)
        {
            double t = (i - first) / (double)SampleRate;
            phase += 2 * Math.PI * baseFreq * (1 + Math.Exp(-t / 0.025)) / SampleRate;
            double env = Math.Min(1, t / 0.003) * Math.Exp(-t / 0.045);
            mix[i] += amplitude * env * Math.Sin(phase);
        }
    }
}
