using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PixlReplay.Engine.Share;

/// <summary>A JPEG copy of a screenshot for a site that will not take the PNG's size.</summary>
public static class PictureJpeg
{
    static readonly long[] Qualities = [95, 90, 85, 80];

    /// <summary>
    /// Writes <paramref name="target"/> no bigger than <paramref name="maxBytes"/>: the best quality that fits, then,
    /// if even quality 80 is too big, the picture a little smaller each time.
    /// </summary>
    public static ShareResult Make(string source, string target, long maxBytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var original = new Bitmap(source);
        double scale = 1;
        for (int round = 0; round < 8; round++, scale *= 0.85)
        {
            using var picture = scale == 1 ? new Bitmap(original) : Scaled(original, scale);
            foreach (long quality in Qualities)
            {
                using var file = new MemoryStream();
                using var options = new EncoderParameters(1);
                options.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                picture.Save(file, codec, options);
                if (file.Length <= maxBytes)
                {
                    File.WriteAllBytes(target, file.ToArray());
                    return new ShareResult(target, file.Length, $"jpeg q{quality}");
                }
            }
        }
        throw new ShareException("The picture could not be made small enough for this site.");
    }

    static Bitmap Scaled(Bitmap original, double scale)
    {
        var smaller = new Bitmap(Math.Max(1, (int)(original.Width * scale)), Math.Max(1, (int)(original.Height * scale)));
        using var g = Graphics.FromImage(smaller);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(original, 0, 0, smaller.Width, smaller.Height);
        return smaller;
    }
}
