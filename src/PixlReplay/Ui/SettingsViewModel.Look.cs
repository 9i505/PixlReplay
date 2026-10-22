using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PixlReplay.App;
using PixlReplay.Settings;
using PixlReplay.Ui.Edit;

namespace PixlReplay.Ui;

/// <summary>A ready-made theme on the Look page: its three colors, shown as a small window.</summary>
public sealed class LookTheme(string key, string accent, string background, string text, Action<LookTheme> choose)
    : Choice<string>(key, _ => { })
{
    public string Label => Text.Get("look.theme." + Value);
    public string Accent => accent;
    public string Background => background;
    public string TextColor => text;
    public Brush AccentBrush { get; } = ThemeAccent.Brush(LookSettings.Parse(accent));
    public Brush BackgroundBrush { get; } = ThemeAccent.Brush(LookSettings.Parse(background));
    public Brush SurfaceBrush { get; } = ThemeAccent.Brush(ThemeColors.Mix(LookSettings.Parse(background), LookSettings.Parse(text), 0.08));
    public Brush TextBrush { get; } = ThemeAccent.Brush(LookSettings.Parse(text));
    public ICommand ChooseCommand => _choose ??= new RelayCommand(_ => choose(this));
    RelayCommand? _choose;

    public bool Matches(LookSettings look) =>
        string.Equals(look.Accent, accent, StringComparison.OrdinalIgnoreCase)
        && string.Equals(look.Background, background, StringComparison.OrdinalIgnoreCase)
        && string.Equals(look.Text, text, StringComparison.OrdinalIgnoreCase);
}

/// <summary>A font the window can use: its key in <see cref="LookSettings.Font"/>, its name, and the font itself.</summary>
public sealed record LookFont(string Key, string Label, FontFamily Family);

/// <summary>
/// The Look page: colors, font, size and a picture behind the window. Unlike the other pages it needs no Apply: each
/// change shows at once and is saved a moment later, and the engine is told so its tray icon follows.
/// </summary>
public sealed partial class SettingsViewModel
{
    public LookSettings Look => _s.Look;

    static readonly string[] AccentPalette = ["#FF5A64", "#FF8F6B", "#FFC93C", "#3DDC84", "#4FC3F7", "#5B8CFF", "#B98CFF", "#F48BA4"];
    static readonly string[] BackgroundPalette = ["#1C1C21", "#101012", "#19191C", "#121826", "#141D18", "#1B1625", "#2E2726", "#F4F1EC"];
    static readonly string[] TextPalette = ["#F2F2F5", "#FFFFFF", "#E8EEF8", "#F5ECEA", "#C9C9D1", "#1E1B18"];

    /// <summary>The ready-made themes: a name (look.theme.*), then accent, background and text.</summary>
    internal static readonly (string Key, string Accent, string Background, string Text)[] ThemePresets =
    [
        ("pixlReplay", LookSettings.DefaultAccent, LookSettings.DefaultBackground, LookSettings.DefaultText),
        ("pixl", "#FF8F6B", "#19191C", "#F0F0F2"),
        ("midnight", "#4FC3F7", "#121826", "#E8EEF8"),
        ("forest", "#3DDC84", "#141D18", "#E9F2EC"),
        ("violet", "#B98CFF", "#1B1625", "#F1ECF8"),
        ("rose", "#F48BA4", "#2E2726", "#F5ECEA"),
        ("gold", "#FFC93C", "#16140F", "#F5F0E4"),
        ("light", "#E5484D", "#F4F1EC", "#1E1B18"),
    ];

    IReadOnlyList<LookTheme>? _themes;

    public IReadOnlyList<LookTheme> LookThemes => _themes ??= ThemePresets
        .Select(p => new LookTheme(p.Key, p.Accent, p.Background, p.Text,
            t => SetLook(l => { l.Accent = t.Accent; l.Background = t.Background; l.Text = t.TextColor; })))
        .ToList();

    IReadOnlyList<SwatchChoice>? _accentSwatches, _backgroundSwatches, _textSwatches;
    public IReadOnlyList<SwatchChoice> AccentSwatches => _accentSwatches ??= AccentPalette.Select(h => new SwatchChoice(h, v => AccentHex = v)).ToList();
    public IReadOnlyList<SwatchChoice> BackgroundSwatches => _backgroundSwatches ??= BackgroundPalette.Select(h => new SwatchChoice(h, v => BackgroundHex = v)).ToList();
    public IReadOnlyList<SwatchChoice> TextSwatches => _textSwatches ??= TextPalette.Select(h => new SwatchChoice(h, v => TextHex = v)).ToList();

    public string AccentHex { get => _s.Look.Accent; set => SetLook(l => l.Accent = value); }
    public string BackgroundHex { get => _s.Look.Background; set => SetLook(l => l.Background = value); }
    public string TextHex { get => _s.Look.Text; set => SetLook(l => l.Text = value); }

    IReadOnlyList<LookFont>? _fonts;

    /// <summary>Segoe UI, the Arabic and English fonts shipped with the app, then every font installed on this PC.</summary>
    public IReadOnlyList<LookFont> LookFonts => _fonts ??= BuildFonts();

    static readonly string[] SymbolFonts = ["Wingdings", "Webdings", "Symbol", "MDL2", "Fluent Icons", "Marlett", "HoloLens", "Emoji", "Bookshelf"];

    static IReadOnlyList<LookFont> BuildFonts()
    {
        var list = new List<LookFont> { new("", Text.Get("look.fontDefault"), ThemeAccent.DefaultFont) };
        list.AddRange(TextFonts.All.Where(f => f.Key != TextFonts.Default).Select(f => new LookFont("app:" + f.Key, f.Label, f.Family)));
        var shipped = list.Select(f => f.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        list.AddRange(Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(name => !shipped.Contains(name) && !SymbolFonts.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => new LookFont(name, name, new FontFamily(name))));
        return list;
    }

    public LookFont? LookFont
    {
        get => LookFonts.FirstOrDefault(f => f.Key == _s.Look.Font) ?? LookFonts[0];
        set => SetLook(l => l.Font = value?.Key ?? "");
    }

    /// <summary>The window's size in percent (80 to 140).</summary>
    public double LookSize
    {
        get => Math.Round(_s.Look.Size * 100);
        set => SetLook(l => l.Size = value / 100);
    }

    public bool LookHasPicture => _s.Look.HasPicture;
    public string LookPictureText => LookHasPicture ? Path.GetFileName(_s.Look.Picture) : Text.Get("look.pictureNone");
    public string LookPictureButton => Text.Get(LookHasPicture ? "look.pictureChange" : "look.pictureChoose");

    /// <summary>How much the background color covers the picture, in percent.</summary>
    public double LookDim { get => Math.Round(_s.Look.PictureDim * 100); set => SetLook(l => l.PictureDim = value / 100); }
    public double LookBlur { get => Math.Round(_s.Look.PictureBlur); set => SetLook(l => l.PictureBlur = value); }
    /// <summary>How solid the cards and side bar are over the picture, in percent.</summary>
    public double LookPanels { get => Math.Round(_s.Look.Panels * 100); set => SetLook(l => l.Panels = value / 100); }

    public ICommand ChoosePictureCommand => _choosePicture ??= new RelayCommand(_ => ChoosePicture());
    public ICommand RemovePictureCommand => _removePicture ??= new RelayCommand(_ => SetPicture(""));
    public ICommand ResetLookCommand => _resetLook ??= new RelayCommand(_ => ResetLook());
    RelayCommand? _choosePicture, _removePicture, _resetLook;

    static string LookFolder => Path.Combine(SettingsStore.AppDataDir, "look");

    /// <summary>Copies the chosen picture into the data folder, so moving or deleting the original keeps the window's.</summary>
    void ChoosePicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Text.Get("look.pickerTitle"),
            Filter = Text.Get("look.pickerFilter") + "|*.jpg;*.jpeg;*.png;*.bmp;*.webp;*.gif;*.jfif",
        };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            Directory.CreateDirectory(LookFolder);
            string copy = Path.Combine(LookFolder, "background-" + Guid.NewGuid().ToString("N")[..8] + Path.GetExtension(dialog.FileName).ToLowerInvariant());
            File.Copy(dialog.FileName, copy, overwrite: true);
            SetPicture(copy);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ObsLogSafe("look: picture not copied: " + e.Message);
        }
    }

    void SetPicture(string path)
    {
        string old = _s.Look.Picture;
        SetLook(l => l.Picture = path);
        // The previous copy is ours (in the data folder) and nothing points at it any more.
        if (old.Length > 0 && old != path && old.StartsWith(LookFolder, StringComparison.OrdinalIgnoreCase))
            try { File.Delete(old); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    void ResetLook()
    {
        SetPicture("");
        SetLook(l =>
        {
            var fresh = new LookSettings();
            l.Accent = fresh.Accent;
            l.Background = fresh.Background;
            l.Text = fresh.Text;
            l.Font = fresh.Font;
            l.Size = fresh.Size;
            l.PictureDim = fresh.PictureDim;
            l.PictureBlur = fresh.PictureBlur;
            l.Panels = fresh.Panels;
        });
    }

    static readonly string[] LookProperties =
    [
        nameof(AccentHex), nameof(BackgroundHex), nameof(TextHex), nameof(LookFont), nameof(LookSize), nameof(LookHasPicture),
        nameof(LookPictureText), nameof(LookPictureButton), nameof(LookDim), nameof(LookBlur), nameof(LookPanels),
    ];

    DispatcherTimer? _lookShow, _lookSave;

    /// <summary>
    /// Changes the look: shown in the window right away (at most every 40 ms while a slider or the color picker is dragged),
    /// saved on its own after 400 ms of quiet. The saved copy the Apply bar compares with takes it at once, so the
    /// bar never asks to apply a look.
    /// </summary>
    void SetLook(Action<LookSettings> change)
    {
        change(_s.Look);
        _s.Look.Normalize();
        var saved = SettingsStore.Deserialize(_savedJson);
        saved.Look = _s.Look.Copy();
        _savedJson = SettingsStore.Serialize(saved);
        RefreshLookChoices();
        Raise(LookProperties);

        _lookShow ??= NewTimer(40, () => ApplyAccent());
        if (!_lookShow.IsEnabled)
            _lookShow.Start();
        _lookSave ??= NewTimer(400, SaveLook);
        _lookSave.Stop();
        _lookSave.Start();
    }

    static DispatcherTimer NewTimer(int ms, Action tick)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) => { timer.Stop(); tick(); };
        return timer;
    }

    void RefreshLookChoices()
    {
        foreach (var s in AccentSwatches)
            s.Refresh(_s.Look.Accent);
        foreach (var s in BackgroundSwatches)
            s.Refresh(_s.Look.Background);
        foreach (var s in TextSwatches)
            s.Refresh(_s.Look.Text);
        string? theme = LookThemes.FirstOrDefault(t => t.Matches(_s.Look))?.Value;
        foreach (var t in LookThemes)
            t.Refresh(theme ?? "");
    }

    /// <summary>Saves only the look (any other unsaved change stays pending) and tells the engine.</summary>
    void SaveLook()
    {
        try
        {
            var disk = SettingsStore.Load(SettingsStore.DefaultPath);
            disk.Look = _s.Look.Copy();
            SettingsStore.Save(SettingsStore.DefaultPath, disk);
            _savedJson = SettingsStore.Serialize(disk);
            IsDirty = SettingsStore.Serialize(_s) != _savedJson;
            Raise(nameof(IsDirty), nameof(Message), nameof(ShowBar));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ObsLogSafe("look: not saved: " + e.Message);
            return;
        }
        Task.Run(() => Send("look", 800));
    }

    /// <summary>A look change still waiting for its save is saved now (before the window reloads its settings or closes).</summary>
    void FlushLook()
    {
        if (_lookSave is { IsEnabled: true })
        {
            _lookSave.Stop();
            SaveLook();
        }
    }

    static void ObsLogSafe(string message)
    {
        try { Engine.ObsLog.Warn(message); } catch (Exception) { }
    }
}
