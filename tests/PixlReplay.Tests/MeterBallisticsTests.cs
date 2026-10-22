using PixlReplay.Engine;

namespace PixlReplay.Tests;

public static class MeterBallisticsTests
{
    [Test]
    public static void Louder_sound_shows_immediately()
    {
        var m = new MeterBallistics();
        m.Update(-12, 0.016);
        Assert.Near(-12, m.BarDb, 0.001, "bar");
    }

    [Test]
    public static void Quieter_sound_falls_at_the_obs_fast_rate()
    {
        var m = new MeterBallistics();
        m.Update(-10, 0.016);
        m.Update(-60, 0.5);
        Assert.Near(-10 - MeterBallistics.FallDbPerSecond * 0.5, m.BarDb, 0.001, "half a second of fall");
    }

    [Test]
    public static void Fall_never_goes_below_the_new_level()
    {
        var m = new MeterBallistics();
        m.Update(-10, 0.016);
        m.Update(-15, 2.0);
        Assert.Near(-15, m.BarDb, 0.001, "stops at target");
    }

    [Test]
    public static void Peak_marker_holds_for_a_second_then_falls()
    {
        var m = new MeterBallistics();
        m.Update(-6, 0.016);
        m.Update(-60, 0.9);
        Assert.Near(-6, m.HoldDb, 0.001, "held");
        m.Update(-60, 0.2);
        Assert.True(m.HoldDb < -6, "falling after the hold time");
    }

    [Test]
    public static void Long_gaps_are_capped_so_the_bar_does_not_jump()
    {
        var m = new MeterBallistics();
        m.Update(-10, 0.016);
        m.Update(-60, 30);
        Assert.Near(-10 - MeterBallistics.FallDbPerSecond * MeterBallistics.MaxStepSeconds, m.BarDb, 0.001, "capped step");
    }
}
