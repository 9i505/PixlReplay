namespace PixlReplay.Engine;

public sealed record EngineStatus(
    bool BufferActive,
    bool Recording,
    double Fps,
    uint LaggedFrames,
    uint SkippedFrames,
    uint TotalFrames,
    int BufferSeconds,
    int BufferTargetSeconds,
    string Encoder,
    int Width,
    int Height,
    long WorkingSetMb,
    int EstimatedBufferMb,
    string ClipsFolder,
    bool FolderFellBack,
    string? LastError,
    bool RecordingPaused = false,
    double RecordingSeconds = 0,
    long RecordingBytes = 0);
