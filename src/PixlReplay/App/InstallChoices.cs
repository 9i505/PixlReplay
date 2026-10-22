using PixlReplay.Settings;

namespace PixlReplay.App;

/// <summary>
/// What the install wizard chose, left as <see cref="FileName"/> in PixlReplay's data folder ("language=de",
/// "startWithWindows=false", one per line): PixlReplay applies it on its next start, then deletes the file.
/// </summary>
public static class InstallChoices
{
    public const string FileName = "install-choices.txt";

    /// <summary>Applies the wizard's lines to <paramref name="settings"/>; anything unknown or broken is left out.</summary>
    public static void Apply(string text, AppSettings settings)
    {
        foreach (string line in text.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = line[..eq].Trim(), value = line[(eq + 1)..].Trim();
            if (key.Equals("language", StringComparison.OrdinalIgnoreCase) && Text.IsSupported(value))
                settings.General.Language = value;
            else if (key.Equals("startWithWindows", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out bool on))
                settings.General.StartWithWindows = on;
        }
    }

    /// <summary>Applies the wizard's file in <paramref name="dataDir"/> to <paramref name="settings"/> and deletes it; false when there is none.</summary>
    public static bool ConsumeInto(AppSettings settings, string dataDir)
    {
        string path = Path.Combine(dataDir, FileName);
        if (!File.Exists(path))
            return false; // every start but the first after the wizard
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false; // unreadable: PixlReplay starts as it is
        }
        Apply(text, settings);
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Applied anyway; it is tried again on the next start.
        }
        return true;
    }
}
