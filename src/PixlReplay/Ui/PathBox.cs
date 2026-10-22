using System.Windows;
using System.Windows.Controls;

namespace PixlReplay.Ui;

/// <summary>Keeps the end of a long read-only path visible (the folder name), instead of the drive letter.</summary>
public static class PathBox
{
    public static readonly DependencyProperty ShowEndProperty = DependencyProperty.RegisterAttached(
        "ShowEnd", typeof(bool), typeof(PathBox), new PropertyMetadata(false, OnChanged));

    public static bool GetShowEnd(DependencyObject d) => (bool)d.GetValue(ShowEndProperty);
    public static void SetShowEnd(DependencyObject d, bool value) => d.SetValue(ShowEndProperty, value);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box || e.NewValue is not true)
            return;
        box.Loaded += (_, _) => ScrollToEnd(box);
        box.TextChanged += (_, _) => ScrollToEnd(box);
        box.SizeChanged += (_, _) => ScrollToEnd(box);
        box.LostKeyboardFocus += (_, _) => ScrollToEnd(box);
    }

    static void ScrollToEnd(TextBox box) =>
        box.Dispatcher.BeginInvoke(() => box.ScrollToHorizontalOffset(double.MaxValue), System.Windows.Threading.DispatcherPriority.Loaded);
}
