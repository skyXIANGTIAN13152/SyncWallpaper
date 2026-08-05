using System.Runtime.InteropServices;
using SyncWallpaper.Core;

namespace SyncWallpaper.DesktopEngine;

/// <summary>
/// Desktop icon adapter using Shell view interfaces rather than Progman/WorkerW list-view coordinates.
/// Different Windows builds can refuse the view service; in that case the caller receives an empty
/// capture or a false result and can leave the existing layout untouched.
/// </summary>
public sealed class WindowsShellDesktopIconProvider : IDesktopIconProvider
{
    private readonly Func<IReadOnlyList<MonitorIdentity>> _monitors;

    public WindowsShellDesktopIconProvider(Func<IReadOnlyList<MonitorIdentity>> monitors) => _monitors = monitors;

    public IReadOnlyList<DesktopIconPosition> Capture()
    {
        try
        {
            var view = GetDesktopView();
            if (view is null) return Array.Empty<DesktopIconPosition>();
            view.GetItemCount(AllView, out var count);
            var result = new List<DesktopIconPosition>();
            for (uint i = 0; i < count; i++)
            {
                if (view.GetItem(i, out var pidl) != 0 || pidl == IntPtr.Zero) continue;
                try
                {
                    if (view.GetItemPosition(pidl, out var point) != 0) continue;
                    var parsing = GetName(pidl, Sigdn.DesktopAbsoluteParsing);
                    var display = GetName(pidl, Sigdn.NormalDisplay);
                    var monitor = _monitors().FirstOrDefault(x => point.X >= x.DesktopX && point.X < x.DesktopX + x.Width &&
                        point.Y >= x.DesktopY && point.Y < x.DesktopY + x.Height);
                    result.Add(new DesktopIconPosition
                    {
                        ParsingName = parsing,
                        DisplayName = display,
                        DesktopPath = parsing,
                        MonitorDevicePath = monitor?.MonitorDevicePath ?? string.Empty,
                        X = point.X,
                        Y = point.Y
                    });
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
            return result;
        }
        catch { return Array.Empty<DesktopIconPosition>(); }
    }

    public bool TrySetPosition(DesktopIconPosition position)
    {
        try
        {
            var view = GetDesktopView();
            if (view is null) return false;
            var pidl = FindPidl(view, position.ParsingName);
            if (pidl == IntPtr.Zero) return false;
            try
            {
                var point = new POINT { X = position.X, Y = position.Y };
                return view.SelectAndPositionItems(1, new[] { pidl }, new[] { point }, 0) == 0;
            }
            finally { Marshal.FreeCoTaskMem(pidl); }
        }
        catch { return false; }
    }

    public bool TrySetViewSettings(int iconSize, bool autoArrange, bool alignToGrid)
    {
        try
        {
            var view = GetDesktopView() as IFolderView2;
            if (view is null) return false;
            var flags = (autoArrange ? FolderViewFlagAutoArrange : 0u) | (alignToGrid ? FolderViewFlagSnapToGrid : 0u);
            return view.SetCurrentFolderFlags(FolderViewFlagAutoArrange | FolderViewFlagSnapToGrid, flags) == 0;
        }
        catch { return false; }
    }

    private static IntPtr FindPidl(IShellView view, string parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return IntPtr.Zero;
        view.GetItemCount(AllView, out var count);
        for (uint i = 0; i < count; i++)
        {
            if (view.GetItem(i, out var pidl) != 0 || pidl == IntPtr.Zero) continue;
            if (string.Equals(GetName(pidl, Sigdn.DesktopAbsoluteParsing), parsingName, StringComparison.OrdinalIgnoreCase))
                return pidl;
            Marshal.FreeCoTaskMem(pidl);
        }
        return IntPtr.Zero;
    }

    private static string GetName(IntPtr pidl, Sigdn sigdn)
    {
        return SHGetNameFromIDList(pidl, sigdn, out var value) == 0 && value != IntPtr.Zero
            ? Marshal.PtrToStringUni(value) ?? string.Empty
            : string.Empty;
    }

    private static IShellView? GetDesktopView()
    {
        var shellWindows = (IShellWindows)new ShellWindows();
        object index = 0;
        var dispatch = shellWindows.Item(ref index);
        if (dispatch is not IServiceProvider serviceProvider) return null;
        var serviceId = SidTopLevelBrowser;
        var interfaceId = typeof(IShellBrowser).GUID;
        serviceProvider.QueryService(ref serviceId, ref interfaceId, out var browserObject);
        if (browserObject is not IShellBrowser browser) return null;
        browser.QueryActiveShellView(out var view);
        return view;
    }

    private const uint AllView = 0xFFFFFFFF;
    private const uint FolderViewFlagAutoArrange = 0x00000001;
    private const uint FolderViewFlagSnapToGrid = 0x00000002;
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    private enum Sigdn : uint
    {
        NormalDisplay = 0,
        DesktopAbsoluteParsing = 0x80028000
    }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [ComImport, Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), ClassInterface(ClassInterfaceType.None)] private class ShellWindows { }
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellWindows
    {
        [DispId(1610743808)] object Item(ref object index);
    }
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object obj);
    }
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        int GetWindow(out IntPtr hwnd); int ContextSensitiveHelp(bool enterMode); int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths); int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject); int RemoveMenusSB(IntPtr hmenuShared); int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text); int EnableModelessSB(bool enable); int TranslateAcceleratorSB(IntPtr msg, ushort id); int BrowseObject(IntPtr pidl, uint w); int GetViewStateStream(uint mode, ref Guid iid, out IntPtr stream); int GetControlWindow(uint id, out IntPtr hwnd); int SendControlMsg(uint id, uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result); int QueryActiveShellView(out IShellView view);
    }
    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        int GetWindow(out IntPtr hwnd); int ContextSensitiveHelp(bool enterMode); int TranslateAccelerator(IntPtr msg); int EnableModeless(bool enable); int UIActivate(uint state); int Refresh(); int CreateViewWindow(IntPtr previous, IntPtr settings, IntPtr browser, IntPtr rect, out IntPtr hwnd); int DestroyViewWindow(); int GetCurrentInfo(IntPtr info); int AddPropertySheetPages(uint reserved, IntPtr callback, IntPtr lParam); int SaveViewState(); int SaveViewStateStream(IntPtr stream); int GetItemObject(uint item, uint riid, out IntPtr obj); int GetItemCount(uint flags, out uint count); int GetSelectionMarkedItem(out uint index); int GetFocusedItem(out uint index); int GetItemPosition(IntPtr pidl, out POINT point); int GetSpacing(out POINT spacing); int GetDefaultSpacing(out POINT spacing); int SelectItem(uint index, uint flags); int SelectAndPositionItems(uint count, IntPtr[] pidls, POINT[] points, uint flags); int GetItem(uint index, out IntPtr pidl);
    }
    [ComImport, Guid("1AC3D9F0-175C-11D1-95BE-00609797EA4F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView2 : IShellView
    {
        new int GetWindow(out IntPtr hwnd); new int ContextSensitiveHelp(bool enterMode); new int TranslateAccelerator(IntPtr msg); new int EnableModeless(bool enable); new int UIActivate(uint state); new int Refresh(); new int CreateViewWindow(IntPtr previous, IntPtr settings, IntPtr browser, IntPtr rect, out IntPtr hwnd); new int DestroyViewWindow(); new int GetCurrentInfo(IntPtr info); new int AddPropertySheetPages(uint reserved, IntPtr callback, IntPtr lParam); new int SaveViewState(); new int SaveViewStateStream(IntPtr stream); new int GetItemObject(uint item, uint riid, out IntPtr obj); new int GetItemCount(uint flags, out uint count); new int GetSelectionMarkedItem(out uint index); new int GetFocusedItem(out uint index); new int GetItemPosition(IntPtr pidl, out POINT point); new int GetSpacing(out POINT spacing); new int GetDefaultSpacing(out POINT spacing); new int SelectItem(uint index, uint flags); new int SelectAndPositionItems(uint count, IntPtr[] pidls, POINT[] points, uint flags); new int GetItem(uint index, out IntPtr pidl);
        int SetCurrentFolderFlags(uint mask, uint flags);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHGetNameFromIDList(IntPtr pidl, Sigdn sigdnName, out IntPtr ppszName);
}
