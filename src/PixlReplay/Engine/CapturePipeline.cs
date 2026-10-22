using PixlReplay.Native;
using PixlReplay.Settings;

namespace PixlReplay.Engine;

/// <summary>Scene, capture and audio sources, encoders and outputs built from <see cref="AppSettings"/>.</summary>
public sealed class CapturePipeline : IDisposable
{
    const uint ChannelVideo = 0, ChannelDesktop = 1, ChannelApp = 2, ChannelMic = 3;
    static readonly TimeSpan MinBufferBeforeSave = TimeSpan.FromSeconds(2);

    readonly AppSettings _s;
    readonly List<IntPtr> _sources = [];
    readonly List<IntPtr> _audioEncoders = [];
    readonly List<(IntPtr Meter, ObsVolmeterCallback Callback)> _meters = [];
    readonly LevelMeter _systemLevel = new();
    readonly LevelMeter _micLevel = new();
    IntPtr _systemSource;
    IntPtr _scene, _videoEncoder, _replay, _quick, _record;
    ObsSignalCallback? _onReplaySaved, _onQuickSaved, _onRecordStopped;
    DateTime _bufferStartedUtc;
    DateTime _saveRequestedUtc;
    volatile bool _saving;
    string? _recordPath;

    readonly Func<IReadOnlyList<string>, IReadOnlyList<string>>? _encoderFilter;
    readonly Voice.VoiceTrigger? _voice;
    IntPtr _micSource;
    ObsAudioCaptureCallback? _micTap;

    /// <param name="encoderFilter">Self-test hook that narrows the encoder list, e.g. to force Intel QSV.</param>
    /// <param name="voice">Smart Save listener that hears the microphone source.</param>
    public CapturePipeline(AppSettings settings, Func<IReadOnlyList<string>, IReadOnlyList<string>>? encoderFilter = null,
        Voice.VoiceTrigger? voice = null)
    {
        _s = settings;
        _encoderFilter = encoderFilter;
        _voice = voice;
    }

    public string ClipsFolder { get; private set; } = "";
    public bool FolderFellBack { get; private set; }
    public EncoderPlan? Plan { get; private set; }
    public EncoderChoice? Choice { get; private set; }
    public int Fps { get; private set; }
    public (int W, int H) Output { get; private set; }
    /// <summary>The canvas: the captured monitor's own size, which screenshots keep.</summary>
    public (int W, int H) Base { get; private set; }
    public string? LastError { get; private set; }
    public bool BufferActive => _replay != IntPtr.Zero && Obs.obs_output_active(_replay);
    public bool Recording => _record != IntPtr.Zero && Obs.obs_output_active(_record);
    public bool RecordingPaused => Recording && Obs.obs_output_paused(_record);
    readonly RecordingClock _recordClock = new();

    /// <summary>
    /// Raised on a libobs thread with the raw file path, whether it was the quick buffer, and which picture it holds:
    /// 0 for the main one, else the extra screen's number.
    /// </summary>
    public event Action<string, bool, int>? ReplaySaved;

    /// <summary>
    /// An extra screen recorded beside the main picture (Capture page, "Also record"): its own capture and scene, mixed at
    /// its own size in a libobs view, with its own video encoder and buffers. The sound is the main picture's (the same
    /// audio encoders), so every screen's clip has it. A save, a quick save or a recording makes one file per screen.
    /// </summary>
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

    /// <summary>The main picture's screen number (as the Capture page counts them) when it is a screen, else null.</summary>
    public int? MainScreen { get; private set; }

    /// <summary>The extra screens being recorded (their numbers), beside the main picture.</summary>
    public IReadOnlyList<int> ExtraScreens => _extras.Select(e => e.Number).ToList();

    /// <summary>The screens' number as the Capture page shows them: their place in libobs' list, from 1.</summary>
    public static int ScreenNumber(IReadOnlyList<MonitorInfo> monitors, string id) =>
        monitors.Select((m, i) => (m, i)).FirstOrDefault(p => p.m.Id == id) is { m: not null } found ? found.i + 1 : 0;

    /// <summary>The most extra screens recorded at once (each takes an encoder and its own buffer in memory).</summary>
    public const int MaxExtraScreens = 3;

    /// <summary>Raised on a libobs thread with the finished recording's path and its length without pauses.</summary>
    public event Action<string, double>? RecordingStopped;

    public void Build()
    {
        var monitors = ObsHost.Monitors();
        var monitor = VideoMath.PickMonitor(monitors, _s.Capture.MonitorId);
        MainScreen = _s.Capture.Mode == CaptureMode.Screen && monitor != null ? ScreenNumber(monitors, monitor.Id) : null;
        // The extra screens still connected, never the one the main picture already shows.
        foreach (string id in _s.Capture.ExtraMonitors.Distinct().Take(MaxExtraScreens))
            if (monitors.FirstOrDefault(m => m.Id == id) is { } extra && !(MainScreen != null && extra.Id == monitor!.Id))
                _extras.Add(new ExtraScreen(ScreenNumber(monitors, extra.Id), extra));
        int baseW = monitor?.Width ?? 1920, baseH = monitor?.Height ?? 1080;
        Base = (baseW, baseH);
        var ids = ObsHost.EncoderIds();
        Choice = EncoderCatalog.Pick(_s.Video.Codec, _encoderFilter?.Invoke(ids) ?? ids);
        Output = VideoMath.OutputSize(baseW, baseH, _s.Video.Resolution);
        int refresh = VideoMath.LimitingRefresh(monitors, monitor, _s.Capture.Mode);
        Fps = VideoMath.CaptureFps(_s.Video.Fps, refresh);
        if (Fps < _s.Video.Fps)
            ObsLog.Info($"{_s.Video.Fps} fps asked, the screen shows {refresh} Hz: recording at {Fps} fps");
        if (Choice.Vendor == EncoderVendor.Software)
            (Output, Fps) = EncoderCatalog.LimitForSoftware(Output, Fps);
        if (!ObsHost.ResetVideo(new VideoConfig(baseW, baseH, Output.W, Output.H, Fps)))
            throw new InvalidOperationException("Could not set up video");

        ClipsFolder = ClipOrganizer.ResolveFolder(_s.Output.Folder, f => ClipOrganizer.Usable(f, App.InstallPaths.Root), out bool fellBack);
        FolderFellBack = fellBack;

        BuildScene(monitor?.Id ?? "", baseW, baseH);
        BuildAudio();
        BuildEncoders();
        _replay = CreateReplayOutput("PixlReplay replay", "PixlReplay %CCYY-%MM-%DD %hh-%mm-%ss", _s.Replay.Seconds, _videoEncoder, Plan!);
        _onReplaySaved = (_, _) => OnSaved(_replay, quick: false);
        Obs.signal_handler_connect(Obs.obs_output_get_signal_handler(_replay), "saved", _onReplaySaved, IntPtr.Zero);

        if (_s.Replay.QuickEnabled)
        {
            _quick = CreateReplayOutput("PixlReplay quick", "PixlReplay quick %CCYY-%MM-%DD %hh-%mm-%ss", _s.Replay.QuickSeconds, _videoEncoder, Plan!);
            _onQuickSaved = (_, _) => OnSaved(_quick, quick: true);
            Obs.signal_handler_connect(Obs.obs_output_get_signal_handler(_quick), "saved", _onQuickSaved, IntPtr.Zero);
        }

        ObsLog.Info($"pipeline built: mode={_s.Capture.Mode} {baseW}x{baseH} -> {Output.W}x{Output.H}@{Fps} " +
                    $"{Choice.Id} cq={Plan!.Cq} max={Plan.MaxBitrateKbps}kbps folder={ClipsFolder}");

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
        x.Output = VideoMath.OutputSize(m.Width, m.Height, _s.Video.Resolution);
        if (Choice!.Vendor == EncoderVendor.Software)
            x.Output = EncoderCatalog.LimitForSoftware(x.Output, Fps).Item1;

        x.Scene = Obs.obs_scene_create_private($"PixlReplay screen {x.Number}");
        IntPtr source = CreateMonitorSource(m.Id, $"Screen {x.Number}");
        AddToScene(x.Scene, source, m.Width, m.Height);
        x.View = Obs.obs_view_create();
        Obs.obs_view_set_source(x.View, 0, Obs.obs_scene_get_source(x.Scene));
        var ovi = ObsHost.VideoInfo(new VideoConfig(m.Width, m.Height, x.Output.W, x.Output.H, Fps));
        x.Video = Obs.obs_view_add2(x.View, ref ovi);
        if (x.Video == IntPtr.Zero)
            throw new InvalidOperationException("libobs could not mix this screen's picture");

        x.Plan = EncoderPresets.Plan(Choice.Codec, _s.Video.Quality, x.Output.W, x.Output.H, Fps,
            _s.Video.CustomCq, _s.Video.CustomMbps, Choice.Vendor != EncoderVendor.Software, _s.Video.Preset);
        x.Encoder = CreateVideoEncoder(x.Plan, $"PixlReplay video {x.Number}");
        Obs.obs_encoder_set_video(x.Encoder, x.Video);

        x.Replay = CreateReplayOutput($"PixlReplay replay {x.Number}", $"PixlReplay screen {x.Number} %CCYY-%MM-%DD %hh-%mm-%ss",
            _s.Replay.Seconds, x.Encoder, x.Plan);
        x.OnReplaySaved = (_, _) => OnSaved(x.Replay, quick: false, x.Number);
        Obs.signal_handler_connect(Obs.obs_output_get_signal_handler(x.Replay), "saved", x.OnReplaySaved, IntPtr.Zero);
        if (_s.Replay.QuickEnabled)
        {
            x.Quick = CreateReplayOutput($"PixlReplay quick {x.Number}", $"PixlReplay screen {x.Number} quick %CCYY-%MM-%DD %hh-%mm-%ss",
                _s.Replay.QuickSeconds, x.Encoder, x.Plan);
            x.OnQuickSaved = (_, _) => OnSaved(x.Quick, quick: true, x.Number);
            Obs.signal_handler_connect(Obs.obs_output_get_signal_handler(x.Quick), "saved", x.OnQuickSaved, IntPtr.Zero);
        }
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

    void BuildScene(string monitorId, int baseW, int baseH)
    {
        _scene = Obs.obs_scene_create("PixlReplay");
        switch (_s.Capture.Mode)
        {
            case CaptureMode.Window:
                using (var d = new ObsData()
                           .Set("window", _s.Capture.Window).Set("priority", 2L).Set("method", 2L)
                           .Set("cursor", _s.Capture.CaptureCursor).Set("client_area", true))
                    AddToScene(_scene, CreateSource("window_capture", "Window", d), baseW, baseH);
                break;

            case CaptureMode.Game:
                if (_s.Capture.GameFallbackToScreen)
                    AddToScene(_scene, CreateMonitorSource(monitorId), baseW, baseH);
                using (var d = new ObsData()
                           .Set("capture_mode", "any_fullscreen").Set("capture_cursor", _s.Capture.CaptureCursor)
                           .Set("anti_cheat_hook", true).Set("capture_overlays", false)
                           .Set("capture_audio", _s.Audio.SourceAudioOnly))
                {
                    IntPtr game = CreateSource("game_capture", "Game", d);
                    AddToScene(_scene, game, baseW, baseH);
                    if (_s.Audio.SourceAudioOnly)
                    {
                        ConfigureAudio(game, SystemMixers(), _s.Audio.SystemVolume);
                        AttachMeter(game, _systemLevel);
                        _systemSource = game;
                    }
                }
                break;

            default:
                AddToScene(_scene, CreateMonitorSource(monitorId), baseW, baseH);
                break;
        }
        Obs.obs_set_output_source(ChannelVideo, Obs.obs_scene_get_source(_scene));
    }

    IntPtr CreateMonitorSource(string monitorId, string name = "Screen")
    {
        // PixlReplay records SDR (NV12, Rec. 709): with Force SDR Windows hands an HDR screen over already in SDR, where the
        // HDR copy recorded black on some HDR monitors (a 4K HDR screen, DXGI, 2026-10-18).
        using var d = new ObsData().Set("monitor_id", monitorId).Set("method", 0L).Set("capture_cursor", _s.Capture.CaptureCursor)
            .Set("force_sdr", true);
        return CreateSource("monitor_capture", name, d);
    }

    void BuildAudio()
    {
        var a = _s.Audio;
        bool appOnly = a.SourceAudioOnly && _s.Capture.Mode == CaptureMode.Window;
        bool gameOnly = a.SourceAudioOnly && _s.Capture.Mode == CaptureMode.Game;

        if (appOnly)
        {
            IntPtr app = CreateAppAudio(_s.Capture.Window, "App audio");
            _systemSource = app;
            ConfigureAudio(app, SystemMixers(), a.SystemVolume);
            AttachMeter(app, _systemLevel);
            Obs.obs_set_output_source(ChannelApp, app);
        }
        else if (a.System && !gameOnly)
        {
            using var d = new ObsData().Set("device_id", a.SystemDevice);
            IntPtr desktop = CreateSource("wasapi_output_capture", "Desktop audio", d);
            _systemSource = desktop;
            ConfigureAudio(desktop, SystemMixers(), a.SystemVolume);
            AttachMeter(desktop, _systemLevel);
            Obs.obs_set_output_source(ChannelDesktop, desktop);
        }

        // Smart Save needs the microphone even when it is not recorded; then it joins no audio track.
        if (a.Mic || (_voice != null && _s.Voice.Enabled))
        {
            using var d = new ObsData().Set("device_id", a.MicDevice);
            IntPtr mic = CreateSource("wasapi_input_capture", "Microphone", d);
            ConfigureAudio(mic, a.Mic ? 1u | (a.SeparateTracks ? VoiceTrack : 0u) : 0u, a.MicVolume);
            AttachMeter(mic, _micLevel);
            Obs.obs_set_output_source(ChannelMic, mic);
            AttachVoice(mic);
        }
    }

    void AttachVoice(IntPtr mic)
    {
        if (_voice == null)
            return;
        var voice = _voice;
        _micTap = (_, _, audioData, muted) =>
        {
            if (!muted && voice.Listening)
                unsafe
                {
                    var data = (ObsAudioData*)audioData;
                    voice.Feed(new ReadOnlySpan<float>((void*)data->Data0, (int)data->Frames));
                }
        };
        Obs.obs_source_add_audio_capture_callback(mic, _micTap, IntPtr.Zero);
        _micSource = mic;
        voice.SetMicConnected(true);
    }

    /// <summary>Feeds the loudest channel of each libobs meter update into <paramref name="level"/>.</summary>
    void AttachMeter(IntPtr source, LevelMeter level)
    {
        IntPtr meter = Obs.obs_volmeter_create(1 /* OBS_FADER_IEC */);
        if (meter == IntPtr.Zero)
            return;
        ObsVolmeterCallback callback = (_, _, peak, _) => level.Add(LoudestChannel(peak));
        Obs.obs_volmeter_add_callback(meter, callback, IntPtr.Zero);
        Obs.obs_volmeter_attach_source(meter, source);
        _meters.Add((meter, callback));
    }

    static unsafe float LoudestChannel(IntPtr peak)
    {
        float* p = (float*)peak;
        return Math.Max(p[0], p[1]);
    }

    uint SystemMixers() => 1u | (_s.Audio.SeparateTracks ? 2u : 0u);

    /// <summary>Track 3's mixer: your voice.</summary>
    const uint VoiceTrack = 4u;

    /// <summary>One app's own sound (its child processes' too), found by its exe whatever its window's title says.</summary>
    IntPtr CreateAppAudio(string window, string name)
    {
        using var d = new ObsData().Set("window", window).Set("priority", 2L);
        return CreateSource("wasapi_process_output_capture", name, d);
    }

    static void ConfigureAudio(IntPtr source, uint mixers, double volume)
    {
        Obs.obs_source_set_audio_mixers(source, mixers);
        Obs.obs_source_set_volume(source, (float)volume);
    }

    /// <summary>MP4 codec tag "hvc1" (big-endian, as obs-ffmpeg-mux expects) so Apple players accept HEVC clips.</summary>
    const long Hvc1Tag = 0x68766331;

    void BuildEncoders()
    {
        var choice = Choice!;
        Plan = EncoderPresets.Plan(choice.Codec, _s.Video.Quality, Output.W, Output.H, Fps,
            _s.Video.CustomCq, _s.Video.CustomMbps, choice.Vendor != EncoderVendor.Software, _s.Video.Preset);
        _videoEncoder = CreateVideoEncoder(Plan, "PixlReplay video");
        Obs.obs_encoder_set_video(_videoEncoder, Obs.obs_get_video());

        foreach (int mixer in AudioMixerIndexes())
        {
            using var d = new ObsData().Set("bitrate", (long)_s.Audio.BitrateKbps);
            IntPtr enc = Obs.obs_audio_encoder_create("ffmpeg_aac", $"PixlReplay audio {mixer + 1}", d.Handle, (nuint)mixer, IntPtr.Zero);
            if (enc == IntPtr.Zero)
                throw new InvalidOperationException("Could not create the AAC encoder");
            Obs.obs_encoder_set_audio(enc, Obs.obs_get_audio());
            _audioEncoders.Add(enc);
        }
    }

    /// <summary>A video encoder of the chosen kind with <paramref name="plan"/>'s settings (the main picture's or a screen's).</summary>
    IntPtr CreateVideoEncoder(EncoderPlan plan, string name)
    {
        var choice = Choice!;
        IntPtr encoder;
        using (var d = new ObsData())
        {
            foreach (var (key, value) in EncoderCatalog.Settings(plan, choice))
            {
                switch (value)
                {
                    case string str: d.Set(key, str); break;
                    case long n: d.Set(key, n); break;
                    case bool b: d.Set(key, b); break;
                }
            }
            if (choice.Codec == VideoCodec.Hevc)
                d.Set("codec_type", Hvc1Tag);
            encoder = Obs.obs_video_encoder_create(choice.Id, name, d.Handle, IntPtr.Zero);
        }
        if (encoder == IntPtr.Zero)
            throw new InvalidOperationException($"Could not create encoder {choice.Id}");
        return encoder;
    }

    /// <summary>Track 1 is the full mix; with separate tracks, 2 is system/app audio and 3 is your voice (the mic).</summary>
    IEnumerable<int> AudioMixerIndexes()
    {
        yield return 0;
        if (!_s.Audio.SeparateTracks)
            yield break;
        if (_s.Audio.System || _s.Audio.SourceAudioOnly)
            yield return 1;
        if (_s.Audio.Mic)
            yield return 2;
    }

    IntPtr CreateReplayOutput(string name, string format, int seconds, IntPtr videoEncoder, EncoderPlan plan)
    {
        // Every screen keeps a buffer: together they stay within the memory one would have.
        long memory = BufferBudget.PhysicalMemoryBytes / (1 + _extras.Count);
        int sizeMb = BufferBudget.CapMb(EncoderPresets.EstimateBufferMb(plan.MaxBitrateKbps, seconds), memory);
        using var d = new ObsData()
            .Set("directory", ClipsFolder).Set("format", format).Set("extension", "mp4").Set("allow_spaces", true)
            .Set("max_time_sec", (long)seconds).Set("max_size_mb", (long)sizeMb);
        IntPtr output = Obs.obs_output_create("replay_buffer", name, d.Handle, IntPtr.Zero);
        if (output == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the replay buffer");
        AttachEncoders(output, videoEncoder);
        return output;
    }

    void AttachEncoders(IntPtr output, IntPtr videoEncoder)
    {
        Obs.obs_output_set_video_encoder(output, videoEncoder);
        for (int i = 0; i < _audioEncoders.Count; i++)
            Obs.obs_output_set_audio_encoder(output, _audioEncoders[i], (nuint)i);
    }

    IntPtr CreateSource(string id, string name, ObsData settings)
    {
        IntPtr source = Obs.obs_source_create(id, name, settings.Handle, IntPtr.Zero);
        if (source == IntPtr.Zero)
            throw new InvalidOperationException($"Could not create source {id}");
        _sources.Add(source);
        return source;
    }

    static void AddToScene(IntPtr scene, IntPtr source, int w, int h)
    {
        IntPtr item = Obs.obs_scene_add(scene, source);
        Obs.obs_sceneitem_set_bounds_type(item, ObsConst.BoundsScaleInner);
        Obs.obs_sceneitem_set_bounds_alignment(item, ObsConst.AlignCenter);
        var bounds = new Vec2(w, h);
        Obs.obs_sceneitem_set_bounds(item, ref bounds);
        var pos = new Vec2(0, 0);
        Obs.obs_sceneitem_set_pos(item, ref pos);
    }

    /// <summary>Starts or stops the replay buffer(s). Returns whether the buffer is active afterwards.</summary>
    public bool SetBufferActive(bool on)
    {
        if (on == BufferActive)
            return on;
        if (!on)
        {
            Obs.obs_output_stop(_replay);
            if (_quick != IntPtr.Zero)
                Obs.obs_output_stop(_quick);
            foreach (var x in _extras)
            {
                Obs.obs_output_stop(x.Replay);
                if (x.Quick != IntPtr.Zero)
                    Obs.obs_output_stop(x.Quick);
            }
            return false;
        }

        if (!Obs.obs_output_start(_replay))
        {
            LastError = Obs.Utf8(Obs.obs_output_get_last_error(_replay)) ?? "The replay buffer could not start";
            ObsLog.Error($"replay start failed: {LastError}");
            return false;
        }
        if (_quick != IntPtr.Zero && !Obs.obs_output_start(_quick))
            ObsLog.Warn("quick buffer could not start");
        foreach (var x in _extras)
        {
            if (!Obs.obs_output_start(x.Replay))
                ObsLog.Warn($"screen {x.Number}: buffer could not start: {Obs.Utf8(Obs.obs_output_get_last_error(x.Replay))}");
            if (x.Quick != IntPtr.Zero && !Obs.obs_output_start(x.Quick))
                ObsLog.Warn($"screen {x.Number}: quick buffer could not start");
        }
        LastError = null;
        _bufferStartedUtc = DateTime.UtcNow;
        return true;
    }

    /// <summary>Requests a save. False when inactive, too early, or a save is already running.</summary>
    public bool SaveReplay(bool quick) => RequestSave(quick) == SaveWatch.Request.Started;

    /// <summary>What a buffer holds now, about (its bitrate times the seconds filled): how long writing it may take.</summary>
    public long BufferBytes => Plan == null || !BufferActive ? 0
        : (long)EncoderPresets.EstimateBufferMb(Plan.MaxBitrateKbps,
            (int)Math.Min(_s.Replay.Seconds, (DateTime.UtcNow - _bufferStartedUtc).TotalSeconds)) * 1024 * 1024;

    /// <summary>Requests a save and says what happened: started, off, just starting, or one is already being written.</summary>
    public SaveWatch.Request RequestSave(bool quick)
    {
        IntPtr output = quick ? _quick : _replay;
        if (output == IntPtr.Zero || !Obs.obs_output_active(output))
            return SaveWatch.Request.Off;
        DateTime now = DateTime.UtcNow;
        if (now - _bufferStartedUtc < MinBufferBeforeSave)
            return SaveWatch.Request.Starting;
        if (_saving && now - _saveRequestedUtc < SaveWatch.WriteLimit(BufferBytes))
            return SaveWatch.Request.Busy;

        _saving = true;
        _saveRequestedUtc = now;
        bool ok = CallSave(output);
        if (!ok)
            _saving = false;
        else
            // The extra screens save the same moment, each into its own file.
            foreach (var x in _extras)
                if ((quick ? x.Quick : x.Replay) is var extra && extra != IntPtr.Zero && Obs.obs_output_active(extra) && !CallSave(extra))
                    ObsLog.Warn($"screen {x.Number}: save refused");
        return ok ? SaveWatch.Request.Started : SaveWatch.Request.Off;
    }

    static bool CallSave(IntPtr output)
    {
        var cd = new CallData();
        bool ok = Obs.proc_handler_call(Obs.obs_output_get_proc_handler(output), "save", ref cd);
        Obs.FreeCallData(ref cd);
        return ok;
    }

    void OnSaved(IntPtr output, bool quick, int screen = 0)
    {
        string? path = null;
        var cd = new CallData();
        if (Obs.proc_handler_call(Obs.obs_output_get_proc_handler(output), "get_last_replay", ref cd) &&
            Obs.calldata_get_string(ref cd, "path", out IntPtr p))
            path = Obs.Utf8(p);
        Obs.FreeCallData(ref cd);
        if (screen == 0)
            _saving = false;
        if (path != null)
            ReplaySaved?.Invoke(path.Replace('/', '\\'), quick, screen);
    }

    /// <summary>
    /// Starts a recording into <paramref name="target"/> (PixlReplay names it like a clip, in the app's folder), or stops the
    /// one running. Returns whether recording is active afterwards.
    /// </summary>
    public bool ToggleRecording(string? target = null)
    {
        if (Recording)
        {
            _recordClock.Pause(DateTime.UtcNow); // its length is what was recorded, not the time the file takes to close
            Obs.obs_output_stop(_record);
            foreach (var x in _extras)
                if (x.Record != IntPtr.Zero && Obs.obs_output_active(x.Record))
                    Obs.obs_output_stop(x.Record);
            return false;
        }

        ReleaseRecordOutput();
        string baseName = target ?? Path.Combine(ClipsFolder, "Recordings", $"Recording {DateTime.Now:yyyy-MM-dd HH-mm-ss}.mp4");
        // With extra screens every file says its screen, the main picture's too ("… - Screen 1.mp4").
        _recordPath = _extras.Count > 0 && MainScreen is { } main ? ClipOrganizer.FreePath(ClipOrganizer.ForScreen(baseName, main)) : baseName;
        Directory.CreateDirectory(Path.GetDirectoryName(_recordPath)!);
        _record = CreateRecordOutput(_recordPath, "PixlReplay record", _videoEncoder);
        string path = _recordPath;
        _onRecordStopped = (_, _) => RecordingStopped?.Invoke(path, _recordClock.Seconds(DateTime.UtcNow));
        Obs.signal_handler_connect(Obs.obs_output_get_signal_handler(_record), "stop", _onRecordStopped, IntPtr.Zero);

        if (Obs.obs_output_start(_record))
        {
            _recordClock.Start(DateTime.UtcNow);
            // Each extra screen into its own file beside the main one ("… - Screen 2.mp4").
            foreach (var x in _extras)
            {
                string screenPath = ClipOrganizer.FreePath(ClipOrganizer.ForScreen(baseName, x.Number));
                x.Record = CreateRecordOutput(screenPath, $"PixlReplay record {x.Number}", x.Encoder);
                if (!Obs.obs_output_start(x.Record))
                    ObsLog.Warn($"screen {x.Number}: recording could not start");
                else
                    ObsLog.Info($"screen {x.Number}: recording into {screenPath}");
            }
            return true;
        }
        LastError = Obs.Utf8(Obs.obs_output_get_last_error(_record)) ?? "Recording could not start";
        ObsLog.Error($"record start failed: {LastError}");
        return false;
    }

    IntPtr CreateRecordOutput(string path, string name, IntPtr videoEncoder)
    {
        IntPtr output;
        using (var d = new ObsData().Set("path", path).Set("muxer_settings", "movflags=frag_keyframe+empty_moov+delay_moov"))
            output = Obs.obs_output_create("ffmpeg_muxer", name, d.Handle, IntPtr.Zero);
        if (output == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the recording");
        AttachEncoders(output, videoEncoder);
        return output;
    }

    /// <summary>
    /// Pauses or resumes the full recording; the file stays one continuous video. The pause sits on the encoders, which
    /// the replay buffer shares, so the buffer takes nothing in while the recording is paused either.
    /// </summary>
    public bool TogglePauseRecording()
    {
        if (!Recording)
            return false;
        bool pause = !Obs.obs_output_paused(_record);
        if (!Obs.obs_output_pause(_record, pause))
        {
            ObsLog.Warn($"record {(pause ? "pause" : "resume")} refused");
            return false;
        }
        foreach (var x in _extras)
            if (x.Record != IntPtr.Zero && Obs.obs_output_active(x.Record) && !Obs.obs_output_pause(x.Record, pause))
                ObsLog.Warn($"screen {x.Number}: record {(pause ? "pause" : "resume")} refused");
        if (pause)
            _recordClock.Pause(DateTime.UtcNow);
        else
            _recordClock.Resume(DateTime.UtcNow);
        return true;
    }

    /// <summary>Mutes system/app/game audio capture, so PixlReplay's own save sound is not recorded into the next clips.</summary>
    public void SetSystemMuted(bool muted)
    {
        if (_systemSource != IntPtr.Zero)
            Obs.obs_source_set_muted(_systemSource, muted);
    }

    /// <summary>Loudest system and mic peaks (dBFS) since the previous call; read by the live meter stream.</summary>
    public (double System, double Mic) TakeLevels() => (_systemLevel.Take(), _micLevel.Take());

    public EngineStatus GetStatus()
    {
        IntPtr video = Obs.obs_get_video();
        int target = _s.Replay.Seconds;
        int filled = BufferActive ? (int)Math.Min(target, (DateTime.UtcNow - _bufferStartedUtc).TotalSeconds) : 0;
        return new EngineStatus(
            BufferActive, Recording, Obs.obs_get_active_fps(), Obs.obs_get_lagged_frames(),
            video == IntPtr.Zero ? 0 : Obs.video_output_get_skipped_frames(video),
            video == IntPtr.Zero ? 0 : Obs.video_output_get_total_frames(video),
            filled, target, Choice?.Id ?? "", Output.W, Output.H, Environment.WorkingSet / (1024 * 1024),
            Plan == null ? 0 : Math.Min(EncoderPresets.EstimateBufferMb(Plan.MaxBitrateKbps, target),
                BufferBudget.CapMb(EncoderPresets.EstimateBufferMb(Plan.MaxBitrateKbps, target), BufferBudget.PhysicalMemoryBytes)),
            ClipsFolder, FolderFellBack, LastError,
            RecordingPaused,
            Recording ? _recordClock.Seconds(DateTime.UtcNow) : 0,
            Recording ? (long)Obs.obs_output_get_total_bytes(_record) : 0);
    }

    void ReleaseRecordOutput()
    {
        ReleaseRecord(ref _record, _onRecordStopped);
        foreach (var x in _extras)
            ReleaseRecord(ref x.Record, null);
    }

    static void ReleaseRecord(ref IntPtr record, ObsSignalCallback? onStopped)
    {
        if (record == IntPtr.Zero)
            return;
        if (Obs.obs_output_active(record))
        {
            Obs.obs_output_stop(record);
            for (int i = 0; i < 50 && Obs.obs_output_active(record); i++)
                Thread.Sleep(100);
        }
        if (onStopped != null)
            Obs.signal_handler_disconnect(Obs.obs_output_get_signal_handler(record), "stop", onStopped, IntPtr.Zero);
        Obs.obs_output_release(record);
        record = IntPtr.Zero;
    }

    static void ReleaseReplay(ref IntPtr output, ObsSignalCallback? callback)
    {
        if (output == IntPtr.Zero)
            return;
        if (Obs.obs_output_active(output))
            Obs.obs_output_force_stop(output);
        if (callback != null)
            Obs.signal_handler_disconnect(Obs.obs_output_get_signal_handler(output), "saved", callback, IntPtr.Zero);
        Obs.obs_output_release(output);
        output = IntPtr.Zero;
    }

    public void Teardown()
    {
        ReleaseRecordOutput();
        foreach (var x in _extras)
            ReleaseExtra(x);
        _extras.Clear();
        ReleaseReplay(ref _quick, _onQuickSaved);
        ReleaseReplay(ref _replay, _onReplaySaved);

        if (_videoEncoder != IntPtr.Zero)
            Obs.obs_encoder_release(_videoEncoder);
        _videoEncoder = IntPtr.Zero;
        foreach (IntPtr enc in _audioEncoders)
            Obs.obs_encoder_release(enc);
        _audioEncoders.Clear();

        if (_micSource != IntPtr.Zero && _micTap != null)
            Obs.obs_source_remove_audio_capture_callback(_micSource, _micTap, IntPtr.Zero);
        _micSource = IntPtr.Zero;
        _voice?.SetMicConnected(false);

        foreach (var (meter, callback) in _meters)
        {
            Obs.obs_volmeter_remove_callback(meter, callback, IntPtr.Zero);
            Obs.obs_volmeter_detach_source(meter);
            Obs.obs_volmeter_destroy(meter);
        }
        _meters.Clear();

        foreach (uint channel in new[] { ChannelVideo, ChannelDesktop, ChannelApp, ChannelMic })
            Obs.obs_set_output_source(channel, IntPtr.Zero);
        _systemSource = IntPtr.Zero;
        foreach (IntPtr source in _sources)
        {
            Obs.obs_source_remove(source);
            Obs.obs_source_release(source);
        }
        _sources.Clear();
        if (_scene != IntPtr.Zero)
        {
            // libobs 32 registers scenes on the main canvas; only obs_source_remove takes it off the canvas.
            // Without this every rebuild leaked the old scene and its capture source.
            Obs.obs_source_remove(Obs.obs_scene_get_source(_scene));
            Obs.obs_scene_release(_scene);
        }
        _scene = IntPtr.Zero;
        _saving = false;
        ObsHost.DrainDestroyQueue();
    }

    public void Dispose() => Teardown();
}
