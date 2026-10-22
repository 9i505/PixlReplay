namespace PixlReplay.App;

/// <summary>Deletes clips chosen in the Clips page, to the Recycle Bin or permanently.</summary>
public static class ClipDeleter
{
    /// <summary>Returns how many files were removed; missing or locked files are skipped.</summary>
    public static int Delete(IEnumerable<string> paths, bool permanent, Action<string> recycle)
    {
        int deleted = 0;
        foreach (string path in paths)
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                if (permanent)
                    File.Delete(path);
                else
                    recycle(path);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
            }
        }
        return deleted;
    }

    /// <summary>"Move to Recycle Bin" chosen by the user; Windows asks first if the file is too big to recycle.</summary>
    public static void Recycle(string path) => RecycleBin.Send(path, RecycleBin.UserFlags);
}
