using System.Windows;
using System.Windows.Media;

namespace PixlReplay.Ui;

/// <summary>A ring that fills clockwise from the top as <see cref="Value"/> goes from 0 to 1 (a save's progress).</summary>
public sealed class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(ProgressRing),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.HotPink, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(nameof(Track), typeof(Brush), typeof(ProgressRing),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight), r = (size - Thickness) / 2;
        if (r <= 0)
            return;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(Track, Thickness), centre, r, r);
        double value = Math.Clamp(Value, 0, 1);
        var pen = new Pen(Fill, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (value >= 0.999)
        {
            dc.DrawEllipse(null, pen, centre, r, r);
            return;
        }
        if (value <= 0.001)
            return;
        double angle = value * 2 * Math.PI;
        var start = new Point(centre.X, centre.Y - r);
        var end = new Point(centre.X + r * Math.Sin(angle), centre.Y - r * Math.Cos(angle));
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(start, false, false);
            g.ArcTo(end, new Size(r, r), 0, value > 0.5, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        dc.DrawGeometry(null, pen, arc);
    }
}
