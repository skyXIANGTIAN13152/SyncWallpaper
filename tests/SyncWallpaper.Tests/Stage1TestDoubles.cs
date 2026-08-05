using SyncWallpaper.Core;
using SyncWallpaper.DisplayEngine;
using CoreRect = SyncWallpaper.Core.Int32Rect;

namespace SyncWallpaper.Tests;

internal sealed class TestLogger : IStage1Logger
{
    public List<string> Messages { get; } = new();
    public void Info(string category, string message) => Messages.Add($"I:{category}:{message}");
    public void Warn(string category, string message) => Messages.Add($"W:{category}:{message}");
    public void Error(string category, string message) => Messages.Add($"E:{category}:{message}");
}

internal sealed class FakeDisplayAdapter : IDisplayTopologyReader, IStagedDisplayConfigurationApplier, IDisplayConfigurationVerifier, IDisplayConfigurationRollbackService, IDisplayModeCatalog
{
    public DisplayConfigurationProfile Current { get; set; } = TestData.DisplayProfile("current");
    public bool FailApply { get; set; }
    public bool FailRollback { get; set; }
    public bool VerifyMatchesTarget { get; set; } = true;
    public bool VerifyAfterRollbackMatches { get; set; } = true;
    public int ApplyCalls { get; private set; }
    public int TopologyCalls { get; private set; }
    public int FinalCalls { get; private set; }
    public int RollbackCalls { get; private set; }
    public Dictionary<string, IReadOnlyList<SyncWallpaper.DisplayEngine.DisplayModeInfo>> Modes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public DisplayTopologySnapshot Capture() => new() { Profile = TestData.Clone(Current), NativeSignature = Current.Name };
    public Task ApplyAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken)
        => ApplyFinalAsync(target, cancellationToken);

    public Task ApplyTopologyAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCalls++;
        TopologyCalls++;
        if (FailApply) throw new InvalidOperationException("fake apply failure");
        Current = TestData.Clone(target);
        return Task.CompletedTask;
    }

    public Task ApplyFinalAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyCalls++;
        FinalCalls++;
        if (FailApply) throw new InvalidOperationException("fake apply failure");
        Current = TestData.Clone(target);
        return Task.CompletedTask;
    }
    private bool _rolledBack;
    public Task<DisplayValidationResult> VerifyAsync(DisplayConfigurationProfile target, CancellationToken cancellationToken)
        => Task.FromResult((_rolledBack ? VerifyAfterRollbackMatches : VerifyMatchesTarget) ? DisplayValidationResult.Valid() : DisplayValidationResult.Invalid("fake mismatch"));
    public Task RollbackAsync(DisplayTopologySnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RollbackCalls++;
        if (FailRollback) throw new InvalidOperationException("fake rollback failure");
        Current = TestData.Clone(snapshot.Profile);
        _rolledBack = true;
        return Task.CompletedTask;
    }
    public IReadOnlyList<SyncWallpaper.DisplayEngine.DisplayModeInfo> GetModes(MonitorIdentity monitor)
        => Modes.TryGetValue(monitor.MonitorDevicePath, out var value) ? value : Array.Empty<SyncWallpaper.DisplayEngine.DisplayModeInfo>();
}

internal sealed class FakeStabilizer : IDisplayChangeStabilizer
{
    public int Calls { get; private set; }
    public int? ThrowOnCall { get; set; }
    public Task WaitForStableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls++;
        if (ThrowOnCall == Calls) throw new OperationCanceledException("fake stabilizer cancellation");
        return Task.CompletedTask;
    }
}

internal sealed class FakeConfirmation : IDisplayConfirmationService
{
    public bool Result { get; set; } = true;
    public int Calls { get; private set; }
    public Task<bool> ConfirmAsync(DisplayConfigurationProfile profile, DisplayValidationResult validation, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

internal sealed class FakeAudioProvider : IAudioEndpointProvider
{
    public List<AudioEndpointReference> Endpoints { get; } = new();
    public Dictionary<AudioEndpointRole, AudioEndpointReference?> Defaults { get; } = new();
    public HashSet<AudioEndpointRole> FailRoles { get; } = new();
    public TimeSpan SetDelay { get; set; }
    public event EventHandler? DevicesChanged;
    public event EventHandler? DefaultsChanged;
    public IReadOnlyList<AudioEndpointReference> Enumerate() => Endpoints.ToArray();
    public AudioEndpointReference? GetDefault(AudioEndpointRole role) => Defaults.TryGetValue(role, out var value) ? value : null;
    public async Task SetDefaultAsync(AudioEndpointReference endpoint, AudioEndpointRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (SetDelay > TimeSpan.Zero) await Task.Delay(SetDelay, cancellationToken);
        if (FailRoles.Contains(role)) throw new InvalidOperationException("fake audio failure");
        Defaults[role] = endpoint;
    }
    public void RaiseDevices() => DevicesChanged?.Invoke(this, EventArgs.Empty);
    public void RaiseDefaults() => DefaultsChanged?.Invoke(this, EventArgs.Empty);
}

internal sealed class FakeWindowPlatform : IWindowPlatform
{
    public List<WindowPositionSnapshot> Windows { get; } = new();
    public List<(IntPtr Handle, CoreRect Bounds, bool Maximize)> Applied { get; } = new();
    public List<string> Started { get; } = new();
    public bool SetResult { get; set; } = true;
    public IReadOnlyList<WindowPositionSnapshot> Enumerate() => Windows.ToArray();
    public bool TrySetPosition(WindowPositionSnapshot window, CoreRect physicalBounds, bool maximize)
    {
        if (SetResult) Applied.Add((window.Handle, physicalBounds, maximize));
        return SetResult;
    }
    public bool TryStartApplication(string executablePath, string arguments) { Started.Add(executablePath); return true; }
}

internal sealed class FakeClock : IAutomationClock
{
    public DateTimeOffset UtcNowValue { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UtcNow => UtcNowValue;
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) { UtcNowValue += delay; return Task.CompletedTask; }
}

internal sealed class FakeActionExecutor : IAutomationActionExecutor
{
    public List<ActionDefinition> Actions { get; } = new();
    public HashSet<string> FailedActions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Task<AutomationExecutionResult> ExecuteAsync(ActionDefinition action, AutomationExecutionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Actions.Add(action);
        return Task.FromResult(new AutomationExecutionResult { Success = !FailedActions.Contains(action.Id), Message = "fake" });
    }
}

internal sealed class FakeDesktopIconProvider : IDesktopIconProvider
{
    public List<DesktopIconPosition> Positions { get; } = new();
    public List<DesktopIconPosition> Applied { get; } = new();
    public bool SetResult { get; set; } = true;
    public bool SettingsResult { get; set; } = true;
    public IReadOnlyList<DesktopIconPosition> Capture() => Positions.ToArray();
    public bool TrySetPosition(DesktopIconPosition position) { if (SetResult) Applied.Add(position); return SetResult; }
    public bool TrySetViewSettings(int iconSize, bool autoArrange, bool alignToGrid) => SettingsResult;
}

internal static class TestData
{
    public static MonitorIdentity Monitor(string path = "PATH-A", string serial = "SERIAL-A", int x = 0, int y = 0, int width = 1920, int height = 1080) => new()
    {
        MonitorDevicePath = path, EdidManufactureId = "AOC", EdidProductCodeId = "B426", EdidSerialNumber = serial,
        ManufacturerName = "AOC", ProductCodeId = "B426", FriendlyName = path, AdapterId = "GPU",
        SourceId = (uint)Math.Max(0, x / 1920), TargetId = (uint)Math.Max(1, x / 1920 + 1), OutputTechnology = 10,
        ConnectorInstance = (uint)Math.Max(0, x / 1920), Width = width, Height = height, Rotation = 1,
        DesktopX = x, DesktopY = y, IsPrimary = x == 0
    };

    public static DisplayConfigurationProfile DisplayProfile(string name, int width = 1920, int height = 1080)
    {
        var monitor = Monitor(width: width, height: height);
        return new DisplayConfigurationProfile
        {
            Name = name,
            Displays = new List<DisplayConfigurationEntry>
            {
                new() { MonitorFingerprint = monitor, AdapterLuid = monitor.AdapterId, SourceId = monitor.SourceId, TargetId = monitor.TargetId,
                    Width = width, Height = height, DesktopX = 0, DesktopY = 0, IsPrimary = true, RefreshRateNumerator = 60, RefreshRateDenominator = 1 }
            }
        };
    }

    public static DisplayConfigurationProfile Clone(DisplayConfigurationProfile profile)
        => System.Text.Json.JsonSerializer.Deserialize<DisplayConfigurationProfile>(System.Text.Json.JsonSerializer.Serialize(profile))!;
}
