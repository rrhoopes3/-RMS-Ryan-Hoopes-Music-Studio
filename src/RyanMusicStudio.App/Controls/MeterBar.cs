using System.Windows;
using System.Windows.Media;

namespace RyanMusicStudio.App.Controls;

/// <summary>
/// Level meter on a decibel scale (-48 dBFS to 0), so a normal vocal or guitar visibly moves it.
/// Sage below -12 dBFS, amber up to -3, red above that or when the input clipped. A thin cream line
/// holds the recent peak for about a second so short loud notes are readable.
/// </summary>
public sealed class MeterBar : FrameworkElement
{
    private const double FloorDb = -48;
    private const double AmberDb = -12;
    private const double RedDb = -3;
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(1.2);

    private double _holdFraction;
    private DateTime _holdSince;

    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(double), typeof(MeterBar),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsClippingProperty =
        DependencyProperty.Register(nameof(IsClipping), typeof(bool), typeof(MeterBar),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Linear peak, 0..1.</summary>
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

    private static double Fraction(double linear)
    {
        if (linear <= 0) return 0;
        var db = 20 * Math.Log10(linear);
        return Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 1 || h <= 1) return;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(23, 26, 30)), null, new Rect(0, 0, w, h));

        var level = Fraction(Level);
        var amberAt = Fraction(Math.Pow(10, AmberDb / 20));
        var redAt = Fraction(Math.Pow(10, RedDb / 20));
        var sage = Color.FromRgb(63, 185, 122);
        var amber = Color.FromRgb(230, 184, 77);
        var red = Color.FromRgb(229, 72, 77);
        if (IsClipping)
        {
            dc.DrawRectangle(new SolidColorBrush(red), null, new Rect(0, 0, level * w, h));
        }
        else
        {
            dc.DrawRectangle(new SolidColorBrush(sage), null, new Rect(0, 0, Math.Min(level, amberAt) * w, h));
            if (level > amberAt)
                dc.DrawRectangle(new SolidColorBrush(amber), null, new Rect(amberAt * w, 0, (Math.Min(level, redAt) - amberAt) * w, h));
            if (level > redAt)
                dc.DrawRectangle(new SolidColorBrush(red), null, new Rect(redAt * w, 0, (level - redAt) * w, h));
        }

        var now = DateTime.UtcNow;
        if (level >= _holdFraction || now - _holdSince > Hold)
        {
            _holdFraction = level;
            _holdSince = now;
        }
        if (_holdFraction > 0.01)
        {
            var hx = Math.Min(w - 1, _holdFraction * w);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(232, 236, 241)), null, new Rect(hx, 0, 2, h));
        }

        // Brass ticks at -24, -12, -6 and -3 dBFS.
        var tick = new SolidColorBrush(Color.FromArgb(150, 61, 187, 111));
        foreach (var db in new[] { -24.0, AmberDb, -6.0, RedDb })
        {
            var x = Fraction(Math.Pow(10, db / 20)) * w;
            dc.DrawRectangle(tick, null, new Rect(x, h - Math.Min(5, h / 2), 1, Math.Min(5, h / 2)));
        }
        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(42, 47, 55)), 1), new Rect(0.5, 0.5, w - 1, h - 1));
    }
}
