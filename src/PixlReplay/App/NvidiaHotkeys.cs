using System.Windows.Forms;
using Microsoft.Win32;

namespace PixlReplay.App;

/// <summary>
/// The shortcuts NVIDIA's app has set (instant replay, recording, overlay…), read from its settings so PixlReplay can say
/// "held by NVIDIA" for a key it could not reserve. Windows itself does not tell which app holds a key.
/// </summary>
public static class NvidiaHotkeys
{
    const string SettingsKey = @"Software\NVIDIA Corporation\Global\ShadowPlay\NVSPCAPS";

    /// <summary>NVIDIA's shortcuts on this PC (none when its app is not installed).</summary>
    public static IReadOnlyList<Hotkey> Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            if (key == null)
                return [];
            return Parse(key.GetValueNames().ToDictionary(n => n, n => key.GetValue(n)));
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    /// <summary>
    /// Each "&lt;Name&gt;HKeyCount" with its "&lt;Name&gt;HKey0…" values: virtual-key codes, modifiers and one key.
    /// </summary>
    public static IReadOnlyList<Hotkey> Parse(IReadOnlyDictionary<string, object?> values)
    {
        var keys = new List<Hotkey>();
        foreach (var (name, value) in values)
        {
            if (!name.EndsWith("HKeyCount", StringComparison.Ordinal) || Number(value) is not { } count || count <= 0)
                continue;
            string prefix = name[..^"Count".Length];
            bool ctrl = false, alt = false, shift = false, win = false;
            Keys? main = null;
            for (int i = 0; i < count; i++)
            {
                switch (values.TryGetValue(prefix + i, out var vk) ? Number(vk) : null)
                {
                    case 0x11 or 0xA2 or 0xA3: ctrl = true; break;
                    case 0x12 or 0xA4 or 0xA5: alt = true; break;
                    case 0x10 or 0xA0 or 0xA1: shift = true; break;
                    case 0x5B or 0x5C: win = true; break;
                    case { } code: main = (Keys)code; break;
                }
            }
            if (main is { } k)
                keys.Add(new Hotkey(k, ctrl, alt, shift, win));
        }
        return keys;
    }

    /// <summary>A REG_BINARY of four little-endian bytes, or a DWORD.</summary>
    static int? Number(object? value) => value switch
    {
        byte[] { Length: >= 4 } b => BitConverter.ToInt32(b, 0),
        int i => i,
        _ => null,
    };
}
