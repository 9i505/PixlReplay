using PixlReplay.Engine;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

public static class AudioLevelTests
{
    // The Audio page's meters jumped to full and fell back on opening, with no sound playing: the first reading was the
    // loudest peak since anyone last looked (minutes or hours before).
    [Test]
    public static void Opening_the_meters_starts_from_now_not_from_the_last_loud_sound()
    {
        var system = new LevelMeter();
        var mic = new LevelMeter();
        system.Add(-0.5f); // a loud moment while the Audio page was closed
        mic.Add(-1f);
        string name = "pixlreplay-test-" + Guid.NewGuid().ToString("N");
        using var server = new PixlReplay.App.PipeServer(_ => "", () => (system.Take(), mic.Take()), name);
        using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.CurrentUserOnly);
        client.Connect(2000);
        using var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, leaveOpen: true);
        writer.WriteLine("levels");

        Assert.Equal("-100.0 -100.0", reader.ReadLine(), "silence now: the old peak is not shown");
        system.Add(-20f);
        var live = Enumerable.Range(0, 5).Select(_ => reader.ReadLine()).ToList();
        Assert.True(live.Contains("-20.0 -100.0"), $"sound from now on still shows ({string.Join(" | ", live)})");
    }

    [Test]
    public static void Meter_reports_the_loudest_peak_since_the_last_read()
    {
        var m = new LevelMeter();
        m.Add(-40f);
        m.Add(-12f);
        m.Add(-25f);
        Assert.Near(-12, m.Take(), 0.001, "max");
    }

    [Test]
    public static void Meter_resets_after_each_read()
    {
        var m = new LevelMeter();
        m.Add(-6f);
        m.Take();
        Assert.Equal(LevelMeter.Silent, m.Take(), "reset");
    }

    [Test]
    public static void Meter_with_no_audio_or_minus_infinity_is_silent()
    {
        var m = new LevelMeter();
        Assert.Equal(LevelMeter.Silent, m.Take(), "nothing");
        m.Add(float.NegativeInfinity);
        Assert.Equal(LevelMeter.Silent, m.Take(), "-inf");
    }

    [Test]
    public static void Bar_maps_minus_60_db_to_empty_and_0_db_to_full()
    {
        Assert.Near(0, LevelMeter.ToBar(-60), 0.001, "-60");
        Assert.Near(0.5, LevelMeter.ToBar(-30), 0.001, "-30");
        Assert.Near(1, LevelMeter.ToBar(0), 0.001, "0");
        Assert.Near(0, LevelMeter.ToBar(LevelMeter.Silent), 0.001, "silent");
        Assert.Near(1, LevelMeter.ToBar(3), 0.001, "clipped");
    }

    [Test]
    public static void Choosing_a_microphone_turns_the_microphone_on()
    {
        var a = new AudioSettings { Mic = false };
        a.SelectMic("{0.0.1.00000000}.{eb3f2fdf}");
        Assert.Equal(true, a.Mic, "on");
        Assert.Equal("{0.0.1.00000000}.{eb3f2fdf}", a.MicDevice, "device");
    }
}
