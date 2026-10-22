using System.ComponentModel;
using System.Windows.Input;
using PixlReplay.App;
using PixlReplay.Engine.Share;

namespace PixlReplay.Ui;

/// <summary>The site accounts in PixlReplay's own browser profile: what is signed in, signing in, signing out.</summary>
public interface IAccountsHost
{
    /// <summary>The Edge WebView2 runtime is on this PC (always on Windows 11).</summary>
    bool Available { get; }
    Task<IReadOnlyDictionary<SharePlatform, bool>> SignedIn();
    /// <summary>Shows the site's sign-in page; true once signed in, false when the window was closed first.</summary>
    Task<bool> SignIn(SharePlatform platform);
    Task SignOut(SharePlatform platform);
    Task SignOutAll();
    /// <summary>What the last check found (null before the first one): shown at once while the page checks again.</summary>
    IReadOnlyDictionary<SharePlatform, bool>? LastKnown();
    void Remember(IReadOnlyDictionary<SharePlatform, bool> state);
}

/// <summary>One site on the Connections page.</summary>
public sealed class AccountRow(SharePlatform platform, ICommand action) : INotifyPropertyChanged
{
    bool? _connected;
    bool _busy;

    public SharePlatform Platform { get; } = platform;
    public string Name => ShareViewModel.PlatformName(Platform);
    public string Logo => $"pack://application:,,,/PixlReplay;component/Ui/Assets/{Platform.ToString().ToLowerInvariant()}.png";
    public ICommand ActionCommand { get; } = action;

    /// <summary>Null while not known yet.</summary>
    public bool? Connected
    {
        get => _connected;
        set { _connected = value; Changed(); }
    }

    public bool IsBusy
    {
        get => _busy;
        set { _busy = value; Changed(); }
    }

    public bool IsConnected => _connected == true;
    /// <summary>Known to be signed out (while not known yet, neither sign in nor sign out is offered).</summary>
    public bool ShowSignIn => _connected == false;
    public string StatusText => _busy ? Text.Get("accounts.working")
        : _connected switch { true => Text.Get("accounts.connected"), false => Text.Get("accounts.notConnected"), null => Text.Get("accounts.checking") };
    public string ActionText => Text.Get(_connected == true ? "accounts.signOut" : "accounts.signIn");

    public event PropertyChangedEventHandler? PropertyChanged;
    void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}

/// <summary>The Connections page: X, TikTok and Instagram, each connected or not, with sign in / sign out.</summary>
public sealed class ConnectionsViewModel : INotifyPropertyChanged
{
    readonly IAccountsHost _host;
    bool _busy;

    public ConnectionsViewModel(IAccountsHost host)
    {
        _host = host;
        Accounts = SiteAccounts.Sites.Select(p => new AccountRow(p, new RelayCommand(_ => _ = Toggle(p), _ => CanAct(p)))).ToList();
        SignOutAllCommand = new RelayCommand(_ => _ = SignOutAll(), _ => !_busy && Accounts.Any(a => a.IsConnected));
        // Checking takes about half a second (it starts PixlReplay's browser): meanwhile show what the last check found.
        if (_host.Available && _host.LastKnown() is { } known)
            foreach (var row in Accounts)
                row.Connected = known.TryGetValue(row.Platform, out bool on) && on;
    }

    public IReadOnlyList<AccountRow> Accounts { get; }
    public ICommand SignOutAllCommand { get; }
    public bool Available => _host.Available;
    public string UnavailableText => Text.Get("accounts.unavailable");

    public event PropertyChangedEventHandler? PropertyChanged;

    AccountRow Row(SharePlatform p) => Accounts.Single(a => a.Platform == p);

    bool CanAct(SharePlatform p) => !_busy && Row(p).Connected != null;

    /// <summary>Reads each site's sign-in from the profile.</summary>
    public async Task Refresh()
    {
        if (!_host.Available)
            return;
        IReadOnlyDictionary<SharePlatform, bool> state;
        try
        {
            state = await _host.SignedIn();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or IOException)
        {
            Engine.ObsLog.Warn("accounts: " + e.Message);
            return; // the rows keep what they showed
        }
        foreach (var row in Accounts)
            row.Connected = state.TryGetValue(row.Platform, out bool on) && on;
        _host.Remember(state);
        Settled();
    }

    async Task Toggle(SharePlatform p)
    {
        var row = Row(p);
        _busy = true;
        row.IsBusy = true;
        Settled();
        try
        {
            if (row.IsConnected)
                await _host.SignOut(p);
            else
                await _host.SignIn(p);
        }
        finally
        {
            row.IsBusy = false;
            _busy = false;
        }
        await Refresh();
    }

    async Task SignOutAll()
    {
        _busy = true;
        foreach (var row in Accounts)
            row.IsBusy = row.IsConnected;
        Settled();
        try
        {
            await _host.SignOutAll();
        }
        finally
        {
            foreach (var row in Accounts)
                row.IsBusy = false;
            _busy = false;
        }
        await Refresh();
    }

    /// <summary>Buttons re-check whether they can act (after work that did not come from a click).</summary>
    void Settled()
    {
        CommandManager.InvalidateRequerySuggested();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
