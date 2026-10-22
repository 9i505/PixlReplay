namespace PixlReplay.App;

public static class ShellPaths
{
    /// <summary>Explorer from the Windows folder, never an explorer.exe found earlier on the search path (e.g. next to PixlReplay.exe).</summary>
    public static string Explorer => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
}
