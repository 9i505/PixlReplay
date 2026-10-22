using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PixlReplay.App;

/// <summary>
/// The keys OBS Studio's shortcuts use (scene collections and profiles), so PixlReplay never offers one: OBS reacts to its
/// key with any modifiers held too (its F10 fires on Alt+F10), so the key alone is what matters.
/// </summary>
public static partial class ObsHotkeys
{
    static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "basic");

    /// <summary>The keys in OBS's settings on this PC (none when OBS is not installed).</summary>
    public static IReadOnlySet<Keys> Read()
    {
        try
        {
            if (!Directory.Exists(Folder))
                return new HashSet<Keys>();
            var files = Directory.EnumerateFiles(Path.Combine(Folder, "scenes"))
                .Where(f => string.Equals(Path.GetExtension(f), ".json", StringComparison.OrdinalIgnoreCase))
                .Concat(Directory.Exists(Path.Combine(Folder, "profiles"))
                    ? Directory.EnumerateFiles(Path.Combine(Folder, "profiles"), "basic.ini", SearchOption.AllDirectories)
                    : []);
            return Parse(files.Select(File.ReadAllText));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new HashSet<Keys>();
        }
    }

    /// <summary>Every "key": "OBS_KEY_…" in these texts, as a key.</summary>
    public static HashSet<Keys> Parse(IEnumerable<string> texts)
    {
        var keys = new HashSet<Keys>();
        foreach (string text in texts)
            foreach (Match m in KeyName().Matches(text))
                if (ToKey(m.Groups[1].Value) is { } key)
                    keys.Add(key);
        return keys;
    }

    static Keys? ToKey(string name)
    {
        if (name.Length == 1 && char.IsAsciiLetterUpper(name[0]))
            return (Keys)name[0];
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
            return Keys.D0 + (name[0] - '0');
        if (name.StartsWith("NUM", StringComparison.Ordinal) && name.Length == 4 && char.IsAsciiDigit(name[3]))
            return Keys.NumPad0 + (name[3] - '0');
        return Enum.TryParse<Keys>(name, ignoreCase: true, out var key) ? key : null;
    }

    [GeneratedRegex(@"""key""\s*:\s*""OBS_KEY_([A-Z0-9_]+)""")]
    private static partial Regex KeyName();
}
