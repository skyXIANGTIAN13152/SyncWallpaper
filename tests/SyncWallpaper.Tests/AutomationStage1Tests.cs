using SyncWallpaper.Automation;
using SyncWallpaper.Core;

namespace SyncWallpaper.Tests;

[TestClass]
public class AutomationStage1Tests
{
    [TestMethod]
    public async Task HigherPriorityRuleRunsFirst()
    {
        var executor = new FakeActionExecutor(); var engine = new AutomationEngine(executor, new TestLogger());
        var low = Rule("low", 1, AutomationActionType.Notify); var high = Rule("high", 10, AutomationActionType.WriteLog);
        await engine.FireAsync(new TriggerDefinition { Type = AutomationTriggerType.Manual }, new[] { low, high }, Context());
        Assert.AreEqual("WriteLog", executor.Actions[0].Type.ToString()); Assert.AreEqual(2, executor.Actions.Count);
    }

    [TestMethod]
    public async Task CooldownSkipsRepeatedExecution()
    {
        var executor = new FakeActionExecutor(); var clock = new FakeClock(); var engine = new AutomationEngine(executor, new TestLogger(), clock);
        var rule = Rule("cooldown", 1, AutomationActionType.Notify); rule.Cooldown = TimeSpan.FromSeconds(10);
        var trigger = new TriggerDefinition { Type = AutomationTriggerType.Manual };
        var first = await engine.FireAsync(trigger, new[] { rule }, Context()); var second = await engine.FireAsync(trigger, new[] { rule }, Context());
        Assert.AreEqual(1, first.Count(x => !x.Skipped)); Assert.IsTrue(second.Any(x => x.Skipped));
    }

    [TestMethod]
    public async Task DebounceUsesSameExecutionGate()
    {
        var executor = new FakeActionExecutor(); var clock = new FakeClock(); var engine = new AutomationEngine(executor, new TestLogger(), clock);
        var rule = Rule("debounce", 1, AutomationActionType.Notify); rule.Debounce = TimeSpan.FromSeconds(5);
        var trigger = new TriggerDefinition { Type = AutomationTriggerType.Manual };
        await engine.FireAsync(trigger, new[] { rule }, Context());
        var skipped = await engine.FireAsync(trigger, new[] { rule }, Context());
        Assert.IsTrue(skipped.Single().Skipped);
        clock.UtcNowValue += TimeSpan.FromSeconds(6);
        var run = await engine.FireAsync(trigger, new[] { rule }, Context());
        Assert.IsFalse(run.Single().Skipped);
    }

    [TestMethod]
    public async Task AncestorRulePreventsRecursion()
    {
        var executor = new FakeActionExecutor(); var engine = new AutomationEngine(executor, new TestLogger());
        var rule = Rule("loop", 1, AutomationActionType.Notify);
        var context = Context(); context.Ancestors.Add(rule.Id);
        var result = await engine.FireAsync(new TriggerDefinition { Type = AutomationTriggerType.Manual }, new[] { rule }, context);
        Assert.IsTrue(result.Single().Skipped); Assert.AreEqual(0, executor.Actions.Count);
    }

    [TestMethod]
    public async Task RequiredConditionStopsRule()
    {
        var executor = new FakeActionExecutor(); var engine = new AutomationEngine(executor, new TestLogger());
        var rule = Rule("condition", 1, AutomationActionType.Notify);
        rule.Conditions.Add(new ConditionDefinition { Key = "mode", ExpectedValue = "three", Required = true });
        var result = await engine.FireAsync(new TriggerDefinition { Type = AutomationTriggerType.Manual }, new[] { rule }, Context(("mode", "laptop")));
        Assert.IsTrue(result.Single().Skipped); Assert.AreEqual(0, executor.Actions.Count);
    }

    [TestMethod]
    public async Task OptionalConditionDoesNotStopRule()
    {
        var executor = new FakeActionExecutor(); var engine = new AutomationEngine(executor, new TestLogger());
        var rule = Rule("optional", 1, AutomationActionType.Notify);
        rule.Conditions.Add(new ConditionDefinition { Key = "mode", ExpectedValue = "three", Required = false });
        var result = await engine.FireAsync(new TriggerDefinition { Type = AutomationTriggerType.Manual }, new[] { rule }, Context(("mode", "laptop")));
        Assert.IsFalse(result.Single().Skipped); Assert.AreEqual(1, executor.Actions.Count);
    }

    [TestMethod]
    public async Task OptionalActionFailureContinues()
    {
        var executor = new FakeActionExecutor(); var engine = new AutomationEngine(executor, new TestLogger());
        var rule = Rule("optional-action", 1, AutomationActionType.Notify);
        var first = rule.Actions[0]; executor.FailedActions.Add(first.Id);
        rule.Actions.Add(new ActionDefinition { Type = AutomationActionType.WriteLog, Required = false });
        var result = await engine.FireAsync(new TriggerDefinition { Type = AutomationTriggerType.Manual }, new[] { rule }, Context());
        Assert.AreEqual(2, executor.Actions.Count); Assert.IsFalse(result[1].Skipped);
    }

    [TestMethod]
    public async Task RequiredActionFailureStopsFollowingActions()
    {
        var executor = new FakeActionExecutor(); var engine = new AutomationEngine(executor, new TestLogger());
        var rule = Rule("required-action", 1, AutomationActionType.Notify);
        rule.Actions[0].Required = true; executor.FailedActions.Add(rule.Actions[0].Id);
        rule.Actions.Add(new ActionDefinition { Type = AutomationActionType.WriteLog, Required = true });
        await engine.FireAsync(new TriggerDefinition { Type = AutomationTriggerType.Manual }, new[] { rule }, Context());
        Assert.AreEqual(1, executor.Actions.Count);
    }

    [TestMethod]
    public async Task StopProcessingPreventsLowerPriorityRules()
    {
        var executor = new FakeActionExecutor(); var engine = new AutomationEngine(executor, new TestLogger());
        var first = Rule("stop", 10, AutomationActionType.Notify); first.StopProcessing = true;
        var second = Rule("later", 1, AutomationActionType.WriteLog);
        await engine.FireAsync(new TriggerDefinition { Type = AutomationTriggerType.Manual }, new[] { first, second }, Context());
        Assert.AreEqual(1, executor.Actions.Count);
    }

    [TestMethod]
    public async Task CancellationIsHonored()
    {
        var executor = new FakeActionExecutor(); var engine = new AutomationEngine(executor, new TestLogger());
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => engine.FireAsync(new TriggerDefinition { Type = AutomationTriggerType.Manual }, new[] { Rule("cancel", 1, AutomationActionType.Notify) }, Context(), source.Token));
    }

    [TestMethod]
    public async Task TriggerValueFiltersRules()
    {
        var executor = new FakeActionExecutor(); var engine = new AutomationEngine(executor, new TestLogger());
        var rule = Rule("value", 1, AutomationActionType.Notify); rule.Trigger.Value = "three";
        var result = await engine.FireAsync(new TriggerDefinition { Type = AutomationTriggerType.Manual, Value = "laptop" }, new[] { rule }, Context());
        Assert.AreEqual(0, result.Count); Assert.AreEqual(0, executor.Actions.Count);
    }

    private static AutomationRule Rule(string name, int priority, AutomationActionType action)
        => new() { Name = name, Priority = priority, Trigger = new TriggerDefinition { Type = AutomationTriggerType.Manual }, Actions = new List<ActionDefinition> { new() { Type = action } } };
    private static AutomationExecutionContext Context(params (string Key, string Value)[] values)
        => new() { TriggerType = AutomationTriggerType.Manual, Data = values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase) };
}
