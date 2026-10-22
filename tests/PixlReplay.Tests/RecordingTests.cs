using PixlReplay.App;
using PixlReplay.Engine;
using PixlReplay.Settings;
using PixlReplay.Ui;

namespace PixlReplay.Tests;

/// <summary>Full recordings: start, pause, resume and stop with hotkeys, and PixlReplay as a plain recorder (instant replay off).</summary>
public static class RecordingTests
{
    static readonly DateTime T0 = new(2026, 10, 14, 20, 0, 0, DateTimeKind.Utc);

    static StatusDto Status(bool recording = false, bool recordingPaused = false, double seconds = 0, long bytes = 0,
        bool bufferActive = true, bool pausedByUser = false) =>
        new(new EngineStatus(bufferActive, recording, 60, 0, 0, 0, 300, 300, "obs_nvenc_av1_tex", 2560, 1440, 700, 600,
                @"D:\Clips", false, null, recordingPaused, seconds, bytes),
            [], null, null, pausedByUser, false, "1.17.0");

    [Test]
    public static void The_recording_time_leaves_out_the_pauses()
    {
        var clock = new RecordingClock();
        clock.Start(T0);
        Assert.Near(3, clock.Seconds(T0.AddSeconds(3)), 0.001, "running");
        clock.Pause(T0.AddSeconds(3));
        Assert.True(clock.Paused, "paused");
        Assert.Near(3, clock.Seconds(T0.AddSeconds(4)), 0.001, "stands still while paused");
        clock.Resume(T0.AddSeconds(4));
        Assert.Near(5, clock.Seconds(T0.AddSeconds(6)), 0.001, "3 s + 2 s, without the 1 s pause");
        clock.Start(T0.AddSeconds(100));
        Assert.Near(0, clock.Seconds(T0.AddSeconds(100)), 0.001, "a new recording starts from zero");
    }

    [Test]
    public static void The_pause_hotkey_is_the_recording_hotkey_with_shift_unless_set()
    {
        var fresh = new AppSettings();
        fresh.Normalize();
        Assert.Equal("Alt+Shift+F9", fresh.Hotkeys.PauseRecord, "default, next to Alt+F9");
        var mine = SettingsStore.Deserialize("""{"hotkeys":{"save":"Ctrl+F8","toggleBuffer":"Ctrl+Shift+F8","record":"Ctrl+F7"}}""");
        Assert.Equal("Ctrl+Shift+F7", mine.Hotkeys.PauseRecord, "settings from before: next to the user's Ctrl+F7");
        var cleared = SettingsStore.Deserialize("""{"hotkeys":{"record":"Ctrl+F7","pauseRecord":""}}""");
        Assert.Equal("", cleared.Hotkeys.PauseRecord, "cleared by the user: stays cleared");
        Assert.Equal("", HotkeyParser.WithShift("Ctrl+Shift+F7"), "already with Shift: no default");
        Assert.Equal("", HotkeyParser.WithShift(""), "no recording hotkey: none");
    }

    [Test]
    public static void Turning_instant_replay_off_or_on_keeps_a_recording_going()
    {
        var before = new AppSettings();
        var after = SettingsStore.Deserialize(SettingsStore.Serialize(before));
        after.Replay.Enabled = false;
        Assert.True(!SettingsDiff.NeedsRebuild(before, after), "no rebuild: the recording would stop");
    }

    [Test]
    public static void Instant_replay_and_the_recording_each_have_their_own_state()
    {
        Assert.Equal("active", StatusLogic.ReplayState(Status(), replayEnabled: true), "instant replay on");
        Assert.Equal("active", StatusLogic.ReplayState(Status(recording: true), replayEnabled: true), "a recording does not change it");
        Assert.Equal("paused", StatusLogic.ReplayState(Status(bufferActive: false, pausedByUser: true), replayEnabled: true), "paused");
        Assert.Equal("off", StatusLogic.ReplayState(Status(bufferActive: false), replayEnabled: false), "turned off: a plain recorder");
        Assert.Equal("off", StatusLogic.ReplayState(Status(recording: true, bufferActive: false), replayEnabled: false), "off while recording");
        Assert.Equal("offline", StatusLogic.ReplayState(null, replayEnabled: true), "engine not running");

        Assert.Equal("idle", StatusLogic.RecordState(Status()), "not recording");
        Assert.Equal("recording", StatusLogic.RecordState(Status(recording: true)), "recording");
        Assert.Equal("paused", StatusLogic.RecordState(Status(recording: true, recordingPaused: true)), "recording paused");
        Assert.Equal("idle", StatusLogic.RecordState(null), "engine not running");
    }

    [Test]
    public static void The_recording_card_shows_its_time_and_size()
    {
        Text.Language = "en";
        Assert.Equal("12:34 • 1.2 GB", StatusLogic.RecordingDetail(754, 1_300_000_000), "time and size");
        Assert.Equal("1:02:05 • 850 MB", StatusLogic.RecordingDetail(3725, 891_289_600), "over an hour");
        Assert.Equal("0:00 • 0 MB", StatusLogic.RecordingDetail(0, 0), "just started");
    }

    [Test]
    public static void Recording_is_start_and_stop_and_the_background_one_is_instant_replay()
    {
        Text.Language = "en";
        Assert.Equal(("Start recording", "Stop", "Pause", "Resume"),
            (Text.Get("status.record"), Text.Get("status.stopRecord"), Text.Get("status.pauseRecord"), Text.Get("status.resumeRecord")), "recording card");
        Assert.Equal(("Instant replay is on", "Instant replay is off"), (Text.Get("status.active"), Text.Get("status.off")), "instant replay card");
        Assert.Equal(("Start / stop recording", "Pause / resume instant replay"), (Text.Get("hotkeys.record"), Text.Get("hotkeys.toggle")), "hotkeys");
        Text.Language = "ar";
        Assert.Equal(("بدء التسجيل", "إيقاف", "إيقاف مؤقت", "استئناف"),
            (Text.Get("status.record"), Text.Get("status.stopRecord"), Text.Get("status.pauseRecord"), Text.Get("status.resumeRecord")), "بطاقة التسجيل");
        Assert.Equal(("الإعادة الفورية تعمل", "الإعادة الفورية"), (Text.Get("status.active"), Text.Get("capture.replay")), "الإعادة الفورية");
        foreach (string key in Text.AllKeys())
            Assert.True(!Text.Get(key).Contains("full recording", StringComparison.OrdinalIgnoreCase) && !Text.Get(key).Contains("تسجيل كامل")
                        && !Text.Get(key).Contains("التسجيل الكامل"), key);
    }

    [Test]
    public static void Only_a_problem_or_instant_replay_off_takes_the_line_under_the_title()
    {
        Text.Language = "en";
        Assert.Equal("", StatusLogic.ReplayDetail("active", null), "on: the hotkey sits in Save clip instead");
        Assert.Equal("", StatusLogic.ReplayDetail("waiting", null), "waiting for a game");
        Assert.Equal("Turn it on in Capture", StatusLogic.ReplayDetail("off", null), "instant replay off");
        Assert.Equal("No GPU", StatusLogic.ReplayDetail("error", "No GPU"), "the problem");
        Assert.Equal("", StatusLogic.ReplayDetail("offline", null), "engine not running");
    }

    [Test]
    public static void The_tray_menu_changes_when_the_recording_is_paused()
    {
        string running = TrayController.MenuKey(TrayState.Recording, false, 30, paused: false, recording: true, "ar");
        string paused = TrayController.MenuKey(TrayState.Recording, false, 30, paused: false, recording: true, "ar", recordingPaused: true);
        Assert.True(running != paused, "pause / resume item");
    }
}
