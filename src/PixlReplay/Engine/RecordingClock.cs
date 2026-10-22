namespace PixlReplay.Engine;

/// <summary>How long a full recording has been recording: the time since it started, without its pauses.</summary>
public sealed class RecordingClock
{
    TimeSpan _before;
    DateTime? _since;

    public bool Paused => _since == null;

    public void Start(DateTime now)
    {
        _before = TimeSpan.Zero;
        _since = now;
    }

    public void Pause(DateTime now)
    {
        if (_since is { } since)
            _before += now - since;
        _since = null;
    }

    public void Resume(DateTime now) => _since ??= now;

    public double Seconds(DateTime now) => (_before + (_since is { } since ? now - since : TimeSpan.Zero)).TotalSeconds;
}
