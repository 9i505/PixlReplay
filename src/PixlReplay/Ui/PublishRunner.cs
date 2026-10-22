using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PixlReplay.Engine.Share;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace PixlReplay.Ui;

public enum PublishResult { Posted, NeedsUser, Cancelled }

/// <param name="PostUrl">The new post's address when the site shows it (X), else null.</param>
/// <param name="Detail">For <see cref="PublishResult.NeedsUser"/>: the step that did not happen.</param>
/// <param name="XPremium">X: whether the account has Premium, as its page showed (null when not seen).</param>
public sealed record PublishOutcome(PublishResult Result, string? PostUrl = null, string? Detail = null, bool? XPremium = null);

/// <summary>Runs a <see cref="PublishRecipe"/> on a page: signs-in pause, tips dismissed, each step tried four times a second.</summary>
public sealed class PublishRunner(CoreWebView2 core, PublishRecipe recipe, string file, Func<Task> load)
{
    /// <summary>Instagram's Create menu opens in well under a second; one retry gap covers a slow one.</summary>
    static readonly TimeSpan RepeatEvery = TimeSpan.FromSeconds(1.5);

    /// <summary>Status text key, whenever it changes.</summary>
    public event Action<string>? StatusChanged;
    /// <summary>True: a sign-in page is showing (show the window). False: signed in, back to the background.</summary>
    public event Action<bool>? LoginNeeded;

    public TimeSpan Interval { get; init; } = TimeSpan.FromMilliseconds(250);

    public async Task<PublishOutcome> Run(string caption, CancellationToken ct)
    {
        var flow = new PublishFlow(recipe, caption);
        var clock = Stopwatch.StartNew();
        string? status = null;
        string? repeatStep = null;
        bool? premium = null;
        bool accountRead = recipe.AccountJs == null;
        TimeSpan lastRepeat = TimeSpan.Zero;
        await load();
        while (true)
        {
            await Task.Delay(Interval, ct);
            var now = clock.Elapsed;
            var before = flow.State;
            flow.LoginSeen(await Bool(PublishRecipe.LoginJs));
            if (flow.State != before && (flow.State == PublishState.NeedsLogin || before == PublishState.NeedsLogin))
                LoginNeeded?.Invoke(flow.State == PublishState.NeedsLogin);
            if (flow.TakeRestart())
            {
                await load();
                continue;
            }
            if (flow.StatusKey != status)
                StatusChanged?.Invoke(status = flow.StatusKey);
            if (flow.State == PublishState.NeedsLogin || flow.Current is not { } step)
                continue;

            foreach (string interrupt in recipe.Interrupts)
                await Bool($"(() => {{ const el = {interrupt}; if (!el || !K.visible(el)) return false; K.fire(el); return true; }})()");

            bool done;
            if (step.Kind == StepKind.Repeat)
            {
                if (repeatStep != step.Name || now - lastRepeat >= RepeatEvery)
                {
                    repeatStep = step.Name;
                    lastRepeat = now;
                    await Eval(step.Js);
                }
                done = await Bool(step.Until!);
            }
            else
                done = await Perform(step, flow.Caption);
            flow.StepResult(done, now);
            if (!accountRead && done)
            {
                // Read once the page is past its first step (signed in, the side menu is there).
                accountRead = true;
                string seen = await Eval(recipe.AccountJs!);
                premium = seen == "true" ? true : seen == "false" ? false : null;
            }

            if (flow.State == PublishState.Posted)
            {
                StatusChanged?.Invoke(flow.StatusKey);
                string? url = JsonSerializer.Deserialize<string?>(await Eval(recipe.PostUrlJs));
                return new PublishOutcome(PublishResult.Posted, url, XPremium: premium);
            }
            if (flow.State == PublishState.NeedsUser)
            {
                StatusChanged?.Invoke(flow.StatusKey);
                return new PublishOutcome(PublishResult.NeedsUser, Detail: flow.FailedStep, XPremium: premium);
            }
        }
    }

    async Task<bool> Perform(RecipeStep step, string caption) => step.Kind switch
    {
        StepKind.WaitFor or StepKind.Done => await Bool(step.Js),
        StepKind.Attach => await Attach(step.Js),
        StepKind.Type or StepKind.ReplaceText => await Type(step.Js, caption, step.Kind == StepKind.ReplaceText),
        StepKind.Click => await Bool($"(() => {{ const el = {step.Js}; if (!el || !K.enabled(el)) return false; K.fire(el); return true; }})()"),
        _ => false,
    };

    /// <summary>Gives the site's upload box the file, the same way browser test tools do (DOM.setFileInputFiles).</summary>
    async Task<bool> Attach(string selector)
    {
        try
        {
            var doc = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{\"depth\":0}"));
            int root = doc.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
            var found = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("DOM.querySelector",
                JsonSerializer.Serialize(new { nodeId = root, selector })));
            int node = found.RootElement.GetProperty("nodeId").GetInt32();
            if (node == 0)
                return false;
            await core.CallDevToolsProtocolMethodAsync("DOM.setFileInputFiles", JsonSerializer.Serialize(new { files = new[] { file }, nodeId = node }));
            return true;
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or JsonException or KeyNotFoundException)
        {
            return false; // the page changed under us; tried again next round
        }
    }

    /// <summary>
    /// Focuses the box, types like a keyboard (the sites' editors ignore text set from script), then takes the focus
    /// away again as a person clicking the Post button would. Instagram only saves the caption when its box loses
    /// focus: without this its post went out with an empty caption although the box showed the text.
    /// </summary>
    async Task<bool> Type(string element, string text, bool replace)
    {
        string focus = $"(() => {{ const e = {element}; if (!e) return false; e.scrollIntoView({{ block: 'center' }}); e.focus(); " +
                       (replace ? "document.execCommand('selectAll'); " : "") + "return true; })()";
        if (!await Bool(focus))
            return false;
        await core.CallDevToolsProtocolMethodAsync("Input.insertText", JsonSerializer.Serialize(new { text }));
        await Eval($"(() => {{ const e = {element}; if (e) e.blur(); return true; }})()");
        return true;
    }

    async Task<string> Eval(string expression)
    {
        try
        {
            return await core.ExecuteScriptAsync(PublishRecipe.Wrap(expression));
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return "null"; // the page is between loads
        }
    }

    async Task<bool> Bool(string expression) => await Eval(expression) == "true";

    /// <summary>
    /// Publishes on a hidden local page shaped like the site (see <see cref="PublishMocks"/>): nothing on screen,
    /// no network, a throwaway profile. Returns null when the outcome is the expected one.
    /// <paramref name="mode"/>: x, tiktok, instagram, login (X behind a sign-in), stuck (X never finishes uploading).
    /// </summary>
    public static string? SelfTest(string mode, string video, string caption, string userDataFolder)
    {
        string? result = "timed out";
        var thread = new Thread(() =>
        {
            var window = WindowPlacement.OffScreen(new Window { Width = 1000, Height = 760, WindowStyle = WindowStyle.None });
            var web = new WebView2();
            window.Content = web;
            window.Loaded += async (_, _) =>
            {
                try
                {
                    result = await RunSelfTest(web, mode, video, caption, userDataFolder);
                }
                catch (Exception e)
                {
                    result = e.GetType().Name + ": " + e.Message;
                }
                window.Close();
            };
            window.Closed += (_, _) => Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            window.Show();
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(90));
        return result;
    }

    static async Task<string?> RunSelfTest(WebView2 web, string mode, string video, string caption, string userDataFolder)
    {
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await web.EnsureCoreWebView2Async(env);
        var core = web.CoreWebView2;
        var platform = mode switch { "tiktok" => SharePlatform.TikTok, "instagram" => SharePlatform.Instagram, _ => SharePlatform.X };
        var recipe = PublishRecipe.For(platform, caption, PixlReplay.Engine.ClipOrganizer.IsPicture(video));
        if (mode == "stuck")
            recipe = recipe with { Steps = recipe.Steps.Select(s => s with { Timeout = TimeSpan.FromSeconds(3) }).ToList() };

        int loads = 0;
        async Task Load()
        {
            string page = mode switch
            {
                "tiktok" => PublishMocks.TikTok,
                "instagram" => PublishMocks.Instagram,
                "stuck" => PublishMocks.X(caption, stuck: true),
                "xpremium" => PublishMocks.X(caption, stuck: false, premium: true),
                "login" when loads == 0 => PublishMocks.SignIn,
                _ => PublishMocks.X(caption, stuck: false),
            };
            loads++;
            var loaded = new TaskCompletionSource();
            void Done(object? s, CoreWebView2NavigationCompletedEventArgs e) => loaded.TrySetResult();
            core.NavigationCompleted += Done;
            core.NavigateToString(page);
            await loaded.Task;
            core.NavigationCompleted -= Done;
        }

        var runner = new PublishRunner(core, recipe, video, Load) { Interval = TimeSpan.FromMilliseconds(200) };
        bool askedToSignIn = false;
        runner.LoginNeeded += needed =>
        {
            if (!needed)
                return;
            askedToSignIn = true;
            // The "user" signs in: the sign-in form goes away.
            _ = core.ExecuteScriptAsync("document.querySelector('input[type=password]').remove()");
        };
        var outcome = await runner.Run(caption, new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        if (core.DocumentTitle.StartsWith("WRONG"))
            return core.DocumentTitle;
        if (mode == "stuck")
            return outcome.Result == PublishResult.NeedsUser && outcome.Detail == "uploaded" ? null : $"expected a hand-over at 'uploaded', got {outcome}";
        if (mode == "login" && !askedToSignIn)
            return "the sign-in page was not noticed";
        if (outcome.Result != PublishResult.Posted)
            return $"not posted: {outcome}";
        if (mode is "x" or "login" && outcome.PostUrl != "https://x.com/pixlreplay/status/123")
            return "post address: " + outcome.PostUrl;
        if (mode == "x" && outcome.XPremium != false)
            return "X account read as " + (outcome.XPremium?.ToString() ?? "unknown") + ", expected free";
        if (mode == "xpremium" && outcome.XPremium != true)
            return "X account read as " + (outcome.XPremium?.ToString() ?? "unknown") + ", expected Premium";

        string posted = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("JSON.stringify(window.__posted || null)")) ?? "null";
        if (posted == "null")
            return "the site never received the post";
        var p = JsonDocument.Parse(posted).RootElement;
        string expectedFile = $"{Path.GetFileName(video)}:{new FileInfo(video).Length}";
        if (p.GetProperty("file").GetString() != expectedFile)
            return "file: " + p.GetProperty("file").GetString();
        if (p.GetProperty("text").GetString()?.Trim() != caption)
            return $"caption: '{p.GetProperty("text").GetString()}'";
        if (mode == "instagram" && p.GetProperty("aspect").GetString() != "original")
            return "Instagram crop: " + p.GetProperty("aspect").GetString();
        if (mode == "tiktok" && await core.ExecuteScriptAsync("!!window.__tipClosed") != "true")
            return "TikTok's tip was left open";
        return null;
    }
}
