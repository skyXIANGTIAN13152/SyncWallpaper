using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using SyncWallpaper.Update.Core;

namespace SyncWallpaper.App;

public partial class UpdateWindow : Window
{
    private readonly AppRuntime _runtime;
    private readonly UpdateCheckResult _release;
    private CancellationTokenSource? _operation;
    public bool IsBusy { get; private set; }
    public bool HandoffReady { get; private set; }

    public UpdateWindow(AppRuntime runtime, UpdateCheckResult release)
    {
        InitializeComponent();
        _runtime = runtime;
        _release = release;
        VersionText.Text = $"{release.CurrentSemanticVersion}  →  {release.LatestSemanticVersion}";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(release.ReleaseNotes) ? "此版本未提供更新说明，可打开发布说明查看详情。" : release.ReleaseNotes;
        if (release.Package is null || !AppUpdateService.IsInstalledLayout)
        {
            InstallButton.IsEnabled = false;
            ProgressText.Text = release.Package is null ? "此版本暂未提供可自动安装的完整更新包，请打开发布说明下载。" : "当前程序未包含更新助手，请先安装完整发布版。";
        }
        Loaded += (_, _) =>
        {
            // This fixed-size prompt must fit on the same high-DPI screen as its owner.
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
            MinHeight = Math.Min(MinHeight, work.Height / dpi.DpiScaleY - 24);
            Height = Math.Min(Height, Math.Max(MinHeight, work.Height / dpi.DpiScaleY - 48));
        };
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy || _release.Package is null) return;
        IsBusy = true;
        InstallButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        LaterButton.Content = "取消下载";
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.IsIndeterminate = true;
        HeadingText.Text = "正在准备更新";
        _operation = new CancellationTokenSource();
        PreparedUpdate? prepared = null;
        System.Diagnostics.Process? helper = null;
        try
        {
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                if (!IsBusy || HandoffReady) return;
                DownloadProgress.IsIndeterminate = value.TotalBytes is null or <= 0;
                if (value.TotalBytes is > 0) DownloadProgress.Value = Math.Clamp(value.BytesReceived * 100d / value.TotalBytes.Value, 0, 100);
                ProgressText.Text = value.Stage + (value.TotalBytes is > 0
                    ? $" · {value.BytesReceived / 1048576d:0.0} / {value.TotalBytes.Value / 1048576d:0.0} MB" : string.Empty);
            });
            prepared = await AppUpdateService.DownloadAsync(_release.Package, progress, _operation.Token);
            _operation.Token.ThrowIfCancellationRequested();
            LaterButton.Content = "取消更新";
            ProgressText.Text = "下载已校验，正在准备重启。";
            DownloadProgress.IsIndeterminate = true;
            helper = await AppUpdateService.StartInstallerAsync(prepared, _runtime.Paths.Root, _operation.Token);
            _operation.Token.ThrowIfCancellationRequested();
            HandoffReady = true;
            ProgressText.Text = "正在重启屏序以完成更新…";
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            HeadingText.Text = "已取消更新";
            ProgressText.Text = "旧版本继续运行。你可以稍后重新尝试。";
        }
        catch (Exception ex)
        {
            _runtime.LogUpdateFailure(ex.ToString());
            HeadingText.Text = "更新未能完成";
            ProgressText.Text = "旧版本继续运行。" + ex.Message;
        }
        finally
        {
            if (!HandoffReady && helper is not null)
            {
                try { if (!helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync(); } }
                catch (Exception ex) { _runtime.LogUpdateFailure("取消更新助手失败：" + ex.Message); }
            }
            helper?.Dispose();
            if (!HandoffReady && prepared is not null)
            {
                try { await Task.Run(() => AppUpdateService.DeleteOwnedWorkspace(prepared.WorkDirectory)); }
                catch (Exception ex) { _runtime.LogUpdateFailure("更新临时文件保留：" + ex.Message); }
            }
            _operation.Dispose();
            _operation = null;
            IsBusy = false;
            if (!HandoffReady)
            {
                DownloadProgress.Visibility = Visibility.Collapsed;
                InstallButton.Content = "重试更新";
                InstallButton.IsEnabled = true;
                LaterButton.Content = "关闭";
                LaterButton.IsEnabled = true;
                CloseButton.IsEnabled = true;
            }
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            _operation?.Cancel();
            LaterButton.IsEnabled = false;
            ProgressText.Text = "正在取消并清理下载文件…";
        }
        else Close();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (IsBusy && !HandoffReady) { e.Cancel = true; Later_Click(this, new RoutedEventArgs()); }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        Later_Click(sender, e);
        e.Handled = true;
    }

    private void OpenRelease_Click(object sender, RoutedEventArgs e) => _runtime.OpenReleasePage(_release.ReleasePageUrl);
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) try { DragMove(); } catch (InvalidOperationException) { }
    }
}
