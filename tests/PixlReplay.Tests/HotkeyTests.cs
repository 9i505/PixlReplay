using System.Windows.Forms;
using PixlReplay.App;

namespace PixlReplay.Tests;

public static class HotkeyTests
{
    [Test]
    public static void Parses_lowercase_and_formats_canonically()
    {
        Assert.True(HotkeyParser.TryParse("ctrl+f8", out var h), "should parse");
        Assert.Equal(Keys.F8, h.Key, "key");
        Assert.Equal(true, h.Ctrl, "ctrl");
        Assert.Equal("Ctrl+F8", HotkeyParser.Format(h), "format");
    }

    [Test]
    public static void Multi_modifier_round_trips()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Shift+F8", out var h), "should parse");
        Assert.Equal("Ctrl+Shift+F8", HotkeyParser.Format(h), "format");
        Assert.True(HotkeyParser.TryParse("shift+alt+win+ctrl+S", out var h2), "should parse");
        Assert.Equal("Ctrl+Alt+Shift+Win+S", HotkeyParser.Format(h2), "canonical order");
    }

    [Test]
    public static void Digits_format_without_d_prefix()
    {
        Assert.True(HotkeyParser.TryParse("Alt+1", out var h), "should parse");
        Assert.Equal(Keys.D1, h.Key, "key");
        Assert.Equal("Alt+1", HotkeyParser.Format(h), "format");
    }

    [Test]
    public static void Invalid_strings_are_rejected()
    {
        foreach (string s in new[] { "", "   ", "Ctrl+", "Foo", "Ctrl+Shift", "Ctrl+ControlKey", "Ctrl+F8+F9" })
            Assert.True(!HotkeyParser.TryParse(s, out _), $"'{s}' should not parse");
    }

    static App.Hotkey Hk(string s)
    {
        HotkeyParser.TryParse(s, out var h);
        return h;
    }

    [Test]
    public static void Combo_taken_by_another_owner_reports_failure()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+F23", out var h), "should parse");
        using var first = new HotkeyManager();
        using var second = new HotkeyManager();
        Assert.True(first.Register("save", h), "first owner registers");
        Assert.True(!second.Register("save", h), "second owner must fail");
        Assert.Equal(false, second.Status["save"], "status records failure");
        Assert.Equal(true, first.Status["save"], "first status ok");
    }

    [Test]
    public static void A_taken_key_is_picked_up_once_the_other_app_lets_go()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+F22", out var h), "should parse");
        using var pixlreplay = new HotkeyManager();
        var other = new HotkeyManager();
        Assert.True(other.Register("x", h), "the other app holds it");
        Assert.True(!pixlreplay.Register("save", h), "taken");
        pixlreplay.RetryPending();
        Assert.Equal(false, pixlreplay.Status["save"], "still taken");
        other.Dispose();
        pixlreplay.RetryPending();
        Assert.Equal(true, pixlreplay.Status["save"], "free now: PixlReplay takes it");
    }

    [Test]
    public static void A_key_that_was_let_go_is_not_retried()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+F21", out var h), "should parse");
        using var pixlreplay = new HotkeyManager();
        var other = new HotkeyManager();
        Assert.True(other.Register("x", h), "the other app holds it");
        Assert.True(!pixlreplay.Register("save", h), "taken");
        pixlreplay.UnregisterAll(); // the user picked other keys
        other.Dispose();
        pixlreplay.RetryPending();
        Assert.True(!pixlreplay.Status.ContainsKey("save"), "the old key stays let go");
    }

    [Test]
    public static void A_key_is_free_only_when_no_app_holds_it()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+F20", out var h), "should parse");
        Assert.True(HotkeyManager.IsFree(h), "nobody has it");
        var other = new HotkeyManager();
        other.Register("x", h);
        Assert.True(!HotkeyManager.IsFree(h), "another app holds it");
        other.Dispose();
        Assert.True(HotkeyManager.IsFree(h), "and checking took nothing: still free");
    }

    [Test]
    public static void Typing_a_key_releases_pixlreplay_keys_until_done()
    {
        // While a hotkey box has focus, PixlReplay's own keys reach the box (and are not run), and the free check is accurate.
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+F19", out var h), "should parse");
        using var pixlreplay = new HotkeyManager();
        Assert.True(pixlreplay.Register("save", h), "registered");
        Assert.True(!HotkeyManager.IsFree(h), "PixlReplay holds it");
        pixlreplay.Suspend();
        Assert.True(pixlreplay.Suspended && HotkeyManager.IsFree(h), "typing: let go");
        pixlreplay.Resume();
        Assert.True(!pixlreplay.Suspended && !HotkeyManager.IsFree(h), "done: PixlReplay's again");
        Assert.Equal(true, pixlreplay.Status["save"], "status");
    }

    [Test]
    public static void A_key_saved_while_typing_is_taken_when_typing_ends()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+Shift+F18", out var h), "should parse");
        using var pixlreplay = new HotkeyManager();
        pixlreplay.Suspend();
        pixlreplay.Register("save", h); // settings applied while the box still had focus
        Assert.True(HotkeyManager.IsFree(h), "not while typing");
        pixlreplay.Resume();
        Assert.True(!HotkeyManager.IsFree(h), "then PixlReplay's");
    }

    [Test]
    public static void A_taken_key_is_told_once_ever()
    {
        string path = Path.Combine(Path.GetTempPath(), "pixlreplay-taken-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            var notice = new TakenKeyNotice(path);
            Assert.True(notice.ShouldTell("Alt+F10"), "the first time");
            Assert.True(!notice.ShouldTell("Alt+F10"), "not again");
            Assert.True(!new TakenKeyNotice(path).ShouldTell("Alt+F10"), "nor after a restart");
            Assert.True(notice.ShouldTell("Alt+F9"), "another key: once too");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public static void The_taken_message_names_one_key_or_counts_several()
    {
        Text.Language = "en";
        Assert.Equal(("Alt+F10 doesn't work", "Another app has it. Change it in Settings"), EngineApp.TakenToast(["Alt+F10"]), "one");
        Assert.Equal(("3 hotkeys don't work", "Alt+F10, Alt+Shift+F10, Alt+F9"), EngineApp.TakenToast(["Alt+F10", "Alt+Shift+F10", "Alt+F9"]), "three");
        Text.Language = "ar";
        Assert.Equal("تعذّر تشغيل 3 اختصارات", EngineApp.TakenToast(["Alt+F10", "Alt+Shift+F10", "Alt+F9"]).Title, "arabic");
        Assert.Equal("تعذّر تشغيل اختصارين", EngineApp.TakenToast(["Alt+F10", "Alt+F9"]).Title, "arabic, two");
        Text.Language = "en";
    }

    static Func<App.Hotkey, bool> FreeExcept(params string[] taken)
    {
        var set = taken.Select(Hk).ToHashSet();
        return h => !set.Contains(h);
    }

    static string KeysOf(PixlReplay.Settings.HotkeySettings k) => $"{k.Save} | {k.ToggleBuffer} | {k.Record} | {k.PauseRecord} | {k.Screenshot}";

    [Test]
    public static void A_first_install_keeps_the_usual_keys_when_they_are_free()
    {
        var keys = new PixlReplay.Settings.HotkeySettings();
        Assert.True(!HotkeyDefaults.PickFree(keys, _ => true), "nothing changed");
        Assert.Equal("Alt+F10 | Alt+Shift+F10 | Alt+F9 | Alt+Shift+F9 | Alt+F12", KeysOf(keys), "the usual keys");
    }

    [Test]
    public static void A_first_install_next_to_nvidia_gets_free_keys()
    {
        var keys = new PixlReplay.Settings.HotkeySettings();
        Assert.True(HotkeyDefaults.PickFree(keys, FreeExcept("Alt+F10", "Alt+Shift+F10", "Alt+F9")), "changed");
        Assert.Equal("Alt+F8 | Alt+F6 | Alt+F7 | Alt+Shift+F9 | Alt+F12", KeysOf(keys), "the next free ones");
    }

    [Test]
    public static void A_first_install_tries_the_second_choice_too()
    {
        var keys = new PixlReplay.Settings.HotkeySettings();
        HotkeyDefaults.PickFree(keys, FreeExcept("Alt+F10", "Alt+F8"));
        Assert.Equal("Ctrl+F8", keys.Save, "third in the list");
        var none = new PixlReplay.Settings.HotkeySettings();
        HotkeyDefaults.PickFree(none, FreeExcept("Alt+F12", "Ctrl+F12", "Ctrl+F3"));
        Assert.Equal("Alt+F12", none.Screenshot, "all taken: the usual key stays (and the one-time message says so)");
    }

    [Test]
    public static void The_choices_start_with_the_defaults_and_never_repeat()
    {
        var d = new PixlReplay.Settings.HotkeySettings();
        Assert.Equal($"{d.Save} | {d.ToggleBuffer} | {d.Record} | {d.PauseRecord ?? HotkeyParser.WithShift(d.Record)} | {d.Screenshot}",
            string.Join(" | ", HotkeyDefaults.Choices.Values.Select(c => c[0])), "the first choice is the default");
        var all = HotkeyDefaults.Choices.Values.SelectMany(c => c).ToList();
        Assert.Equal(all.Count, all.Distinct().Count(), "no key is a choice for two actions");
        Assert.True(all.All(k => HotkeyParser.TryParse(k, out _)), "all valid");
    }

    [Test]
    public static void PixlReplay_own_window_is_told_apart_from_a_game()
    {
        Assert.True(ForegroundApp.IsSameApp(@"D:\PixlReplay\bin\64bit\PixlReplay.exe", @"d:\pixlreplay\BIN\64bit\pixlreplay.exe"), "the settings window: same exe");
        Assert.True(!ForegroundApp.IsSameApp(@"C:\Games\Game.exe", @"D:\PixlReplay\bin\64bit\PixlReplay.exe"), "a game");
        Assert.True(!ForegroundApp.IsSameApp(null, @"D:\PixlReplay\bin\64bit\PixlReplay.exe"), "unknown");
    }
}
