using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

namespace SyncWallpaper.App;

/// <summary>Static vector atmosphere: no timers, image files, or wallpaper changes.</summary>
public sealed class MysteryBackdrop : FrameworkElement
{
    public static readonly DependencyProperty SurfaceProperty = DependencyProperty.Register(
        nameof(Surface), typeof(Brush), typeof(MysteryBackdrop),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(MysteryBackdrop),
        new FrameworkPropertyMetadata(Brushes.Tan, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Surface { get => (Brush)GetValue(SurfaceProperty); set => SetValue(SurfaceProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public MysteryBackdrop()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SetResourceReference(SurfaceProperty, "WindowSurface");
        SetResourceReference(AccentProperty, "Cyan");
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize), 14, 14));
        dc.DrawRectangle(Surface, null, new Rect(RenderSize));
        dc.PushTransform(new ScaleTransform(ActualWidth / 1280, ActualHeight / 804));
        var color = (Accent as SolidColorBrush)?.Color ?? Color.FromRgb(210, 180, 122);
        dc.DrawEllipse(Glow(color, .17), null, new Point(1100, 0), 580, 490);
        dc.DrawEllipse(Glow(color, .10), null, new Point(410, 820), 620, 420);
        dc.DrawEllipse(Glow(color, .06), null, new Point(830, 395), 520, 200);
        var halo = new RadialGradientBrush();
        halo.GradientStops.Add(new GradientStop(Colors.Transparent, .77));
        halo.GradientStops.Add(new GradientStop(WithAlpha(color, .13), .965));
        halo.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        halo.Freeze();
        dc.DrawEllipse(halo, null, new Point(1100, 40), 285, 285);
        dc.DrawEllipse(null, Pen(color, .16), new Point(1100, 40), 246, 246);
        dc.DrawEllipse(null, Pen(color, .22), new Point(1100, 40), 282, 282);
        dc.PushTransform(new RotateTransform(-35, 1100, 40));
        dc.DrawEllipse(null, Pen(color, .12), new Point(1100, 40), 363, 202);
        dc.Pop();
        DrawPath(dc, color, .12, "M734,164 L869,101 1003,164 1122,97 1258,181");
        DrawPath(dc, color, .075, "M235,705 C345,627 450,632 565,684 S886,733 1058,592 1296,431 1359,465");
        DrawPath(dc, color, .055, "M212,730 C341,652 444,647 574,714 S873,735 1029,622 1249,459 1336,490");
        foreach (var star in new[] { (869d, 101d, 2d), (1003d, 164d, 2.4), (1122d, 97d, 2d),
            (1258d, 181d, 1.5), (686d, 132d, 1d), (1240d, 299d, 1d), (335d, 648d, 1d), (925d, 493d, 1d) })
            dc.DrawEllipse(Brush(color, .36), null, new Point(star.Item1, star.Item2), star.Item3, star.Item3);
        dc.Pop();
        dc.Pop();
    }

    private static Color WithAlpha(Color color, double opacity) => Color.FromArgb((byte)(255 * opacity), color.R, color.G, color.B);
    private static SolidColorBrush Brush(Color color, double opacity)
    {
        var brush = new SolidColorBrush(WithAlpha(color, opacity)); brush.Freeze(); return brush;
    }
    private static Pen Pen(Color color, double opacity)
    {
        var pen = new Pen(Brush(color, opacity), 1); pen.Freeze(); return pen;
    }
    private static RadialGradientBrush Glow(Color color, double opacity)
    {
        var brush = new RadialGradientBrush(WithAlpha(color, opacity), Colors.Transparent);
        brush.Freeze(); return brush;
    }
    private static void DrawPath(DrawingContext dc, Color color, double opacity, string data)
    {
        var geometry = Geometry.Parse(data); geometry.Freeze();
        dc.DrawGeometry(null, Pen(color, opacity), geometry);
    }
}
