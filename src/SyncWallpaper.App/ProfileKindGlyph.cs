using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Size = System.Windows.Size;

namespace SyncWallpaper.App;

/// <summary>Distinct hollow marks for generic and display-specific wallpaper profiles.</summary>
public sealed class ProfileKindGlyph : FrameworkElement
{
    public static readonly DependencyProperty IsGenericProperty = DependencyProperty.Register(
        nameof(IsGeneric), typeof(bool), typeof(ProfileKindGlyph),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(ProfileKindGlyph),
        new FrameworkPropertyMetadata(CreateDefaultStroke(), FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsGeneric { get => (bool)GetValue(IsGenericProperty); set => SetValue(IsGenericProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    private static readonly Geometry GenericCorners = Geometry.Parse("M9,3.5 H3.5 V9 M23,3.5 h5.5 V9 M28.5,23 v5.5 H23 M9,28.5 H3.5 V23");
    private static readonly Geometry GenericLandscape = new RectangleGeometry(new Rect(7.5, 12, 10.5, 8), 1.2, 1.2);
    private static readonly Geometry GenericPortrait = new RectangleGeometry(new Rect(21.5, 9, 4.5, 14), 1.1, 1.1);
    private static readonly Geometry SpecificDiamond = Geometry.Parse("M16,2.5 L29.5,16 16,29.5 2.5,16 Z");
    private static readonly Geometry SpecificScreen = new RectangleGeometry(new Rect(10.5, 11.5, 11, 7.5), 1, 1);
    private static readonly Geometry SpecificStand = Geometry.Parse("M16,19 v3 M13,22 h6");

    static ProfileKindGlyph()
    {
        GenericCorners.Freeze(); GenericLandscape.Freeze(); GenericPortrait.Freeze();
        SpecificDiamond.Freeze(); SpecificScreen.Freeze(); SpecificStand.Freeze();
    }

    public ProfileKindGlyph() { IsHitTestVisible = false; }

    private static Brush CreateDefaultStroke()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xD2, 0xB4, 0x7A));
        brush.Freeze();
        return brush;
    }

    protected override Size MeasureOverride(Size availableSize)
        => new(Math.Min(32, availableSize.Width), Math.Min(32, availableSize.Height));

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var scale = Math.Min(ActualWidth, ActualHeight) / 32;
        if (scale <= 0) return;
        dc.PushTransform(new TranslateTransform((ActualWidth - 32 * scale) / 2, (ActualHeight - 32 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        var pen = new Pen(Stroke, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (IsGeneric)
        {
            dc.DrawGeometry(null, pen, GenericCorners);
            dc.DrawGeometry(null, pen, GenericLandscape);
            dc.DrawGeometry(null, pen, GenericPortrait);
        }
        else
        {
            dc.DrawGeometry(null, pen, SpecificDiamond);
            dc.DrawGeometry(null, pen, SpecificScreen);
            dc.DrawGeometry(null, pen, SpecificStand);
        }
        dc.Pop(); dc.Pop();
    }
}
