namespace PixlReplay.Engine;

/// <summary>
/// Meter movement like OBS Studio: rises instantly, falls at a fixed rate ("Fast", 23.53 dB/s),
/// and keeps a peak marker for one second before it falls too.
/// </summary>
public sealed class MeterBallistics
{
    public const double FallDbPerSecond = 23.53;
    public const double HoldSeconds = 1.0;
    public const double MaxStepSeconds = 1.0;
    const double Floor = LevelMeter.Silent;

    double _holdAge;

    public double BarDb { get; private set; } = Floor;
    public double HoldDb { get; private set; } = Floor;

    public void Update(double targetDb, double dtSeconds)
    {
        double dt = Math.Clamp(dtSeconds, 0, MaxStepSeconds);
        BarDb = targetDb >= BarDb ? targetDb : Math.Max(targetDb, BarDb - FallDbPerSecond * dt);

        if (BarDb >= HoldDb)
        {
            HoldDb = BarDb;
            _holdAge = 0;
        }
        else
        {
            _holdAge += dtSeconds;
            if (_holdAge > HoldSeconds)
                HoldDb = Math.Max(BarDb, HoldDb - FallDbPerSecond * dt);
        }
    }
}
