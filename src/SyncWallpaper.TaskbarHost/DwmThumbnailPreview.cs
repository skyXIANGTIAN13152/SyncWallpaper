using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace SyncWallpaper.TaskbarHost;

public sealed record TaskbarThumbnailLayout(
    int WindowWidth,
    int WindowHeight,
    int ContentLeft,
    int ContentTop,
    int ContentWidth,
    int ContentHeight);

public static class TaskbarThumbnailLayoutCalculator
{
    public static TaskbarThumbnailLayout Calculate(
        int sourceWidth,
        int sourceHeight,
        int maximumContentWidth,
        int maximumContentHeight,
        int headerHeight,
        int padding)
    {
        sourceWidth = Math.Max(1, sourceWidth);
        sourceHeight = Math.Max(1, sourceHeight);
        maximumContentWidth = Math.Max(1, maximumContentWidth);
        maximumContentHeight = Math.Max(1, maximumContentHeight);
        headerHeight = Math.Max(0, headerHeight);
        padding = Math.Max(0, padding);
        var scale = Math.Min(1d, Math.Min((double)maximumContentWidth / sourceWidth, (double)maximumContentHeight / sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale, MidpointRounding.AwayFromZero));
        return new TaskbarThumbnailLayout(
            width + padding * 2,
            headerHeight + height + padding * 2,
            padding,
            headerHeight + padding,
            width,
            height);
    }
}

internal sealed class DwmThumbnailPreviewWindow : Window, IDisposable
{
    private readonly TextBlock _title;
    private nint _handle;
    private nint _thumbnail;
    private bool _disposed;
    public bool IsPreviewVisible => !_disposed && _thumbnail != 0 && IsVisible;

    public DwmThumbnailPreviewWindow()
    {
        Title = "屏序窗口预览";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(7, 15, 29));
        SizeToContent = SizeToContent.Manual;

        _title = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, 7, 10, 0)
        };
        Content = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(220, 49, 188, 232)),
            Background = new SolidColorBrush(Color.FromRgb(7, 15, 29)),
            Child = _title
        };
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(_handle, GwlExStyle).ToInt64();
            SetWindowLongPtr(_handle, GwlExStyle, new nint(style | WsExToolWindow | WsExNoActivate));
        };
    }

    public bool ShowFor(TaskbarTaskItem task, FrameworkElement anchor)
    {
        if (_disposed || task.Handle == 0 || !IsWindow(task.Handle)) return false;
        HidePreview();
        try
        {
            if (!IsVisible) Show();
            if (_handle == 0) _handle = new WindowInteropHelper(this).Handle;
            if (DwmRegisterThumbnail(_handle, task.Handle, out _thumbnail) != 0 || _thumbnail == 0)
            {
                Hide();
                return false;
            }
            if (DwmQueryThumbnailSourceSize(_thumbnail, out var sourceSize) != 0)
            {
                HidePreview();
                return false;
            }

            var owner = Window.GetWindow(anchor);
            var ownerHandle = owner is null ? 0 : new WindowInteropHelper(owner).Handle;
            var dpi = ownerHandle == 0 ? 96u : Math.Max(96u, GetDpiForWindow(ownerHandle));
            var scale = dpi / 96d;
            var layout = TaskbarThumbnailLayoutCalculator.Calculate(
                sourceSize.Width,
                sourceSize.Height,
                (int)Math.Round(360 * scale),
                (int)Math.Round(220 * scale),
                (int)Math.Round(30 * scale),
                (int)Math.Round(8 * scale));

            _title.Text = task.Title;
            Width = layout.WindowWidth / scale;
            Height = layout.WindowHeight / scale;
            var anchorPoint = anchor.PointToScreen(new Point(0, 0));
            var monitor = MonitorFromPoint(new PointNative((int)anchorPoint.X, (int)anchorPoint.Y), MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(monitor, ref info);
            var x = Math.Clamp((int)anchorPoint.X, info.Work.Left, Math.Max(info.Work.Left, info.Work.Right - layout.WindowWidth));
            var y = (int)anchorPoint.Y - layout.WindowHeight - (int)Math.Round(8 * scale);
            if (y < info.Work.Top)
                y = (int)(anchorPoint.Y + anchor.ActualHeight * scale + 8 * scale);
            y = Math.Clamp(y, info.Work.Top, Math.Max(info.Work.Top, info.Work.Bottom - layout.WindowHeight));
            SetWindowPos(_handle, HwndTopmost, x, y, layout.WindowWidth, layout.WindowHeight,
                SwpNoActivate | SwpShowWindow | SwpNoOwnerZOrder);

            var destination = new RectNative
            {
                Left = layout.ContentLeft,
                Top = layout.ContentTop,
                Right = layout.ContentLeft + layout.ContentWidth,
                Bottom = layout.ContentTop + layout.ContentHeight
            };
            var properties = new DwmThumbnailProperties
            {
                Flags = DwmTnpRectDestination | DwmTnpVisible | DwmTnpOpacity | DwmTnpSourceClientAreaOnly,
                Destination = destination,
                Opacity = 255,
                Visible = 1,
                SourceClientAreaOnly = 0
            };
            if (DwmUpdateThumbnailProperties(_thumbnail, ref properties) == 0) return true;
            HidePreview();
            return false;
        }
        catch
        {
            HidePreview();
            return false;
        }
    }

    public void HidePreview()
    {
        if (_thumbnail != 0)
        {
            DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = 0;
        }
        if (IsVisible) Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_thumbnail != 0)
        {
            DwmUnregisterThumbnail(_thumbnail);
            _thumbnail = 0;
        }
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        HidePreview();
        Close();
    }

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint MonitorDefaultToNearest = 2;
    private const uint DwmTnpRectDestination = 0x00000001;
    private const uint DwmTnpOpacity = 0x00000004;
    private const uint DwmTnpVisible = 0x00000008;
    private const uint DwmTnpSourceClientAreaOnly = 0x00000010;

    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(PointNative point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("dwmapi.dll")] private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmUnregisterThumbnail(nint thumbnail);
    [DllImport("dwmapi.dll")] private static extern int DwmQueryThumbnailSourceSize(nint thumbnail, out SizeNative size);
    [DllImport("dwmapi.dll")] private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref DwmThumbnailProperties properties);

    [StructLayout(LayoutKind.Sequential)] private struct PointNative { public PointNative(int x, int y) { X = x; Y = y; } public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct SizeNative { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] private struct RectNative { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public RectNative Monitor;
        public RectNative Work;
        public uint Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DwmThumbnailProperties
    {
        public uint Flags;
        public RectNative Destination;
        public RectNative Source;
        public byte Opacity;
        public int Visible;
        public int SourceClientAreaOnly;
    }
}
