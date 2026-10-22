namespace PixlReplay.Engine.Voice;

/// <summary>Streaming 48 kHz → 16 kHz converter: windowed-sinc low-pass at 7.2 kHz, then every third sample.</summary>
public sealed class Resampler48To16
{
    const int Taps = 127;
    static readonly float[] Kernel = BuildKernel();

    float[] _history = new float[Taps - 1];
    int _next = Taps - 1;

    public float[] Process(ReadOnlySpan<float> input)
    {
        var x = new float[_history.Length + input.Length];
        _history.CopyTo(x, 0);
        input.CopyTo(x.AsSpan(_history.Length));

        var output = new float[Math.Max(0, (x.Length - _next + 2) / 3)];
        int count = 0, p = _next;
        for (; p < x.Length; p += 3)
        {
            float acc = 0;
            for (int k = 0; k < Taps; k++)
                acc += Kernel[k] * x[p - k];
            output[count++] = acc;
        }
        _next = p - input.Length;
        _history = x[^(Taps - 1)..];
        return count == output.Length ? output : output[..count];
    }

    static float[] BuildKernel()
    {
        const double cutoff = 7200.0 / 48000.0;
        var h = new double[Taps];
        double m = Taps - 1, sum = 0;
        for (int i = 0; i < Taps; i++)
        {
            double t = i - m / 2;
            double sinc = t == 0 ? 2 * cutoff : Math.Sin(2 * Math.PI * cutoff * t) / (Math.PI * t);
            double blackman = 0.42 - 0.5 * Math.Cos(2 * Math.PI * i / m) + 0.08 * Math.Cos(4 * Math.PI * i / m);
            h[i] = sinc * blackman;
            sum += h[i];
        }
        return [.. h.Select(v => (float)(v / sum))];
    }
}
