using System.Windows.Input;
using PixlReplay.Ui;

namespace PixlReplay.Tests;

/// <summary>PixlReplay's mouse pointers: Windows' own (white, the user's scheme), the editor's tools in the same white; no hand anywhere.</summary>
public static class ThemeCursorTests
{
    static void OnSta(Action test)
    {
        string? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                error = e.ToString();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.True(error == null, error ?? "");
    }

    /// <summary>The usual pointers are Windows' own, nothing redrawn: the arrow, text, cross, move and the resize arrows.</summary>
    [Test]
    public static void Every_usual_pointer_is_windows_own() => OnSta(() =>
    {
        var windows = new Dictionary<ThemeCursors.Shape, Cursor>
        {
            [ThemeCursors.Shape.Arrow] = Cursors.Arrow,
            [ThemeCursors.Shape.IBeam] = Cursors.IBeam,
            [ThemeCursors.Shape.Cross] = Cursors.Cross,
            [ThemeCursors.Shape.Move] = Cursors.SizeAll,
            [ThemeCursors.Shape.SizeWE] = Cursors.SizeWE,
            [ThemeCursors.Shape.SizeNS] = Cursors.SizeNS,
            [ThemeCursors.Shape.SizeNWSE] = Cursors.SizeNWSE,
            [ThemeCursors.Shape.SizeNESW] = Cursors.SizeNESW,
        };
        foreach (var shape in Enum.GetValues<ThemeCursors.Shape>().Where(s => !ThemeCursors.IsTool(s)))
            Assert.True(ThemeCursors.Get(shape) == windows[shape], $"{shape}: Windows' own");
    });

    /// <summary>The tool pointers are white where Windows' arrow is white, with a dark edge; no rose left.</summary>
    [Test]
    public static void The_tools_are_white_like_windows_arrow() => OnSta(() =>
    {
        foreach (var tool in Enum.GetValues<ThemeCursors.Shape>().Where(ThemeCursors.IsTool))
        {
            var picture = ThemeCursors.Picture(tool)!;
            int white = 0, rose = 0;
            for (int i = 0; i < picture.Bgra.Length; i += 4)
            {
                if (picture.Bgra[i + 3] < 250)
                    continue;
                if (picture.Bgra[i] > 240 && picture.Bgra[i + 1] > 240 && picture.Bgra[i + 2] > 240)
                    white++;
                if (Math.Abs(picture.Bgra[i + 2] - 0xF4) < 10 && Math.Abs(picture.Bgra[i + 1] - 0x8B) < 10 && Math.Abs(picture.Bgra[i] - 0xA4) < 10)
                    rose++;
            }
            Assert.True(white > 8 && rose == 0, $"{tool}: white ({white}), no rose ({rose})");
        }
    });

    /// <summary>
    /// The editor's tools have pointers of their own at the size of Windows' arrow: a pen with its tip as the hot spot, and
    /// a cross (the hot spot) with a shape, a drop or crop corners; each one different.
    /// </summary>
    [Test]
    public static void Each_tool_has_its_own_pointer_at_windows_size() => OnSta(() =>
    {
        var arrow = ThemeCursors.SystemPicture(ThemeCursors.Shape.Arrow)!;
        var tools = Enum.GetValues<ThemeCursors.Shape>().Where(ThemeCursors.IsTool).ToList();
        Assert.Equal(4, tools.Count, "pen, shapes, blur, crop");
        var pictures = tools.Select(t => ThemeCursors.Picture(t)!).ToList();
        Assert.True(pictures.All(p => p.Width == arrow.Width && p.Height == arrow.Height), "the size of Windows' arrow");
        var pen = pictures[0];
        Assert.True(pen.Hot.X < pen.Width / 4 && pen.Hot.Y > pen.Height * 3 / 4, $"the pen draws from its tip, bottom left ({pen.Hot})");
        int at = (pen.Hot.Y * pen.Width + pen.Hot.X) * 4;
        Assert.True(pen.Bgra[at + 3] > 0, "the tip is drawn at the hot spot");
        Assert.Equal(4, pictures.Select(p => Convert.ToBase64String(p.Bgra)).Distinct().Count(), "four different pointers");
        Assert.True(tools.All(t => ThemeCursors.Get(t) != Cursors.Cross), "made (not Windows' cross)");
    });

    /// <summary>A pointer an element chose stays Windows' own; only the hand becomes the arrow.</summary>
    [Test]
    public static void Windows_pointers_stay_and_the_hand_is_the_arrow() => OnSta(() =>
    {
        Assert.True(ThemeCursors.Themed(Cursors.Arrow) == Cursors.Arrow, "arrow");
        Assert.True(ThemeCursors.Themed(Cursors.Hand) == Cursors.Arrow, "no hand: the arrow");
        Assert.True(ThemeCursors.Themed(Cursors.IBeam) == Cursors.IBeam, "text");
        Assert.True(ThemeCursors.Themed(Cursors.SizeAll) == Cursors.SizeAll, "move");
        Assert.True(ThemeCursors.Themed(Cursors.Wait) == Cursors.Wait, "busy");
    });
}
