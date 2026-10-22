using System.ComponentModel;
using System.Windows.Input;
using PixlReplay.App;
using PixlReplay.Engine.Share;

namespace PixlReplay.Ui;

/// <summary>One site of a post to all of them: where it is (waiting, preparing, posting, posted...) and what to open after.</summary>
public sealed class SiteJob : INotifyPropertyChanged
{
    enum State { Waiting, Working, Posted, NeedsUser, Failed, Cancelled }

    State _state;
    string _status;

    public SiteJob(SharePlatform platform, Action<SiteJob> open)
    {
        Platform = platform;
        _status = Text.Get("share.job.waiting");
        OpenCommand = new RelayCommand(_ => open(this), _ => CanOpen);
    }

    public SharePlatform Platform { get; }
    public string Name => ShareViewModel.PlatformName(Platform);
    public string Logo => $"pack://application:,,,/PixlReplay;component/Ui/Assets/{Platform.ToString().ToLowerInvariant()}.png";
    public ICommand OpenCommand { get; }

    public string StatusText => _status;
    /// <summary>Why it did not post (shown on hover).</summary>
    public string Detail { get; private set; } = "";
    public string? PostUrl { get; private set; }

    public bool IsBusy => _state == State.Working;
    public bool IsPosted => _state == State.Posted;
    public bool NeedsUser => _state == State.NeedsUser;
    public bool IsFailed => _state == State.Failed;
    public bool CanOpen => _state is State.Posted or State.NeedsUser;
    public string OpenLabel => Text.Get(NeedsUser ? "share.job.finish" : "share.job.open");

    internal void Preparing(double progress) => Set(State.Working, Text.Format("share.job.preparing", (int)Math.Round(progress * 100)));
    internal void Status(string key) => Set(State.Working, Text.Get(key));

    internal void Finished(PublishOutcome outcome)
    {
        PostUrl = outcome.PostUrl;
        switch (outcome.Result)
        {
            case PublishResult.Posted: Set(State.Posted, Text.Get("share.job.posted")); break;
            case PublishResult.NeedsUser: Set(State.NeedsUser, Text.Get("share.job.needsUser")); break;
            default: Cancelled(); break;
        }
    }

    internal void Failed(string reason)
    {
        Detail = reason;
        Set(State.Failed, Text.Get("share.job.failed"));
    }

    internal void Cancelled() => Set(State.Cancelled, Text.Get("share.job.cancelled"));

    void Set(State state, string status)
    {
        _state = state;
        _status = status;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
