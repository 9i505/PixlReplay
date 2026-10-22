namespace PixlReplay.Ui.Edit;

/// <param name="Label">The string key of what it does.</param>
/// <param name="Keys">The keys to press, as shown on key caps ("t:" before a string key: words, not a key).</param>
/// <param name="Command">What the window sends to <see cref="EditorViewModel.KeyCommand"/> (null: a mouse gesture).</param>
public sealed record ShortcutLine(string Label, IReadOnlyList<string> Keys, string? Command);

/// <param name="Title">The string key of the group's heading.</param>
public sealed record ShortcutGroup(string Title, IReadOnlyList<ShortcutLine> Lines);

/// <summary>The editor's shortcuts as the sheet shows them (two columns); the tests check each one does something.</summary>
public static class EditorShortcuts
{
    public static IReadOnlyList<ShortcutGroup> Left { get; } =
    [
        new("keys.play", [
            new("keys.playPause", ["Space"], "Space"),
            new("keys.move", ["←", "→"], "Right"),
            new("keys.fine", ["Shift", "←", "→"], "Shift+Right"),
            new("keys.jump", ["↑", "↓"], "Down"),
            new("keys.ends", ["Home", "End"], "End"),
        ]),
        new("keys.parts", [
            new("keys.split", ["S"], "S"),
            new("keys.splitOr", ["Ctrl", "B"], "Ctrl+B"),
            new("keys.cutBefore", ["Q"], "Q"),
            new("keys.cutAfter", ["W"], "W"),
        ]),
        new("keys.timeline", [
            new("keys.zoom", ["Ctrl", "+", "−"], "Ctrl+Plus"),
            new("keys.fit", ["\\"], "Backslash"),
            new("keys.shortcuts", ["?"], "?"),
        ]),
    ];

    public static IReadOnlyList<ShortcutGroup> Right { get; } =
    [
        new("keys.choose", [
            new("keys.all", ["Ctrl", "A"], "Ctrl+A"),
            new("keys.addOne", ["Ctrl", "t:keys.click"], null),
            new("keys.range", ["Shift", "t:keys.click"], null),
            new("keys.box", ["t:keys.dragBox"], null),
            new("keys.row", ["t:keys.clickName"], null),
            new("keys.none", ["Esc"], "Escape"),
        ]),
        new("keys.change", [
            new("keys.trim", ["Q", "W"], "Q"),
            new("keys.nudge", ["Alt", "←", "→"], "Alt+Right"),
            new("keys.copy", ["Ctrl", "C", "V"], "Ctrl+C"),
            new("keys.duplicate", ["Ctrl", "D"], "Ctrl+D"),
            new("keys.type", ["Enter"], "Enter"),
            new("keys.delete", ["Delete"], "Delete"),
            new("keys.undo", ["Ctrl", "Z", "Y"], "Ctrl+Z"),
        ]),
    ];
}
