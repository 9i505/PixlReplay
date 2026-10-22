using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PixlReplay.App;

public enum TrayState { Starting, Active, Waiting, Paused, Recording, Error }

/// <summary>Tray icon and menu. Icons are drawn at runtime, so no image files ship with the app.</summary>
public sealed class TrayController : IDisposable
{
    readonly NotifyIcon _icon = new();
    readonly Dictionary<string, Icon> _icons = [];
    readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = 1500 };
    TrayState _state = TrayState.Starting;
    string? _menuKey;

    public event Action? SaveClicked, QuickClicked, ScreenshotClicked, PauseClicked, RecordClicked, PauseRecordClicked, FolderClicked, ShareClicked, DeleteLastClicked, SettingsClicked, ExitClicked;

    public TrayController()
    {
        DrawIcons();
        _icon.Icon = _icons["waiting"];
        _icon.Text = Tooltip;
        _icon.Visible = true;
        _icon.DoubleClick += (_, _) => SettingsClicked?.Invoke();
        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); ApplyIcon(); };
    }

    /// <summary>The tray icon's tooltip: just the name (the icon's color already tells the state).</summary>
    public static string Tooltip => Text.Get("app");

    /// <summary>Everything the menu's items depend on. The tooltip is not part of it.</summary>
    public static string MenuKey(TrayState state, bool quickEnabled, int quickSeconds, bool paused, bool recording, string language,
        bool canUndo = false, bool canShare = false, bool recordingPaused = false, bool lastIsShot = false) =>
        $"{state}|{quickEnabled}|{quickSeconds}|{paused}|{recording}|{language}|{canUndo}|{canShare}|{recordingPaused}|{lastIsShot}";

    /// <summary>Updates the icon; rebuilds the menu only when its items change (so an open menu stays open).</summary>
    /// <param name="canUndo">A clip saved in this session can still be sent to the Recycle Bin from the menu.</param>
    /// <param name="canShare">There is a last clip to open in the share panel.</param>
    /// <param name="recordingPaused">The full recording is paused: its menu item resumes it.</param>
    /// <param name="lastIsShot">The last thing saved is a screenshot: the share and delete items say so.</param>
    public void Update(TrayState state, bool quickEnabled, int quickSeconds, bool paused, bool recording, bool canUndo = false,
        bool canShare = false, bool recordingPaused = false, bool lastIsShot = false)
    {
        _state = state;
        _icon.Text = Tooltip;
        if (!_flashTimer.Enabled)
            ApplyIcon();

        string key = MenuKey(state, quickEnabled, quickSeconds, paused, recording, Text.Language, canUndo, canShare, recordingPaused, lastIsShot);
        if (key == _menuKey)
            return;
        _menuKey = key;

        var menu = TrayMenu.Create(Text.IsRightToLeft);
        menu.Items.Add(Text.Get("tray.save"), null, (_, _) => SaveClicked?.Invoke());
        if (quickEnabled)
            menu.Items.Add(Text.Format("tray.quick", Text.Count("count.seconds", quickSeconds)), null, (_, _) => QuickClicked?.Invoke());
        menu.Items.Add(Text.Get("tray.screenshot"), null, (_, _) => ScreenshotClicked?.Invoke());
        // While a full recording runs, pause is for the recording (as on the Status page).
        if (recording)
            menu.Items.Add(Text.Get(recordingPaused ? "tray.resumeRecord" : "tray.pauseRecord"), null, (_, _) => PauseRecordClicked?.Invoke());
        else
            menu.Items.Add(Text.Get(paused ? "tray.resume" : "tray.pause"), null, (_, _) => PauseClicked?.Invoke());
        menu.Items.Add(Text.Get(recording ? "tray.recordStop" : "tray.recordStart"), null, (_, _) => RecordClicked?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Text.Get("tray.openFolder"), null, (_, _) => FolderClicked?.Invoke());
        if (canShare)
            menu.Items.Add(Text.Get(lastIsShot ? "tray.shareLastShot" : "tray.share"), null, (_, _) => ShareClicked?.Invoke());
        if (canUndo)
            menu.Items.Add(Text.Get(lastIsShot ? "tray.deleteLastShot" : "tray.deleteLast"), null, (_, _) => DeleteLastClicked?.Invoke());
        menu.Items.Add(Text.Get("tray.settings"), null, (_, _) => SettingsClicked?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Text.Get("tray.exit"), null, (_, _) => ExitClicked?.Invoke());
        var old = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = menu;
        old?.Dispose();
    }

    void DrawIcons()
    {
        foreach (string kind in new[] { "active", "waiting", "paused", "recording", "error", "saved" })
            _icons[kind] = DrawIcon(kind);
    }

    /// <summary>Active and recording use the theme color; saved, error and idle keep their meaning colors.</summary>
    public static uint ColorFor(string kind) => kind switch
    {
        "active" or "recording" => Settings.ThemeColors.Accent,
        "saved" => Settings.ThemeColors.Success,
        "error" => Settings.ThemeColors.Warning,
        _ => Settings.ThemeColors.Idle,
    };

    /// <summary>Draws the icons again in the Look page's colors (they changed, or were just read at start).</summary>
    public void Redraw()
    {
        var old = _icons.Values.ToList();
        DrawIcons();
        ApplyIcon();
        _menuKey = null; // the menu's colors too, at its next update
        foreach (var icon in old)
            icon.Dispose();
    }

    /// <summary>Briefly shows the green "saved" icon.</summary>
    public void FlashSaved()
    {
        _icon.Icon = _icons["saved"];
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    void ApplyIcon() => _icon.Icon = _icons[_state switch
    {
        TrayState.Active => "active",
        TrayState.Paused => "paused",
        TrayState.Recording => "recording",
        TrayState.Error => "error",
        _ => "waiting",
    }];

    static Icon DrawIcon(string kind)
    {
        using var bmp = DrawBitmap(kind);
        IntPtr h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }

    /// <summary>
    /// The 32 px tray picture for <paramref name="kind"/>: the app's P in the state's color (a play mark would be too small
    /// to see at tray size), with a dot while recording and two bars while paused.
    /// </summary>
    public static Bitmap DrawBitmap(string kind)
    {
        const int size = 32;
        var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            var plateColor = Color.FromArgb(unchecked((int)(0xFF000000 | Settings.ThemeColors.Background)));
            using (var bg = new SolidBrush(plateColor))
            using (var path = GdiShapes.Rounded(new RectangleF(1, 1, size - 2, size - 2), 8))
                g.FillPath(bg, path);

            Color color = Color.FromArgb(unchecked((int)(0xFF000000 | ColorFor(kind))));
            using var brush = new SolidBrush(color);
            using (var letter = AppIconArt.Letter(new RectangleF(5, 4, 22, 24)))
                g.FillPath(brush, letter);
            if (kind is "recording" or "paused")
            {
                // A badge at the foot of the bowl, ringed in the plate's color so it stands off the letter.
                using var ring = new SolidBrush(plateColor);
                g.FillEllipse(ring, 17, 17, 14, 14);
                if (kind == "recording")
                    g.FillEllipse(brush, 19.5f, 19.5f, 9, 9);
                else
                {
                    g.FillRectangle(brush, 20.5f, 20, 2.6f, 8);
                    g.FillRectangle(brush, 25, 20, 2.6f, 8);
                }
            }
        }
        return bmp;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _flashTimer.Dispose();
        foreach (var i in _icons.Values)
            i.Dispose();
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
}
