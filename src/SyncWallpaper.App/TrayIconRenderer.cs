using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace SyncWallpaper.App;

/// <summary>
/// Renders the small monitor-topology icon used by the Windows notification area.
/// It intentionally contains no nebula, eye, text or fine HUD marks so that the
/// silhouette remains legible at 16 px and at high-DPI scaling factors.
/// </summary>
internal static class TrayIconRenderer
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 48, 64];

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
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(Color.Transparent);

        var scale = size / 64f;
        var accent = state switch
        {
            TrayIconState.Error => Color.FromArgb(255, 255, 79, 78),
            TrayIconState.Paused => Color.FromArgb(255, 166, 184, 211),
            _ => Color.FromArgb(255, 53, 231, 255)
        };
        var accentDim = state switch
        {
            TrayIconState.Error => Color.FromArgb(220, 195, 46, 48),
            TrayIconState.Paused => Color.FromArgb(205, 101, 120, 148),
            _ => Color.FromArgb(220, 26, 139, 204)
        };
        var stroke = Math.Max(1.2f, 4f * scale);
        var ringRect = new RectangleF(5f * scale, 5f * scale, 54f * scale, 54f * scale);

        // A soft static halo is only used for the larger source frames. It is
        // baked into the bitmap and does not create a rendering loop in the app.
        if (size >= 24)
        {
            using var halo = new Pen(Color.FromArgb(35, accent), Math.Max(2f, 9f * scale));
            graphics.DrawEllipse(halo, ringRect);
        }

        using (var ringPen = new Pen(accentDim, stroke))
        {
            if (state == TrayIconState.Recognizing)
            {
                graphics.DrawArc(ringPen, ringRect, -82, 286);
                graphics.DrawArc(ringPen, ringRect, 218, 22);
                DrawScanDots(graphics, ringRect, accent, scale);
            }
            else if (state == TrayIconState.Error)
            {
                graphics.DrawArc(ringPen, ringRect, -48, 250);
                graphics.DrawArc(ringPen, ringRect, 238, 42);
            }
            else
            {
                graphics.DrawEllipse(ringPen, ringRect);
            }
        }

        DrawMonitorTopology(graphics, state, accent, accentDim, scale);

        if (state == TrayIconState.Paused)
            DrawPauseMark(graphics, accent, scale);
        else if (state == TrayIconState.Error)
            DrawWarningMark(graphics, accent, scale);
        else if (state == TrayIconState.Normal)
            DrawNormalStatusDot(graphics, accent, scale);

        return bitmap;
    }

    private static void DrawMonitorTopology(Graphics graphics, TrayIconState state, Color accent, Color accentDim, float scale)
    {
        var screenPen = new Pen(accent, Math.Max(1.3f, 3.6f * scale)) { LineJoin = LineJoin.Round };
        var secondaryPen = new Pen(accentDim, Math.Max(1.1f, 3.1f * scale)) { LineJoin = LineJoin.Round };
        using (screenPen)
        using (secondaryPen)
        {
            var monitor = new RectangleF(15f * scale, 24f * scale, 28f * scale, 19f * scale);
            graphics.DrawRoundedRectangle(screenPen, monitor, Math.Max(1f, 3f * scale));
            graphics.DrawLine(screenPen, 29f * scale, 43f * scale, 29f * scale, 49f * scale);
            graphics.DrawLine(screenPen, 23f * scale, 49f * scale, 35f * scale, 49f * scale);

            // The second display is deliberately offset, matching the supplied
            // taskbar reference while remaining recognizable at 16 px.
            var companion = new RectangleF(38f * scale, 32f * scale, 17f * scale, 14f * scale);
            if (state == TrayIconState.Error)
            {
                var oldStyle = secondaryPen.DashStyle;
                secondaryPen.DashStyle = DashStyle.Dash;
                graphics.DrawRoundedRectangle(secondaryPen, companion, Math.Max(1f, 2f * scale));
                secondaryPen.DashStyle = oldStyle;
            }
            else
            {
                graphics.DrawRoundedRectangle(secondaryPen, companion, Math.Max(1f, 2f * scale));
            }

            graphics.DrawLine(secondaryPen, 41f * scale, 48f * scale, 54f * scale, 48f * scale);
            if (state == TrayIconState.Error)
            {
                using var crack = new Pen(accent, Math.Max(1.2f, 3f * scale)) { LineJoin = LineJoin.Round };
                graphics.DrawLines(crack,
                [
                    new PointF(35f * scale, 44f * scale),
                    new PointF(38f * scale, 47f * scale),
                    new PointF(36f * scale, 50f * scale)
                ]);
            }
        }
    }

    private static void DrawScanDots(Graphics graphics, RectangleF ringRect, Color accent, float scale)
    {
        using var brush = new SolidBrush(accent);
        var center = new PointF(ringRect.Left + ringRect.Width / 2f, ringRect.Top + ringRect.Height / 2f);
        var radius = ringRect.Width / 2f;
        foreach (var angle in new[] { -64f, -48f, -32f, -16f })
        {
            var radians = angle * Math.PI / 180d;
            var x = center.X + (float)Math.Cos(radians) * radius;
            var y = center.Y + (float)Math.Sin(radians) * radius;
            var dot = Math.Max(1.2f, 2.6f * scale);
            graphics.FillEllipse(brush, x - dot / 2f, y - dot / 2f, dot, dot);
        }
    }

    private static void DrawNormalStatusDot(Graphics graphics, Color accent, float scale)
    {
        using var brush = new SolidBrush(accent);
        var diameter = Math.Max(2f, 8f * scale);
        graphics.FillEllipse(brush, 20f * scale - diameter / 2f, 52f * scale - diameter / 2f, diameter, diameter);
        var orbitDot = Math.Max(1.5f, 4f * scale);
        graphics.FillEllipse(brush, 49f * scale - orbitDot / 2f, 12f * scale - orbitDot / 2f, orbitDot, orbitDot);
    }

    private static void DrawPauseMark(Graphics graphics, Color accent, float scale)
    {
        using var brush = new SolidBrush(accent);
        var width = Math.Max(1.5f, 4.2f * scale);
        graphics.FillRectangle(brush, 27f * scale, 10f * scale, width, 9f * scale);
        graphics.FillRectangle(brush, 35f * scale, 10f * scale, width, 9f * scale);
    }

    private static void DrawWarningMark(Graphics graphics, Color accent, float scale)
    {
        var points = new[]
        {
            new PointF(49f * scale, 42f * scale),
            new PointF(59f * scale, 58f * scale),
            new PointF(39f * scale, 58f * scale)
        };
        using var fill = new SolidBrush(accent);
        using var textBrush = new SolidBrush(Color.FromArgb(255, 25, 22, 30));
        graphics.FillPolygon(fill, points);
        using var markPen = new Pen(textBrush, Math.Max(1.2f, 2.5f * scale)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(markPen, 49f * scale, 47f * scale, 49f * scale, 52f * scale);
        graphics.FillEllipse(textBrush, 48f * scale, 54f * scale, Math.Max(1.4f, 2.5f * scale), Math.Max(1.4f, 2.5f * scale));
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
