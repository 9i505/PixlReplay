namespace PixlReplay.Engine.Share;

/// <summary>Where prepared copies go: %LOCALAPPDATA%\PixlReplay\Share\&lt;platform&gt;\, kept for a few days.</summary>
public static class ShareFiles
{
    static readonly TimeSpan KeepFor = TimeSpan.FromDays(3);

    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, "Share");

    /// <summary>The clip's own name, so the upload shows the game and the date, one folder per platform.</summary>
    public static string Target(string folder, SharePlatform platform, string clipPath, string extension = ".mp4") =>
        Path.Combine(folder, platform.ToString(), Path.GetFileNameWithoutExtension(clipPath) + extension);

    /// <summary>Removes prepared copies older than three days. Only touches this app's own share folder.</summary>
    public static void Clean(string folder, DateTime now)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (string file in Directory.EnumerateFiles(folder, "*.mp4*", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(folder, "*.jpg", SearchOption.AllDirectories)))
        {
            try
            {
                if (now - File.GetLastWriteTime(file) > KeepFor)
                    File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still being uploaded or open somewhere: next time.
            }
        }
    }
}
