using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixlReplay.Ui.Edit;

/// <summary>
/// A row of colours: the palette's dots, then a rainbow dot for any other colour, which opens the picker. The chosen
/// colour (<see cref="Colour"/>, "#RRGGBB") is ringed; one not in the palette rings the rainbow dot and shows in it.
/// </summary>
public partial class ColourRow : UserControl
{
    public static readonly DependencyProperty SwatchesProperty = DependencyProperty.Register(nameof(Swatches), typeof(IEnumerable<SwatchChoice>), typeof(ColourRow),
        new PropertyMetadata(null, (d, _) => ((ColourRow)d).Follow()));

    public static readonly DependencyProperty ColourProperty = DependencyProperty.Register(nameof(Colour), typeof(string), typeof(ColourRow),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColourRow)d).Follow()));

    public static readonly DependencyProperty GroupNameProperty = DependencyProperty.Register(nameof(GroupName), typeof(string), typeof(ColourRow),
        new PropertyMetadata("colour"));

    public static readonly DependencyProperty CustomBrushProperty = DependencyProperty.Register(nameof(CustomBrush), typeof(Brush), typeof(ColourRow),
        new PropertyMetadata(Brushes.White));

    public ColourRow() => InitializeComponent();

    public IEnumerable<SwatchChoice>? Swatches
    {
        get => (IEnumerable<SwatchChoice>?)GetValue(SwatchesProperty);
        set => SetValue(SwatchesProperty, value);
    }

    public string Colour
    {
        get => (string)GetValue(ColourProperty);
        set => SetValue(ColourProperty, value);
    }

    /// <summary>The radio group (one per row: a text's, a line's, a shape's).</summary>
    public string GroupName
    {
        get => (string)GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    /// <summary>The colour shown in the rainbow dot: the one chosen in the picker.</summary>
    public Brush CustomBrush => (Brush)GetValue(CustomBrushProperty);

    /// <summary>The rainbow dot is the chosen one when the colour is none of the palette's, and shows that colour.</summary>
    void Follow()
    {
        bool custom = ColourMath.TryParse(Colour, out string hex)
            && !(Swatches ?? []).Any(s => string.Equals(s.Hex, hex, StringComparison.OrdinalIgnoreCase));
        if (custom)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            SetValue(CustomBrushProperty, brush);
        }
        Custom.IsChecked = custom;
    }

    void OnCustom(object sender, RoutedEventArgs e)
    {
        Custom.IsChecked = true;
        PickerPopup.IsOpen = true;
    }
}
