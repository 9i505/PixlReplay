using PixlReplay.Engine.Voice;

namespace PixlReplay.Tests;

/// <summary>Smart Save with two phrases: one saves a clip, the other takes a screenshot.</summary>
public static class VoiceRoutingTests
{
    [Test]
    public static void The_closer_phrase_under_its_own_threshold_acts()
    {
        Assert.Equal(null, VoiceRouting.Pick(0.08, 0.06, 0.09, 0.06), "neither heard");
        Assert.Equal(null, VoiceRouting.Pick(null, 0.06, null, 0.06), "no phrases");
        Assert.Equal(VoiceAction.Clip, VoiceRouting.Pick(0.05, 0.06, null, 0.06), "only a clip phrase");
        Assert.Equal(VoiceAction.Shot, VoiceRouting.Pick(null, 0.06, 0.05, 0.06), "only a screenshot phrase");
        Assert.Equal(VoiceAction.Shot, VoiceRouting.Pick(0.07, 0.06, 0.05, 0.06), "only the screenshot phrase is under its threshold");
        Assert.Equal(VoiceAction.Shot, VoiceRouting.Pick(0.05, 0.06, 0.03, 0.06), "both: the closer one");
        Assert.Equal(VoiceAction.Shot, VoiceRouting.Pick(0.059, 0.06, 0.04, 0.05), "closer for its own threshold (0.98 against 0.8)");
        Assert.Equal(VoiceAction.Clip, VoiceRouting.Pick(0.02, 0.06, 0.04, 0.05), "and the other way");
    }

    [Test]
    public static void A_screenshot_phrase_too_close_to_the_clip_phrase_is_refused()
    {
        Assert.True(VoiceRouting.TooClose(0.060, 0.066), "under the easy threshold: they would be mixed up");
        Assert.True(!VoiceRouting.TooClose(0.070, 0.066), "far enough apart");
    }

    [Test]
    public static void Each_action_waits_out_its_own_cooldown()
    {
        var t = new DateTime(2026, 10, 15, 22, 0, 0);
        Assert.True(VoiceRouting.Ready(DateTime.MinValue, t), "never triggered");
        Assert.True(!VoiceRouting.Ready(t, t.AddSeconds(3)), "3 s after: no");
        Assert.True(VoiceRouting.Ready(t, t + VoiceTrigger.Cooldown), "after the cooldown: yes");
    }

    [Test]
    public static void Each_phrase_keeps_its_own_samples()
    {
        string dir = Path.Combine(Path.GetTempPath(), "pixlreplay-tests", Guid.NewGuid().ToString("N"));
        try
        {
            float[][] samples = [[0.1f, 0.2f], [0.3f], [0.4f, 0.5f, 0.6f]];
            VoicePhrase.SaveSamples(dir, samples, VoiceAction.Shot);
            Assert.True(File.Exists(Path.Combine(dir, "shot1.wav")) && File.Exists(Path.Combine(dir, "shot3.wav")), "shot1..3.wav");
            Assert.True(VoicePhrase.HasSamples(dir, VoiceAction.Shot) && !VoicePhrase.HasSamples(dir), "the clip phrase is separate");
            Assert.Equal(3, VoicePhrase.LoadSamples(dir, VoiceAction.Shot).Count, "loaded");
            VoicePhrase.DeleteSamples(dir, VoiceAction.Shot);
            Assert.True(!VoicePhrase.HasSamples(dir, VoiceAction.Shot), "deleted on its own");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }
}
