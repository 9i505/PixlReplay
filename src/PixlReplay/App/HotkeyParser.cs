using System.Windows.Forms;

namespace PixlReplay.App;

public readonly record struct Hotkey(Keys Key, bool Ctrl, bool Alt, bool Shift, bool Win);

public static class HotkeyParser
{
    static readonly HashSet<Keys> ModifierKeys =
    [
        Keys.ControlKey, Keys.LControlKey, Keys.RControlKey, Keys.Control,
        Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, Keys.Shift,
        Keys.Menu, Keys.LMenu, Keys.RMenu, Keys.Alt,
        Keys.LWin, Keys.RWin, Keys.None,
    ];

    public static bool TryParse(string? s, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        Keys? key = null;
        foreach (string raw in s.Split('+'))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; continue;
                case "alt": alt = true; continue;
                case "shift": shift = true; continue;
                case "win" or "windows": win = true; continue;
            }

            if (key != null || !TryParseKey(part, out Keys k))
                return false;
            key = k;
        }

        if (key == null)
            return false;
        hotkey = new Hotkey(key.Value, ctrl, alt, shift, win);
        return true;
    }

    static bool TryParseKey(string part, out Keys key)
    {
        key = Keys.None;
        if (part.Length == 1 && char.IsAsciiDigit(part[0]))
        {
            key = Keys.D0 + (part[0] - '0');
            return true;
        }
        if (part.Length == 0 || char.IsAsciiDigit(part[0]))
            return false;
        return Enum.TryParse(part, ignoreCase: true, out key) && Enum.IsDefined(key) && !ModifierKeys.Contains(key);
    }

    /// <summary>The same keys plus Shift ("Ctrl+F7" -> "Ctrl+Shift+F7"); empty when there is none or it has Shift already.</summary>
    public static string WithShift(string hotkey) =>
        TryParse(hotkey, out var h) && !h.Shift ? Format(h with { Shift = true }) : "";

    public static string Format(Hotkey h)
    {
        var parts = new List<string>(5);
        if (h.Ctrl) parts.Add("Ctrl");
        if (h.Alt) parts.Add("Alt");
        if (h.Shift) parts.Add("Shift");
        if (h.Win) parts.Add("Win");
        parts.Add(h.Key is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (h.Key - Keys.D0))).ToString() : h.Key.ToString());
        return string.Join("+", parts);
    }
}
