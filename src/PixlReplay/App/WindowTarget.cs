namespace PixlReplay.App;

/// <summary>Reads libobs window strings "title:class:exe" (":" is stored as "#3A", "#" as "#22").</summary>
public static class WindowTarget
{
    static string[] Parts(string? window) => string.IsNullOrEmpty(window) ? [] : window.Split(':');

    static string Decode(string s) => s.Replace("#3A", ":").Replace("#22", "#");

    public static string? Title(string? window) => Parts(window) is { Length: 3 } p ? Decode(p[0]) : null;

    public static string? ExeName(string? window) =>
        Parts(window) is { Length: 3 } p && p[2].Length > 0 ? Path.GetFileNameWithoutExtension(Decode(p[2])) : null;
}
