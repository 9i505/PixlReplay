using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixlReplay.Engine.Edit;
using PixlReplay.Ui.Edit;
using TextEffect = PixlReplay.Engine.Edit.TextEffect;

namespace PixlReplay.Tests;

/// <summary>Items drawn for the preview and the export, colour and blur on frames, and edited pictures.</summary>
public static class EditRenderTests
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

    /// <summary>The item drawn on a transparent 640x360 picture, as BGRA rows.</summary>
    static byte[] Draw(EditItem item, out int stride)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            ItemRenderer.Draw(dc, item, new Size(640, 360));
        var bmp = new RenderTargetBitmap(640, 360, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        stride = 640 * 4;
        var pixels = new byte[stride * 360];
        bmp.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    static byte Alpha(byte[] bgra, int stride, int x, int y) => bgra[y * stride + x * 4 + 3];

    [Test]
    public static void A_text_item_is_drawn_where_it_sits() => OnSta(() =>
    {
        var text = new TextItem(Guid.NewGuid(), 0, 5, "GG", new Rect(0.1, 0.1, 0.3, 0.1), "#FFFFFF", 0.06, TextEffect.Box);
        var px = Draw(text, out int stride);
        Assert.True(Alpha(px, stride, 160, 54) > 0, "inside its box");
        Assert.Equal((byte)0, Alpha(px, stride, 600, 300), "nothing elsewhere");
    });

    [Test]
    public static void An_arabic_text_stays_inside_its_box() => OnSta(() =>
    {
        // No backing: only the letters are drawn. Box 0.1..0.4 across (64..256 px of 640).
        var text = new TextItem(Guid.NewGuid(), 0, 5, "أقوى لقطة", new Rect(0.1, 0.1, 0.3, 0.12), "#FFFFFF", 0.06, TextEffect.Plain);
        var px = Draw(text, out int stride);
        int inside = 0, outside = 0;
        for (int y = 36; y < 80; y++)
            for (int x = 0; x < 640; x++)
                if (Alpha(px, stride, x, y) > 0)
                {
                    if (x >= 60 && x <= 260)
                        inside++;
                    else
                        outside++;
                }
        Assert.True(inside > 50, $"the letters are in the box: {inside}");
        Assert.Equal(0, outside, "and nowhere else");
    });

    [Test]
    public static void An_arrow_has_a_head() => OnSta(() =>
    {
        var arrow = new ShapeItem(Guid.NewGuid(), 0, 5, ShapeKind.Arrow, new Point(0.1, 0.5), new Point(0.8, 0.5), "#F48BA4", 0.01);
        var px = Draw(arrow, out int stride);
        Assert.True(Alpha(px, stride, 300, 180) > 0, "the line");
        Assert.True(Alpha(px, stride, 504, 176) > 0, "the head, wider than the line, near the tip");
        Assert.Equal((byte)0, Alpha(px, stride, 192, 176), "the line alone is thin");
    });

    /// <summary>A drawing's strokes are drawn apart (lifting the pen leaves a gap).</summary>
    [Test]
    public static void A_drawings_strokes_are_not_joined() => OnSta(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "pixlreplay-edit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var drawing = new StrokeItem(Guid.NewGuid(), 0, 5,
                [[new Point(0.1, 0.5), new Point(0.2, 0.5)], [new Point(0.8, 0.5), new Point(0.9, 0.5)]], "#FFFFFF", 0.01);
            var picture = ItemRenderer.RenderPng(drawing, 1000, 1000, folder);
            var frame = new FormatConvertedBitmap(
                BitmapDecoder.Create(new Uri(picture.Path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
            byte Alpha(int x, int y)
            {
                var pixel = new byte[4];
                frame.CopyPixels(new Int32Rect(x - picture.X, y - picture.Y, 1, 1), pixel, 4, 0);
                return pixel[3];
            }
            Assert.True(Alpha(150, 500) > 0 && Alpha(850, 500) > 0, "both strokes drawn");
            Assert.Equal((byte)0, Alpha(500, 500), "nothing between them");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    });

    /// <summary>
    /// An item's picture is only as big as the item (a full-frame picture per caption would hold gigabytes for a long
    /// clip with many lines), and says where in the frame it goes.
    /// </summary>
    [Test]
    public static void The_png_holds_only_the_item_and_says_where_it_goes() => OnSta(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "pixlreplay-edit-" + Guid.NewGuid().ToString("N"));
        try
        {
            var stroke = new StrokeItem(Guid.NewGuid(), 0, 5, [[new Point(0.2, 0.2), new Point(0.3, 0.25), new Point(0.4, 0.2)]], "#FFFFFF", 0.01);
            var picture = ItemRenderer.RenderPng(stroke, 2560, 1440, folder);
            Assert.True(File.Exists(picture.Path), "written");
            var frame = BitmapDecoder.Create(new Uri(picture.Path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.True(frame.PixelWidth is > 512 and < 600, $"as wide as the drawing ({frame.PixelWidth})");
            Assert.True(frame.PixelHeight is > 72 and < 140, $"as tall as the drawing ({frame.PixelHeight})");
            Assert.True(picture.X is > 470 and <= 512 && picture.Y is > 250 and <= 288, $"placed at the drawing ({picture.X}, {picture.Y})");
            var pixel = new byte[4];
            new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0)
                .CopyPixels(new Int32Rect((int)(0.3 * 2560) - picture.X, (int)(0.25 * 1440) - picture.Y, 1, 1), pixel, 4, 0);
            Assert.True(pixel[3] > 0, "the line is where it was drawn");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    });

    static byte[] Solid(int width, int height, byte b, byte g, byte r)
    {
        var px = new byte[width * height * 4];
        for (int i = 0; i < px.Length; i += 4)
        {
            px[i] = b;
            px[i + 1] = g;
            px[i + 2] = r;
            px[i + 3] = 255;
        }
        return px;
    }

    [Test]
    public static void Mono_removes_colour()
    {
        TestFfmpeg.Load();
        var px = Solid(64, 36, 30, 40, 220);
        using (var filter = new FrameFilter(64, 36, ColourLooks.Filter(ColourLook.Mono, 1)))
            filter.Apply(px);
        int b = px[0], g = px[1], r = px[2];
        Assert.True(Math.Abs(r - g) < 12 && Math.Abs(g - b) < 12, $"grey: {r},{g},{b}");
    }

    [Test]
    public static void No_filter_changes_nothing()
    {
        TestFfmpeg.Load();
        var px = Solid(64, 36, 30, 40, 220);
        using (var filter = new FrameFilter(64, 36, null))
            filter.Apply(px);
        Assert.True(px[0] == 30 && px[1] == 40 && px[2] == 220, "untouched");
    }

    [Test]
    public static void Blur_softens_only_its_box()
    {
        // Black and white stripes: blurred inside the box they turn grey, outside they stay sharp.
        int w = 120, h = 60;
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte v = (byte)(x / 2 % 2 == 0 ? 255 : 0);
                int i = (y * w + x) * 4;
                px[i] = px[i + 1] = px[i + 2] = v;
                px[i + 3] = 255;
            }
        FrameFilter.Blur(px, w, h, new Int32Rect(0, 0, 60, 60));
        int inside = px[(30 * w + 30) * 4], outside = px[(30 * w + 90) * 4];
        Assert.True(inside is > 60 and < 200, $"inside is grey: {inside}");
        Assert.True(outside is 0 or 255, $"outside unchanged: {outside}");
    }

    /// <summary>A 400x200 dark blue PNG in a temp folder.</summary>
    static string SamplePicture(string folder)
    {
        Directory.CreateDirectory(folder);
        var px = Solid(400, 200, 90, 30, 20);
        var bmp = BitmapSource.Create(400, 200, 96, 96, PixelFormats.Bgra32, null, px, 400 * 4);
        string path = Path.Combine(folder, "shot.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using (var f = File.Create(path))
            encoder.Save(f);
        return path;
    }

    static (int Width, int Height, byte[] Bgra) ReadPng(string path)
    {
        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        var px = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(px, converted.PixelWidth * 4, 0);
        return (converted.PixelWidth, converted.PixelHeight, px);
    }

    [Test]
    public static void Crop_sets_the_size() => OnSta(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "pixlreplay-pic-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = SamplePicture(folder);
            var p = EditProject.For(source, 0) with { Crop = new Rect(0, 0, 0.5, 0.5) };
            string target = Path.Combine(folder, "edited.png");
            PictureComposer.Save(source, p, target);
            var (w, h, _) = ReadPng(target);
            Assert.Equal(200, w, "width");
            Assert.Equal(100, h, "height");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    [Test]
    public static void Items_are_drawn_before_the_crop_moves_them() => OnSta(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "pixlreplay-pic-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = SamplePicture(folder);
            // A white rectangle from (40,20) to (80,40) in the 400x200 picture; the crop keeps its top-left quarter.
            var box = new ShapeItem(Guid.NewGuid(), 0, 0, ShapeKind.Rectangle, new Point(0.1, 0.1), new Point(0.2, 0.2), "#FFFFFF", 0.02);
            var p = EditProject.For(source, 0).With(box) with { Crop = new Rect(0, 0, 0.5, 0.5) };
            string target = Path.Combine(folder, "edited.png");
            PictureComposer.Save(source, p, target);
            var (w, _, px) = ReadPng(target);
            int edge = (20 * w + 60) * 4; // on the rectangle's top edge, same place after a crop from the corner
            Assert.True(px[edge] > 200 && px[edge + 1] > 200 && px[edge + 2] > 200, "the white edge is there");
            int inside = (30 * w + 60) * 4;
            Assert.True(px[inside + 2] < 60, "inside stays the picture");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });

    [Test]
    public static void The_original_is_untouched() => OnSta(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "pixlreplay-pic-" + Guid.NewGuid().ToString("N"));
        try
        {
            string source = SamplePicture(folder);
            byte[] before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source));
            var blur = new BlurItem(Guid.NewGuid(), 0, 0, new Rect(0.2, 0.2, 0.3, 0.3));
            PictureComposer.Save(source, EditProject.For(source, 0).With(blur), Path.Combine(folder, "edited.png"));
            byte[] after = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(source));
            Assert.True(before.AsSpan().SequenceEqual(after), "same bytes");
            Assert.Equal(0, Directory.GetFiles(folder, "*.part").Length, "no part file left");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    });
    /// <summary>
    /// A screenshot's colour look works in a window that never played a clip: FFmpeg loads itself when first needed
    /// (a fresh PixlReplay process, as when the window opens on the Library and the user edits a screenshot).
    /// </summary>
    [Test]
    public static void A_picture_look_works_in_a_fresh_process()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pixlreplay-edit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string source = Path.Combine(dir, "shot.png"), target = Path.Combine(dir, "shot (edited).png");
        var sta = new Thread(() =>
        {
            var pixels = new byte[64 * 64 * 4];
            for (int i = 0; i < pixels.Length; i += 4)
                (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = ((byte)90, (byte)110, (byte)150, (byte)255);
            var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(64, 64, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 64 * 4);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(source);
            encoder.Save(file);
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();

        string obs = @"C:\Program Files\obs-studio\bin\64bit";
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "PixlReplay.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (string arg in new[] { "--selftest-picture-look", source, target })
            start.ArgumentList.Add(arg);
        // This test build has no FFmpeg beside PixlReplay.exe; an installed PixlReplay has OBS's copy there.
        start.Environment["PIXLREPLAY_FFMPEG"] = obs;
        using var process = System.Diagnostics.Process.Start(start)!;
        string said = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode, "saved: " + said.Trim());
        Assert.True(File.Exists(target), "the edited picture");
    }

    /// <summary>The ten fonts are really there (a missing one would quietly draw in the default font).</summary>
    [Test]
    public static void Each_font_draws_its_own_letters() => OnSta(() =>
    {
        Assert.Equal(10, TextFonts.All.Count, "ten fonts");
        var widths = TextFonts.All.Select(f => ItemRenderer.Layout(
            new TextItem(Guid.Empty, 0, 1, "PixlReplay بكسل", new Rect(0, 0, 1, 1), "#FFFFFF", 0.1, TextEffect.Plain, f.Key), new Size(1000, 1000))
            .WidthIncludingTrailingWhitespace).ToList();
        Assert.Equal(10, widths.Select(w => Math.Round(w)).Distinct().Count(), "each its own width: " + string.Join(", ", widths.Select(w => w.ToString("0"))));
        Assert.Equal(TextFonts.All[0].Key, TextFonts.For("no such font").Key, "an unknown font is the default one");
    });

    /// <summary>An outline rings light letters with dark; a shadow falls below and to the right; plain has neither.</summary>
    [Test]
    public static void Outline_and_shadow_set_light_text_off() => OnSta(() =>
    {
        string folder = Path.Combine(Path.GetTempPath(), "pixlreplay-edit-" + Guid.NewGuid().ToString("N"));
        try
        {
            (int Dark, double DarkX, double DarkY, double LightX, double LightY) Look(TextEffect effect)
            {
                var text = new TextItem(Guid.NewGuid(), 0, 1, "GG", new Rect(0.2, 0.3, 0.6, 0.4), "#FFFFFF", 0.2, effect);
                var picture = ItemRenderer.RenderPng(text, 400, 400, folder);
                var frame = new FormatConvertedBitmap(
                    BitmapDecoder.Create(new Uri(picture.Path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
                var pixels = new byte[frame.PixelWidth * frame.PixelHeight * 4];
                frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
                int dark = 0, light = 0;
                double dx = 0, dy = 0, lx = 0, ly = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] < 200)
                        continue;
                    int x = i / 4 % frame.PixelWidth, y = i / 4 / frame.PixelWidth;
                    int top = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
                    if (top < 70)
                        (dark, dx, dy) = (dark + 1, dx + x, dy + y);
                    else if (top > 200)
                        (light, lx, ly) = (light + 1, lx + x, ly + y);
                }
                return (dark, dx / Math.Max(1, dark), dy / Math.Max(1, dark), lx / Math.Max(1, light), ly / Math.Max(1, light));
            }

            Assert.Equal(0, Look(TextEffect.Plain).Dark, "plain: no dark");
            var outline = Look(TextEffect.Outline);
            Assert.True(outline.Dark > 300, $"outline: dark around the letters ({outline.Dark})");
            Assert.True(Math.Abs(outline.DarkX - outline.LightX) < 3 && Math.Abs(outline.DarkY - outline.LightY) < 3, "all around them");
            var shadow = Look(TextEffect.Shadow);
            Assert.True(shadow.Dark > 100, $"shadow: dark beside the letters ({shadow.Dark})");
            Assert.True(shadow.DarkX > shadow.LightX + 1 && shadow.DarkY > shadow.LightY + 1, "below and to the right");
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    });
}
