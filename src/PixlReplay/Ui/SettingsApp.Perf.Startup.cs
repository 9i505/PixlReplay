using System.Diagnostics;
using System.Windows.Threading;
using PixlReplay.App;
using PixlReplay.Settings;

namespace PixlReplay.Ui;

/// <summary>
/// PixlReplay.exe --perf startup|grabs|library: how long the user waits for each step (the window drawn, the engine
/// answering, the Library's pictures, each page, the player, the editor and its still after a seek, the share panel,
/// closing), run off screen and muted. Every line is "PERF t &lt;step&gt; ms=&lt;since the process started&gt; step=&lt;its own&gt;".
/// </summary>
public static partial class SettingsApp
{
    static double _lastStep;

    /// <summary>One timing line; its step is measured from <paramref name="sinceMs"/> when given, else from the last line.</summary>
    static void T(string what, double? sinceMs = null)
    {
        double now = Up() * 1000;
        double step = sinceMs is { } s ? now - s : now - _lastStep;
        Console.WriteLine(FormattableString.Invariant(
            $"PERF t {what} ms={now:F0} step={step:F0} jit={System.Runtime.JitInfo.GetCompilationTime().TotalMilliseconds:F0}"));
        _lastStep = now;
    }

    static double NowMs() => Up() * 1000;

    /// <summary>Lets the window lay out and hand its frame to the render thread (what "drawn" means for the user).</summary>
    static void Settle() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    /// <summary>Runs the window until <paramref name="ready"/> (checked every few ms) or the time is up; true when ready.</summary>
    static bool WaitUntil(Func<bool> ready, double seconds)
    {
        if (ready())
            return true;
        var frame = new DispatcherFrame();
        bool ok = false;
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(4) };
        timer.Tick += (_, _) =>
        {
            if (ready())
                ok = true;
            else if (DateTime.UtcNow < deadline)
                return;
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        return ok;
    }

    /// <summary>The longest the window's thread was busy (a timer that should tick every 4 ms, and how late it was).</summary>
    sealed class StallProbe : IDisposable
    {
        readonly DispatcherTimer _timer = new(DispatcherPriority.Send) { Interval = TimeSpan.FromMilliseconds(4) };
        readonly Stopwatch _since = Stopwatch.StartNew();
        public double LongestMs { get; private set; }
        public int Over50 { get; private set; }

        public StallProbe()
        {
            _timer.Tick += (_, _) =>
            {
                double gap = _since.Elapsed.TotalMilliseconds - 4;
                _since.Restart();
                if (gap > LongestMs)
                    LongestMs = gap;
                if (gap > 50)
                    Over50++;
            };
            _timer.Start();
        }

        public void Dispose() => _timer.Stop();
    }

    static int PerfStartup(string clip)
    {
        T("main");
        StartupTrace = what => T("  " + what);
        Text.Language = SettingsStore.Load(SettingsStore.DefaultPath).General.Language;
        T("settings-loaded");
        var app = CreateApplication();
        T("app-created");
        Edit.PlayerSession.Muted = true;
        Engine.Share.ClipTranscoder.RequireLoaded();
        T("ffmpeg-loaded");
        var vm = new SettingsViewModel();
        T("vm-created");
        var window = WindowPlacement.OffScreen(new MainWindow(vm));
        T("window-created");
        bool rendered = false;
        window.ContentRendered += (_, _) => rendered = true;
        window.Show();
        T("shown");
        Settle();
        T("laid-out");
        WaitUntil(() => rendered, 5);
        T("rendered");
        vm.Start();
        bool online = WaitUntil(() => vm.EngineOnline, 5);
        T(online ? "engine-online" : "engine-not-answering");

        // The Library: the cards, their first picture, every picture of the first page.
        window.ShowPage("clips");
        Settle();
        var items = window.Library.Items;
        T($"clips-page-{items.Count}-cards");
        WaitUntil(() => items.Count > 0 && items[0].Thumbnail != null, 10);
        T("clips-first-picture");
        WaitUntil(() => items.All(i => i.Thumbnail != null), 30);
        T($"clips-pictures-{items.Count(i => i.Thumbnail != null)}");

        // Each page, twice: the first time includes its code being compiled, the second is the page itself.
        foreach (int pass in new[] { 1, 2 })
        foreach (string page in PerfPages.Where(p => p != "clips"))
        {
            double at = NowMs();
            window.ShowPage(page);
            Settle();
            T($"page-{page}-{pass}", at);
        }
        window.ShowPage("clips");
        Settle();

        // The player (the Library's own viewer): opened with its still, then playing to its first picture.
        var item = items.FirstOrDefault(c => string.Equals(c.Path, clip, StringComparison.OrdinalIgnoreCase));
        if (item != null)
        {
            double at = NowMs();
            window.ShowVideo(item);
            T("player-opened", at);
            Settle();
            T("player-shown", at);
            double play = NowMs();
            window.VideoLayer.TogglePlayNow();
            WaitUntil(() => window.VideoLayer.FramesShown > 0, 5);
            T("player-first-picture", play);
            Wait(1);
            double close = NowMs();
            window.VideoLayer.Hide();
            Settle();
            T("player-closed", close);
        }

        // The editor: opened (the window's thread is busy for all of it), laid out, its first still.
        {
            double at = NowMs();
            var editor = window.OpenEditor(clip, isPicture: false, Edit.EditorMode.Library);
            T("editor-opened", at);
            if (editor == null)
            {
                Console.WriteLine("PERF editor did not open");
                return 1;
            }
            bool still = false;
            editor.Playback.StillRead += _ => still = true;
            Settle();
            T("editor-laid-out", at);
            WaitUntil(() => still, 5);
            T("editor-first-still", at);

            // Seeks: the playhead put somewhere, until the still for it is read (includes the 60 ms settle wait).
            double length = editor.Model.Project.OutputDuration;
            var seeks = new List<double>();
            using (var probe = new StallProbe())
            {
                foreach (double f in new[] { 0.5, 0.1, 0.9, 0.3, 0.7, 0.05, 0.95, 0.45, 0.55, 0.2 })
                {
                    still = false;
                    double from = NowMs();
                    editor.Model.PlayheadOutput = f * length;
                    WaitUntil(() => still, 5);
                    seeks.Add(NowMs() - from);
                }
                Console.WriteLine(FormattableString.Invariant(
                    $"PERF seeks n={seeks.Count} avg={seeks.Average():F0} max={seeks.Max():F0} min={seeks.Min():F0} ms, window thread busy up to {probe.LongestMs:F0} ms ({probe.Over50} stalls over 50 ms)"));
            }
            // Stepping a tenth of a second at a time (Shift and an arrow), from the middle.
            editor.Model.PlayheadOutput = 0.5 * length;
            WaitUntil(() => still, 5);
            var steps = new List<double>();
            using (var probe = new StallProbe())
            {
                for (int i = 0; i < 10; i++)
                {
                    still = false;
                    double from = NowMs();
                    editor.Model.KeyCommand("Shift+Right");
                    WaitUntil(() => still, 5);
                    steps.Add(NowMs() - from);
                }
                Console.WriteLine(FormattableString.Invariant(
                    $"PERF steps n={steps.Count} avg={steps.Average():F0} max={steps.Max():F0} ms, window thread busy up to {probe.LongestMs:F0} ms"));
            }
            // Playing: three seconds, then what the player reports.
            editor.Model.PlayheadOutput = 0.25 * length;
            WaitUntil(() => still, 5);
            double play = NowMs();
            editor.Model.KeyCommand("Space");
            WaitUntil(() => editor.Playback.FramesShown > 0, 5);
            T("editor-play-first-picture", play);
            Wait(3);
            editor.Model.KeyCommand("Space");
            Console.WriteLine(FormattableString.Invariant($"PERF play {editor.Playback.PlayReport}"));
            double close = NowMs();
            window.CloseEditorNow();
            Settle();
            T("editor-closed", close);
        }

        // The share panel: open and drawn, its own player's first picture, its filmstrip.
        {
            double at = NowMs();
            var share = window.ShowShare(clip);
            var card = share.PreviewFrame;
            T("share-opened", at);
            Settle();
            T("share-shown", at);
            WaitUntil(() => share.PreviewFrame != null && !ReferenceEquals(share.PreviewFrame, card), 5);
            T("share-own-frame", at);
            WaitUntil(() => share.Filmstrip.Count >= ShareViewModel.FilmstripFrames && share.Filmstrip.All(f => f != null), 10);
            T($"share-filmstrip-{share.Filmstrip.Count(f => f != null)}", at);
            double close = NowMs();
            share.Close();
            Settle();
            T("share-closed", close);
        }

        double closing = NowMs();
        vm.Stop();
        window.Close();
        T("closed", closing);
        return 0;
    }

    /// <summary>parts: how long each of the window's pieces takes to build on its own, before the window (its XAML cost).</summary>
    static int PerfParts()
    {
        Text.Language = SettingsStore.Load(SettingsStore.DefaultPath).General.Language;
        T("main");
        CreateApplication();
        T("app-created");
        foreach (var (what, make) in new (string, Func<object>)[]
                 {
                     ("share-overlay", () => new ShareOverlay()), ("video-viewer", () => new Edit.VideoViewer()), ("picture-viewer", () => new PictureViewer()),
                     ("status-page", () => new Pages.StatusPage()), ("clips-page", () => new Pages.ClipsPage()), ("share-overlay-again", () => new ShareOverlay()),
                 })
        {
            double at = NowMs();
            GC.KeepAlive(make());
            T(what, at);
        }
        double window = NowMs();
        GC.KeepAlive(new MainWindow(new SettingsViewModel()));
        T("window", window);
        return 0;
    }

    /// <summary>grabs &lt;clip&gt; &lt;count&gt;: a frame reader's exact seeks across the clip, then one-picture steps forward.</summary>
    static int PerfGrabs(string clip, int count)
    {
        Engine.Share.ClipTranscoder.RequireLoaded();
        var info = Engine.Share.ClipTranscoder.Probe(clip);
        using var grabber = new Engine.Share.FrameGrabber(clip);
        grabber.Grab(0, 1280, exact: true);
        var random = new Random(7);
        var times = new List<double>();
        for (int i = 0; i < count; i++)
        {
            double at = random.NextDouble() * info.Duration;
            var watch = Stopwatch.StartNew();
            grabber.Grab(at, 1280, exact: true);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine(FormattableString.Invariant($"PERF grabs exact n={count} avg={times.Average():F1} max={times.Max():F1} min={times.Min():F1} ms"));
        times.Clear();
        double frame = 1 / Math.Max(1, info.Fps);
        double start = info.Duration / 2;
        grabber.Grab(start, 1280, exact: true);
        for (int i = 1; i <= count; i++)
        {
            var watch = Stopwatch.StartNew();
            grabber.Grab(start + i * frame, 1280, exact: true);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine(FormattableString.Invariant($"PERF grabs step n={count} avg={times.Average():F1} max={times.Max():F1} min={times.Min():F1} ms"));
        times.Clear();
        for (int i = 0; i < count; i++)
        {
            double at = random.NextDouble() * info.Duration;
            var watch = Stopwatch.StartNew();
            grabber.Grab(at, 1280, exact: false);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        Console.WriteLine(FormattableString.Invariant($"PERF grabs keyframe n={count} avg={times.Average():F1} max={times.Max():F1} ms"));
        return 0;
    }

    /// <summary>library &lt;folder&gt;: the Library's scan and its cards for a folder (large libraries), with the pictures of the first page.</summary>
    static int PerfLibrary(string folder)
    {
        Text.Language = "en";
        var app = CreateApplication();
        var watch = Stopwatch.StartNew();
        var scan = Engine.ClipLibrary.Scan(folder);
        Console.WriteLine(FormattableString.Invariant($"PERF library scan files={scan.Count} ms={watch.Elapsed.TotalMilliseconds:F0}"));
        var clips = new ClipsViewModel(() => folder, () => "", () => "");
        watch.Restart();
        clips.Refresh();
        Console.WriteLine(FormattableString.Invariant($"PERF library refresh cards={clips.Items.Count} ms={watch.Elapsed.TotalMilliseconds:F0}"));
        watch.Restart();
        clips.Refresh();
        Console.WriteLine(FormattableString.Invariant($"PERF library refresh-again ms={watch.Elapsed.TotalMilliseconds:F1}"));
        watch.Restart();
        WaitUntil(() => clips.Items.All(i => i.Thumbnail != null), 60);
        Console.WriteLine(FormattableString.Invariant($"PERF library pictures={clips.Items.Count(i => i.Thumbnail != null)} ms={watch.Elapsed.TotalMilliseconds:F0}"));
        watch.Restart();
        clips.Filter = scan.Select(c => c.Group).Distinct().Skip(1).FirstOrDefault() ?? "";
        Console.WriteLine(FormattableString.Invariant($"PERF library filter cards={clips.Items.Count} ms={watch.Elapsed.TotalMilliseconds:F1}"));
        watch.Restart();
        clips.Filter = "";
        Console.WriteLine(FormattableString.Invariant($"PERF library unfilter cards={clips.Items.Count} ms={watch.Elapsed.TotalMilliseconds:F1}"));
        watch.Restart();
        while (clips.HasMore)
            clips.LoadMore();
        Console.WriteLine(FormattableString.Invariant($"PERF library all-cards={clips.Items.Count} ms={watch.Elapsed.TotalMilliseconds:F0}"));
        watch.Restart();
        clips.SelectAllCommand.Execute(null);
        Console.WriteLine(FormattableString.Invariant($"PERF library select-all ms={watch.Elapsed.TotalMilliseconds:F1} selected={clips.SelectedCount}"));
        app.Shutdown();
        return 0;
    }
}
