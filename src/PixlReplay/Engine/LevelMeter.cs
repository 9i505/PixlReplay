namespace PixlReplay.Engine;

/// <summary>Holds the loudest peak (dBFS) reported by libobs since the last read. Written on the audio thread.</summary>
public sealed class LevelMeter
{
    public const double Silent = -100;
    const double BarFloorDb = -60;

    readonly object _gate = new();
    float _max = float.NegativeInfinity;

    public void Add(float peakDb)
    {
        lock (_gate)
            if (peakDb > _max)
                _max = peakDb;
    }

    /// <summary>The loudest peak since the previous call, or <see cref="Silent"/>.</summary>
    public double Take()
    {
        lock (_gate)
        {
            float max = _max;
            _max = float.NegativeInfinity;
            return float.IsFinite(max) ? max : Silent;
        }
    }

    /// <summary>0..1 bar length for a dBFS level (-60 dB and below is empty).</summary>
    public static double ToBar(double db) => Math.Clamp((db - BarFloorDb) / -BarFloorDb, 0, 1);
}
