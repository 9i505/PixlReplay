namespace PixlReplay.Engine.Voice;

/// <summary>Time-scale modification by waveform-similarity overlap-add: changes speed, keeps pitch.</summary>
public static class Wsola
{
    /// <summary>Returns <paramref name="x"/> played <paramref name="rate"/> times as fast (0.9 = slower, 1.1 = faster).</summary>
    public static float[] Stretch(float[] x, double rate, int frame = 512, int hop = 256, int tolerance = 128)
    {
        int outLength = (int)(x.Length / rate);
        var src = new float[x.Length + frame + 2 * tolerance + hop];
        x.CopyTo(src, 0);
        var window = new float[frame];
        for (int i = 0; i < frame; i++)
            window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (frame - 1)));

        var sum = new float[outLength + frame];
        var weight = new float[outLength + frame];
        int previous = 0;
        for (int k = 0; k * hop < outLength; k++)
        {
            int position = 0;
            if (k > 0)
            {
                // Pick the frame near the nominal position that best continues the previous one.
                int nominal = (int)Math.Round(k * hop * rate);
                int lo = Math.Max(0, nominal - tolerance), hi = Math.Min(nominal + tolerance, src.Length - frame);
                var target = src.AsSpan(previous + hop, frame);
                double best = double.NegativeInfinity;
                for (int p = lo; p <= hi; p++)
                {
                    double c = Dot(src.AsSpan(p, frame), target);
                    if (c > best)
                    {
                        best = c;
                        position = p;
                    }
                }
            }
            int o = k * hop;
            for (int i = 0; i < frame; i++)
            {
                sum[o + i] += src[position + i] * window[i];
                weight[o + i] += window[i];
            }
            previous = position;
        }

        var y = new float[outLength];
        for (int i = 0; i < outLength; i++)
            y[i] = sum[i] / Math.Max(weight[i], 1e-3f);
        return y;
    }

    static double Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++)
            s += a[i] * b[i];
        return s;
    }
}
