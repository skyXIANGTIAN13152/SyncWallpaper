using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Size = System.Windows.Size;

namespace SyncWallpaper.App;

/// <summary>Hollow landscape/portrait display mark, with transparent interiors.</summary>
public sealed class BrandGlyph : FrameworkElement
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(BrandGlyph),
        new FrameworkPropertyMetadata(Brushes.Tan, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    private static readonly Geometry Stand = Geometry.Parse("M111,190 L111,209 M83,209 L139,209");
    private static readonly Geometry Diamond = Geometry.Parse("M128,26 L143,41 128,56 113,41 Z");

    static BrandGlyph() { Stand.Freeze(); Diamond.Freeze(); }
    public BrandGlyph() { IsHitTestVisible = false; SetResourceReference(StrokeProperty, "AccentGradient"); }
    protected override Size MeasureOverride(Size availableSize)
        => new(Math.Min(48, availableSize.Width), Math.Min(48, availableSize.Height));
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var scale = Math.Min(ActualWidth, ActualHeight) / 256;
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 256 * scale) / 2, (ActualHeight - 256 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var pen = new Pen(Stroke, 9) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawRoundedRectangle(null, pen, new Rect(28, 82, 166, 108), 12, 12);
        dc.DrawGeometry(null, pen, Stand);
        dc.DrawRoundedRectangle(null, pen, new Rect(164, 118, 64, 110), 10, 10);
        dc.DrawGeometry(null, pen, Diamond);
        dc.Pop(); dc.Pop();
    }
}
