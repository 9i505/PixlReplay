using System.Windows;
using System.Windows.Threading;
using PixlReplay.Ui;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace PixlReplay.Tests;

/// <summary>
/// The sharing browser keeps sign-ins but not the sites' caches (650 MB had piled up in 3 days): checked in a hidden
/// WebView2 on a local page (no network, nothing on screen, a throwaway profile).
/// </summary>
public static class SiteCacheTests
{
    [Test]
    public static void Clearing_takes_caches_only_never_sign_ins()
    {
        var kinds = SiteCaches.Kinds;
        Assert.True(kinds.HasFlag(CoreWebView2BrowsingDataKinds.DiskCache) && kinds.HasFlag(CoreWebView2BrowsingDataKinds.CacheStorage), "caches");
        foreach (var kept in new[] { CoreWebView2BrowsingDataKinds.Cookies, CoreWebView2BrowsingDataKinds.LocalStorage,
                     CoreWebView2BrowsingDataKinds.IndexedDb, CoreWebView2BrowsingDataKinds.AllDomStorage })
            Assert.True((kinds & kept) == 0, $"{kept} stays (it holds the sign-in)");
        Assert.True(SiteCaches.BrowserArguments.Contains("--disk-cache-size="), "the disk cache has a ceiling between clears");
    }

    [Test]
    public static void A_page_cache_goes_and_its_cookie_stays()
    {
        string root = Path.Combine(Path.GetTempPath(), "pixlreplay-tests", "sitecache-" + Guid.NewGuid().ToString("N"));
        string site = Path.Combine(root, "site"), profile = Path.Combine(root, "profile");
        Directory.CreateDirectory(site);
        File.WriteAllText(Path.Combine(site, "index.html"), "<!doctype html><title>t</title>");
        string? problem = "timed out";
        var thread = new Thread(() =>
        {
            var window = WindowPlacement.OffScreen(new Window { Width = 400, Height = 300, WindowStyle = WindowStyle.None });
            var web = new WebView2();
            window.Content = web;
            window.Loaded += async (_, _) =>
            {
                try
                {
                    var env = await CoreWebView2Environment.CreateAsync(null, profile,
                        new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = SiteCaches.BrowserArguments });
                    await web.EnsureCoreWebView2Async(env);
                    var core = web.CoreWebView2;
                    core.SetVirtualHostNameToFolderMapping("pixlreplay.test", site, CoreWebView2HostResourceAccessKind.Allow);
                    var loaded = new TaskCompletionSource();
                    core.NavigationCompleted += (_, _) => loaded.TrySetResult();
                    core.Navigate("https://pixlreplay.test/index.html");
                    await loaded.Task;
                    await core.ExecuteScriptAsync(
                        "document.cookie = 'signedin=1; max-age=3600; secure';" +
                        "caches.open('site').then(c => c.put('/big', new Response('x'.repeat(2000000)))).then(() => document.title = 'cached')");
                    for (int i = 0; i < 50 && core.DocumentTitle != "cached"; i++)
                        await Task.Delay(100);
                    string before = await Wait(core, "(async () => (await caches.keys()).length)()");
                    await SiteCaches.Clear(core.Profile);
                    string after = await Wait(core, "(async () => (await caches.keys()).length)()");
                    var cookies = await core.CookieManager.GetCookiesAsync("https://pixlreplay.test");
                    problem = core.DocumentTitle != "cached" ? "the page could not fill its cache"
                        : before != "1" ? $"cache before: {before}"
                        : after != "0" ? $"cache after clearing: {after}"
                        : !cookies.Any(c => c.Name == "signedin") ? "the sign-in cookie was cleared"
                        : null;
                }
                catch (Exception e)
                {
                    problem = e.GetType().Name + ": " + e.Message;
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
        Thread.Sleep(500); // the browser lets go of its files
        try { Directory.Delete(root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Assert.Equal(null, problem, "cache cleared, sign-in kept");
    }

    /// <summary>A promise's value (ExecuteScriptAsync gives "{}" for a promise, so it is stored and read back).</summary>
    static async Task<string> Wait(CoreWebView2 core, string promise)
    {
        await core.ExecuteScriptAsync($"window.__r = undefined; ({promise}).then(v => window.__r = String(v))");
        for (int i = 0; i < 50; i++)
        {
            string value = await core.ExecuteScriptAsync("window.__r === undefined ? null : window.__r");
            if (value != "null")
                return value.Trim('"');
            await Task.Delay(100);
        }
        return "no answer";
    }
}
