using System.Windows.Forms;
using PixlReplay.App;
using PixlReplay.Engine;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

/// <summary>Keys that keep working in games (off NVIDIA's shortcuts) and Smart Save hearing a real microphone.</summary>
public static class HotkeyVoiceTests
{
    static readonly HashSet<Hotkey> Nvidia =
    [
        new(Keys.F10, false, true, false, false), // save replay
        new(Keys.F9, false, true, false, false), // record
        new(Keys.F1, false, true, false, false), // screenshot
    ];

    [Test]
    public static void Keys_on_nvidias_shortcuts_move_to_free_ones()
    {
        var keys = new HotkeySettings { PauseRecord = "Alt+Shift+F9" };
        bool moved = HotkeyDefaults.AvoidNvidia(keys, Nvidia, _ => true);
        Assert.Equal(true, moved, "moved");
        Assert.Equal("Alt+F8", keys.Save, "save");
        Assert.Equal("Alt+F7", keys.Record, "record");
        Assert.Equal("Alt+Shift+F10", keys.ToggleBuffer, "not NVIDIA's: kept");
        Assert.Equal("Alt+F12", keys.Screenshot, "kept");
    }

    [Test]
    public static void Without_nvidia_nothing_moves()
    {
        var keys = new HotkeySettings();
        Assert.Equal(false, HotkeyDefaults.AvoidNvidia(keys, new HashSet<Hotkey>(), _ => true), "no NVIDIA app");
        Assert.Equal("Alt+F10", keys.Save, "save stays");
    }

    [Test]
    public static void A_first_install_never_takes_nvidias_keys()
    {
        var keys = new HotkeySettings();
        HotkeyDefaults.PickFree(keys, h => HotkeyDefaults.Usable(h, Nvidia, _ => true));
        Assert.Equal("Alt+F8", keys.Save, "save");
        Assert.Equal("Alt+F7", keys.Record, "record");
    }

    static readonly List<(string Name, string Id)> Mics =
    [
        ("Default", "default"),
        ("Microphone (Realtek(R) Audio)", "realtek"),
        ("Microphone Array (Intel® Smart Sound Technology for Digital Microphones)", "array"),
        ("Microphone (fifine Microphone)", "fifine"),
        ("SteelSeries Sonar - Microphone (SteelSeries Sonar Virtual Audio Device)", "sonar"),
        ("What U Hear (VB-Audio Virtual Cable)", "cable"),
    ];

    [Test]
    public static void Smart_save_hears_a_real_microphone_when_the_recording_takes_a_virtual_one()
    {
        Assert.Equal("fifine", VoiceMic.Pick("", "cable", Mics, null), "a plugged-in microphone first");
        Assert.Equal("fifine", VoiceMic.Pick("", "default", Mics, "What U Hear (VB-Audio Virtual Cable)"), "Windows' default is the cable");
        Assert.Equal("realtek", VoiceMic.Pick("", "realtek", Mics, null), "a real recording microphone is heard as it is");
        Assert.Equal("array", VoiceMic.Pick("array", "cable", Mics, null), "the user's choice");
        Assert.Equal("fifine", VoiceMic.Pick("gone", "cable", Mics, null), "a choice no longer plugged in");
        Assert.True(VoiceMic.IsVirtual("CABLE Output (VB-Audio Virtual Cable)") && VoiceMic.IsVirtual("Voicemeeter Out B1"), "virtual");
        Assert.True(!VoiceMic.IsVirtual("Microphone (fifine Microphone)"), "real");
    }

    [Test]
    public static void Changing_smart_saves_microphone_rebuilds_the_capture()
    {
        var before = new AppSettings();
        var after = SettingsStore.Deserialize(SettingsStore.Serialize(before));
        after.Voice.MicDevice = "fifine";
        Assert.Equal(true, SettingsDiff.NeedsRebuild(before, after), "its own microphone source");
    }
}
