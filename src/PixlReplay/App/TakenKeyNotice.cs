using PixlReplay.Settings;

namespace PixlReplay.App;

/// <summary>
/// Remembers which hotkeys the user was told another app holds, so that small message shows once per key, ever
/// (kept in a file beside the settings, which the settings window never rewrites).
/// </summary>
public sealed class TakenKeyNotice(string path)
{
    public static string DefaultPath => Path.Combine(SettingsStore.AppDataDir, "taken-keys.txt");

    /// <summary>True the first time a key is reported taken; false after, also after a restart.</summary>
    public bool ShouldTell(string hotkey)
    {
        var told = Read();
        if (!told.Add(hotkey))
            return false;
        try
        {
            File.WriteAllLines(path, told);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return true;
    }

    HashSet<string> Read()
    {
        try
        {
            return File.Exists(path) ? [.. File.ReadAllLines(path)] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
