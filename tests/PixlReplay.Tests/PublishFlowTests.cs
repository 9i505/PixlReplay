using PixlReplay.Engine.Share;

namespace PixlReplay.Tests;

public static class PublishFlowTests
{
    static readonly TimeSpan S = TimeSpan.FromSeconds(1);

    static PublishRecipe Recipe(params RecipeStep[] steps) =>
        new(SharePlatform.X, "https://example.com/", steps, [], "null");

    [Test]
    public static void Steps_run_in_order_and_the_last_one_means_posted()
    {
        var flow = new PublishFlow(Recipe(
            new RecipeStep("box", StepKind.WaitFor, "true", TimeSpan.FromSeconds(30)),
            new RecipeStep("post", StepKind.Click, "x", TimeSpan.FromSeconds(30)),
            new RecipeStep("sent", StepKind.Done, "true", TimeSpan.FromSeconds(30))), "hi");
        Assert.Equal("box", flow.Current!.Name, "first");
        flow.StepResult(false, 1 * S);
        Assert.Equal("box", flow.Current!.Name, "still waiting");
        flow.StepResult(true, 2 * S);
        Assert.Equal("post", flow.Current!.Name, "next");
        flow.StepResult(true, 3 * S);
        flow.StepResult(true, 4 * S);
        Assert.Equal(PublishState.Posted, flow.State, "posted");
    }

    [Test]
    public static void A_step_that_never_happens_hands_over_to_the_user_unless_it_is_optional()
    {
        var flow = new PublishFlow(Recipe(
            new RecipeStep("crop", StepKind.Click, "x", TimeSpan.FromSeconds(10), Optional: true),
            new RecipeStep("post", StepKind.Click, "x", TimeSpan.FromSeconds(10))), "hi");
        flow.StepResult(false, 0 * S);
        flow.StepResult(false, 11 * S);
        Assert.Equal("post", flow.Current!.Name, "optional step skipped after its time");
        flow.StepResult(false, 12 * S);
        Assert.Equal(PublishState.Running, flow.State, "the next step gets its own time");
        flow.StepResult(false, 23 * S);
        Assert.Equal(PublishState.NeedsUser, flow.State, "hand over");
        Assert.Equal("post", flow.FailedStep, "which step");
    }

    [Test]
    public static void Signing_in_pauses_the_clock_and_starts_over_afterwards()
    {
        var flow = new PublishFlow(Recipe(
            new RecipeStep("box", StepKind.WaitFor, "true", TimeSpan.FromSeconds(10)),
            new RecipeStep("post", StepKind.Click, "x", TimeSpan.FromSeconds(10))), "hi");
        flow.StepResult(true, 1 * S);
        Assert.Equal("post", flow.Current!.Name, "moved on");
        flow.LoginSeen(true);
        Assert.Equal(PublishState.NeedsLogin, flow.State, "login");
        Assert.Equal("share.pub.login", flow.StatusKey, "asks to sign in");
        flow.StepResult(false, TimeSpan.FromMinutes(5));
        Assert.Equal(PublishState.NeedsLogin, flow.State, "no timeout while signing in");
        flow.LoginSeen(false);
        Assert.Equal(PublishState.Running, flow.State, "back to work");
        Assert.True(flow.TakeRestart(), "the page is loaded again");
        Assert.True(!flow.TakeRestart(), "only once");
        Assert.Equal("box", flow.Current!.Name, "from the first step");
        flow.StepResult(false, TimeSpan.FromMinutes(5) + 5 * S);
        Assert.Equal(PublishState.Running, flow.State, "fresh time for the step");
    }

    [Test]
    public static void Caption_steps_are_skipped_when_there_is_no_caption()
    {
        var flow = new PublishFlow(Recipe(
            new RecipeStep("caption", StepKind.Type, "x", TimeSpan.FromSeconds(10)),
            new RecipeStep("post", StepKind.Click, "x", TimeSpan.FromSeconds(10))), "");
        Assert.Equal("post", flow.Current!.Name, "nothing to type");
    }

    [Test]
    public static void Each_site_starts_where_its_post_box_is_and_ends_on_a_sign_of_success()
    {
        var x = PublishRecipe.For(SharePlatform.X, "#EldenRing #gaming");
        Assert.Equal("https://x.com/intent/post?text=%23EldenRing%20%23gaming", x.StartUrl, "X composer with caption");
        Assert.True(x.Steps.Any(s => s.Kind == StepKind.Attach && s.Js.Contains("[role=dialog] input[data-testid=fileInput]")), "X attaches in the dialog, not the timeline box");
        Assert.True(x.Steps.Any(s => s.Kind == StepKind.Click && s.Js.Contains("tweetButton")), "X post button");

        var t = PublishRecipe.For(SharePlatform.TikTok, "x");
        Assert.True(t.Steps.Any(s => s.Kind == StepKind.Click && s.Js.Contains("post_video_button")), "TikTok post button");
        Assert.True(t.Interrupts.Any(i => i.Contains("Got it")), "TikTok tips are dismissed");

        var i = PublishRecipe.For(SharePlatform.Instagram, "x");
        Assert.True(i.Steps.Any(s => s.Js.Contains("Select crop")), "Instagram keeps the original size");
        Assert.Equal(StepKind.Done, i.Steps[^1].Kind, "ends on success");
        Assert.True(i.Steps[^1].Js.Contains("has been shared"), "Instagram success text");
    }

    [Test]
    public static void Pictures_use_each_sites_photo_upload()
    {
        var x = PublishRecipe.For(SharePlatform.X, "x", picture: true);
        Assert.True(x.Steps.Any(s => s.Js.Contains("[data-testid=attachments] img")), "X waits for the attached picture");
        Assert.True(!x.Steps.Any(s => s.Js.Contains("attachments] video")), "not for a video");
        Assert.True(PublishRecipe.For(SharePlatform.X, "x").Steps.Any(s => s.Js.Contains("attachments] video")), "videos as before");

        var t = PublishRecipe.For(SharePlatform.TikTok, "x", picture: true);
        Assert.True(t.StartUrl.EndsWith("/tiktokstudio/upload?tab=photo", StringComparison.Ordinal), "TikTok's Photos tab (seen 2026-10-15)");
        Assert.True(t.Steps.Any(s => s.Kind == StepKind.Attach && s.Js.Contains("accept*=image")), "its picture box");
        Assert.True(!t.Steps.Any(s => s.Js.Contains("accept*=video")), "not the video box");
        Assert.True(t.Steps.Any(s => s.Kind == StepKind.Click && s.Js.Contains("post_video_button")), "TikTok post button");

        var i = PublishRecipe.For(SharePlatform.Instagram, "x", picture: true);
        Assert.True(i.Steps.Any(s => s.Kind == StepKind.Attach), "Instagram takes pictures in the same box");
    }
}
