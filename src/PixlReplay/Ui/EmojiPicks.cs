namespace PixlReplay.Ui;

/// <summary>An emoji in the caption picker and its colour picture (WPF draws emoji text in black and white).</summary>
public sealed record EmojiPick(string Emoji)
{
    /// <summary>"1f525.png": the code points, as the pictures in Ui/Assets/Emoji are named.</summary>
    public string File => string.Join("-", EnumerateCodePoints(Emoji).Select(c => c.ToString("x"))) + ".png";
    public string Source => "pack://application:,,,/PixlReplay;component/Ui/Assets/Emoji/" + File;

    static IEnumerable<int> EnumerateCodePoints(string s)
    {
        for (int i = 0; i < s.Length; i += char.IsSurrogatePair(s, i) ? 2 : 1)
            yield return char.ConvertToUtf32(s, i);
    }
}

/// <summary>A tab of the picker: its name (a text key) and the emoji on the tab.</summary>
public sealed record EmojiCategory(string NameKey, EmojiPick Icon, IReadOnlyList<EmojiPick> Emoji)
{
    public string Name => App.Text.Get(NameKey);
}

/// <summary>
/// The caption picker's emoji, by tab: popular ones for game clips first. All are long in Unicode (13 or older), so
/// X, TikTok and Instagram show them on every phone. The pictures are Windows' own (Microsoft's Fluent Emoji, MIT),
/// drawn once from Segoe UI Emoji.
/// </summary>
public static class EmojiPicks
{
    static EmojiCategory Tab(string key, string icon, string emoji) =>
        new(key, new EmojiPick(icon), emoji.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(e => new EmojiPick(e)).ToList());

    public static readonly IReadOnlyList<EmojiCategory> Categories =
    [
        Tab("share.emoji.popular", "🔥",
            "🔥 😂 🤣 💀 😱 🤯 😎 🥶 😤 😭 🙏 👀 💯 👑 🏆 🎮 🕹️ 🎯 ⚡ 💥 💪 👏 🙌 🤝 ✨ 🚀 ❤️ 😈 👻 🎉 🍿 ⚔️ 🛡️ 🧠 🤡 😏 🥵 🎬 ✅ ⭐"),
        Tab("share.emoji.faces", "😀",
            "😀 😃 😄 😁 😆 😅 🤣 😂 🙂 🙃 😉 😊 😇 🥰 😍 🤩 😘 😋 😛 😜 🤪 😝 🤑 🤗 🤭 🤫 🤔 🤐 🤨 😐 😑 😶 😏 😒 🙄 😬 😌 😔 😪 🤤 " +
            "😴 😷 🤒 🤕 🤢 🤮 🤧 🥵 🥶 🥴 😵 🤯 🤠 🥳 😎 🤓 🧐 😕 😟 🙁 ☹️ 😮 😯 😲 😳 🥺 😦 😧 😨 😰 😥 😢 😭 😱 😖 😣 😞 😓 😩 😫 " +
            "😤 😡 😠 🤬 😈 👿 💀 ☠️ 💩 🤡 👹 👺 👻 👽 👾 🤖"),
        Tab("share.emoji.hands", "👍",
            "👋 🤚 🖐️ ✋ 🖖 👌 🤌 🤏 ✌️ 🤞 🤟 🤘 🤙 👈 👉 👆 👇 ☝️ 👍 👎 ✊ 👊 🤛 🤜 👏 🙌 👐 🤲 🤝 🙏 ✍️ 💅 🤳 💪 🦾 🦵 👀 👁️ 🧠 🫀"),
        Tab("share.emoji.games", "🎮",
            "🎮 🕹️ 🎯 🎲 🧩 ♟️ 🎳 🏆 🥇 🥈 🥉 🏅 🎖️ ⚽ 🏀 🏈 ⚾ 🎾 🏐 🏉 🥊 🥋 ⛳ 🏹 🎣 🛹 🏂 🏋️ 🤺 🏎️ 🏁 🎬 🎧 🎤 🎸 🎹 🥁 🎺 📸 🎥 " +
            "⚔️ 🛡️ 🗡️ 🔫 💣 🧨 🪓 🔪 💰 💎 🗺️ 🧭 🏰 🚀 🛸 🚁"),
        Tab("share.emoji.symbols", "❤️",
            "❤️ 🧡 💛 💚 💙 💜 🖤 🤍 🤎 💔 ❣️ 💕 💞 💓 💗 💖 💘 💝 💯 💢 💥 💫 💦 💨 💬 💭 💤 ✨ 🌟 ⭐ ⚡ 🔥 🌈 ☀️ ❄️ ✅ ❌ ⚠️ 🔔 🎉 " +
            "🎊 🎁 🏳️ 🏴 🚩 🔴 🟠 🟡 🟢 🔵 🟣 ⚫ ⚪ 🆕 🆒 🆙 🔝 🔞 ⏰ ⌛"),
        Tab("share.emoji.nature", "🐶",
            "🐶 🐱 🐭 🐹 🐰 🦊 🐻 🐼 🐨 🐯 🦁 🐮 🐷 🐸 🐵 🙈 🙉 🙊 🐔 🐧 🐦 🦅 🦉 🐺 🐗 🐴 🦄 🐝 🦋 🐌 🐞 🐢 🐍 🦖 🦕 🐙 🦈 🐬 🐳 🐊 " +
            "🍕 🍔 🍟 🌭 🌮 🍿 🍩 🍪 🎂 🍫 🍺 ☕ 🥤 🍉 🍓 🍒 🌹 🌵 🍀 🌙"),
    ];

    /// <summary>Every emoji once (the pictures to have).</summary>
    public static IReadOnlyList<EmojiPick> All { get; } = Categories.SelectMany(c => c.Emoji.Append(c.Icon)).Distinct().ToList();
}
