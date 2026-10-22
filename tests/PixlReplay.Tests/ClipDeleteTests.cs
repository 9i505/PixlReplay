using PixlReplay.App;

namespace PixlReplay.Tests;

public static class ClipDeleteTests
{
    static List<string> MakeFiles(int n)
    {
        string dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pixlreplay-tests", Guid.NewGuid().ToString("N"))).FullName;
        return Enumerable.Range(0, n).Select(i =>
        {
            string p = Path.Combine(dir, $"clip{i}.mp4");
            File.WriteAllText(p, "x");
            return p;
        }).ToList();
    }

    [Test]
    public static void Permanent_delete_removes_every_selected_file()
    {
        var files = MakeFiles(3);
        int deleted = ClipDeleter.Delete(files, permanent: true, recycle: _ => throw new Exception("must not recycle"));
        Assert.Equal(3, deleted, "count");
        Assert.True(files.All(f => !File.Exists(f)), "all gone");
    }

    [Test]
    public static void Recycle_mode_hands_each_file_to_the_recycle_bin()
    {
        var files = MakeFiles(2);
        var recycled = new List<string>();
        int deleted = ClipDeleter.Delete(files, permanent: false, recycle: recycled.Add);
        Assert.Equal(2, deleted, "count");
        Assert.Equal(2, recycled.Count, "recycled");
        Assert.True(files.All(File.Exists), "files left to the recycle bin delegate");
    }

    [Test]
    public static void Missing_or_locked_files_are_skipped_not_fatal()
    {
        var files = MakeFiles(2);
        File.Delete(files[0]);
        using var locked = new FileStream(files[1], FileMode.Open, FileAccess.Read, FileShare.None);
        int deleted = ClipDeleter.Delete(files, permanent: true, recycle: _ => { });
        Assert.Equal(0, deleted, "nothing deleted");
    }
}
