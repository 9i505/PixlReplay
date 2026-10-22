using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PixlReplay.App;

namespace PixlReplay.Ui;

/// <summary>A screenshot at full size over the settings window, with what can be done with it.</summary>
public partial class PictureViewer : UserControl
{
    readonly DispatcherTimer _copiedFor = new() { Interval = TimeSpan.FromSeconds(1.6) };
    ClipItem? _item;

    public PictureViewer()
    {
        InitializeComponent();
        _copiedFor.Tick += (_, _) => ShowCopied(false);
    }

    public event Action<ClipItem>? ShareRequested, ShowRequested, DeleteRequested, EditRequested;

    /// <summary>The picture a step (+1 next, -1 before) away on the Library page, or null; set by the window.</summary>
    public Func<ClipItem, int, ClipItem?>? Neighbor { get; set; }

    public bool IsOpen => _item != null;

    public void Show(ClipItem item)
    {
        _item = item;
        Picture.Source = PictureClipboard.TryLoad(item.Path); // gone or unreadable: the viewer still opens, with its buttons
        Heading.Text = item.App;
        Meta.Text = item.Meta;
        ShowCopied(false);
        // Each chevron points the way its button goes: in Arabic "next" sits on the left and points left.
        bool rtl = FlowDirection == FlowDirection.RightToLeft;
        PrevIcon.Text = rtl ? "" : "";
        NextIcon.Text = rtl ? "" : "";
        PrevButton.IsEnabled = Neighbor?.Invoke(item, -1) != null;
        NextButton.IsEnabled = Neighbor?.Invoke(item, +1) != null;
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => CloseButton.Focus(), DispatcherPriority.Input);
    }

    public void Hide()
    {
        _item = null;
        Picture.Source = null; // lets go of the picture (it may be deleted next)
        Visibility = Visibility.Collapsed;
    }

    void ShowCopied(bool copied)
    {
        _copiedFor.Stop();
        CopyLabel.Text = Text.Get(copied ? "viewer.copied" : "viewer.copy");
        CopyIcon.Text = copied ? "" : "";
        if (copied)
            _copiedFor.Start();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape && IsOpen)
        {
            Hide();
            e.Handled = true;
        }
        // The arrow that points at a button goes that way, in either reading direction.
        else if (e.Key is Key.Left or Key.Right && IsOpen)
        {
            int step = e.Key == Key.Right ? +1 : -1;
            Step(FlowDirection == FlowDirection.RightToLeft ? -step : step);
            e.Handled = true;
        }
    }

    void OnPrev(object sender, RoutedEventArgs e) => Step(-1);

    void OnNext(object sender, RoutedEventArgs e) => Step(+1);

    void Step(int step)
    {
        if (_item != null && Neighbor?.Invoke(_item, step) is { } next)
            Show(next);
    }

    void OnScrimClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource == Scrim)
            Hide();
    }

    void OnPictureClick(object sender, MouseButtonEventArgs e) => e.Handled = true; // a click on the picture keeps it open

    void OnClose(object sender, RoutedEventArgs e) => Hide();

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_item != null && File.Exists(_item.Path) && PictureClipboard.Copy(_item.Path))
            ShowCopied(true);
    }

    void OnShare(object sender, RoutedEventArgs e) => Act(ShareRequested, close: true);

    void OnShow(object sender, RoutedEventArgs e) => Act(ShowRequested, close: false);

    void OnDelete(object sender, RoutedEventArgs e) => Act(DeleteRequested, close: true);

    void OnEdit(object sender, RoutedEventArgs e) => Act(EditRequested, close: true);

    void Act(Action<ClipItem>? handler, bool close)
    {
        if (_item is not { } item)
            return;
        if (close)
            Hide();
        handler?.Invoke(item);
    }
}
