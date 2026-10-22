using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PixlReplay.App;

namespace PixlReplay.Ui;

/// <summary>A read-only box that records the next key combination pressed while it has focus.</summary>
public sealed class HotkeyBox : TextBox
{
    /// <summary>True when a hotkey box gets focus (keys pressed now are being typed), false when it loses it.</summary>
    public static event Action<bool>? TypingChanged;

    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey), typeof(string), typeof(HotkeyBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((HotkeyBox)d).Refresh()));

    public string Hotkey
    {
        get => (string)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        FlowDirection = FlowDirection.LeftToRight;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        Cursor = Cursors.Hand;
        Loaded += (_, _) => Refresh();
        Refresh();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        Text = App.Text.Get("hotkeys.press");
        TypingChanged?.Invoke(true);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Refresh();
        TypingChanged?.Invoke(false);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Back or Key.Delete)
        {
            Hotkey = "";
            Keyboard.ClearFocus();
            return;
        }
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Tab)
            return;

        var mods = Keyboard.Modifiers;
        var h = new App.Hotkey((System.Windows.Forms.Keys)KeyInterop.VirtualKeyFromKey(key),
            mods.HasFlag(ModifierKeys.Control), mods.HasFlag(ModifierKeys.Alt), mods.HasFlag(ModifierKeys.Shift),
            mods.HasFlag(ModifierKeys.Windows));
        string formatted = HotkeyParser.Format(h);
        if (!HotkeyParser.TryParse(formatted, out _))
            return;
        // PixlReplay's own keys are let go while a box has focus, so a key held now is another app's: it would not work.
        if (!HotkeyManager.IsFree(h))
        {
            Refuse();
            return;
        }
        Hotkey = formatted;
        Keyboard.ClearFocus();
    }

    /// <summary>Says so inside the box for two seconds, keeps the old key, and waits for another.</summary>
    void Refuse()
    {
        Text = App.Text.Get("hotkeys.takenBox");
        _refused ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refused.Stop();
        _refused.Tick -= OnRefusedShown;
        _refused.Tick += OnRefusedShown;
        _refused.Start();
    }

    void OnRefusedShown(object? sender, EventArgs e)
    {
        _refused!.Stop();
        if (IsKeyboardFocused)
            Text = App.Text.Get("hotkeys.press");
    }

    DispatcherTimer? _refused;

    void Refresh()
    {
        if (!IsKeyboardFocused)
            Text = string.IsNullOrEmpty(Hotkey) ? App.Text.Get("hotkeys.none") : Hotkey;
    }
}
