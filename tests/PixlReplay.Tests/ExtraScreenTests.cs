using PixlReplay.App;
using PixlReplay.Engine;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

/// <summary>More screens recorded at once: the setting, the screens' numbers and each screen's file name.</summary>
public static class ExtraScreenTests
{
    static readonly List<MonitorInfo> Two =
    [
        new(@"\\?\DISPLAY#A", "A: 1920x1080 @ 0,0 (Primary Monitor)", 1920, 1080, true),
        new(@"\\?\DISPLAY#B", "B: 2560x1600 @ -2560,4", 2560, 1600, false),
    ];

    [Test]
    public static void Screens_are_numbered_as_the_capture_page_lists_them()
    {
        Assert.Equal(1, CapturePipeline.ScreenNumber(Two, @"\\?\DISPLAY#A"), "first");
        Assert.Equal(2, CapturePipeline.ScreenNumber(Two, @"\\?\DISPLAY#B"), "second");
        Assert.Equal(0, CapturePipeline.ScreenNumber(Two, "gone"), "a screen no longer there");
    }

    [Test]
    public static void Each_screens_file_says_which_screen_it_is()
    {
        Text.Language = "en";
        Assert.Equal(@"D:\Clips\Game\Game 2026-10-22 13-20-00 - Screen 2.mp4",
            ClipOrganizer.ForScreen(@"D:\Clips\Game\Game 2026-10-22 13-20-00.mp4", 2), "english");
        Text.Language = "ar";
        Assert.Equal(@"D:\Clips\Game 2026-10-22 13-20-00 - الشاشة 1.mp4",
            ClipOrganizer.ForScreen(@"D:\Clips\Game 2026-10-22 13-20-00.mp4", 1), "arabic");
        Text.Language = "en";
    }

    [Test]
    public static void The_extra_screens_setting_is_kept_clean()
    {
        var s = SettingsStore.Deserialize("""{"capture":{"extraMonitors":["a","a","","b","c","d"]}}""");
        Assert.Equal(CapturePipeline.MaxExtraScreens, s.Capture.ExtraMonitors.Count, "at most three");
        Assert.Equal("a,b,c", string.Join(",", s.Capture.ExtraMonitors), "no repeats, no blanks, in order");
        Assert.Equal(0, SettingsStore.Deserialize("{}").Capture.ExtraMonitors.Count, "none by default");
    }

    [Test]
    public static void Choosing_screens_rebuilds_the_capture()
    {
        var before = new AppSettings();
        var after = SettingsStore.Deserialize(SettingsStore.Serialize(before));
        after.Capture.ExtraMonitors.Add(@"\\?\DISPLAY#B");
        Assert.Equal(true, SettingsDiff.NeedsRebuild(before, after), "a new screen needs its own capture");
    }
}
