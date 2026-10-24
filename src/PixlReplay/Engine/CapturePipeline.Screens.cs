using PixlReplay.App;
using PixlReplay.Native;
using PixlReplay.Settings;

namespace PixlReplay.Engine;

// More screens (Capture page, "More screens"): each extra screen is recorded beside the main picture, with its own
// capture and scene mixed at its own size in a libobs view, its own video encoder and its own replay buffers (in RAM:
// the disk ring is the main picture's). The sound is the main picture's (the same audio encoders), so every screen's
// clip has it. A save, a quick save or a recording makes one file per screen.
public sealed partial class CapturePipeline
{
    sealed class ExtraScreen(int number, MonitorInfo monitor)
    {
        public int Number => number;
        public MonitorInfo Monitor => monitor;
        public (int W, int H) Output;
        public EncoderPlan? Plan;
        public IntPtr Scene, View, Video, Encoder, Replay, Quick, Record;
        public ObsSignalCallback? OnReplaySaved, OnQuickSaved;
    }

    readonly List<ExtraScreen> _extras = [];

    /// <summary>The most extra screens recorded at once (each takes an encoder and its own buffer in memory).</summary>
    public const int MaxExtraScreens = 3;

    /// <summary>The main picture's screen number (as the Capture page counts them) when it is a screen, else null.</summary>
    public int? MainScreen { get; private set; }

    /// <summary>The extra screens being recorded (their numbers), beside the main picture.</summary>
    public IReadOnlyList<int> ExtraScreens => _extras.Select(e => e.Number).ToList();

    /// <summary>Raised on a libobs thread with an extra screen's raw clip path, whether it was the quick buffer, and its number.</summary>
    public event Action<string, bool, int>? ScreenSaved;

    /// <summary>The screens' number as the Capture page shows them: their place in libobs's list, from 1.</summary>
    public static int ScreenNumber(IReadOnlyList<MonitorInfo> monitors, string id) =>
        monitors.Select((m, i) => (m, i)).FirstOrDefault(p => p.m.Id == id) is { m: not null } found ? found.i + 1 : 0;

    /// <summary>Which screens to record beside the main picture: the chosen ones still connected, never the main one.</summary>
    void PickExtras(IReadOnlyList<MonitorInfo> monitors, MonitorInfo? main)
    {
        _extras.Clear();
        MainScreen = _mode == CaptureMode.Screen && main != null ? ScreenNumber(monitors, main.Id) : null;
        foreach (string id in _s.Capture.ExtraMonitors.Distinct().Take(MaxExtraScreens))
            if (monitors.FirstOrDefault(m => m.Id == id) is { } extra && !(MainScreen != null && extra.Id == main!.Id))
                _extras.Add(new ExtraScreen(ScreenNumber(monitors, extra.Id), extra));
    }

    void BuildExtras()
    {
        foreach (var extra in _extras.ToList())
        {
            try
            {
                BuildExtra(extra);
                ObsLog.Info($"extra screen {extra.Number} built: {extra.Monitor.Width}x{extra.Monitor.Height} -> " +
                            $"{extra.Output.W}x{extra.Output.H}@{Fps} max={extra.Plan!.MaxBitrateKbps}kbps");
            }
            catch (Exception e)
            {
                // One screen that cannot be recorded (no encoder left, the screen gone) never stops the main one.
                ObsLog.Warn($"extra screen {extra.Number} not recorded: {e.Message}");
                ReleaseExtra(extra);
                _extras.Remove(extra);
            }
        }
    }

    /// <summary>An extra screen's capture, its picture mixed at its own size, its encoder and its buffers.</summary>
    void BuildExtra(ExtraScreen x)
    {
        var m = x.Monitor;
        x.Output = VideoMath.OutputSize(m.Width, m.Height, _video.Resolution);
        if (Choice!.Vendor == EncoderVendor.Software)
            x.Output = EncoderCatalog.LimitForSoftware(x.Output, Fps).Item1;

        x.Scene = Obs.obs_scene_create_private($"PixlReplay screen {x.Number}");
        IntPtr source = CreateMonitorSource(m.Id);
        IntPtr item = Obs.obs_scene_add(x.Scene, source);
        Obs.obs_sceneitem_set_bounds_type(item, ObsConst.BoundsScaleInner);
        Obs.obs_sceneitem_set_bounds_alignment(item, ObsConst.AlignCenter);
        var bounds = new Vec2(m.Width, m.Height);
        var pos = new Vec2(0, 0);
        Obs.obs_sceneitem_set_bounds(item, ref bounds);
        Obs.obs_sceneitem_set_pos(item, ref pos);
        x.View = Obs.obs_view_create();
        Obs.obs_view_set_source(x.View, 0, Obs.obs_scene_get_source(x.Scene));
        var ovi = ObsHost.VideoInfo(new VideoConfig(m.Width, m.Height, x.Output.W, x.Output.H, Fps));
        x.Video = Obs.obs_view_add2(x.View, ref ovi);
        if (x.Video == IntPtr.Zero)
            throw new InvalidOperationException("libobs could not mix this screen's picture");

        x.Plan = EncoderPresets.Plan(Choice.Codec, _video.Quality, x.Output.W, x.Output.H, Fps,
            _video.CustomCq, _video.CustomMbps, Choice.Vendor != EncoderVendor.Software, _video.Preset);
        x.Encoder = CreateVideoEncoder(x.Plan, $"PixlReplay video {x.Number}");
        Obs.obs_encoder_set_video(x.Encoder, x.Video);

        x.Replay = ExtraReplay($"PixlReplay replay {x.Number}", $"PixlReplay screen {x.Number} %CCYY-%MM-%DD %hh-%mm-%ss", _s.Replay.Seconds, x);
        x.OnReplaySaved = (_, _) => OnScreenSaved(x.Replay, quick: false, x.Number);
        Obs.signal_handler_connect(Obs.obs_output_get_signal_handler(x.Replay), "saved", x.OnReplaySaved, IntPtr.Zero);
        if (_s.Replay.QuickEnabled)
        {
            x.Quick = ExtraReplay($"PixlReplay quick {x.Number}", $"PixlReplay screen {x.Number} quick %CCYY-%MM-%DD %hh-%mm-%ss",
                _s.Replay.QuickSeconds, x);
            x.OnQuickSaved = (_, _) => OnScreenSaved(x.Quick, quick: true, x.Number);
            Obs.signal_handler_connect(Obs.obs_output_get_signal_handler(x.Quick), "saved", x.OnQuickSaved, IntPtr.Zero);
        }
    }

    /// <summary>An extra screen's replay buffer in RAM: every screen keeps one, together within the memory one would have.</summary>
    IntPtr ExtraReplay(string name, string format, int seconds, ExtraScreen x)
    {
        long memory = BufferBudget.PhysicalMemoryBytes / (1 + _extras.Count);
        int mb = BufferBudget.CapMb(EncoderPresets.EstimateBufferMb(x.Plan!.MaxBitrateKbps, seconds), memory);
        IntPtr output = NewReplay(name, ClipsFolder, format, seconds, mb);
        AttachEncoders(output, x.Encoder);
        return output;
    }

    /// <summary>The picture from <paramref name="video"/>, the sound from the main picture's tracks.</summary>
    void AttachEncoders(IntPtr output, IntPtr video)
    {
        Obs.obs_output_set_video_encoder(output, video);
        for (int i = 0; i < _audioEncoders.Count; i++)
            Obs.obs_output_set_audio_encoder(output, _audioEncoders[i], (nuint)i);
    }

    void OnScreenSaved(IntPtr output, bool quick, int screen)
    {
        if (LastReplayPath(output) is { } path)
            ScreenSaved?.Invoke(path, quick, screen);
    }

    void SetExtrasActive(bool on)
    {
        foreach (var x in _extras)
        {
            if (!on)
            {
                Obs.obs_output_stop(x.Replay);
                if (x.Quick != IntPtr.Zero)
                    Obs.obs_output_stop(x.Quick);
                continue;
            }
            if (!Obs.obs_output_start(x.Replay))
                ObsLog.Warn($"screen {x.Number}: buffer could not start: {Obs.Utf8(Obs.obs_output_get_last_error(x.Replay))}");
            if (x.Quick != IntPtr.Zero && !Obs.obs_output_start(x.Quick))
                ObsLog.Warn($"screen {x.Number}: quick buffer could not start");
        }
    }

    /// <summary>The extra screens save the same moment as the main picture, each into its own file.</summary>
    void SaveExtras(bool quick)
    {
        foreach (var x in _extras)
            if ((quick ? x.Quick : x.Replay) is var output && output != IntPtr.Zero && Obs.obs_output_active(output) && !CallSave(output))
                ObsLog.Warn($"screen {x.Number}: save refused");
    }

    /// <summary>Each extra screen records into its own file beside the main one ("… - Screen 2.mp4").</summary>
    void StartExtraRecords(string mainPath)
    {
        foreach (var x in _extras)
        {
            string path = ClipOrganizer.FreePath(ClipOrganizer.ForScreen(mainPath, x.Number));
            using (var d = new ObsData().Set("path", path).Set("muxer_settings", MuxerSettings))
                x.Record = Obs.obs_output_create("ffmpeg_muxer", $"PixlReplay record {x.Number}", d.Handle, IntPtr.Zero);
            if (x.Record == IntPtr.Zero)
                continue;
            AttachEncoders(x.Record, x.Encoder);
            if (!Obs.obs_output_start(x.Record))
                ObsLog.Warn($"screen {x.Number}: recording could not start");
            else
                ObsLog.Info($"screen {x.Number}: recording into {path}");
        }
    }

    void StopExtraRecords()
    {
        foreach (var x in _extras)
            if (x.Record != IntPtr.Zero && Obs.obs_output_active(x.Record))
                Obs.obs_output_stop(x.Record);
    }

    void PauseExtraRecords(bool pause)
    {
        foreach (var x in _extras)
            if (x.Record != IntPtr.Zero && Obs.obs_output_active(x.Record) && !Obs.obs_output_pause(x.Record, pause))
                ObsLog.Warn($"screen {x.Number}: record {(pause ? "pause" : "resume")} refused");
    }

    void ReleaseExtras()
    {
        foreach (var x in _extras)
            ReleaseExtra(x);
        _extras.Clear();
    }

    /// <summary>Takes an extra screen apart: its outputs, encoder and view first, then its scene (the sources go with the rest).</summary>
    static void ReleaseExtra(ExtraScreen x)
    {
        ReleaseRecord(ref x.Record, null);
        ReleaseReplay(ref x.Quick, x.OnQuickSaved);
        ReleaseReplay(ref x.Replay, x.OnReplaySaved);
        if (x.Encoder != IntPtr.Zero)
            Obs.obs_encoder_release(x.Encoder);
        x.Encoder = IntPtr.Zero;
        if (x.View != IntPtr.Zero)
        {
            if (x.Video != IntPtr.Zero)
                Obs.obs_view_remove(x.View);
            Obs.obs_view_set_source(x.View, 0, IntPtr.Zero);
            Obs.obs_view_destroy(x.View);
        }
        x.View = x.Video = IntPtr.Zero;
        if (x.Scene != IntPtr.Zero)
        {
            // libobs 32 puts even a private scene on the main canvas: taken off it, it is freed (as the main scene is).
            Obs.obs_source_remove(Obs.obs_scene_get_source(x.Scene));
            Obs.obs_scene_release(x.Scene);
        }
        x.Scene = IntPtr.Zero;
    }
}
