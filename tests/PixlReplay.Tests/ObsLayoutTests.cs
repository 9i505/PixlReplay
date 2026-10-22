using PixlReplay.Engine;

namespace PixlReplay.Tests;

/// <summary>The program in one folder: where libobs starts and where its plugins and their data are.</summary>
public static class ObsLayoutTests
{
    [Test]
    public static void Libobs_finds_its_data_two_levels_up_from_where_it_starts()
    {
        string start = ObsHost.LibobsWorkingDirectory(@"C:\K\current");
        Assert.Equal(@"C:\K\current\data\libobs", start, "libobs starts in data\\libobs");
        // libobs looks for its own files as ../../data/libobs/ from where it starts.
        Assert.Equal(start, Path.GetFullPath(Path.Combine(start, "..", "..", "data", "libobs")), "and finds them there");
        Assert.Equal((@"C:\K\current\obs-plugins\64bit\win-capture.dll", @"C:\K\current\data\obs-plugins\win-capture\"),
            ObsHost.ModulePaths(@"C:\K\current", "win-capture"), "a plugin and its data");
    }
}
