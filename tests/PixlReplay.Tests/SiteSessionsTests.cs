using System.Windows;
using System.Windows.Threading;
using PixlReplay.Engine.Share;
using PixlReplay.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace PixlReplay.Tests;

/// <summary>Reading and removing site sign-ins in a real WebView2 profile: a throwaway one, in a window off screen.</summary>
public static class SiteSessionsTests
{
    /// <summary>Runs <paramref name="body"/> with a browser on its own profile; returns null or the failure.</summary>
    static string? WithBrowser(Func<CoreWebView2, Task> body)
    {
        string profile = Path.Combine(Path.GetTempPath(), "pixlreplay-tests", "webview-" + Guid.NewGuid().ToString("N"));
        string? result = "timed out";
        var thread = new Thread(() =>
        {
            var window = WindowPlacement.OffScreen(new Window { Width = 400, Height = 300, WindowStyle = WindowStyle.None });
            var web = new WebView2();
            window.Content = web;
            window.Loaded += async (_, _) =>
            {
                try
                {
                    await web.EnsureCoreWebView2Async(await CoreWebView2Environment.CreateAsync(null, profile));
                    await body(web.CoreWebView2);
                    result = null;
                }
                catch (Exception e)
                {
                    result = e.Message;
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
        thread.Join(TimeSpan.FromSeconds(60));
        try
        {
            Directory.Delete(profile, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        return result;
    }

    static void Add(CoreWebView2 core, string name, string domain, string value = "v") =>
        core.CookieManager.AddOrUpdateCookie(core.CookieManager.CreateCookie(name, value, domain, "/"));

    [Test]
    public static void The_profiles_sign_ins_are_read_per_site_and_one_site_is_signed_out_alone()
    {
        string? failure = WithBrowser(async core =>
        {
            var before = await SiteSessions.SignedIn(core);
            Assert.Equal("X:False TikTok:False Instagram:False", string.Join(" ", before.Select(p => $"{p.Key}:{p.Value}")), "a new profile");

            Add(core, "auth_token", ".x.com");
            Add(core, "ct0", ".x.com");
            Add(core, "sessionid", ".instagram.com");
            Add(core, "keep", ".example.com");
            var signedIn = await SiteSessions.SignedIn(core);
            Assert.Equal("X:True TikTok:False Instagram:True", string.Join(" ", signedIn.Select(p => $"{p.Key}:{p.Value}")), "signed in to X and Instagram");

            await SiteSessions.SignOut(core, SharePlatform.X);
            var after = await SiteSessions.SignedIn(core);
            Assert.Equal("X:False TikTok:False Instagram:True", string.Join(" ", after.Select(p => $"{p.Key}:{p.Value}")), "only X signed out");
            var left = (await core.CookieManager.GetCookiesAsync(null)).Select(c => c.Name).OrderBy(n => n).ToList();
            Assert.Equal("keep sessionid", string.Join(" ", left), "X's cookies gone, the others kept");
        });
        Assert.True(failure == null, failure ?? "");
    }
}
