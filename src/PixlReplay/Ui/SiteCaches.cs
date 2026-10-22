using Microsoft.Web.WebView2.Core;

namespace PixlReplay.Ui;

/// <summary>
/// The sharing browser keeps the sign-ins (cookies, site storage) but not the sites' caches: X and TikTok had piled up
/// 650 MB in 3 days. The caches are cleared after each post and the disk cache has a ceiling in between.
/// </summary>
public static class SiteCaches
{
    /// <summary>What a clear takes: downloaded files and the pages' own caches, never cookies or site storage.</summary>
    public const CoreWebView2BrowsingDataKinds Kinds = CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage;

    /// <summary>The browser's disk cache stays under 64 MB between clears.</summary>
    public const string BrowserArguments = "--disk-cache-size=67108864";

    public static async Task Clear(CoreWebView2Profile profile)
    {
        try
        {
            await profile.ClearBrowsingDataAsync(Kinds);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // A browser closing under it: the next post clears it.
        }
    }
}
