using System.Windows;
using System.Windows.Controls;

namespace PixlReplay.Ui;

/// <summary>Takes the width it is given and the height that keeps <see cref="Ratio"/> (width / height).</summary>
public sealed class AspectBox : Decorator
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(nameof(Ratio), typeof(double), typeof(AspectBox),
        new FrameworkPropertyMetadata(16.0 / 9, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double Ratio { get => (double)GetValue(RatioProperty); set => SetValue(RatioProperty, value); }

    Size Fit(Size available)
    {
        double ratio = Ratio > 0 && double.IsFinite(Ratio) ? Ratio : 16.0 / 9;
        double width = double.IsInfinity(available.Width) ? 400 : available.Width;
        return new Size(width, Math.Round(width / ratio));
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var size = Fit(constraint);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = Fit(arrangeSize);
        Child?.Arrange(new Rect(size));
        return size;
    }
}
