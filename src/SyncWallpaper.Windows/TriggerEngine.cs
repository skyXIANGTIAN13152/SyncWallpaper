using SyncWallpaper.Core;

namespace SyncWallpaper.Windows;

public sealed class TriggerEngine
{
    private readonly Func<FunctionAction, CancellationToken, Task> _runAction;
    public TriggerEngine(Func<FunctionAction, CancellationToken, Task> runAction) => _runAction = runAction;
    public async Task FireAsync(TriggerEvent eventType, TriggerDocument document, string? process = null, string? title = null, string? activeProfile = null, CancellationToken token = default)
    {
        var rules = document.Rules.Where(r => r.Enabled && r.Event == eventType &&
            (string.IsNullOrWhiteSpace(r.MonitorProfileId) || r.MonitorProfileId.Equals(activeProfile, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrWhiteSpace(r.ProcessNamePattern) || (process?.Contains(r.ProcessNamePattern, StringComparison.OrdinalIgnoreCase) ?? false)) &&
            (string.IsNullOrWhiteSpace(r.WindowTitlePattern) || (title?.Contains(r.WindowTitlePattern, StringComparison.OrdinalIgnoreCase) ?? false)));
        foreach (var rule in rules)
        {
            if (rule.DelayMilliseconds > 0) await Task.Delay(rule.DelayMilliseconds, token);
            foreach (var action in rule.Actions) await _runAction(action, token);
        }
    }
}
