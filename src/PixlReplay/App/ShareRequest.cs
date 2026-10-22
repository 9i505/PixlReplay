using System.Globalization;

namespace PixlReplay.App;

/// <summary>
/// "Share last clip" from the tray: the engine leaves the clip's path in a small file and opens the settings
/// window, which takes it (once) and opens the share panel. A request older than a minute is ignored, so a
/// window opened much later does not pop the panel unasked.
/// </summary>
public static class ShareRequest
{
    static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);

    public static string DefaultPath => Path.Combine(Settings.SettingsStore.AppDataDir, "share-request.txt");

    public static void Write(string file, string clipPath, DateTime utcNow)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, utcNow.ToString("O", CultureInfo.InvariantCulture) + "\n" + clipPath);
    }

    public static string? Take(string file, DateTime utcNow)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        string[] lines = text.Split('\n', 2);
        if (lines.Length < 2 || !DateTime.TryParse(lines[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var written))
            return null;
        if (utcNow - written.ToUniversalTime() > MaxAge)
            return null;
        string path = lines[1].Trim();
        return path.Length > 0 ? path : null;
    }
}
