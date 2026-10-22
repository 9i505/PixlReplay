using System.Drawing;
using PixlReplay.App;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

public static class SaveFeedbackTests
{
    [Test]
    public static void Sound_and_notification_are_on_by_default()
    {
        var g = new GeneralSettings();
        Assert.Equal(true, g.SoundOnSave, "sound");
        Assert.Equal(true, g.ToastOnSave, "notification");
    }

    [Test]
    public static void Chime_is_a_short_48khz_mono_wav()
    {
        byte[] wav = Chime.Wav();
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4), "riff");
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4), "wave");
        Assert.Equal(48000, BitConverter.ToInt32(wav, 24), "sample rate");
        Assert.Equal((short)1, BitConverter.ToInt16(wav, 22), "mono");
        int dataBytes = BitConverter.ToInt32(wav, 40);
        Assert.Equal(wav.Length - 44, dataBytes, "data size");
        double seconds = dataBytes / 2.0 / 48000;
        Assert.True(seconds is > 0.25 and < 0.6, $"length {seconds:F2}s");
    }

    [Test]
    public static void Chime_is_gentle_and_click_free()
    {
        byte[] wav = Chime.Wav();
        int samples = (wav.Length - 44) / 2;
        short Sample(int i) => BitConverter.ToInt16(wav, 44 + i * 2);
        int peak = 0;
        for (int i = 0; i < samples; i++)
            peak = Math.Max(peak, Math.Abs((int)Sample(i)));
        Assert.True(peak < 32767 * 0.45, $"peak {peak} should stay under -7 dBFS");
        Assert.True(Math.Abs((int)Sample(0)) < 200 && Math.Abs((int)Sample(samples - 1)) < 200, "starts and ends at silence");
    }

    [Test]
    public static void Chime_is_a_low_soft_bubble()
    {
        byte[] wav = Chime.Wav();
        int samples = (wav.Length - 44) / 2;
        short Sample(int i) => BitConverter.ToInt16(wav, 44 + i * 2);
        int window = 48000 * 120 / 1000, crossings = 0, peak = 0;
        for (int i = 1; i < window; i++)
            if ((Sample(i - 1) < 0) != (Sample(i) < 0))
                crossings++;
        for (int i = 0; i < samples; i++)
            peak = Math.Max(peak, Math.Abs((int)Sample(i)));
        double hz = crossings / 2.0 / 0.12;
        Assert.True(hz < 700, $"pitch {hz:F0} Hz should stay low");
        Assert.True(peak < 32767 * 0.32, $"peak {peak} should be quieter than the old chime");
    }

    [Test]
    public static void Toast_sits_in_the_top_right_of_the_work_area()
    {
        var p = SaveToast.Place(new Rectangle(0, 0, 2560, 1400), new Size(320, 72), margin: 16);
        Assert.Equal(new Point(2560 - 320 - 16, 16), p, "position");
        var second = SaveToast.Place(new Rectangle(-2560, 0, 2560, 1400), new Size(320, 72), margin: 16);
        Assert.Equal(new Point(-320 - 16, 16), second, "left monitor");
    }

    [Test]
    public static void Toast_fades_in_holds_and_fades_out()
    {
        Assert.Near(0, SaveToast.Opacity(0), 0.001, "start");
        Assert.True(SaveToast.Opacity(SaveToast.FadeIn / 2) is > 0 and < 1, "fading in");
        Assert.Near(1, SaveToast.Opacity(SaveToast.FadeIn + 0.5), 0.001, "held");
        Assert.True(SaveToast.Opacity(SaveToast.Total - SaveToast.FadeOut / 2) is > 0 and < 1, "fading out");
        Assert.Near(0, SaveToast.Opacity(SaveToast.Total), 0.001, "gone");
    }

    [Test]
    public static void A_problem_shows_an_amber_mark_not_the_green_check()
    {
        // The icon sits 16 px in, 30 px wide, centred in the 64 px height; this point is on its disc, off the mark.
        using (var ok = SaveToast.Paint("Saved", "Game", rtl: false, scale: 1f))
        {
            var c = ok.GetPixel(22, 32);
            Assert.True(c.G > 190 && c.R < 100, $"a save: green ({c})");
        }
        using (var problem = SaveToast.Paint("Alt+F10 doesn't work", "Another app is using it", rtl: false, scale: 1f, warning: true))
        {
            var c = problem.GetPixel(22, 32);
            Assert.True(c.R > 230 && c.G is > 140 and < 200 && c.B < 80, $"a problem: amber ({c})");
        }
    }
}
