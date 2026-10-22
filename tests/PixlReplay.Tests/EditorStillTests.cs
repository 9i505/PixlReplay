using System.Windows.Threading;
using PixlReplay.Engine.Edit;
using PixlReplay.Engine.Share;
using PixlReplay.Ui.Edit;

namespace PixlReplay.Tests;

/// <summary>The editor's paused picture: read off the window's thread, shown on it.</summary>
public static class EditorStillTests
{
    static readonly string Clip = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Share", "clip-av1-2tracks.mp4");

    [Test]
    public static void The_still_is_read_off_the_windows_thread_and_shown_on_it() => OnSta(() =>
    {
        TestFfmpeg.Load();
        var info = ClipTranscoder.Probe(Clip);
        var vm = new EditorViewModel(EditProject.For(Clip, info.Duration), isPicture: false, EditorMode.Library, 16 / 9.0, info.AudioStreams);
        int window = Environment.CurrentManagedThreadId, shownOn = 0, shown = 0, readFired = 0;
        using var playback = new EditorPlayback(vm, _ =>
        {
            shownOn = Environment.CurrentManagedThreadId;
            shown++;
        }, _ => { });
        playback.StillRead += _ => readFired = Environment.CurrentManagedThreadId;
        Assert.True(PumpUntil(() => shown > 0, 5), "the first still shows");
        Assert.True(shownOn == window, $"shown on thread {shownOn}, the window's is {window}");
        Assert.True(readFired == window, $"the still-read event on thread {readFired}, the window's is {window}");
        Assert.True(playback.StillReadOn != 0 && playback.StillReadOn != window, $"read on thread {playback.StillReadOn}: the window's thread must not decode");

        // The playhead moved: the still for the new moment shows, on the window's thread again.
        int before = shown;
        vm.PlayheadOutput = 2.5;
        Assert.True(PumpUntil(() => shown > before, 5), "the still after a move shows");
        Assert.True(shownOn == window, "shown on the window's thread after a move");
    });

    [Test]
    public static void Saving_then_closing_lets_go_of_the_preview_twice_without_an_error() => OnSta(() =>
    {
        // Saving lets go of the preview (the clip may be replaced); the editor closing afterwards lets go of it again.
        TestFfmpeg.Load();
        var info = ClipTranscoder.Probe(Clip);
        var vm = new EditorViewModel(EditProject.For(Clip, info.Duration), isPicture: false, EditorMode.Library, 16 / 9.0, info.AudioStreams);
        int shown = 0;
        var playback = new EditorPlayback(vm, _ => shown++, _ => { });
        Assert.True(PumpUntil(() => shown > 0, 5), "the first still shows (its reading thread runs)");
        playback.Dispose();
        PumpUntil(() => false, 0.5); // the reading thread ends and lets go of what it held
        playback.Dispose();
    });

    /// <summary>Runs the window's thread (timers and all) until <paramref name="ready"/> or the time is up.</summary>
    static bool PumpUntil(Func<bool> ready, double seconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!ready() && DateTime.UtcNow < deadline)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
        return ready();
    }

    static void OnSta(Action test)
    {
        string? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception e)
            {
                error = e.ToString();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.True(error == null, error ?? "");
    }
}
