using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class WindowsDisplayConfirmationService : IDisplayConfirmationService
{
    public Task<bool> ConfirmAsync(DisplayConfigurationProfile profile, DisplayValidationResult validation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<bool>(cancellationToken);
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
            return ShowAsync(profile, validation, timeout, cancellationToken);
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) return Task.FromResult(false);
        return dispatcher.InvokeAsync(() => ShowAsync(profile, validation, timeout, cancellationToken)).Task.Unwrap();
    }

    private static Task<bool> ShowAsync(DisplayConfigurationProfile profile, DisplayValidationResult validation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = new Window
        {
            Title = "屏序 SyncWallpaper",
            Width = 520,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Topmost = true,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(9, 19, 33))
        };
        var stack = new StackPanel { Margin = new Thickness(24) };
        var remaining = Math.Max(1, (int)Math.Ceiling(timeout.TotalSeconds));
        var text = new TextBlock { Foreground = System.Windows.Media.Brushes.White, FontSize = 16, TextWrapping = TextWrapping.Wrap };
        var details = new TextBlock { Foreground = System.Windows.Media.Brushes.LightGray, Margin = new Thickness(0, 16, 0, 16), TextWrapping = TextWrapping.Wrap };
        details.Text = validation.Differences.Count == 0 ? "已应用目标显示配置，请确认显示正常。" :
            string.Join(Environment.NewLine, validation.Differences.Select(x => $"{x.Subject}：{x.CurrentValue} → {x.TargetValue}"));
        var keep = new System.Windows.Controls.Button { Content = $"保留此显示配置（{remaining} 秒）", Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        stack.Children.Add(text); stack.Children.Add(details); stack.Children.Add(keep); window.Content = stack;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Finish(bool result)
        {
            timer.Stop();
            if (window.IsVisible) window.Close();
            tcs.TrySetResult(result);
        }
        keep.Click += (_, _) => Finish(true);
        window.Closed += (_, _) => tcs.TrySetResult(false);
        var cancellation = cancellationToken.Register(() => window.Dispatcher.BeginInvoke(() => Finish(false)));
        timer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining <= 0) Finish(false);
            else keep.Content = $"保留此显示配置（{remaining} 秒）";
        };
        text.Text = $"是否保留“{profile.Name}”显示配置？{Environment.NewLine}未确认将自动恢复旧配置。";
        window.Show();
        timer.Start();
        _ = tcs.Task.ContinueWith(_ => cancellation.Dispose(), TaskScheduler.Default);
        return tcs.Task;
    }
}
