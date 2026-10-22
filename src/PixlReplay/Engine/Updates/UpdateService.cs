using Velopack;
using Velopack.Sources;

namespace PixlReplay.Engine.Updates;

/// <summary>What the update service needs from Velopack (a stand-in in tests).</summary>
public interface IUpdater
{
    /// <summary>The newer version available, or null.</summary>
    Task<string?> Check();
    Task Download(Action<int> progress, CancellationToken ct);
    /// <summary>Starts Velopack's updater, which waits for PixlReplay to exit, applies the update and starts PixlReplay again.</summary>
    void ApplyAndExit(string[] restartArgs);
}

/// <summary>
/// PixlReplay's updates, in the engine: looks for a newer version, says so once, downloads it when the user presses Update
/// and restarts into it (never during a recording). Failures are logged, never shown, and tried again the next day.
/// </summary>
public sealed class UpdateService(IUpdater? updater, string seenFile, bool localSource)
{
    volatile UpdateStatus? _status;
    volatile bool _downloaded;

    public UpdateStatus? Status => _status;

    public DateTime? LastCheck { get; private set; }

    /// <summary>Off (a development build, not installed by the Setup).</summary>
    public bool Available => updater != null;

    /// <summary>A version not announced before (once per version, kept across restarts).</summary>
    public event Action<string>? NewVersion;

    /// <summary>The service for this PixlReplay: Velopack's when installed, from GitHub or a local test folder.</summary>
    public static UpdateService ForThisInstall(string appDataDir)
    {
        string seen = Path.Combine(appDataDir, "update-seen.txt");
        if (App.InstallPaths.Root == null)
            return new UpdateService(null, seen, localSource: false);
        string? folder = UpdateRules.LocalFeed(appDataDir);
        var manager = folder != null
            ? new UpdateManager(folder)
            : new UpdateManager(new GithubSource(UpdateRules.Repository, null, prerelease: AppInfo.Version.Contains('-')));
        return new UpdateService(new VelopackUpdater(manager), seen, folder != null);
    }

    public async Task Check(DateTime now)
    {
        if (updater == null)
            return;
        LastCheck = now;
        try
        {
            if (await updater.Check().ConfigureAwait(false) is not { } version)
                return;
            if (_status?.Version != version)
            {
                _downloaded = false;
                _status = new UpdateStatus(version, UpdateState.Available, 0, UpdateRules.NotesUrl(version, localSource));
            }
            var seen = ReadSeen();
            if (UpdateRules.ShouldNotify(version, seen))
            {
                File.AppendAllLines(seenFile, [version]);
                NewVersion?.Invoke(version);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            ObsLog.Warn("update check: " + e.Message);
        }
    }

    /// <summary>Downloads the update found; when it goes in is the engine's call afterwards (<see cref="Settle"/>).</summary>
    public async Task Download()
    {
        if (updater == null || _status is not { State: UpdateState.Available or UpdateState.Failed } status)
            return;
        _status = status with { State = UpdateState.Downloading, Percent = 0 };
        try
        {
            await updater.Download(p => _status = _status! with { Percent = p }, CancellationToken.None).ConfigureAwait(false);
            _status = _status! with { Percent = 100 };
            _downloaded = true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            ObsLog.Warn("update download: " + e.Message);
            _status = _status! with { State = UpdateState.Failed };
        }
    }

    /// <summary>
    /// Hands over to Velopack's updater; PixlReplay must exit right after. <paramref name="openWindow"/>: PixlReplay comes back with
    /// its window (the user pressed Update there); otherwise only in the tray (an update applied after a recording).
    /// </summary>
    public void Apply(bool openWindow)
    {
        try
        {
            updater?.ApplyAndExit(openWindow ? [] : ["--background"]);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // PixlReplay still leaves (half closed, it would keep its lock and never start again); the next start is the old one.
            ObsLog.Warn("update apply: " + e.Message);
        }
    }

    /// <summary>
    /// On the engine's thread, where recordings start and stop: true when the downloaded update goes in now; otherwise it
    /// waits for the recording or the window (asked again on the tray's tick and when a recording ends).
    /// </summary>
    public bool Settle(bool recording, bool windowBusy)
    {
        if (!_downloaded || _status is not { } status)
            return false;
        _status = status with { State = UpdateRules.WhenDownloaded(recording, windowBusy) };
        return _status.State == UpdateState.Ready;
    }

    HashSet<string> ReadSeen()
    {
        try
        {
            return File.Exists(seenFile) ? [.. File.ReadAllLines(seenFile).Select(l => l.Trim()).Where(l => l.Length > 0)] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    sealed class VelopackUpdater(UpdateManager manager) : IUpdater
    {
        UpdateInfo? _found;

        public async Task<string?> Check()
        {
            _found = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            return _found?.TargetFullRelease.Version.ToString();
        }

        public Task Download(Action<int> progress, CancellationToken ct) =>
            _found == null ? Task.CompletedTask : manager.DownloadUpdatesAsync(_found, progress, ct);

        public void ApplyAndExit(string[] restartArgs) =>
            manager.WaitExitThenApplyUpdates(_found?.TargetFullRelease, silent: true, restart: true, restartArgs);
    }
}
