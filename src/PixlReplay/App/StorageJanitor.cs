namespace PixlReplay.App;

/// <summary>Keeps PixlReplay's own clips under a size limit by sending the oldest to the Recycle Bin.</summary>
public static class StorageJanitor
{
    /// <summary>Oldest-first paths to delete so the total drops under <paramref name="limitBytes"/>. Never the newest clip.</summary>
    public static List<string> SelectForDeletion(IEnumerable<(string Path, long Size, DateTime Time)> files, long limitBytes)
    {
        var victims = new List<string>();
        if (limitBytes <= 0)
            return victims;

        var ordered = files.OrderBy(f => f.Time).ToList();
        long total = ordered.Sum(f => f.Size);
        for (int i = 0; i < ordered.Count - 1 && total > limitBytes; i++)
        {
            victims.Add(ordered[i].Path);
            total -= ordered[i].Size;
        }
        return victims;
    }

    /// <summary>
    /// Only clips and screenshots listed in <paramref name="index"/> (saved by PixlReplay) count and can be removed; any
    /// other video or picture in the folder is left alone. Returns how many files were removed.
    /// </summary>
    public static int Enforce(string folder, long limitBytes, ClipIndex index, Action<string> recycle)
    {
        if (limitBytes <= 0 || !Directory.Exists(folder))
            return 0;

        var mine = index.Load();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var root = new DirectoryInfo(folder);
        var files = root.EnumerateFiles("*.mp4", options).Concat(root.EnumerateFiles("*.png", options))
            .Where(f => mine.Contains(f.FullName))
            .Select(f => (f.FullName, f.Length, f.LastWriteTime));
        int deleted = 0;
        foreach (string path in SelectForDeletion(files, limitBytes))
        {
            try
            {
                recycle(path);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
            }
        }
        index.Prune();
        return deleted;
    }

    public static int Enforce(string folder, int maxFolderGb, ClipIndex index) =>
        Enforce(folder, (long)maxFolderGb << 30, index, path => RecycleBin.Send(path, RecycleBin.BackgroundFlags));
}
