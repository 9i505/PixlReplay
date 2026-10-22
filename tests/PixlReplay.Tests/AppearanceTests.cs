using System.Windows.Media;
using PixlReplay.App;
using PixlReplay.Settings;
using PixlReplay.Ui;

namespace PixlReplay.Tests;

public static class AppearanceTests
{
    static LookSettings Look(string accent, string background, string text) => new() { Accent = accent, Background = background, Text = text };

    [Test]
    public static void Pixl_Replay_starts_with_its_own_theme_coral_on_graphite()
    {
        ThemeColors.Use(new LookSettings());
        Assert.Equal(0xFF5A64u, ThemeColors.Accent, "coral red, the play mark's color");
        Assert.Equal(0x1C1C21u, ThemeColors.Background, "graphite window");
        Assert.Equal(0xF2F2F5u, ThemeColors.Text, "near-white text");
    }

    [Test]
    public static void Settings_saved_with_a_theme_color_still_load()
    {
        var s = SettingsStore.Deserialize("""{"general":{"accent":"red","language":"en"}}""");
        Assert.Equal("en", s.General.Language, "the rest is read; the old color choice is ignored");
        Assert.Equal(LookSettings.DefaultAccent, s.Look.Accent, "and the look starts as Pixl Replay's");
    }

    [Test]
    public static void A_broken_look_is_mended_when_read()
    {
        var s = SettingsStore.Deserialize("""{"look":{"accent":"rose","background":"#12345","text":"#abcdef","size":9,"panels":-1}}""");
        Assert.Equal(LookSettings.DefaultAccent, s.Look.Accent, "not a color: the default");
        Assert.Equal(LookSettings.DefaultBackground, s.Look.Background, "five digits: the default");
        Assert.Equal("#ABCDEF", s.Look.Text, "a color is kept (upper case)");
        Assert.Equal(LookSettings.MaxSize, s.Look.Size, "size within reach");
        Assert.True(s.Look.Panels >= 0.2, "panels never vanish");
    }

    [Test]
    public static void Every_ready_theme_stays_readable()
    {
        foreach (var (key, accent, background, text) in SettingsViewModel.ThemePresets)
        {
            ThemeColors.Use(Look(accent, background, text));
            Assert.True(ThemeColors.Contrast(ThemeColors.Text, ThemeColors.Surface) >= 7, $"{key}: text on the cards");
            Assert.True(ThemeColors.Contrast(ThemeColors.TextSecondary, ThemeColors.Surface) >= 4.5, $"{key}: secondary text on the cards");
            Assert.True(ThemeColors.Contrast(ThemeColors.Accent, ThemeColors.OnAccent) >= 4.5,
                $"{key}: text on the accent {ThemeColors.Contrast(ThemeColors.Accent, ThemeColors.OnAccent):F1}:1");
        }
        ThemeColors.Use(new LookSettings());
    }

    [Test]
    public static void Text_on_the_accent_takes_whichever_reads_better()
    {
        ThemeColors.Use(Look("#FFC93C", "#16140F", "#F5F0E4"));
        Assert.True(ThemeColors.OnAccent != 0xFFFFFF, "dark on yellow");
        ThemeColors.Use(Look("#3A1FB0", "#16140F", "#F5F0E4"));
        Assert.Equal(0xFFFFFFu, ThemeColors.OnAccent, "white on deep blue");
        ThemeColors.Use(new LookSettings());
    }

    [Test]
    public static void Hover_is_lighter_and_the_soft_tint_stays_a_tint_of_the_background()
    {
        ThemeColors.Use(new LookSettings());
        Assert.True(ThemeColors.Luminance(ThemeColors.Hover) > ThemeColors.Luminance(ThemeColors.Accent), "hover");
        Assert.True(ThemeColors.Contrast(ThemeColors.Soft, ThemeColors.Background) < 1.6, "soft is a tint, not a fill");
        Assert.True(ThemeColors.Contrast(ThemeColors.Soft, ThemeColors.Text) >= 7, "text stays easy to read on it");
    }

    [Test]
    public static void Theme_resources_take_the_look_and_fluent_controls_follow_it()
    {
        var r = new System.Windows.ResourceDictionary();
        ThemeAccent.Apply(r, Look("#3DDC84", "#141D18", "#E9F2EC"));
        Assert.Equal(Color.FromRgb(0x3D, 0xDC, 0x84), ((SolidColorBrush)r["Accent"]).Color, "accent");
        Assert.Equal(Color.FromRgb(0x3D, 0xDC, 0x84), (Color)r["SystemAccentColor"], "sliders and switches, not the Windows accent");
        Assert.Equal(Color.FromRgb(0x14, 0x1D, 0x18), ((SolidColorBrush)r["WindowBg"]).Color, "the window");
        Assert.Equal(Color.FromRgb(0xE9, 0xF2, 0xEC), ((SolidColorBrush)r["TextPrimary"]).Color, "the text");
        Assert.Equal(Color.FromRgb(0xE9, 0xF2, 0xEC), ((SolidColorBrush)r["TextFillColorPrimaryBrush"]).Color, "Fluent's text too");
        Assert.True(r.Contains("AccentHover") && r.Contains("AccentSoft"), "hover and soft");
        Assert.Equal((byte)255, ((SolidColorBrush)r["Surface"]).Color.A, "solid cards without a picture");
        ThemeAccent.Apply(r, new LookSettings());
    }

    [Test]
    public static void Over_a_picture_the_panels_show_it_and_pop_ups_stay_solid()
    {
        string picture = Path.Combine(Path.GetTempPath(), "pixlreplay-look-" + Guid.NewGuid().ToString("N") + ".png");
        using (var bmp = new System.Drawing.Bitmap(8, 8))
            bmp.Save(picture, System.Drawing.Imaging.ImageFormat.Png);
        try
        {
            var r = new System.Windows.ResourceDictionary();
            ThemeAccent.Apply(r, new LookSettings { Picture = picture, Panels = 0.6 });
            Assert.Equal((byte)153, ((SolidColorBrush)r["Surface"]).Color.A, "cards at 60%");
            Assert.Equal((byte)153, ((SolidColorBrush)r["NavBg"]).Color.A, "the side bar too");
            Assert.Equal((byte)255, ((SolidColorBrush)r["FlyoutBackground"]).Color.A, "pop-ups stay readable");
            Assert.Equal((byte)255, ((SolidColorBrush)r["WindowBg"]).Color.A, "the window under the picture");
            Assert.True(ThemeAccent.Picture(ThemeAccent.Current) != null, "the picture is read");
            ThemeAccent.Apply(r, new LookSettings());
        }
        finally
        {
            File.Delete(picture);
        }
    }

    [Test]
    public static void The_font_choice_names_a_real_font()
    {
        Assert.Equal(ThemeAccent.DefaultFont, ThemeAccent.FontFor(""), "default");
        Assert.True(ThemeAccent.FontFor("app:cairo").Source.Contains("Cairo"), "a font shipped beside the app");
        Assert.True(ThemeAccent.FontFor("Arial").Source.StartsWith("Arial"), "an installed font");
    }

    [Test]
    public static void Tray_icon_uses_the_theme_color()
    {
        ThemeColors.Use(Look("#4FC3F7", "#121826", "#E8EEF8"));
        Assert.Equal(0x4FC3F7u, TrayController.ColorFor("active"), "active");
        Assert.Equal(0x4FC3F7u, TrayController.ColorFor("recording"), "recording");
        Assert.Equal(0x2ED47Au, TrayController.ColorFor("saved"), "saved stays green");
        Assert.Equal(0xFFB020u, TrayController.ColorFor("error"), "error stays amber");
        ThemeColors.Use(new LookSettings());
    }

    [Test]
    public static void Appearance_language_sound_and_hotkeys_keep_the_buffer()
    {
        var before = new AppSettings();
        var after = SettingsStore.Deserialize(SettingsStore.Serialize(before));
        after.General.Language = "en";
        after.General.SoundOnSave = false;
        after.General.ToastOnSave = false;
        after.General.StartWithWindows = false;
        after.Hotkeys.Save = "Ctrl+F8";
        after.Look.Accent = "#3DDC84";
        after.Look.Size = 1.2;
        Assert.Equal(false, SettingsDiff.NeedsRebuild(before, after), "no restart");
    }

    [Test]
    public static void Capture_and_output_changes_restart_the_buffer()
    {
        var before = new AppSettings();
        AppSettings Changed(Action<AppSettings> change)
        {
            var s = SettingsStore.Deserialize(SettingsStore.Serialize(before));
            change(s);
            return s;
        }
        Assert.Equal(true, SettingsDiff.NeedsRebuild(before, Changed(s => s.Video.Fps = 30)), "fps");
        Assert.Equal(true, SettingsDiff.NeedsRebuild(before, Changed(s => s.Replay.Seconds = 120)), "length");
        Assert.Equal(true, SettingsDiff.NeedsRebuild(before, Changed(s => s.Audio.Mic = false)), "mic");
        Assert.Equal(true, SettingsDiff.NeedsRebuild(before, Changed(s => s.Output.Folder = @"D:\Other")), "folder");
        Assert.Equal(true, SettingsDiff.NeedsRebuild(before, Changed(s => s.General.OnlyWhenGaming = !s.General.OnlyWhenGaming)), "games only");
    }
}
