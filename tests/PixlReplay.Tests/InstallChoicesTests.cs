using PixlReplay.App;
using PixlReplay.Settings;

namespace PixlReplay.Tests;

/// <summary>The install wizard's choices (language, start with Windows) reach PixlReplay once, on its next start.</summary>
public static class InstallChoicesTests
{
    [Test]
    public static void Unknown_or_broken_lines_change_nothing()
    {
        var settings = new AppSettings();
        settings.General.Language = "ar";
        InstallChoices.Apply("language=xx\nstartWithWindows=maybe\n# a note\nsomething=1\n=\n", settings);
        Assert.Equal("ar", settings.General.Language, "a language PixlReplay does not have is ignored");
        Assert.Equal(true, settings.General.StartWithWindows, "not a yes or no: left as it was");
    }

    [Test]
    public static void The_choices_are_applied_once_and_their_file_goes()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pixlreplay-choices-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, InstallChoices.FileName), "language=pt-BR\r\nstartWithWindows=false\r\n");
            var settings = new AppSettings();
            settings.General.Language = "en";
            Assert.True(InstallChoices.ConsumeInto(settings, dir), "there was a wizard file");
            Assert.Equal("pt-BR", settings.General.Language, "its language");
            Assert.Equal(false, settings.General.StartWithWindows, "start with Windows was unticked");
            Assert.True(!File.Exists(Path.Combine(dir, InstallChoices.FileName)), "the file is gone after use");
            settings.General.Language = "es";
            Assert.True(!InstallChoices.ConsumeInto(settings, dir), "the next start finds nothing");
            Assert.Equal("es", settings.General.Language, "and changes nothing");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
