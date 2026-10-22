using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixlReplay.Settings;

namespace PixlReplay.Ui;

/// <summary>
/// Puts the Look page's choices into the resources that Theme.xaml and the pages reference dynamically: every color, the
/// window's font, and the picture behind the window (with the panels see-through over it). A change shows at once.
/// </summary>
public static class ThemeAccent
{
    /// <summary>The look just applied; the window shows its picture and size from it.</summary>
    public static event Action<LookSettings>? Changed;

    public static LookSettings Current { get; private set; } = new();

    /// <summary>Applies <paramref name="look"/>, or the look in use when there is none.</summary>
    public static void Apply(ResourceDictionary resources, LookSettings? look = null)
    {
        look ??= Current;
        Current = look.Copy();
        ThemeColors.Use(look);
        bool picture = look.HasPicture;
        // Over a picture the panels let it show through; pop-ups (menus, lists, tooltips) stay solid to stay readable.
        byte panel = picture ? (byte)Math.Round(255 * look.Panels) : (byte)255;
        byte hover = picture ? (byte)Math.Round(255 * Math.Min(1, look.Panels + 0.1)) : (byte)255;

        resources["WindowBg"] = Brush(ThemeColors.Background);
        resources["Bg"] = Brush(ThemeColors.Background, panel);
        resources["NavBg"] = Brush(ThemeColors.NavBg, panel);
        resources["Surface"] = Brush(ThemeColors.Surface, panel);
        resources["CardHover"] = Brush(ThemeColors.CardHover, hover);
        resources["Stroke"] = Brush(ThemeColors.Stroke);
        resources["StrokeStrong"] = Brush(ThemeColors.StrokeStrong);
        resources["ControlStroke"] = Brush(ThemeColors.ControlStroke);
        resources["TextPrimary"] = Brush(ThemeColors.Text);
        resources["TextSecondary"] = Brush(ThemeColors.TextSecondary);
        resources["Idle"] = Brush(ThemeColors.Idle);
        resources["IdleSoft"] = Brush(ThemeColors.Mix(ThemeColors.Surface, ThemeColors.Text, 0.06), panel);
        resources["SuccessSoft"] = Brush(ThemeColors.Mix(ThemeColors.Surface, ThemeColors.Success, 0.12), panel);
        resources["WarningSoft"] = Brush(ThemeColors.Mix(ThemeColors.Surface, ThemeColors.Warning, 0.12), panel);

        resources["Accent"] = Brush(ThemeColors.Accent);
        resources["AccentHover"] = Brush(ThemeColors.Hover);
        resources["AccentSoft"] = Brush(ThemeColors.Soft, panel);
        resources["OnAccent"] = Brush(ThemeColors.OnAccent);
        foreach (string key in FluentAccentColors)
            resources[key] = Color(ThemeColors.Accent);
        foreach (string key in FluentAccentBrushes)
            resources[key] = Brush(ThemeColors.Accent);

        // Fields and pop-ups (Fluent's own templates ask for these by name).
        resources["FieldFill"] = Brush(ThemeColors.FieldFill, panel);
        resources["ComboBoxBackgroundPointerOver"] = Brush(ThemeColors.FieldHover, panel);
        resources["TextControlBackgroundPointerOver"] = Brush(ThemeColors.FieldHover, panel);
        resources["ComboBoxBackgroundFocused"] = Brush(ThemeColors.FieldFill, panel);
        resources["TextControlBackgroundFocused"] = Brush(ThemeColors.FieldFill, panel);
        resources["ComboBoxBorderBrushFocused"] = Brush(ThemeColors.StrokeStrong);
        resources["ComboBoxBackgroundDisabled"] = Brush(ThemeColors.Surface, panel);
        resources["TextControlBackgroundDisabled"] = Brush(ThemeColors.Surface, panel);
        resources["ComboBoxBorderBrushDisabled"] = Brush(ThemeColors.Stroke);
        resources["TextControlBorderBrushDisabled"] = Brush(ThemeColors.Stroke);
        resources["ComboBoxItemBackgroundSelected"] = Brush(ThemeColors.ItemSelected);
        foreach (string key in PopupBackgrounds)
            resources[key] = Brush(ThemeColors.Surface);
        foreach (string key in PopupBorders)
            resources[key] = Brush(ThemeColors.Stroke);
        // Fluent's text, so a light background gets dark text in lists and fields too.
        resources["TextFillColorPrimaryBrush"] = Brush(ThemeColors.Text);
        resources["TextFillColorSecondaryBrush"] = Brush(ThemeColors.TextSecondary);
        resources["TextFillColorTertiaryBrush"] = Brush(ThemeColors.Idle);
        resources["TextFillColorDisabledBrush"] = Brush(ThemeColors.Mix(ThemeColors.Text, ThemeColors.Background, 0.55));

        var font = FontFor(look.Font);
        resources["UiFont"] = font;
        resources["ContentControlThemeFontFamily"] = font;

        Changed?.Invoke(Current);
    }

    /// <summary>The window's font for a Look page choice: Segoe UI, a font shipped beside the app, or an installed one.</summary>
    public static FontFamily FontFor(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return DefaultFont;
        if (key.StartsWith("app:", StringComparison.Ordinal))
            return Edit.TextFonts.For(key[4..]).Family;
        return new FontFamily(key + ", Segoe UI");
    }

    public static readonly FontFamily DefaultFont = new("Segoe UI Variable Text, Segoe UI");

    static string? _pictureFor;
    static DateTime _pictureTime;
    static ImageSource? _picture;

    /// <summary>The picture behind the window, read once per file (scaled down to at most 2560 px wide).</summary>
    public static ImageSource? Picture(LookSettings look)
    {
        if (!look.HasPicture)
            return null;
        var time = File.GetLastWriteTimeUtc(look.Picture);
        if (look.Picture == _pictureFor && time == _pictureTime)
            return _picture;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(look.Picture);
            image.DecodePixelWidth = 2560;
            image.EndInit();
            image.Freeze();
            _picture = image;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            _picture = null;
        }
        _pictureFor = look.Picture;
        _pictureTime = time;
        return _picture;
    }

    static readonly string[] FluentAccentColors =
    [
        "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
        "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
    ];

    static readonly string[] FluentAccentBrushes =
    [
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
        "SliderThumbBackground", "SliderThumbBackgroundPointerOver",
        "ComboBoxItemPillFillBrush", // the bar beside the chosen entry of a drop-down list
    ];

    static readonly string[] PopupBackgrounds = ["ComboBoxDropDownBackground", "ContextMenuBackground", "FlyoutBackground", "ToolTipBackground"];
    static readonly string[] PopupBorders = ["ComboBoxDropDownBorderBrush", "ContextMenuBorderBrush", "FlyoutBorderBrush", "ToolTipBorderBrush"];

    static System.Windows.Media.Color Color(uint rgb, byte alpha = 255) =>
        System.Windows.Media.Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static SolidColorBrush Brush(uint rgb, byte alpha = 255)
    {
        var b = new SolidColorBrush(Color(rgb, alpha));
        b.Freeze();
        return b;
    }
}
