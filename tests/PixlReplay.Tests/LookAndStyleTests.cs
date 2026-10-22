using System.Windows;
using System.Windows.Media.Imaging;
using PixlReplay.Engine.Edit;
using PixlReplay.Ui.Edit;

namespace PixlReplay.Tests;

/// <summary>The colour looks, the text styles and the colour picker's arithmetic.</summary>
public static class LookAndStyleTests
{
    [Test]
    public static void Every_look_filters_a_picture_at_full_and_half_strength()
    {
        TestFfmpeg.Load();
        const int w = 48, h = 32;
        var plain = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // Blocks with edges and a gradient, so every look (even sharpening) has something to change.
                int i = (y * w + x) * 4;
                bool dark = (x / 8 + y / 8) % 2 == 0;
                plain[i] = (byte)(dark ? 30 + x * 2 : 190);
                plain[i + 1] = (byte)(dark ? 60 : 120 + y * 3);
                plain[i + 2] = (byte)(dark ? 140 : 70);
                plain[i + 3] = 255;
            }
        foreach (ColourLook look in Enum.GetValues<ColourLook>())
        {
            string? filter = ColourLooks.Filter(look, 1);
            if (look == ColourLook.None)
            {
                Assert.True(filter == null, "no look, no filter");
                continue;
            }
            Assert.True(filter != null, $"{look} has a filter");
            Assert.True(ColourLooks.Filter(look, 0) == null, $"{look} at no strength is no filter");
            var pixels = (byte[])plain.Clone();
            using (var full = new FrameFilter(w, h, filter))
                full.Apply(pixels); // a filter this FFmpeg build does not have throws here
            Assert.True(!pixels.AsSpan().SequenceEqual(plain), $"{look} changes the picture");
            using var half = new FrameFilter(w, h, ColourLooks.Filter(look, 0.5));
            half.Apply((byte[])plain.Clone());
        }
    }

    [Test]
    public static void Glow_and_neon_reach_past_the_letters_and_a_pill_has_a_background() => OnSta(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "pixlreplay-test-effects-" + Guid.NewGuid().ToString("N"));
        try
        {
            Overlay Render(TextEffect effect) => ItemRenderer.RenderPng(
                new TextItem(Guid.NewGuid(), 0, 1, "PixlReplay", new Rect(0.3, 0.4, 0.4, 0.2), "#F48BA4", 0.1, effect), 640, 360, folder);
            static int Width(Overlay overlay)
            {
                using var stream = File.OpenRead(overlay.Path);
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).PixelWidth;
            }
            var plain = Render(TextEffect.Plain);
            var glow = Render(TextEffect.Glow);
            var neon = Render(TextEffect.Neon);
            var pill = Render(TextEffect.Pill);
            var box = Render(TextEffect.Box);
            Assert.True(Width(glow) > Width(plain), $"the glow reaches past the letters ({Width(glow)} vs {Width(plain)})");
            Assert.True(Width(neon) > Width(plain), $"the neon light reaches past the letters ({Width(neon)} vs {Width(plain)})");
            Assert.True(Width(pill) == Width(box) && pill.X == box.X, "the pill's background fills the box like the plain box");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    [Test]
    public static void Colours_round_trip_through_hue_saturation_and_value()
    {
        foreach (string hex in new[] { "#F48BA4", "#FFFFFF", "#000000", "#3DDC84", "#4FC3F7", "#111111", "#FF0000", "#00FF00", "#0000FF", "#7F7F7F" })
        {
            var (h, s, v) = ColourMath.ToHsv(hex);
            Assert.Equal(hex, ColourMath.ToHex(h, s, v), hex);
        }
        Assert.True(ColourMath.TryParse("f48ba4", out string parsed) && parsed == "#F48BA4", "without the hash, any case");
        Assert.True(ColourMath.TryParse(" #3DDC84 ", out parsed) && parsed == "#3DDC84", "spaces around");
        Assert.True(!ColourMath.TryParse("#12", out _), "too short");
        Assert.True(!ColourMath.TryParse("#GGGGGG", out _), "not hex");
    }

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
}
