using System.Drawing;
using PixlReplay.App;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

/// <summary>
/// Pixl Replay's mark (the window and taskbar icon, app.ico and the tray): the Pixl P with a play mark in the accent on a
/// plate in the window's background; the tray colors the letter by state.
/// </summary>
public static class AppIconTests
{
    static uint Rgb(Color c) => (uint)(c.R << 16 | c.G << 8 | c.B);

    /// <summary>Where a point of the art lands in an icon of <paramref name="size"/> (the box Draw puts the letter in).</summary>
    static Point At(int size, PointF art)
    {
        float h = size * (size <= 24 ? 0.78f : 0.66f);
        var p = AppIconArt.Fit([art], new RectangleF((size - h) / 2, (size - h) / 2, h, h))[0];
        return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
    }

    [Test]
    public static void The_mark_is_the_letter_with_the_play_mark_in_the_accent_on_the_plate()
    {
        ThemeColors.Use(new LookSettings());
        using var mark = AppIconArt.Draw(256, 0xFF5A64);
        var play = At(256, new PointF(162, 131)); // inside the triangle
        Assert.Equal(0xFF5A64u, Rgb(mark.GetPixel(play.X, play.Y)), "the play mark");
        var stem = At(256, new PointF(47, 200));
        Assert.Equal(ThemeColors.Text, Rgb(mark.GetPixel(stem.X, stem.Y)), "the letter in the text color");
        var counter = At(256, new PointF(120, 100)); // inside the bowl, left of the play mark
        Assert.Equal(ThemeColors.Background, Rgb(mark.GetPixel(counter.X, counter.Y)), "the counter shows the plate");
        Assert.Equal(ThemeColors.Background, Rgb(mark.GetPixel(128, 12)), "the plate: the window's own background");
        Assert.Equal(0, (int)mark.GetPixel(0, 0).A, "rounded corner");
        using var green = AppIconArt.Draw(256, 0x3DDC84);
        Assert.Equal(0x3DDC84u, Rgb(green.GetPixel(play.X, play.Y)), "another accent");
    }

    [Test]
    public static void The_tray_colors_the_letter_by_state()
    {
        ThemeColors.Use(new LookSettings());
        foreach (string kind in new[] { "active", "waiting", "paused", "recording", "error", "saved" })
        {
            using var tray = TrayController.DrawBitmap(kind);
            // The stem, in the tray's 22 x 24 letter box at (5, 4).
            var stem = AppIconArt.Fit([new PointF(47, 150)], new RectangleF(5, 4, 22, 24))[0];
            Assert.Equal(TrayController.ColorFor(kind), Rgb(tray.GetPixel((int)stem.X, (int)stem.Y)), $"tray '{kind}': the letter");
        }
        using var recording = TrayController.DrawBitmap("recording");
        Assert.Equal(TrayController.ColorFor("recording"), Rgb(recording.GetPixel(24, 24)), "recording: the dot");
        using var paused = TrayController.DrawBitmap("paused");
        Assert.Equal(ThemeColors.Background, Rgb(paused.GetPixel(24, 24)), "paused: between the two bars");
    }

    [Test]
    public static void The_icon_file_has_every_size_windows_asks_for()
    {
        byte[] ico = AppIconArt.Ico();
        Assert.Equal((0, 1), (BitConverter.ToUInt16(ico, 0), BitConverter.ToUInt16(ico, 2)), "an icon file");
        Assert.Equal(AppIconArt.Sizes.Length, (int)BitConverter.ToUInt16(ico, 4), "frames");
        using var icon = new Icon(new MemoryStream(ico), 32, 32);
        Assert.Equal(32, icon.Width, "Windows reads it");
    }
}
