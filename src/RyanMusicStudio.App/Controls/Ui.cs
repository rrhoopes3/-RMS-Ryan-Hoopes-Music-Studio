using System.Windows;

namespace RyanMusicStudio.App.Controls;

/// <summary>
/// Attached settings the shared button template reads. CornerRadius defaults to a pill; pads and
/// round transport buttons set their own.
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Ui),
            new FrameworkPropertyMetadata(new CornerRadius(999)));

    /// <summary>Flat buttons drop the sheen and shadow (e.g. the mixer's track-name label).</summary>
    public static readonly DependencyProperty FlatProperty =
        DependencyProperty.RegisterAttached("Flat", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));

    public static bool GetFlat(DependencyObject d) => (bool)d.GetValue(FlatProperty);
    public static void SetFlat(DependencyObject d, bool value) => d.SetValue(FlatProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, CornerRadius value) => d.SetValue(CornerRadiusProperty, value);
}
