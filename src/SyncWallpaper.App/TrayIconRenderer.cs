using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace SyncWallpaper.App;

/// <summary>
/// Hollow landscape/portrait screens, with an independent state mark above.
/// No backing disc, filled screens, halo, or animation is needed at tray sizes.
/// </summary>
internal static class TrayIconRenderer
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 48, 64];
    private static readonly Color Gold = Color.FromArgb(210, 180, 122);
    private static readonly Color WarmWhite = Color.FromArgb(243, 235, 220);
    private static readonly Color Sage = Color.FromArgb(165, 181, 169);
    private static readonly Color Copper = Color.FromArgb(213, 160, 133);

    public static Icon Create(TrayIconState state)
    {
        var frames = IconSizes.Select(size => EncodePng(Render(state, size))).ToArray();
        var ico = BuildIco(frames, IconSizes);
        using var stream = new MemoryStream(ico, writable: false);
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    internal static Bitmap Render(TrayIconState state, int size)
    {
        if (size < 8) throw new ArgumentOutOfRangeException(nameof(size));

        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var scale = size / 32f;
        var accent = state switch
        {
            TrayIconState.Error => Copper,
            TrayIconState.Paused or TrayIconState.Unmatched => Sage,
            _ => Gold
        };
        using var outline = CreatePen(accent, Math.Max(1.1f, 1.8f * scale));
        DrawScreens(graphics, outline, scale);
        DrawStateMark(graphics, state, accent, scale);
        return bitmap;
    }

    private static Pen CreatePen(Color color, float width) => new(color, width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round
    };

    private static void DrawScreens(Graphics graphics, Pen pen, float scale)
    {
        // Leave a real gap where the portrait display overlaps the landscape
        // display. At 16 px, crossed outlines would collapse into a solid blob.
        using var landscape = new GraphicsPath();
        landscape.AddLine(22f * scale, 14f * scale, 22f * scale, 13f * scale);
        landscape.AddArc(18f * scale, 11f * scale, 4f * scale, 4f * scale, 0, -90);
        landscape.AddLine(20f * scale, 11f * scale, 5f * scale, 11f * scale);
        landscape.AddArc(3f * scale, 11f * scale, 4f * scale, 4f * scale, 270, -90);
        landscape.AddLine(3f * scale, 13f * scale, 3f * scale, 23f * scale);
        landscape.AddArc(3f * scale, 21f * scale, 4f * scale, 4f * scale, 180, -90);
        landscape.AddLine(5f * scale, 25f * scale, 18f * scale, 25f * scale);
        graphics.DrawPath(pen, landscape);
        graphics.DrawLine(pen, 12.5f * scale, 25f * scale, 12.5f * scale, 28f * scale);
        graphics.DrawLine(pen, 9f * scale, 28f * scale, 16f * scale, 28f * scale);
        graphics.DrawRoundedRectangle(pen, new RectangleF(20.5f * scale, 16f * scale, 8.5f * scale, 13f * scale), 1.5f * scale);
    }

    private static void DrawStateMark(Graphics graphics, TrayIconState state, Color accent, float scale)
    {
        using var pen = CreatePen(state == TrayIconState.Generic ? WarmWhite : accent, Math.Max(1.1f, 1.8f * scale));
        switch (state)
        {
            case TrayIconState.Normal:
                graphics.DrawLines(pen,
                [
                    new PointF(12f * scale, 5f * scale),
                    new PointF(15f * scale, 8f * scale),
                    new PointF(21f * scale, 2f * scale)
                ]);
                break;
            case TrayIconState.Generic:
                graphics.DrawPolygon(pen,
                [
                    new PointF(16f * scale, 1.5f * scale),
                    new PointF(19.5f * scale, 5f * scale),
                    new PointF(16f * scale, 8.5f * scale),
                    new PointF(12.5f * scale, 5f * scale)
                ]);
                break;
            case TrayIconState.Unmatched:
                graphics.DrawLine(pen, 12f * scale, 5f * scale, 20f * scale, 5f * scale);
                break;
            case TrayIconState.Paused:
                graphics.DrawLine(pen, 13f * scale, 2f * scale, 13f * scale, 8f * scale);
                graphics.DrawLine(pen, 19f * scale, 2f * scale, 19f * scale, 8f * scale);
                break;
            case TrayIconState.Error:
                graphics.DrawLine(pen, 16f * scale, 1.5f * scale, 16f * scale, 5f * scale);
                using (var brush = new SolidBrush(accent))
                {
                    var dot = Math.Max(1f, 1.8f * scale);
                    graphics.FillEllipse(brush, 16f * scale - dot / 2f, 8.5f * scale - dot / 2f, dot, dot);
                }
                break;
            case TrayIconState.Recognizing:
                using (var brush = new SolidBrush(accent))
                {
                    var dot = Math.Max(1f, 2f * scale);
                    foreach (var x in new[] { 11f, 16f, 21f })
                        graphics.FillEllipse(brush, x * scale - dot / 2f, 5f * scale - dot / 2f, dot, dot);
                }
                break;
        }
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using (bitmap)
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
    }

    private static byte[] BuildIco(IReadOnlyList<byte[]> frames, IReadOnlyList<int> sizes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        var offset = 6 + frames.Count * 16;
        for (var index = 0; index < frames.Count; index++)
        {
            var size = sizes[index];
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frames[index].Length);
            writer.Write((uint)offset);
            offset += frames[index].Length;
        }

        foreach (var frame in frames) writer.Write(frame);
        writer.Flush();
        return stream.ToArray();
    }
}

internal static class GraphicsExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, RectangleF rectangle, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2f;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.DrawPath(pen, path);
    }
}
