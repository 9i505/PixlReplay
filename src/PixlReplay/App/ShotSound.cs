using System.Media;

namespace PixlReplay.App;

/// <summary>
/// The screenshot sound: a soft two-part shutter (a short filtered click, then a quieter one), synthesized at startup
/// so no sound file ships. Distinct from the clip's <see cref="Chime"/>.
/// </summary>
public static class ShotSound
{
    const int SampleRate = 48000;
    const double Seconds = 0.16;
    const double TargetPeak = 0.25;

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
        var mix = new double[(int)(SampleRate * Seconds)];
        var noise = new Random(7); // the same click every time
        AddClick(mix, noise, 0.000, 1.0, 0.006);
        AddClick(mix, noise, 0.055, 0.55, 0.010);
        return Chime.ToWav(mix, SampleRate, TargetPeak);
    }

    /// <summary>Noise through a band-pass around 2–4 kHz with a 1 ms attack and a decay of <paramref name="decay"/> seconds.</summary>
    static void AddClick(double[] mix, Random noise, double start, double amplitude, double decay)
    {
        int first = (int)(start * SampleRate);
        double low = 0, high = 0, previous = 0;
        for (int i = first; i < mix.Length; i++)
        {
            double t = (i - first) / (double)SampleRate;
            double env = Math.Min(1, t / 0.001) * Math.Exp(-t / decay);
            if (t > 0.002 && env < 1e-4)
                break; // faded out (not the silent first sample of the attack)
            double x = noise.NextDouble() * 2 - 1;
            low += 0.45 * (x - low);                // low-pass: keeps it from hissing
            high = 0.85 * (high + low - previous);   // high-pass: takes the thump out
            previous = low;
            mix[i] += amplitude * env * high;
        }
    }
}
