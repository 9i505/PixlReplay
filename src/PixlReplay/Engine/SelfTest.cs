using System.Diagnostics;
using PixlReplay.Settings;

namespace PixlReplay.Engine;

/// <summary>Headless probes used from the command line during development and verification.</summary>
public static class SelfTest
{
    static string LogDir => Path.Combine(SettingsStore.AppDataDir, "logs");

    public static int Host(TextWriter output)
    {
        ObsHost.Start(AppContext.BaseDirectory, SettingsStore.AppDataDir, LogDir, new VideoConfig(1920, 1080, 1920, 1080, 30));
        ObsHost.LoadLater(); // the self-tests try every encoder
        try
        {
            output.WriteLine($"log: {ObsLog.CurrentPath}");
            output.WriteLine($"failed modules: {string.Join(", ", ObsHost.FailedModules)}");
            output.WriteLine("encoders: " + string.Join(", ", ObsHost.EncoderIds()));
            foreach (var m in ObsHost.Monitors())
                output.WriteLine($"monitor: {m.Width}x{m.Height} primary={m.Primary} {m.Name}");
            output.WriteLine($"mic devices: {ObsHost.ListProperty("wasapi_input_capture", "device_id").Count}");
            foreach (var (name, value) in ObsHost.ListProperty("window_capture", "window"))
                output.WriteLine($"window: {name} => {value}");
            return ObsHost.FailedModules.Count == 0 ? 0 : 1;
        }
        finally
        {
            ObsHost.Shutdown();
        }
    }

    /// <summary>
    /// A screenshot of the canvas while the pipeline runs: the PNG must have the canvas's size and real content, and a
    /// second press while the first is under way must be ignored. Into the temp folder (deleted); nothing shows or plays.
    /// </summary>
    public static int Screenshot(TextWriter output)
    {
        var s = new AppSettings();
        s.Output.Folder = Path.Combine(Path.GetTempPath(), "pixlreplay-selftest-shot");
        s.Normalize();
        ObsHost.Start(AppContext.BaseDirectory, SettingsStore.AppDataDir, LogDir, new VideoConfig(1920, 1080, 1920, 1080, 30));
        ObsHost.LoadLater(); // the self-tests try every encoder
        var pipeline = new CapturePipeline(s);
        try
        {
            pipeline.Build();
            Thread.Sleep(1000);
            var (w, h) = pipeline.Base;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var first = ScreenshotGrabber.Grab(w, h, TimeSpan.FromSeconds(2));
            var second = ScreenshotGrabber.Grab(w, h, TimeSpan.FromSeconds(2));
            var frame = first.GetAwaiter().GetResult();
            long grabMs = clock.ElapsedMilliseconds;
            bool secondIgnored = second.IsCompleted && second.Result == null;
            if (frame == null)
            {
                output.WriteLine("screenshot: FAIL no frame");
                return 2;
            }
            clock.Restart();
            string path = ScreenshotWriter.Write(ClipOrganizer.TargetPath(s.Output.Folder, "Desktop", DateTime.Now, true, ".png"), frame);
            long writeMs = clock.ElapsedMilliseconds;
            int colours;
            (int W, int H) size;
            double same, flipped, swapped;
            using (var png = new System.Drawing.Bitmap(path))
            {
                size = (png.Width, png.Height);
                var seen = new HashSet<int>();
                for (int y = 0; y < png.Height; y += 37)
                    for (int x = 0; x < png.Width; x += 41)
                        seen.Add(png.GetPixel(x, y).ToArgb());
                colours = seen.Count;
                // The same monitor copied by Windows: the picture must match it, not its upside-down or its red/blue swap.
                using var screen = new System.Drawing.Bitmap(png.Width, png.Height);
                var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
                using (var g = System.Drawing.Graphics.FromImage(screen))
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, screen.Size);
                (same, flipped, swapped) = Compare(png, screen);
            }
            bool again = ScreenshotGrabber.Grab(w, h, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult() != null;
            output.WriteLine($"screenshot: {size.W}x{size.H} (canvas {w}x{h}), {colours} colours sampled, grab {grabMs} ms, png {writeMs} ms, " +
                             $"{new FileInfo(path).Length / 1024} KB; second press ignored {secondIgnored}; another after it {again}");
            output.WriteLine($"screenshot vs the screen (mean difference of 32x18 blocks): as is {same:F1}, upside down {flipped:F1}, red/blue swapped {swapped:F1}");
            bool ok = size == (w, h) && colours > 1 && secondIgnored && again && same < flipped && same < swapped && same < 12;
            output.WriteLine(ok ? "screenshot ok" : "screenshot: FAIL");
            return ok ? 0 : 1;
        }
        catch (Exception e)
        {
            output.WriteLine($"FAIL {e.GetType().Name}: {e.Message}");
            return 4;
        }
        finally
        {
            pipeline.Dispose();
            ObsHost.Shutdown();
            try
            {
                Directory.Delete(s.Output.Folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Mean colour difference of 32x18 block averages: as is, upside down, and with red and blue swapped.</summary>
    static (double Same, double Flipped, double Swapped) Compare(System.Drawing.Bitmap a, System.Drawing.Bitmap b)
    {
        const int Cols = 32, Rows = 18;
        static (double R, double G, double B)[,] Blocks(System.Drawing.Bitmap bmp)
        {
            var sums = new (double R, double G, double B)[Cols, Rows];
            for (int y = 0; y < bmp.Height; y += 4)
                for (int x = 0; x < bmp.Width; x += 4)
                {
                    var c = bmp.GetPixel(x, y);
                    int bx = x * Cols / bmp.Width, by = y * Rows / bmp.Height;
                    var s = sums[bx, by];
                    sums[bx, by] = (s.R + c.R, s.G + c.G, s.B + c.B);
                }
            double n = (bmp.Width / 4.0 / Cols) * (bmp.Height / 4.0 / Rows);
            for (int i = 0; i < Cols; i++)
                for (int j = 0; j < Rows; j++)
                    sums[i, j] = (sums[i, j].R / n, sums[i, j].G / n, sums[i, j].B / n);
            return sums;
        }
        var pa = Blocks(a);
        var pb = Blocks(b);
        double same = 0, flipped = 0, swapped = 0;
        for (int i = 0; i < Cols; i++)
            for (int j = 0; j < Rows; j++)
            {
                var x = pa[i, j];
                var y = pb[i, j];
                var f = pb[i, Rows - 1 - j];
                same += Math.Abs(x.R - y.R) + Math.Abs(x.G - y.G) + Math.Abs(x.B - y.B);
                flipped += Math.Abs(x.R - f.R) + Math.Abs(x.G - f.G) + Math.Abs(x.B - f.B);
                swapped += Math.Abs(x.R - y.B) + Math.Abs(x.G - y.G) + Math.Abs(x.B - y.R);
            }
        double count = Cols * Rows * 3;
        return (same / count, flipped / count, swapped / count);
    }

    /// <summary>
    /// A full recording paused in the middle while the replay buffer runs (they share the encoders): 3 s, a 1 s pause,
    /// 2 s. The file must be one 5 s video, the clock must leave the pause out, and the buffer must still save afterwards.
    /// Screen capture into the temp folder: nothing shows, nothing plays, and the files are deleted.
    /// </summary>
    public static int Recording(TextWriter output)
    {
        var s = new AppSettings();
        s.Output.Folder = Path.Combine(Path.GetTempPath(), "pixlreplay-selftest-record");
        s.Normalize();
        ObsHost.Start(AppContext.BaseDirectory, SettingsStore.AppDataDir, LogDir, new VideoConfig(1920, 1080, 1920, 1080, 30));
        ObsHost.LoadLater(); // the self-tests try every encoder
        var pipeline = new CapturePipeline(s);
        try
        {
            pipeline.Build();
            string? path = null;
            double clockSeconds = 0;
            using var stopped = new SemaphoreSlim(0);
            pipeline.RecordingStopped += (p, secs) => { path = p; clockSeconds = secs; stopped.Release(); };
            string? clip = null;
            using var saved = new SemaphoreSlim(0);
            pipeline.ReplaySaved += (p, _, screen) => { if (screen == 0) { clip = p; saved.Release(); } };

            string target = ClipOrganizer.TargetPath(s.Output.Folder, "Desktop", DateTime.Now, perAppFolders: true);
            if (!pipeline.SetBufferActive(true) || !pipeline.ToggleRecording(target))
            {
                output.WriteLine($"FAIL start: {pipeline.LastError}");
                return 2;
            }
            Thread.Sleep(3000);
            bool paused = pipeline.TogglePauseRecording() && pipeline.RecordingPaused;
            double atPause = pipeline.GetStatus().RecordingSeconds;
            Thread.Sleep(1000);
            double afterPause = pipeline.GetStatus().RecordingSeconds;
            bool resumed = pipeline.TogglePauseRecording() && !pipeline.RecordingPaused;
            Thread.Sleep(2000);
            long bytes = pipeline.GetStatus().RecordingBytes;
            pipeline.ToggleRecording();
            stopped.Wait(TimeSpan.FromSeconds(15));

            double fileSeconds = path != null && File.Exists(path) ? Share.ClipTranscoder.Probe(path).Duration : 0;
            bool bufferSaves = pipeline.SaveReplay(false) && saved.Wait(TimeSpan.FromSeconds(30));
            output.WriteLine($"record: paused={paused} resumed={resumed} clock at pause {atPause:F2} s, a second later {afterPause:F2} s");
            output.WriteLine($"record: clock {clockSeconds:F2} s, file {fileSeconds:F2} s, {bytes / 1024} KB while recording, {path}");
            output.WriteLine($"buffer after the pause: saved={bufferSaves} {clip}");
            bool inAppFolder = path == target;
            output.WriteLine($"record: in the app's folder like a clip: {inAppFolder}");
            bool ok = inAppFolder && paused && resumed && Math.Abs(afterPause - atPause) < 0.05 && Math.Abs(clockSeconds - 5) < 0.4
                      && Math.Abs(fileSeconds - 5) < 0.7 && bytes > 0 && bufferSaves;
            output.WriteLine(ok ? "record: ok" : "record: FAIL");
            return ok ? 0 : 1;
        }
        catch (Exception e)
        {
            output.WriteLine($"FAIL {e.GetType().Name}: {e.Message}");
            return 4;
        }
        finally
        {
            pipeline.Dispose();
            ObsHost.Shutdown();
            try
            {
                Directory.Delete(s.Output.Folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Builds the pipeline with default settings (plus overrides), buffers for <c>seconds</c>, saves once,
    /// and prints the file and resource figures. Options: --mode, --codec, --res, --fps, --quality, --mic,
    /// --window "title:class:exe", --quick, --record, --folder, --voice (Smart Save listener in enroll mode, temp folder).
    /// </summary>
    public static int Pipeline(string[] args, TextWriter output)
    {
        int seconds = args.Length > 1 && int.TryParse(args[1], out int n) ? n : 20;
        var s = new AppSettings();
        s.Output.Folder = Path.Combine(Path.GetTempPath(), "pixlreplay-selftest");
        bool record = false, voiceTest = false;
        string? only = null;
        for (int i = 2; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : "";
            switch (args[i])
            {
                case "--mode": s.Capture.Mode = Enum.Parse<CaptureMode>(next, true); i++; break;
                case "--codec": s.Video.Codec = Enum.Parse<VideoCodec>(next, true); i++; break;
                case "--res": s.Video.Resolution = Enum.Parse<OutputResolution>(next, true); i++; break;
                case "--fps": s.Video.Fps = int.Parse(next); i++; break;
                case "--quality": s.Video.Quality = Enum.Parse<QualityPreset>(next, true); i++; break;
                case "--window": s.Capture.Window = next; i++; break;
                case "--folder": s.Output.Folder = next; i++; break;
                case "--mic": s.Audio.Mic = true; break;
                case "--mic-device": s.Audio.SelectMic(next); i++; break;
                case "--source-audio": s.Audio.SourceAudioOnly = true; break;
                case "--quick": s.Replay.QuickEnabled = true; s.Replay.QuickSeconds = 5; break;
                case "--record": record = true; break;
                case "--only": only = next; i++; break;
                case "--voice": voiceTest = true; s.Voice.Enabled = true; break;
            }
        }
        s.Normalize();

        ObsHost.Start(AppContext.BaseDirectory, SettingsStore.AppDataDir, LogDir, new VideoConfig(1920, 1080, 1920, 1080, 30));
        ObsHost.LoadLater(); // the self-tests try every encoder
        string voiceDir = Path.Combine(Path.GetTempPath(), "pixlreplay-selftest-voice");
        using var voice = voiceTest ? new Voice.VoiceTrigger(Voice.SpeechModels.DefaultDirectory, voiceDir) : null;
        var pipeline = new CapturePipeline(s, only == null ? null : ids => ids.Where(id => id.Contains(only) || id.Contains("aac")).ToList(), voice);
        try
        {
            pipeline.Build();
            var saved = new List<string>();
            using var savedSignal = new SemaphoreSlim(0);
            pipeline.ReplaySaved += (path, quick, _) => { lock (saved) saved.Add((quick ? "quick " : "main ") + path); savedSignal.Release(); };
            string? recordingPath = null;
            using var recordSignal = new SemaphoreSlim(0);
            pipeline.RecordingStopped += (path, _) => { recordingPath = path; recordSignal.Release(); };

            if (!pipeline.SetBufferActive(true))
            {
                output.WriteLine($"FAIL start: {pipeline.LastError}");
                return 2;
            }
            output.WriteLine($"early save accepted: {pipeline.SaveReplay(false)} (expected False)");
            if (record)
                output.WriteLine($"recording: {pipeline.ToggleRecording()}");
            voice?.StartEnroll();

            var proc = Process.GetCurrentProcess();
            Thread.Sleep(3000);
            proc.Refresh();
            TimeSpan cpu0 = proc.TotalProcessorTime;
            var sw = Stopwatch.StartNew();
            Thread.Sleep(Math.Max(1, seconds - 3) * 1000);
            proc.Refresh();
            double cpuCore = (proc.TotalProcessorTime - cpu0).TotalMilliseconds / sw.Elapsed.TotalMilliseconds * 100;

            for (int i = 0; i < 3; i++)
            {
                Thread.Sleep(700);
                var (system, mic) = pipeline.TakeLevels();
                output.WriteLine($"levels: system={system:F1} dB mic={mic:F1} dB");
            }
            if (voice != null)
            {
                var vs = voice.Status();
                output.WriteLine($"voice: available={vs.Available} mode={vs.Mode} blocks={voice.BlocksReceived} analysed={voice.ChunksAnalysed} chunks " +
                                 $"({voice.ChunksAnalysed * 0.032:F1} s of audio)");
                voice.Cancel();
                try { Directory.Delete(voiceDir, true); } catch (IOException) { }
            }
            var st = pipeline.GetStatus();
            output.WriteLine($"encoder: {st.Encoder} ({pipeline.Choice?.Vendor}) {st.Width}x{st.Height} fps={st.Fps:F1} lagged={st.LaggedFrames} skipped={st.SkippedFrames}/{st.TotalFrames}");
            output.WriteLine($"cpu: {cpuCore:F1}% of one core ({cpuCore / Environment.ProcessorCount:F2}% total), ram: {proc.WorkingSet64 / (1024 * 1024)} MB private: {proc.PrivateMemorySize64 / (1024 * 1024)} MB");
            output.WriteLine($"folder: {st.ClipsFolder} fellBack={st.FolderFellBack} estimate={st.EstimatedBufferMb} MB");

            bool first = pipeline.SaveReplay(false);
            bool second = pipeline.SaveReplay(false);
            output.WriteLine($"save accepted: {first}, immediate second accepted: {second} (expected True, False)");
            if (s.Replay.QuickEnabled)
            {
                savedSignal.Wait(TimeSpan.FromSeconds(30));
                output.WriteLine($"quick save accepted: {pipeline.SaveReplay(true)}");
            }
            int expectedSaves = s.Replay.QuickEnabled ? 2 : 1;
            for (int i = saved.Count; i < expectedSaves; i++)
                savedSignal.Wait(TimeSpan.FromSeconds(30));
            Thread.Sleep(1500);
            lock (saved)
                foreach (string line in saved)
                {
                    string path = line[(line.IndexOf(' ') + 1)..];
                    output.WriteLine($"saved {line} ({new FileInfo(path).Length / 1024} KB)");
                }

            if (record)
            {
                pipeline.ToggleRecording();
                recordSignal.Wait(TimeSpan.FromSeconds(15));
                output.WriteLine($"recording saved: {recordingPath} ({(recordingPath != null && File.Exists(recordingPath) ? new FileInfo(recordingPath).Length / 1024 : 0)} KB)");
            }
            return saved.Count == expectedSaves ? 0 : 3;
        }
        catch (Exception e)
        {
            output.WriteLine($"FAIL {e.GetType().Name}: {e.Message}");
            return 4;
        }
        finally
        {
            pipeline.Dispose();
            var leaked = ObsHost.AliveSources();
            output.WriteLine($"leaked sources after teardown: {(leaked.Count == 0 ? "none" : string.Join(", ", leaked))}");
            ObsHost.Shutdown();
            if (leaked.Count > 0)
                Environment.Exit(5);
        }
    }
}
