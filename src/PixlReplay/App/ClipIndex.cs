using System.Text;

namespace PixlReplay.App;

/// <summary>
/// The clips PixlReplay itself saved, one full path per line, so the folder size limit only ever removes
/// PixlReplay's own clips and never other videos the user keeps in the same folder.
/// </summary>
public sealed class ClipIndex(string path)
{
    static readonly Encoding Utf8 = new UTF8Encoding(false);
    readonly object _gate = new();

    public static string DefaultPath => Path.Combine(Settings.SettingsStore.AppDataDir, "clips.txt");

    public void Add(string clip)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, Path.GetFullPath(clip) + Environment.NewLine, Utf8);
        }
    }

    /// <summary>Full paths, compared case-insensitively like Windows does.</summary>
    public HashSet<string> Load()
    {
        lock (_gate)
            return File.Exists(path)
                ? new HashSet<string>(File.ReadAllLines(path, Utf8).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Forgets clips that no longer exist (deleted, moved or recycled).</summary>
    public void Prune()
    {
        lock (_gate)
        {
            if (!File.Exists(path))
                return;
            var keep = File.ReadAllLines(path, Utf8).Where(l => l.Length > 0 && File.Exists(l))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string tmp = path + ".tmp";
            File.WriteAllLines(tmp, keep, Utf8);
            File.Move(tmp, path, overwrite: true);
        }
    }
}
