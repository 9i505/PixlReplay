using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixlReplay.Ui.Edit;

/// <summary>
/// Any colour: a field of saturation and brightness in a hue, the hue bar beside it, and the colour's code to type or
/// paste. <see cref="Colour"/> is "#RRGGBB", changed as the user drags (the text or line follows at once).
/// </summary>
public partial class ColourPicker : UserControl
{
    public static readonly DependencyProperty ColourProperty = DependencyProperty.Register(nameof(Colour), typeof(string), typeof(ColourPicker),
        new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColourPicker)d).Show()));

    double _h, _s, _v = 1;
    bool _own, _dragField, _dragHue;

    public ColourPicker()
    {
        InitializeComponent();
        Loaded += (_, _) => Show();
        SizeChanged += (_, _) => Place();
    }

    public string Colour
    {
        get => (string)GetValue(ColourProperty);
        set => SetValue(ColourProperty, value);
    }

    /// <summary>The marks, the field's hue, the preview and the code follow a colour set from outside.</summary>
    void Show()
    {
        if (_own)
            return;
        if (ColourMath.TryParse(Colour, out string hex))
        {
            var (h, s, v) = ColourMath.ToHsv(hex);
            if (s > 0)
                _h = h; // a grey has no hue of its own: the field keeps the last one, so it does not jump
            _s = s;
            _v = v;
            _own = true;
            try
            {
                if (HexBox.Text != hex[1..])
                    HexBox.Text = hex[1..];
            }
            finally
            {
                _own = false;
            }
        }
        Place();
    }

    void Place()
    {
        HueFill.Fill = Frozen(ColourMath.ToHex(_h, 1, 1));
        Preview.Fill = Frozen(ColourMath.ToHex(_h, _s, _v));
        FieldMark.Margin = new Thickness(_s * Field.ActualWidth - 7, (1 - _v) * Field.ActualHeight - 7, 0, 0);
        HueMark.Margin = new Thickness(0, _h / 360 * HueBar.ActualHeight - 3, 0, 0);
    }

    /// <summary>The colour from the marks' places: out through <see cref="Colour"/>, and the code with it.</summary>
    void Pick()
    {
        string hex = ColourMath.ToHex(_h, _s, _v);
        _own = true;
        try
        {
            Colour = hex;
            HexBox.Text = hex[1..];
        }
        finally
        {
            _own = false;
        }
        Place();
    }

    void OnFieldDown(object sender, MouseButtonEventArgs e)
    {
        _dragField = true;
        Field.CaptureMouse();
        FromField(e.GetPosition(Field));
    }

    void OnFieldMove(object sender, MouseEventArgs e)
    {
        if (_dragField)
            FromField(e.GetPosition(Field));
    }

    void OnFieldUp(object sender, MouseButtonEventArgs e)
    {
        _dragField = false;
        Field.ReleaseMouseCapture();
    }

    void FromField(Point p)
    {
        _s = Math.Clamp(p.X / Math.Max(1, Field.ActualWidth), 0, 1);
        _v = 1 - Math.Clamp(p.Y / Math.Max(1, Field.ActualHeight), 0, 1);
        Pick();
    }

    void OnHueDown(object sender, MouseButtonEventArgs e)
    {
        _dragHue = true;
        HueBar.CaptureMouse();
        FromHue(e.GetPosition(HueBar));
    }

    void OnHueMove(object sender, MouseEventArgs e)
    {
        if (_dragHue)
            FromHue(e.GetPosition(HueBar));
    }

    void OnHueUp(object sender, MouseButtonEventArgs e)
    {
        _dragHue = false;
        HueBar.ReleaseMouseCapture();
    }

    void FromHue(Point p)
    {
        _h = Math.Clamp(p.Y / Math.Max(1, HueBar.ActualHeight), 0, 0.9999) * 360;
        Pick();
    }

    void OnHexChanged(object sender, TextChangedEventArgs e)
    {
        if (_own || !ColourMath.TryParse(HexBox.Text, out string hex) || hex == Colour)
            return;
        var (h, s, v) = ColourMath.ToHsv(hex);
        if (s > 0)
            _h = h;
        _s = s;
        _v = v;
        _own = true;
        try
        {
            Colour = hex;
        }
        finally
        {
            _own = false;
        }
        Place();
    }

    static SolidColorBrush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
