using PixlReplay.Engine.Edit;

namespace PixlReplay.Tests;

/// <summary>The FFmpeg filter text an edit becomes: colour looks, cuts, speed, blur, items and sound.</summary>
public static class EditGraphTests
{
    [Test]
    public static void Each_look_is_a_filter_and_none_is_nothing()
    {
        Assert.Equal(null, ColourLooks.Filter(ColourLook.None, 1), "none");
        Assert.Equal(null, ColourLooks.Filter(ColourLook.Vivid, 0), "no strength");
        Assert.Equal("eq=saturation=1.35:contrast=1.08", ColourLooks.Filter(ColourLook.Vivid, 1), "vivid");
        Assert.Equal("eq=saturation=1.175:contrast=1.04", ColourLooks.Filter(ColourLook.Vivid, 0.5), "half vivid");
        Assert.Equal("eq=contrast=1.12:saturation=0.85,colorbalance=rs=-0.05:bs=0.07:rh=0.06:bh=-0.04",
            ColourLooks.Filter(ColourLook.Cinematic, 1), "cinematic");
        Assert.Equal("colorbalance=rs=0.08:gs=0.02:bs=-0.08:rm=0.05:bm=-0.05", ColourLooks.Filter(ColourLook.Warm, 1), "warm");
        Assert.Equal("colorbalance=rs=-0.07:bs=0.08:bm=0.05", ColourLooks.Filter(ColourLook.Cool, 1), "cool");
        Assert.Equal("unsharp=5:5:0.9:5:5:0", ColourLooks.Filter(ColourLook.Sharp, 1), "sharp");
        Assert.Equal("hue=s=0", ColourLooks.Filter(ColourLook.Mono, 1), "mono");
    }

    [Test]
    public static void Mono_at_half_strength_keeps_half_the_colour()
    {
        Assert.Equal("hue=s=0.5", ColourLooks.Filter(ColourLook.Mono, 0.5), "half");
        Assert.Equal("unsharp=5:5:0.45:5:5:0", ColourLooks.Filter(ColourLook.Sharp, 0.5), "half sharp");
    }

    static readonly IReadOnlyDictionary<Guid, Overlay> NoPngs = new Dictionary<Guid, Overlay>();

    static EditGraph Build(EditProject p, int audioStreams = 3, IReadOnlyDictionary<Guid, Overlay>? pngs = null) =>
        EditGraph.Build(p, 1920, 1080, 60, audioStreams, pngs ?? NoPngs);

    [Test]
    public static void An_unedited_clip_passes_through()
    {
        var g = Build(EditProject.For("c.mp4", 30));
        Assert.True(g.Video.StartsWith("[vin]", StringComparison.Ordinal) && g.Video.EndsWith("[vout]", StringComparison.Ordinal), g.Video);
        Assert.True(g.Video.Contains("trim=start=0:end=30", StringComparison.Ordinal), g.Video);
        Assert.True(!g.Video.Contains("concat", StringComparison.Ordinal), "one piece: no concat");
        Assert.True(g.Audio.Contains("[aout]", StringComparison.Ordinal), g.Audio);
        Assert.Equal("aout,agame,amic", string.Join(",", g.AudioOutputs), "the mix, the game and the mic, like the clip");
    }

    [Test]
    public static void A_cut_concats_two_pieces()
    {
        var g = Build(EditProject.For("c.mp4", 30).SplitAt(5).SplitAt(20).DeleteSegmentAt(10));
        Assert.True(g.Video.Contains("concat=n=2:v=1:a=0", StringComparison.Ordinal), g.Video);
        Assert.True(g.Video.Contains("trim=start=20:end=30", StringComparison.Ordinal), g.Video);
        Assert.True(g.Audio.Contains("atrim=start=20:end=30", StringComparison.Ordinal), g.Audio);
        Assert.True(g.Audio.Contains("concat=n=2:v=0:a=1", StringComparison.Ordinal), g.Audio);
    }

    [Test]
    public static void Speed_two_uses_setpts_and_atempo()
    {
        var g = Build(EditProject.For("c.mp4", 30).SetSpeed(0, 10, 2));
        Assert.True(g.Video.Contains("setpts=(PTS-STARTPTS)/2", StringComparison.Ordinal), g.Video);
        Assert.True(g.Audio.Contains("atempo=2", StringComparison.Ordinal), g.Audio);
        Assert.True(g.Video.Contains("fps=60", StringComparison.Ordinal), "the clip's frame rate throughout");
    }

    [Test]
    public static void Quarter_speed_chains_atempo()
    {
        var g = Build(EditProject.For("c.mp4", 30).SetSpeed(0, 10, 0.25));
        Assert.True(g.Audio.Contains("atempo=0.5,atempo=0.5", StringComparison.Ordinal), g.Audio);
    }

    [Test]
    public static void A_blur_is_cropped_blurred_and_laid_over_in_its_time()
    {
        var blur = new BlurItem(Guid.NewGuid(), 3, 5, new System.Windows.Rect(0.5, 0.5, 0.25, 0.25));
        var g = Build(EditProject.For("c.mp4", 30).With(blur));
        Assert.True(g.Video.Contains("crop=480:270:960:540", StringComparison.Ordinal), g.Video);
        Assert.True(g.Video.Contains("boxblur", StringComparison.Ordinal), g.Video);
        Assert.True(g.Video.Contains("enable='between(t,3,5)'", StringComparison.Ordinal), g.Video);
    }

    [Test]
    public static void Game_and_mic_mix_with_their_levels()
    {
        var g = Build(EditProject.For("c.mp4", 30) with { Sound = new SoundLevels(Game: 0.5, MicMuted: true) });
        Assert.Equal("1,2", string.Join(",", g.AudioStreams), "game and mic tracks");
        Assert.True(g.Audio.Contains("volume=0.5,asplit=2[", StringComparison.Ordinal), g.Audio);
        Assert.True(g.Audio.Contains("volume=0,asplit=2[", StringComparison.Ordinal), g.Audio);
        Assert.True(g.Audio.Contains("[go0]anull[agame]", StringComparison.Ordinal) && g.Audio.Contains("[mo0]anull[amic]", StringComparison.Ordinal),
            "game and mic kept apart at their levels: " + g.Audio);
        Assert.True(g.Audio.Contains("amix=inputs=2", StringComparison.Ordinal), g.Audio);
    }

    [Test]
    public static void One_stream_uses_the_all_level()
    {
        var g = Build(EditProject.For("c.mp4", 30) with { Sound = new SoundLevels(All: 0.7) }, audioStreams: 1);
        Assert.Equal("0", string.Join(",", g.AudioStreams), "the mix");
        Assert.True(g.Audio.Contains("volume=0.7[", StringComparison.Ordinal), g.Audio);
        Assert.True(!g.Audio.Contains("amix", StringComparison.Ordinal), g.Audio);
        var silent = Build(EditProject.For("c.mp4", 30), audioStreams: 0);
        Assert.True(silent.Audio == "" && silent.AudioStreams.Count == 0, "no sound: no audio graph");
    }

    [Test]
    public static void A_louder_part_has_its_own_volume()
    {
        var p = EditProject.For("c.mp4", 30).SplitAt(10).WithParts([1], k => k with { Game = 1.5, Mic = 0.5 })
            with { Sound = new SoundLevels(Game: 0.8) };
        var g = Build(p);
        Assert.True(g.Audio.Contains("atrim=start=0:end=10,asetpts=PTS-STARTPTS,volume=0.8,asplit=2[", StringComparison.Ordinal), "first part's game: " + g.Audio);
        Assert.True(g.Audio.Contains("atrim=start=10:end=30,asetpts=PTS-STARTPTS,volume=1.2,asplit=2[", StringComparison.Ordinal), "second part's game 0.8 x 1.5: " + g.Audio);
        Assert.True(g.Audio.Contains("atrim=start=10:end=30,asetpts=PTS-STARTPTS,volume=0.5,asplit=2[", StringComparison.Ordinal), "second part's mic: " + g.Audio);
        Assert.True(g.Audio.Contains("concat=n=2:v=0:a=1[aout]", StringComparison.Ordinal), g.Audio);
        Assert.True(g.Audio.Contains("[gq0][gq1]concat=n=2:v=0:a=1[agame]", StringComparison.Ordinal)
                    && g.Audio.Contains("[mq0][mq1]concat=n=2:v=0:a=1[amic]", StringComparison.Ordinal), "game and mic joined apart: " + g.Audio);
        var one = Build(EditProject.For("c.mp4", 30).SplitAt(10).WithParts([0], k => k with { Game = 2, Speed = 2 }), audioStreams: 1);
        Assert.True(one.Audio.Contains("atrim=start=0:end=10,asetpts=PTS-STARTPTS,volume=2,atempo=2[", StringComparison.Ordinal), "a one-track clip: " + one.Audio);
    }

    [Test]
    public static void A_part_look_is_on_only_while_that_part_plays()
    {
        var p = EditProject.For("c.mp4", 30).SplitAt(10).WithParts([1], k => k with { Look = ColourLook.Mono });
        var g = Build(p);
        Assert.True(g.Video.Contains("hue=s=0:enable='between(t,10,30)'", StringComparison.Ordinal), g.Video);
        Assert.True(!g.Video.Contains("between(t,0,10)", StringComparison.Ordinal), "the first part has none: " + g.Video);
        var both = Build(p with { Look = ColourLook.Warm });
        Assert.True(both.Video.Contains("colorbalance=rs=0.08:gs=0.02:bs=-0.08:rm=0.05:bm=-0.05:enable='between(t,0,10)'", StringComparison.Ordinal),
            "the clip's look on the other part: " + both.Video);
        Assert.True(both.Video.IndexOf("hue=s=0", StringComparison.Ordinal) < both.Video.IndexOf("overlay", StringComparison.Ordinal)
                    || !both.Video.Contains("overlay", StringComparison.Ordinal), "looks come before texts, so texts keep their colour");
    }

    [Test]
    public static void Overlay_paths_are_escaped()
    {
        var item = new TextItem(Guid.NewGuid(), 1, 2, "GG", new System.Windows.Rect(0.1, 0.1, 0.2, 0.1), "#FFFFFF", 0.06, TextEffect.Box);
        var g = Build(EditProject.For("c.mp4", 30).With(item), pngs: new Dictionary<Guid, Overlay> { [item.Id] = new(@"C:\T\a.png", 128, 72) });
        Assert.True(g.Video.Contains(@"movie='C\:/T/a.png'", StringComparison.Ordinal), g.Video);
        Assert.True(g.Video.Contains("overlay=128:72:enable='between(t,1,2)'", StringComparison.Ordinal), "laid where the item is: " + g.Video);
    }
}
