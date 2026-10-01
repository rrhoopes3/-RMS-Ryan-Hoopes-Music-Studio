using System.Windows;
using System.Windows.Media;

namespace RyanMusicStudio.App.Controls;

public sealed class MeterBar : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(double), typeof(MeterBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsClippingProperty =
        DependencyProperty.Register(nameof(IsClipping), typeof(bool), typeof(MeterBar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public bool IsClipping
    {
        get => (bool)GetValue(IsClippingProperty);
        set => SetValue(IsClippingProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 1 || h <= 1) return;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(34, 26, 21)), null, new Rect(0, 0, w, h));
        var fill = Math.Clamp(Level, 0, 1) * w;
        var color = IsClipping ? Color.FromRgb(196, 60, 60) : Level > 0.85 ? Color.FromRgb(230, 184, 77) : Color.FromRgb(126, 139, 106);
        dc.DrawRectangle(new SolidColorBrush(color), null, new Rect(0, 0, fill, h));
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(63, 50, 40)), 1), new Rect(0.5, 0.5, w - 1, h - 1));
    }
}
