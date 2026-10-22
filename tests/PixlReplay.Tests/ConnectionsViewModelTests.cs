using PixlReplay.App;
using PixlReplay.Engine.Share;
using PixlReplay.Ui;

namespace PixlReplay.Tests;

/// <summary>The Connections page: which site accounts PixlReplay can post to, signing in and out.</summary>
public static class ConnectionsViewModelTests
{
    sealed class FakeAccounts : IAccountsHost
    {
        public bool Available { get; set; } = true;
        public readonly Dictionary<SharePlatform, bool> State = new() { [SharePlatform.X] = true, [SharePlatform.TikTok] = false, [SharePlatform.Instagram] = true };
        public readonly List<string> Calls = [];
        public TaskCompletionSource<bool>? PendingSignIn;
        /// <summary>When set, the check waits for it (a real check starts a browser and takes about half a second).</summary>
        public TaskCompletionSource<IReadOnlyDictionary<SharePlatform, bool>>? PendingCheck;
        public IReadOnlyDictionary<SharePlatform, bool>? Known;

        public Task<IReadOnlyDictionary<SharePlatform, bool>> SignedIn()
        {
            Calls.Add("read");
            if (PendingCheck != null)
                return PendingCheck.Task;
            return Task.FromResult<IReadOnlyDictionary<SharePlatform, bool>>(new Dictionary<SharePlatform, bool>(State));
        }

        public IReadOnlyDictionary<SharePlatform, bool>? LastKnown() => Known;
        public void Remember(IReadOnlyDictionary<SharePlatform, bool> state) => Known = new Dictionary<SharePlatform, bool>(state);

        public Task<bool> SignIn(SharePlatform platform)
        {
            Calls.Add("in " + platform);
            if (PendingSignIn != null)
                return PendingSignIn.Task;
            State[platform] = true;
            return Task.FromResult(true);
        }

        public Task SignOut(SharePlatform platform)
        {
            Calls.Add("out " + platform);
            State[platform] = false;
            return Task.CompletedTask;
        }

        public Task SignOutAll()
        {
            Calls.Add("out all");
            foreach (var p in State.Keys.ToList())
                State[p] = false;
            return Task.CompletedTask;
        }
    }

    static (ConnectionsViewModel Vm, FakeAccounts Host) Open()
    {
        Text.Language = "en";
        var host = new FakeAccounts();
        var vm = new ConnectionsViewModel(host);
        _ = vm.Refresh();
        return (vm, host);
    }

    static AccountRow Row(ConnectionsViewModel vm, SharePlatform p) => vm.Accounts.Single(a => a.Platform == p);

    [Test]
    public static void Each_site_shows_whether_it_is_connected_and_offers_the_matching_button()
    {
        var (vm, _) = Open();
        Assert.Equal("X TikTok Instagram", string.Join(" ", vm.Accounts.Select(a => a.Name)), "the three sites");
        var x = Row(vm, SharePlatform.X);
        var tiktok = Row(vm, SharePlatform.TikTok);
        Assert.Equal(("Connected", "Sign out"), (x.StatusText, x.ActionText), "X");
        Assert.Equal(("Not connected", "Sign in"), (tiktok.StatusText, tiktok.ActionText), "TikTok");
        Assert.True(x.Logo.EndsWith("x.png") && tiktok.Logo.EndsWith("tiktok.png"), x.Logo);
        Assert.True(vm.SignOutAllCommand.CanExecute(null), "something to sign out of");
    }

    [Test]
    public static void Signing_in_opens_the_sites_sign_in_and_then_shows_it_connected()
    {
        var (vm, host) = Open();
        var tiktok = Row(vm, SharePlatform.TikTok);
        tiktok.ActionCommand.Execute(null);
        Assert.True(host.Calls.Contains("in TikTok"), string.Join(", ", host.Calls));
        Assert.Equal("Connected", tiktok.StatusText, "connected now");
    }

    [Test]
    public static void Signing_out_of_one_site_leaves_the_others_signed_in()
    {
        var (vm, host) = Open();
        Row(vm, SharePlatform.X).ActionCommand.Execute(null);
        Assert.True(host.Calls.Contains("out X"), string.Join(", ", host.Calls));
        Assert.Equal(("Not connected", "Connected"), (Row(vm, SharePlatform.X).StatusText, Row(vm, SharePlatform.Instagram).StatusText), "X only");
    }

    [Test]
    public static void Signing_out_of_all_accounts_clears_every_site()
    {
        var (vm, host) = Open();
        vm.SignOutAllCommand.Execute(null);
        Assert.True(host.Calls.Contains("out all"), string.Join(", ", host.Calls));
        Assert.True(vm.Accounts.All(a => a.StatusText == "Not connected"), "all signed out");
        Assert.True(!vm.SignOutAllCommand.CanExecute(null), "nothing left to sign out of");
    }

    [Test]
    public static void While_a_sign_in_window_is_open_its_row_waits_and_the_buttons_rest()
    {
        var (vm, host) = Open();
        host.PendingSignIn = new TaskCompletionSource<bool>();
        var tiktok = Row(vm, SharePlatform.TikTok);
        tiktok.ActionCommand.Execute(null);
        Assert.True(tiktok.IsBusy && !tiktok.ActionCommand.CanExecute(null), "waiting");
        Assert.True(!vm.SignOutAllCommand.CanExecute(null), "no sign out meanwhile");
        host.State[SharePlatform.TikTok] = true;
        host.PendingSignIn.SetResult(true);
        Assert.True(!tiktok.IsBusy && tiktok.StatusText == "Connected", tiktok.StatusText);
    }

    [Test]
    public static void The_page_shows_the_last_known_sign_ins_at_once_and_the_check_then_confirms_them()
    {
        Text.Language = "en";
        var host = new FakeAccounts
        {
            Known = new Dictionary<SharePlatform, bool> { [SharePlatform.X] = true, [SharePlatform.TikTok] = false, [SharePlatform.Instagram] = true },
            PendingCheck = new(),
        };
        var vm = new ConnectionsViewModel(host);
        _ = vm.Refresh();
        var x = Row(vm, SharePlatform.X);
        var instagram = Row(vm, SharePlatform.Instagram);
        Assert.Equal(("Connected", "Sign out", false), (x.StatusText, x.ActionText, x.ShowSignIn), "X as last time, before the check ends");
        Assert.Equal(("Not connected", true), (Row(vm, SharePlatform.TikTok).StatusText, Row(vm, SharePlatform.TikTok).ShowSignIn), "TikTok as last time");
        // The check ends: Instagram was signed out since.
        host.PendingCheck.SetResult(new Dictionary<SharePlatform, bool> { [SharePlatform.X] = true, [SharePlatform.TikTok] = false, [SharePlatform.Instagram] = false });
        Assert.Equal(("Connected", "Not connected"), (x.StatusText, instagram.StatusText), "as the check found");
        Assert.True(host.Known is { } known && !known[SharePlatform.Instagram] && known[SharePlatform.X], "kept for next time");
    }

    [Test]
    public static void Before_the_first_check_a_site_says_it_is_checking_and_offers_no_button()
    {
        Text.Language = "en";
        var host = new FakeAccounts { PendingCheck = new() };
        var vm = new ConnectionsViewModel(host);
        _ = vm.Refresh();
        var x = Row(vm, SharePlatform.X);
        Assert.Equal("Checking…", x.StatusText, "not known yet");
        Assert.True(!x.ShowSignIn && !x.IsConnected, "neither sign in nor sign out shown");
        Assert.True(!x.ActionCommand.CanExecute(null), "nothing to do yet");
        host.PendingCheck.SetResult(new Dictionary<SharePlatform, bool>(host.State));
        Assert.True(x.IsConnected && !x.ShowSignIn, "known now");
    }

    [Test]
    public static void Without_the_browser_the_page_says_what_is_missing()
    {
        Text.Language = "en";
        var host = new FakeAccounts { Available = false };
        var vm = new ConnectionsViewModel(host);
        _ = vm.Refresh();
        Assert.True(!vm.Available && vm.UnavailableText.Contains("WebView2"), vm.UnavailableText);
        Assert.True(!host.Calls.Contains("read"), "nothing read");
    }
}
