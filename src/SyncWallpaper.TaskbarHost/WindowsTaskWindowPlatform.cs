using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SyncWallpaper.Core;

namespace SyncWallpaper.TaskbarHost;

public sealed class WindowsTaskWindowPlatform : ITaskWindowPlatform
{
    private readonly int _ownProcessId = Environment.ProcessId;

    public int ExplorerProcessId
    {
        get
        {
            var shell = GetShellWindow();
            if (shell != 0)
            {
                GetWindowThreadProcessId(shell, out var pid);
                return unchecked((int)pid);
            }
            try { return Process.GetProcessesByName("explorer").FirstOrDefault()?.Id ?? 0; }
            catch { return 0; }
        }
    }

    public IReadOnlyList<TaskWindowCandidate> Enumerate()
    {
        var result = new List<TaskWindowCandidate>();
        EnumWindows((window, _) =>
        {
            try
            {
                var candidate = CreateCandidate(window);
                if (candidate is not null) result.Add(candidate);
            }
            catch
            {
                // One protected or disappearing window must not invalidate the
                // entire task list. It will be considered again on the next event.
            }
            return true;
        }, 0);
        return result;
    }

    public TaskWindowActionResult ActivateOrMinimize(nint handle)
    {
        if (handle == 0 || !IsWindow(handle)) return TaskWindowActionResult.Missing;
        try
        {
            if (GetForegroundWindow() == handle && !IsIconic(handle))
                return ShowWindow(handle, SwMinimize) ? TaskWindowActionResult.Minimized : TaskWindowActionResult.Failed;

            var restored = false;
            if (IsIconic(handle)) restored = ShowWindow(handle, SwRestore);
            if (SetForegroundWindow(handle)) return restored ? TaskWindowActionResult.Restored : TaskWindowActionResult.Activated;
            return IsElevated(handle) ? TaskWindowActionResult.AccessDenied : TaskWindowActionResult.Failed;
        }
        catch
        {
            return TaskWindowActionResult.Failed;
        }
    }

    public TaskWindowCloseResult Close(nint handle)
    {
        if (handle == 0 || !IsWindow(handle)) return TaskWindowCloseResult.Missing;
        try
        {
            if (PostMessage(handle, WmClose, 0, 0)) return TaskWindowCloseResult.Requested;
            return IsElevated(handle) ? TaskWindowCloseResult.AccessDenied : TaskWindowCloseResult.Failed;
        }
        catch
        {
            return TaskWindowCloseResult.Failed;
        }
    }

    private TaskWindowCandidate? CreateCandidate(nint window)
    {
        if (window == 0 || !IsWindow(window)) return null;
        GetWindowThreadProcessId(window, out var rawPid);
        var processId = unchecked((int)rawPid);
        var className = ReadClass(window);
        var title = ReadTitle(window);
        var processPath = ReadProcessPath(rawPid);
        var processName = Path.GetFileNameWithoutExtension(processPath);
        var appUserModelId = ReadAppUserModelId(rawPid);

        if (className.Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase))
        {
            var child = FindUwpContentWindow(window);
            if (child != 0)
            {
                GetWindowThreadProcessId(child, out var childPid);
                var childPath = ReadProcessPath(childPid);
                var childAppId = ReadAppUserModelId(childPid);
                if (!string.IsNullOrWhiteSpace(childPath))
                {
                    processPath = childPath;
                    processName = Path.GetFileNameWithoutExtension(childPath);
                }
                if (!string.IsNullOrWhiteSpace(childAppId)) appUserModelId = childAppId;
            }
        }

        var exStyle = unchecked((ulong)GetWindowLongPtr(window, GwlExStyle).ToInt64());
        var bounds = ReadBounds(window);
        var rootOwner = GetAncestor(window, GaRootOwner);
        return new TaskWindowCandidate
        {
            Handle = window,
            ProcessId = processId,
            Title = title,
            ProcessName = processName,
            ProcessPath = processPath,
            WindowClass = className,
            AppUserModelId = appUserModelId,
            Bounds = bounds,
            IsVisible = IsWindowVisible(window),
            IsCloaked = IsCloaked(window),
            IsToolWindow = (exStyle & WsExToolWindow) != 0,
            IsAppWindow = (exStyle & WsExAppWindow) != 0,
            IsNoActivate = (exStyle & WsExNoActivate) != 0,
            HasOwner = GetWindow(window, GwOwner) != 0,
            IsRootOwner = rootOwner == 0 || rootOwner == window,
            IsOwnProcess = processId == _ownProcessId,
            IsMinimized = IsIconic(window),
            IsForeground = GetForegroundWindow() == window,
            IsUwp = className.Equals("ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(appUserModelId),
            IsElevated = IsElevated(window)
        };
    }

    private static Int32Rect ReadBounds(nint window)
    {
        var placement = new WindowPlacement { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
        if (IsIconic(window) && GetWindowPlacement(window, ref placement))
        {
            var normal = placement.NormalPosition;
            if (normal.Right > normal.Left && normal.Bottom > normal.Top)
                return new Int32Rect(normal.Left, normal.Top, normal.Right - normal.Left, normal.Bottom - normal.Top);
        }
        if (!GetWindowRect(window, out var fallback)) return default;
        var rect = DwmGetWindowAttributeRect(window, DwmwaExtendedFrameBounds, out var frame, Marshal.SizeOf<Rect>()) == 0 ? frame : fallback;
        return new Int32Rect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
    }

    private static nint FindUwpContentWindow(nint parent)
    {
        nint found = 0;
        EnumChildWindows(parent, (child, _) =>
        {
            if (ReadClass(child).Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase))
            {
                found = child;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private static string ReadTitle(nint window)
    {
        var length = Math.Clamp(GetWindowTextLength(window) + 1, 2, 4096);
        var buffer = new StringBuilder(length);
        GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string ReadClass(nint window)
    {
        var buffer = new StringBuilder(256);
        GetClassName(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string ReadProcessPath(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0) return string.Empty;
        try
        {
            var buffer = new StringBuilder(4096);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : string.Empty;
        }
        finally { CloseHandle(process); }
    }

    private static string ReadAppUserModelId(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == 0) return string.Empty;
        try
        {
            uint length = 0;
            var first = GetApplicationUserModelId(process, ref length, null);
            if (first != ErrorInsufficientBuffer || length == 0) return string.Empty;
            var buffer = new StringBuilder(checked((int)length));
            return GetApplicationUserModelId(process, ref length, buffer) == 0 ? buffer.ToString() : string.Empty;
        }
        finally { CloseHandle(process); }
    }

    private static bool IsElevated(nint window)
    {
        GetWindowThreadProcessId(window, out var pid);
        var process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == 0) return false;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token)) return false;
            try
            {
                var elevation = new TokenElevation();
                return GetTokenInformation(token, TokenElevationClass, ref elevation, Marshal.SizeOf<TokenElevation>(), out _)
                    && elevation.TokenIsElevated != 0;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    private static bool IsCloaked(nint window)
        => DwmGetWindowAttributeInt(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    public void Dispose() { }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private const int ErrorInsufficientBuffer = 122;
    private const int GwlExStyle = -20;
    private const ulong WsExToolWindow = 0x00000080;
    private const ulong WsExAppWindow = 0x00040000;
    private const ulong WsExNoActivate = 0x08000000;
    private const uint GwOwner = 4;
    private const uint GaRootOwner = 3;
    private const int SwMinimize = 6;
    private const int SwRestore = 9;
    private const int DwmwaExtendedFrameBounds = 9;
    private const int DwmwaCloaked = 14;
    private const uint WmClose = 0x0010;

    private delegate bool EnumWindowsProc(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint parent, EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder className, int maximum);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowAttributeRect(nint window, int attribute, out Rect value, int size);
    [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowAttributeInt(nint window, int attribute, out int value, int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder? appUserModelId);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(nint token, int classId, ref TokenElevation elevation, int size, out int returnLength);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPlacement
    {
        public uint Length;
        public uint Flags;
        public uint ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect NormalPosition;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TokenElevation { public int TokenIsElevated; }
}
