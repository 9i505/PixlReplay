using PixlReplay.Settings;

namespace PixlReplay.App;

/// <summary>
/// PixlReplay's keys on a first install. The usual ones are NVIDIA's own defaults too, so for each action a short list is
/// tried in order and the first key no app holds is kept: a new user's keys all work without opening the settings.
/// </summary>
public static class HotkeyDefaults
{
    /// <summary>
    /// Per action, the default first, then the keys tried when it is taken: two keys each (easy to press), never F9,
    /// F10 or F11 (OBS's), and none that Windows, browsers or NVIDIA use. No key appears twice.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Choices = new Dictionary<string, string[]>
    {
        ["save"] = ["Alt+F10", "Alt+F8", "Ctrl+F8"],
        ["toggle"] = ["Alt+Shift+F10", "Alt+F6", "Ctrl+F6"],
        ["record"] = ["Alt+F9", "Alt+F7", "Ctrl+F7"],
        ["pauseRecord"] = ["Alt+Shift+F9", "Alt+F5", "Ctrl+F2"],
        ["screenshot"] = ["Alt+F12", "Ctrl+F12", "Ctrl+F3"],
    };

    /// <summary>
    /// A free key for <paramref name="action"/> from its choices: none that PixlReplay already uses (its own taken key
    /// included) and none another app holds. Null when every choice is taken.
    /// </summary>
    public static string? FreeChoice(string action, IEnumerable<string> inUse, Func<Hotkey, bool> isFree)
    {
        if (!Choices.TryGetValue(action, out var choices))
            return null;
        var used = inUse.Where(k => HotkeyParser.TryParse(k, out _)).Select(k => HotkeyParser.TryParse(k, out var h) ? h : default).ToHashSet();
        return choices.FirstOrDefault(c => HotkeyParser.TryParse(c, out var h) && !used.Contains(h) && isFree(h));
    }

    /// <summary>
    /// Sets each action to its first free choice (the default when all are taken). True when any key is not the
    /// default, so the user is told once.
    /// </summary>
    public static bool PickFree(HotkeySettings keys, Func<Hotkey, bool> isFree)
    {
        bool changed = false;
        foreach (var (action, choices) in Choices)
        {
            string picked = choices.FirstOrDefault(c => HotkeyParser.TryParse(c, out var h) && isFree(h)) ?? choices[0];
            changed |= picked != choices[0];
            switch (action)
            {
                case "save": keys.Save = picked; break;
                case "toggle": keys.ToggleBuffer = picked; break;
                case "record": keys.Record = picked; break;
                case "pauseRecord": keys.PauseRecord = picked; break;
                case "screenshot": keys.Screenshot = picked; break;
            }
        }
        return changed;
    }
}
