using System.Windows;
using System.Windows.Interop;
using PixlReplay.Engine.Share;

namespace PixlReplay.Ui;

/// <summary>The Connections page's link to PixlReplay's site browser profile; the invisible browser lives in <paramref name="owner"/>.</summary>
public sealed class WpfAccountsHost(Window owner) : IAccountsHost
{
    IntPtr Handle => new WindowInteropHelper(owner).EnsureHandle();

    public bool Available => ShareSites.Available;
    public Task<IReadOnlyDictionary<SharePlatform, bool>> SignedIn() => ShareSites.SignedIn(Handle);
    public Task<bool> SignIn(SharePlatform platform) => ShareSites.SignIn(platform);
    public Task SignOut(SharePlatform platform) => ShareSites.SignOut(platform, Handle);
    public Task SignOutAll() => ShareSites.SignOut();
    public IReadOnlyDictionary<SharePlatform, bool>? LastKnown() => ShareSites.LastKnown();
    public void Remember(IReadOnlyDictionary<SharePlatform, bool> state) => ShareSites.Remember(state);
}
