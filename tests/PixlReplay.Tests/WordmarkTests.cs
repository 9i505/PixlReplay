namespace PixlReplay.Tests;

/// <summary>The name as drawn in the sidebar: Kai + rose italic ros (Fraunces), and كاي + rose slanted روس (Aref Ruqaa).</summary>
public static class WordmarkTests
{
    static string Assets
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "PixlReplay.sln")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "src", "PixlReplay", "Ui", "Assets");
        }
    }

    [Test]
    public static void The_name_is_drawn_in_english_and_in_arabic_on_a_clear_background()
    {
        foreach (string lang in new[] { "en", "ar" })
        {
            string path = Path.Combine(Assets, $"wordmark-{lang}.png");
            Assert.True(File.Exists(path), path);
            using var image = new System.Drawing.Bitmap(path);
            Assert.True(image.Height >= 110, $"{lang}: drawn large enough to stay sharp at 200% and more ({image.Height} px)");
            Assert.Equal(0, (int)image.GetPixel(0, 0).A, $"{lang}: clear background");
            bool rose = false;
            for (int x = 0; x < image.Width && !rose; x += 2)
                for (int y = 0; y < image.Height && !rose; y += 2)
                {
                    var p = image.GetPixel(x, y);
                    rose = p.A > 200 && p.R > 230 && p.G is > 120 and < 160 && p.B is > 150 and < 180;
                }
            Assert.True(rose, $"{lang}: the rose part");
        }
    }
}
