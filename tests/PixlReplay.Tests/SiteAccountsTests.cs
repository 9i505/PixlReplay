using PixlReplay.Engine.Share;

namespace PixlReplay.Tests;

/// <summary>Which saved cookies mean "signed in" to X, TikTok or Instagram, and which belong to each site.</summary>
public static class SiteAccountsTests
{
    [Test]
    public static void A_site_counts_as_signed_in_only_with_its_own_session_cookie()
    {
        var cookies = new[]
        {
            new SiteCookie("auth_token", ".x.com", "abc"),
            new SiteCookie("guest_id", ".x.com", "v1"),
            new SiteCookie("sessionid", ".instagram.com", "123"),
            new SiteCookie("sessionid", ".example.com", "zzz"),
        };
        Assert.True(SiteAccounts.IsSignedIn(SharePlatform.X, cookies), "X");
        Assert.True(SiteAccounts.IsSignedIn(SharePlatform.Instagram, cookies), "Instagram");
        Assert.True(!SiteAccounts.IsSignedIn(SharePlatform.TikTok, cookies), "TikTok: another site's sessionid does not count");
        Assert.True(SiteAccounts.IsSignedIn(SharePlatform.TikTok, [new SiteCookie("sessionid", ".tiktok.com", "s")]), "TikTok");
        Assert.True(!SiteAccounts.IsSignedIn(SharePlatform.X, [new SiteCookie("auth_token", ".x.com", "")]), "an emptied cookie is signed out");
    }

    [Test]
    public static void Cookies_of_a_site_and_its_subdomains_are_its_own_and_lookalikes_are_not()
    {
        Assert.True(SiteAccounts.Owns(SharePlatform.X, ".x.com") && SiteAccounts.Owns(SharePlatform.X, "api.x.com"), "x.com");
        Assert.True(SiteAccounts.Owns(SharePlatform.X, ".twitter.com"), "the old twitter.com");
        Assert.True(!SiteAccounts.Owns(SharePlatform.X, "max.com") && !SiteAccounts.Owns(SharePlatform.X, "x.com.evil.net"), "lookalikes");
        Assert.True(SiteAccounts.Owns(SharePlatform.TikTok, "www.tiktok.com") && !SiteAccounts.Owns(SharePlatform.TikTok, ".instagram.com"), "TikTok");
    }

    [Test]
    public static void The_last_sign_ins_are_kept_one_line_per_site_and_read_back()
    {
        var state = new Dictionary<SharePlatform, bool> { [SharePlatform.X] = true, [SharePlatform.TikTok] = false, [SharePlatform.Instagram] = true };
        string text = SiteAccounts.FormatKnown(state);
        Assert.Equal("X=1\nTikTok=0\nInstagram=1", text.Replace("\r", ""), "the file");
        var back = SiteAccounts.ParseKnown(text);
        Assert.True(back is { Count: 3 } && back[SharePlatform.X] && !back[SharePlatform.TikTok] && back[SharePlatform.Instagram], "read back");
        Assert.True(SiteAccounts.ParseKnown("") == null && SiteAccounts.ParseKnown("garbage\nCopy=1") == null, "nothing usable: not known");
        var partial = SiteAccounts.ParseKnown("X=1");
        Assert.True(partial is { Count: 3 } && partial[SharePlatform.X] && !partial[SharePlatform.TikTok], "a site not listed: not signed in");
    }

    [Test]
    public static void Each_site_has_its_sign_in_page()
    {
        foreach (var p in SiteAccounts.Sites)
        {
            var url = new Uri(SiteAccounts.SignInUrl(p));
            Assert.True(url.Scheme == "https" && SiteAccounts.Owns(p, url.Host), $"{p}: {url}");
        }
        Assert.Equal("X TikTok Instagram", string.Join(" ", SiteAccounts.Sites), "the sites, in the panel's order");
    }
}
