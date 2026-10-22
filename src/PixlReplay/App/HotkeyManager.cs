using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PixlReplay.App;

/// <summary>
/// System-wide hotkeys via RegisterHotKey on a message-only window: Windows matches the keys itself and PixlReplay wakes
/// only for its own combos. A key another app holds is tried again by <see cref="RetryPending"/>; while the user types a
/// key into a hotkey box, PixlReplay's keys are let go (<see cref="Suspend"/>), so the box gets them and nothing runs.
/// <para>
/// Many games register their keyboard with RIDEV_NOHOTKEYS: while they are in front Windows sends no WM_HOTKEY at all, so
/// keys that worked on the desktop did nothing in the game. A watcher thread also reads the keys' state (as OBS does),
/// and whichever sees a press first runs it, once.
/// </para>
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    const int ProbeId = 0xBFFF;
    static readonly IntPtr HWND_MESSAGE = new(-3);

    readonly Dictionary<int, string> _idsByAtom = [];
    readonly Dictionary<string, bool> _status = [];
    /// <summary>Every key PixlReplay wants, by id (kept while suspended, registered again on resume).</summary>
    readonly Dictionary<string, Hotkey> _wanted = [];
    /// <summary>Keys another app held when they were registered, tried again by <see cref="RetryPending"/>.</summary>
    readonly Dictionary<string, Hotkey> _pending = [];
    int _nextAtom = 1;

    public event Action<string>? Pressed;

    public IReadOnlyDictionary<string, bool> Status => _status;

    /// <summary>Whether PixlReplay's keys are let go because the user is typing one into a hotkey box.</summary>
    public bool Suspended { get => _suspended; private set => _suspended = value; }
    volatile bool _suspended;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams { Parent = HWND_MESSAGE });
        _watcher = new Thread(Watch) { IsBackground = true, Name = "PixlReplay keys", Priority = ThreadPriority.AboveNormal };
        _watcher.Start();
    }

    // ------------------------------------------------------------------ the key watcher

    readonly Thread _watcher;
    volatile bool _stopping;
    /// <summary>The keys to watch, swapped whole when they change (read by the watcher without a lock).</summary>
    volatile KeyValuePair<string, Hotkey>[] _watched = [];
    readonly Dictionary<string, long> _lastFired = [];
    readonly object _fireLock = new();
    /// <summary>Every id ever watched, never shrinking: a press posted by the watcher names its id by place here.</summary>
    readonly List<string> _ids = [];
    const int WM_WATCHED = 0x8001; // WM_APP + 1

    /// <summary>A press seen twice (Windows' message and the watcher) within this long runs once.</summary>
    const int SamePressMs = 400;

    /// <summary>How often the watcher reads the keys: quick enough for any press, nothing measurable on the CPU.</summary>
    const int WatchMs = 15;

    void Watch()
    {
        var down = new Dictionary<string, bool>();
        while (!_stopping)
        {
            Thread.Sleep(WatchMs);
            if (Suspended)
            {
                down.Clear();
                continue;
            }
            foreach (var (id, hotkey) in _watched)
            {
                bool pressed = IsDown(hotkey);
                bool was = down.GetValueOrDefault(id);
                down[id] = pressed;
                if (pressed && !was)
                    Fire(id, "watch");
            }
        }
    }

    /// <summary>The combination is held now: its key and exactly its modifiers (Alt+Shift+F10 is not Alt+F10).</summary>
    static bool IsDown(Hotkey h) =>
        Held((int)h.Key)
        && Held(0x12) == h.Alt && Held(0x11) == h.Ctrl && Held(0x10) == h.Shift && (Held(0x5B) || Held(0x5C)) == h.Win;

    static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>
    /// Runs <paramref name="id"/>, unless the same press already did: at once for Windows' message (on the window's
    /// thread already), posted to the window for the watcher's (actions always run on the window's thread).
    /// </summary>
    void Fire(string id, string by)
    {
        long now = Environment.TickCount64;
        int index;
        lock (_fireLock)
        {
            if (_lastFired.TryGetValue(id, out long last) && now - last < SamePressMs)
                return;
            _lastFired[id] = now;
            index = _ids.IndexOf(id);
            if (index < 0)
            {
                _ids.Add(id);
                index = _ids.Count - 1;
            }
        }
        if (by == "windows")
        {
            Pressed?.Invoke(id);
            return;
        }
        LastWatchedPress = (id, DateTime.Now);
        PostMessage(Handle, WM_WATCHED, index, IntPtr.Zero);
    }

    /// <summary>The last press only the watcher saw (Windows sent nothing): for the log.</summary>
    public (string Id, DateTime At)? LastWatchedPress { get; private set; }

    void UpdateWatched() => _watched = Suspended ? [] : [.. _wanted];

    // ------------------------------------------------------------------ suspending while a key is typed

    DateTime _suspendedAt;

    /// <summary>
    /// The longest the keys stay let go for a hotkey box: a key is typed in seconds, so a box left focused (or a "typed"
    /// message that never came) does not leave PixlReplay without keys.
    /// </summary>
    static readonly TimeSpan MaxSuspend = TimeSpan.FromSeconds(20);

    /// <summary>Takes the keys back when a suspension has outlasted <see cref="MaxSuspend"/> (the engine's tick asks).</summary>
    public void ResumeIfForgotten()
    {
        if (Suspended && DateTime.UtcNow - _suspendedAt > MaxSuspend)
            Resume();
    }

    /// <summary>Whether no app (PixlReplay included) holds these keys: reserved for an instant and let go.</summary>
    public static bool IsFree(Hotkey hotkey)
    {
        bool free = RegisterHotKey(IntPtr.Zero, ProbeId, Modifiers(hotkey), (uint)hotkey.Key);
        if (free)
            UnregisterHotKey(IntPtr.Zero, ProbeId);
        return free;
    }

    /// <summary>Registers <paramref name="hotkey"/> under <paramref name="id"/>; false when another app owns it.</summary>
    public bool Register(string id, Hotkey hotkey)
    {
        _wanted[id] = hotkey;
        UpdateWatched();
        if (Suspended)
        {
            _status[id] = true; // taken when typing ends
            return true;
        }
        bool ok = Reserve(id, hotkey);
        if (ok)
            _pending.Remove(id);
        else
            _pending[id] = hotkey;
        return ok;
    }

    /// <summary>
    /// Tries the keys another app held once more (when the settings window asks for the status, so only while it is
    /// open): one Windows call per such key, nothing at all when every key works.
    /// </summary>
    public void RetryPending()
    {
        if (_pending.Count == 0 || Suspended)
            return;
        foreach (var (id, hotkey) in _pending.ToList())
            if (Reserve(id, hotkey))
                _pending.Remove(id);
    }

    /// <summary>Lets PixlReplay's keys go while one is typed into a hotkey box.</summary>
    public void Suspend()
    {
        if (Suspended)
            return;
        ReleaseAll();
        Suspended = true;
        _suspendedAt = DateTime.UtcNow;
        UpdateWatched();
    }

    /// <summary>Takes the keys again after typing (the ones saved meanwhile included).</summary>
    public void Resume()
    {
        if (!Suspended)
            return;
        Suspended = false;
        _pending.Clear();
        foreach (var (id, hotkey) in _wanted)
            if (!Reserve(id, hotkey))
                _pending[id] = hotkey;
        UpdateWatched();
    }

    public void UnregisterAll()
    {
        ReleaseAll();
        _status.Clear();
        _wanted.Clear();
        _pending.Clear();
        UpdateWatched();
    }

    void ReleaseAll()
    {
        foreach (int atom in _idsByAtom.Keys)
            UnregisterHotKey(Handle, atom);
        _idsByAtom.Clear();
    }

    bool Reserve(string id, Hotkey hotkey)
    {
        int atom = _nextAtom++;
        bool ok = RegisterHotKey(Handle, atom, Modifiers(hotkey), (uint)hotkey.Key);
        if (ok)
            _idsByAtom[atom] = id;
        _status[id] = ok;
        return ok;
    }

    static uint Modifiers(Hotkey hotkey)
    {
        uint mods = MOD_NOREPEAT;
        if (hotkey.Ctrl) mods |= MOD_CONTROL;
        if (hotkey.Alt) mods |= MOD_ALT;
        if (hotkey.Shift) mods |= MOD_SHIFT;
        if (hotkey.Win) mods |= MOD_WIN;
        return mods;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && _idsByAtom.TryGetValue((int)m.WParam, out string? id))
            Fire(id, "windows");
        else if (m.Msg == WM_WATCHED)
        {
            string? watched;
            lock (_fireLock)
                watched = (int)m.WParam >= 0 && (int)m.WParam < _ids.Count ? _ids[(int)m.WParam] : null;
            if (watched != null && !Suspended)
                Pressed?.Invoke(watched);
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        _stopping = true;
        UnregisterAll();
        DestroyHandle();
    }

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, int msg, nint wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
