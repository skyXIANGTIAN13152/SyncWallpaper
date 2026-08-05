using SyncWallpaper.Core;

namespace SyncWallpaper.Automation;

public sealed class SystemAutomationClock : IAutomationClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}

public sealed class AutomationEngine : IAutomationEngine
{
    private readonly IAutomationActionExecutor _executor;
    private readonly IAutomationClock _clock;
    private readonly IStage1Logger _logger;
    private readonly Dictionary<string, DateTimeOffset> _lastExecution = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public AutomationEngine(IAutomationActionExecutor executor, IStage1Logger logger, IAutomationClock? clock = null)
    {
        _executor = executor; _logger = logger; _clock = clock ?? new SystemAutomationClock();
    }

    public async Task<IReadOnlyList<AutomationExecutionResult>> FireAsync(TriggerDefinition trigger, IEnumerable<AutomationRule> rules, AutomationExecutionContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<AutomationExecutionResult>();
        var candidates = rules
            .Where(x => x.Enabled && x.Trigger.Type == trigger.Type &&
                (string.IsNullOrWhiteSpace(x.Trigger.Value) || string.Equals(x.Trigger.Value, trigger.Value, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var rule in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (context.Ancestors.Contains(rule.Id))
            {
                results.Add(Skipped(rule.Id, string.Empty, "检测到递归执行，已跳过。"));
                continue;
            }
            if (!ConditionsPass(rule, context))
            {
                results.Add(Skipped(rule.Id, string.Empty, "条件不满足。"));
                continue;
            }
            if (!CanRun(rule))
            {
                results.Add(Skipped(rule.Id, string.Empty, "冷却时间或防抖时间内，已跳过。"));
                continue;
            }
            MarkRun(rule);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (rule.MaximumExecutionTime > TimeSpan.Zero) timeout.CancelAfter(rule.MaximumExecutionTime);
            var childContext = new AutomationExecutionContext
            {
                ExecutionId = context.ExecutionId,
                TriggerType = context.TriggerType,
                Value = context.Value,
                ActiveDisplayProfileId = context.ActiveDisplayProfileId,
                StartedAt = context.StartedAt,
                Ancestors = new HashSet<string>(context.Ancestors, StringComparer.OrdinalIgnoreCase) { rule.Id },
                Data = context.Data
            };

            var stopRule = false;
            foreach (var action in rule.Actions)
            {
                var started = _clock.UtcNow;
                try
                {
                    var result = await _executor.ExecuteAsync(action, childContext, timeout.Token);
                    results.Add(result with { RuleId = rule.Id, ActionId = action.Id, Duration = _clock.UtcNow - started });
                    if (!result.Success && action.Required && !rule.ContinueOnError)
                    {
                        stopRule = true;
                        break;
                    }
                }
                catch (OperationCanceledException) when (timeout.IsCancellationRequested)
                {
                    results.Add(new AutomationExecutionResult { RuleId = rule.Id, ActionId = action.Id, Success = false, Message = "动作超时或已取消。", Duration = _clock.UtcNow - started });
                    stopRule = true;
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error("Automation", $"规则 {rule.Name} 动作失败：{ex.Message}");
                    results.Add(new AutomationExecutionResult { RuleId = rule.Id, ActionId = action.Id, Success = false, Message = ex.Message, Duration = _clock.UtcNow - started });
                    if (action.Required && !rule.ContinueOnError)
                    {
                        stopRule = true;
                        break;
                    }
                }
            }
            if (stopRule && rule.StopProcessing) break;
            if (rule.StopProcessing) break;
        }
        return results;
    }

    private bool ConditionsPass(AutomationRule rule, AutomationExecutionContext context)
    {
        foreach (var condition in rule.Conditions)
        {
            var actual = context.Data.TryGetValue(condition.Key, out var value) ? value : string.Empty;
            var pass = string.Equals(actual, condition.ExpectedValue, StringComparison.OrdinalIgnoreCase);
            if (!pass && condition.Required) return false;
        }
        return true;
    }

    private bool CanRun(AutomationRule rule)
    {
        lock (_gate)
        {
            if (!_lastExecution.TryGetValue(rule.Id, out var last)) return true;
            var elapsed = _clock.UtcNow - last;
            var cooldown = rule.Cooldown > rule.Debounce ? rule.Cooldown : rule.Debounce;
            return elapsed >= cooldown;
        }
    }

    private void MarkRun(AutomationRule rule)
    {
        lock (_gate) _lastExecution[rule.Id] = _clock.UtcNow;
    }

    private static AutomationExecutionResult Skipped(string ruleId, string actionId, string message)
        => new() { RuleId = ruleId, ActionId = actionId, Skipped = true, Success = true, Message = message };
}

public sealed class DelegateAutomationActionExecutor : IAutomationActionExecutor
{
    private readonly Func<ActionDefinition, AutomationExecutionContext, CancellationToken, Task<bool>> _handler;
    public DelegateAutomationActionExecutor(Func<ActionDefinition, AutomationExecutionContext, CancellationToken, Task<bool>> handler) => _handler = handler;
    public async Task<AutomationExecutionResult> ExecuteAsync(ActionDefinition action, AutomationExecutionContext context, CancellationToken cancellationToken)
        => new() { Success = await _handler(action, context, cancellationToken), Message = action.Type.ToString() };
}
