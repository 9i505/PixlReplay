using PixlReplay.App;

namespace PixlReplay.Tests;

/// <summary>The notes under the options and the editor's hints and explainer: one short line each, in every language.</summary>
public static class NoteLengthTests
{
    const int MaxNote = 60;

    static bool IsNote(string key) =>
        key.EndsWith("Tip", StringComparison.Ordinal) || key.Contains(".hint.", StringComparison.Ordinal)
        || key.StartsWith("editor.intro.", StringComparison.Ordinal) || key is "accounts.subtitle" or "editor.recaption.body";

    [Test]
    public static void Every_note_is_one_short_line_in_every_language()
    {
        var notes = Text.AllKeys().Where(IsNote).ToList();
        var tooLong = new List<string>();
        foreach (var language in Text.Languages)
            LanguageTests.WithLanguage(language.Code, () =>
            {
                foreach (string key in notes)
                    if (Text.Get(key).Length > MaxNote)
                        tooLong.Add($"{key} ({language.Code}, {Text.Get(key).Length})");
            });
        Assert.True(tooLong.Count == 0, "too long: " + string.Join(", ", tooLong));
    }
}
