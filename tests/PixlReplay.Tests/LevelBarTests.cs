using System.Windows.Threading;
using PixlReplay.App;
using PixlReplay.Engine;
using PixlReplay.Ui;

namespace PixlReplay.Tests;

/// <summary>
/// The Audio page's meters draw every display frame only while their level moves: once it has settled (silence, or a
/// steady sound) they rest, and a new level wakes them at once. What they show is unchanged.
/// </summary>
public static class LevelBarTests
{
    static void OnSta(Action test)
    {
        string? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                error = e.ToString();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.True(error == null, error ?? "");
    }

    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    static double Settle(LevelBar bar, ref double ms)
    {
        for (int i = 0; i < 1000 && bar.Rendering; i++)
            bar.Frame(TimeSpan.FromMilliseconds(ms += 16));
        return bar.BarDb;
    }

    [Test]
    public static void A_meter_rests_while_its_level_stays_and_wakes_when_it_moves() => OnSta(() =>
    {
        var stream = new LevelStream(connect: false);
        LevelBar.StreamFactory = () => stream;
        try
        {
            var bar = new LevelBar { Channel = "system" };
            bar.Attach();
            Assert.True(bar.Rendering, "drawing when it comes on screen");
            // Frame times after the real ones the dispatcher may have drawn while pumping.
            double ms = 1e7;
            bar.Frame(TimeSpan.FromMilliseconds(ms));
            Assert.True(!bar.Rendering, "silence that stays: no more frames");

            stream.Push(-12, LevelMeter.Silent);
            Pump();
            Assert.Near(-12, bar.BarDb, 1e-9, "a new level wakes it: the bar jumps to it (a rise is instant)");
            Assert.Near(-12, bar.HoldDb, 1e-9, "its peak mark is on the level");
            Assert.True(!bar.Rendering, "a steady level: resting again");

            stream.Push(LevelMeter.Silent, -20);
            Pump();
            Assert.True(bar.Rendering, "the sound stopped: it falls back, frame by frame");
            Assert.Near(LevelMeter.Silent, Settle(bar, ref ms), 1e-9, "back to silence, then rests");
            Assert.True(ms > 1e7 + 1000, "it fell at its own pace (the peak mark holds a second first)");
            bar.Detach();
            Assert.True(!bar.Rendering, "off screen: nothing");
        }
        finally
        {
            LevelBar.StreamFactory = () => new LevelStream();
        }
    });
}
