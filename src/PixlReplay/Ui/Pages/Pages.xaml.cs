using System.Windows.Controls;

namespace PixlReplay.Ui.Pages;

public partial class StatusPage : UserControl { public StatusPage() => InitializeComponent(); }

public partial class ClipsPage : UserControl
{
    ScrollViewer? _scroller;

    public ClipsPage()
    {
        InitializeComponent();
        // The page sits in the window's scroller: near its end, the next page of clips is added.
        Loaded += (_, _) =>
        {
            _scroller = FindScroller(this);
            if (_scroller != null)
                _scroller.ScrollChanged += OnScroll;
        };
        Unloaded += (_, _) =>
        {
            if (_scroller != null)
                _scroller.ScrollChanged -= OnScroll;
            _scroller = null;
        };
    }

    void OnScroll(object sender, ScrollChangedEventArgs e)
    {
        // Only when the user scrolls down near the end, or the cards do not fill the window yet; never while hidden
        // behind another page (the scroller is shared) or while the page is being laid out again.
        if (!IsVisible || DataContext is not ClipsViewModel { HasMore: true } vm || _scroller is not { } s)
            return;
        bool nearEnd = s.VerticalOffset + s.ViewportHeight >= s.ExtentHeight - 600;
        bool notFilled = s.ExtentHeight <= s.ViewportHeight + 1;
        if ((nearEnd && e.VerticalChange > 0) || notFilled)
            vm.LoadMore();
    }

    static ScrollViewer? FindScroller(System.Windows.DependencyObject from)
    {
        for (var d = System.Windows.Media.VisualTreeHelper.GetParent(from); d != null; d = System.Windows.Media.VisualTreeHelper.GetParent(d))
            if (d is ScrollViewer sv)
                return sv;
        return null;
    }
}

public partial class CapturePage : UserControl { public CapturePage() => InitializeComponent(); }

public partial class QualityPage : UserControl { public QualityPage() => InitializeComponent(); }

public partial class AudioPage : UserControl { public AudioPage() => InitializeComponent(); }

public partial class HotkeysPage : UserControl { public HotkeysPage() => InitializeComponent(); }

public partial class SmartSavePage : UserControl { public SmartSavePage() => InitializeComponent(); }

public partial class ConnectionsPage : UserControl { public ConnectionsPage() => InitializeComponent(); }

public partial class GeneralPage : UserControl { public GeneralPage() => InitializeComponent(); }

public partial class LookPage : UserControl { public LookPage() => InitializeComponent(); }
