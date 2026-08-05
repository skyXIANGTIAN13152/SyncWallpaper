using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SyncWallpaper.AudioEngine;
using SyncWallpaper.Automation;
using SyncWallpaper.Core;
using SyncWallpaper.DesktopEngine;
using SyncWallpaper.DisplayEngine;
using SyncWallpaper.WindowEngine;
using SyncWallpaper.Windows;
using CoreRect = SyncWallpaper.Core.Int32Rect;

namespace SyncWallpaper.App;

public sealed class AppRuntime : IDisposable
{
    public readonly DataPaths Paths;
    public readonly ConfigurationStore Store;
    public readonly AppSettings Settings;
    public ProfilesDocument Profiles { get; private set; }
    public LibraryDocument Library { get; private set; }
    public MonitorProfilesDocument MonitorProfiles { get; private set; }
    public WindowPositionProfilesDocument WindowProfiles { get; private set; }
    public TriggerDocument Triggers { get; private set; }
    public DisplayConfigurationDocument DisplayConfigurations { get; }
    public AudioProfilesDocument AudioProfiles { get; private set; }
    public DesktopIconProfilesDocument DesktopIconProfiles { get; private set; }
    public ModulePerformanceDocument ModulePerformance { get; private set; }
    public ModuleRuntimeDocument ModuleRuntime { get; private set; }
    public IReadOnlyList<MonitorIdentity> Monitors { get; private set; } = Array.Empty<MonitorIdentity>();
    public MatchResult? LastMatch { get; private set; }
    public DisplayConfigurationApplyResult? LastDisplayTransaction { get; private set; }
    public WallpaperTransactionStatus LastWallpaperTransaction => _apply.LastTransaction;
    public AudioConfigurationResult? LastAudioResult { get; private set; }
    public WindowRestoreResult? LastWindowRestore { get; private set; }
    public DesktopIconRestoreResult? LastDesktopRestore { get; private set; }
    public IReadOnlyList<AutomationExecutionResult> LastAutomationResults { get; private set; } = Array.Empty<AutomationExecutionResult>();
    public string StatusText { get; private set; } = "正在启动";
    public string LastMessage { get; private set; } = "等待显示器检测";
    public DateTime? LastAppliedAt { get; private set; }
    public ModuleManager Modules { get; }
    public event EventHandler? StateChanged;

    private readonly MonitorDiscoveryService _discovery = new();
    private readonly ProfileMatcher _matcher = new();
    private readonly LogService _log;
    private readonly WallpaperLibraryService _library;
    private readonly WallpaperApplyService _apply;
    private readonly DisplayChangeCoordinator _coordinator;
    private readonly ExplorerRecoveryCoordinator _explorerRecovery;
    private readonly SafeModePolicy _safeMode = new();
    private readonly StartupService _startup = new();
    private readonly MonitorConfigurationService _monitorConfiguration = new();
    private readonly DisplayProfileRepository _displayRepository;
    private WindowsDisplayConfigurationAdapter? _displayAdapter;
    private DisplayConfigurationTransactionService? _displayTransaction;
    private IAudioEndpointProvider? _audioProvider;
    private IDisposable? _audioProviderDisposable;
    private IAudioConfigurationEngine? _audioEngine;
    private WindowsWindowPlatform? _windowPlatform;
    private WindowsWindowEventSource? _windowEvents;
    private readonly WindowsResourceDiagnosticsProvider _resourceDiagnostics = new();
    private WindowLayoutEngine? _windowEngine;
    private WindowsShellDesktopIconProvider? _desktopIconProvider;
    private DesktopIconLayoutEngine? _desktopIconEngine;
    private AutomationEngine? _automationEngine;
    private TriggerEngine? _legacyTriggerEngine;
    private EventHandler<WindowEvent>? _windowEventHandler;
    private bool _displayModuleRunning;
    private bool _audioModuleRunning;
    private bool _windowModuleRunning;
    private bool _desktopModuleRunning;
    private bool _automationModuleRunning;
    private bool _wallpaperModuleRunning;
    private readonly DateTime _createdAt = DateTime.UtcNow;
    private string _lastWindowEvent = "尚未收到窗口事件";

    public AppRuntime()
    {
        Paths = new DataPaths();
        Store = new ConfigurationStore(Paths);
        Settings = Store.Load("settings.json", new AppSettings());
        var loadedProfiles = Store.Load("profiles.json", new ProfilesDocument());
        var loadedProfilesSchema = loadedProfiles.SchemaVersion;
        Profiles = ProfileSchemaMigrator.Migrate(loadedProfiles);
        Library = Store.Load("library.json", new LibraryDocument());
        MonitorProfiles = Store.Load("monitor-profiles.json", new MonitorProfilesDocument());
        WindowProfiles = Store.Load("window-profiles.json", new WindowPositionProfilesDocument());
        Triggers = Store.Load("triggers.json", new TriggerDocument());
        AudioProfiles = Store.Load("audio-profiles.json", new AudioProfilesDocument());
        DesktopIconProfiles = Store.Load("desktop-icons.json", new DesktopIconProfilesDocument());
        ModulePerformance = Store.Load("module-performance.json", new ModulePerformanceDocument());
        ModuleRuntime = Store.Load("module-runtime.json", new ModuleRuntimeDocument());
        Settings.Modules ??= new ModuleConfiguration();
        if (Settings.SafeMode) { Settings.AutoMatchEnabled = false; StatusText = "安全模式"; LastMessage = Settings.SafeModeReason; }

        _log = new LogService(Paths);
        if (loadedProfilesSchema < ProfileSchemaMigrator.CurrentSchemaVersion)
        {
            try { Store.Save("profiles.json", Profiles); _log.Info("Config", $"profiles.json 已迁移到 SchemaVersion={Profiles.SchemaVersion}"); }
            catch (Exception ex) { _log.Warn("Config", "保存迁移后的 profiles.json 失败：" + ex.Message); }
        }
        _library = new WallpaperLibraryService(Store);
        _apply = new WallpaperApplyService(new WallpaperRenderService(Paths), message => _log.Write("Wallpaper", message));
        _explorerRecovery = new ExplorerRecoveryCoordinator(
            async token => { if (token.IsCancellationRequested) return; await MatchAndApplyAsync(); },
            message => _log.Warn("ExplorerRecovery", message));
        _apply.ExplorerUnavailable += message => _explorerRecovery.NotifyUnavailable(message);
        _apply.TransactionChanged += (_, status) =>
        {
            LastMessage = status.Message;
            if (status.State is WallpaperTransactionState.Failed or WallpaperTransactionState.RollbackFailed)
                _log.Warn("Wallpaper", status.Message);
            if (status.State == WallpaperTransactionState.RollbackFailed)
            {
                _safeMode.Record(SafeModeTrigger.WallpaperRollbackFailure, status.Message);
                Settings.SafeMode = true;
                Settings.SafeModeReason = status.Message;
                Settings.AutoMatchEnabled = false;
                try { Store.Save("settings.json", Settings); } catch { }
            }
            RaiseChanged();
        };

        _displayRepository = new DisplayProfileRepository(Store);
        DisplayConfigurations = new DisplayConfigurationDocument { Profiles = _displayRepository.List().ToList() };
        _coordinator = new DisplayChangeCoordinator(_discovery, OnStableDisplaysAsync, value =>
        {
            _log.Info("SystemEvent", value);
            _explorerRecovery.NotifyShellEvent(value);
        });
        Modules = new ModuleManager(message => _log.Info("Module", message), runtime: ModuleRuntime,
            persist: document => Store.Save("module-runtime.json", document));
        RegisterModules();
        Modules.StateChanged += Modules_StateChanged;
        foreach (var faulted in Modules.Snapshot(Settings.Modules).Where(x => x.FaultDisabled))
            Settings.Modules.SetEnabled(faulted.Id, false);
    }

    private void Modules_StateChanged(object? sender, ModuleStatusSnapshot status)
    {
        if (status.State != ModuleLifecycleState.Faulted || status.Id == SyncWallpaperModule.Wallpaper) return;
        if (status.FaultDisabled)
        {
            Settings.Modules.SetEnabled(status.Id, false);
            _log.Warn("Module", $"{status.DisplayName} 已自动禁用：{status.LastError ?? "未知故障"}");
        }
        else
            _log.Warn("Module", $"{status.DisplayName} 故障，模块管理器将按退避策略尝试恢复：{status.LastError ?? "未知故障"}");
        try { Store.Save("settings.json", Settings); } catch { }
        RaiseChanged();
    }

    private void RegisterModules()
    {
        Modules.Register(new ModuleDefinition(SyncWallpaperModule.Wallpaper, "壁纸自动匹配（核心）", false, Array.Empty<SyncWallpaperModule>()),
            new DelegateModuleController(
                _ => { _wallpaperModuleRunning = true; return Task.CompletedTask; },
                _ => { _wallpaperModuleRunning = false; return Task.CompletedTask; },
                () => _wallpaperModuleRunning, () => _wallpaperModuleRunning ? Environment.ProcessId : null,
                () => "核心事件协调器；无独立 Hook", () => null));

        Modules.Register(new ModuleDefinition(SyncWallpaperModule.DisplayEngine, "Display Engine", false, Array.Empty<SyncWallpaperModule>()),
            new DelegateModuleController(
                _ => { EnsureDisplayEngine(); _displayModuleRunning = true; return Task.CompletedTask; },
                _ => { DisposeDisplayEngine(); _displayModuleRunning = false; return Task.CompletedTask; },
                () => _displayModuleRunning, () => _displayModuleRunning ? Environment.ProcessId : null,
                () => _displayModuleRunning ? "CCD 事务服务已注册" : "未注册 CCD Hook", () => null));

        Modules.Register(new ModuleDefinition(SyncWallpaperModule.AudioEngine, "Audio Engine", false, Array.Empty<SyncWallpaperModule>()),
            new DelegateModuleController(
                _ => { EnsureAudioEngine(); _audioModuleRunning = true; return Task.CompletedTask; },
                _ => { DisposeAudioEngine(); _audioModuleRunning = false; return Task.CompletedTask; },
                () => _audioModuleRunning, () => _audioModuleRunning ? Environment.ProcessId : null,
                () => _audioModuleRunning ? "Core Audio COM 已建立" : "COM 已释放", () => null));

        Modules.Register(new ModuleDefinition(SyncWallpaperModule.WindowEngine, "Window Engine", false, Array.Empty<SyncWallpaperModule>()),
            new DelegateModuleController(
                _ => { EnsureWindowEngine(); _windowModuleRunning = true; return Task.CompletedTask; },
                _ => { DisposeWindowEngine(); _windowModuleRunning = false; return Task.CompletedTask; },
                () => _windowModuleRunning, () => _windowModuleRunning ? Environment.ProcessId : null,
                () => _windowEvents?.IsActive == true ? "WinEventHook 已注册" : "WinEventHook 已注销", () => null));

        Modules.Register(new ModuleDefinition(SyncWallpaperModule.Automation, "Automation", false, Array.Empty<SyncWallpaperModule>()),
            new DelegateModuleController(
                _ => { EnsureAutomationEngine(); _automationModuleRunning = true; return Task.CompletedTask; },
                _ => { DisposeAutomationEngine(); _automationModuleRunning = false; return Task.CompletedTask; },
                () => _automationModuleRunning, () => _automationModuleRunning ? Environment.ProcessId : null,
                () => _automationModuleRunning ? "规则监听已注册" : "监听已注销", () => null));

        Modules.Register(new ModuleDefinition(SyncWallpaperModule.DesktopEngine, "Desktop Engine", false, Array.Empty<SyncWallpaperModule>()),
            new DelegateModuleController(
                _ => { EnsureDesktopEngine(); _desktopModuleRunning = true; return Task.CompletedTask; },
                _ => { DisposeDesktopEngine(); _desktopModuleRunning = false; return Task.CompletedTask; },
                () => _desktopModuleRunning, () => _desktopModuleRunning ? Environment.ProcessId : null,
                () => _desktopModuleRunning ? "Shell COM 按需调用" : "COM 已释放", () => null));

        foreach (var module in new[] { SyncWallpaperModule.TaskbarHost, SyncWallpaperModule.ShellHost, SyncWallpaperModule.ScreenSaverHost, SyncWallpaperModule.RemoteHost, SyncWallpaperModule.OnlineWallpaperProviders })
        {
            Modules.Register(new ModuleDefinition(module, module.ToString(), true, Array.Empty<SyncWallpaperModule>()),
                new HostProcessModuleController(module, message => _log.Info("Module", message)));
        }
    }

    private void EnsureDisplayEngine()
    {
        if (_displayAdapter is not null && _displayTransaction is not null) return;
        var adapter = new WindowsDisplayConfigurationAdapter(_discovery);
        var displayValidator = new DisplayConfigurationValidator(adapter);
        var displayStabilizer = new WindowsDisplayChangeStabilizer(adapter);
        _displayAdapter = adapter;
        _displayTransaction = new DisplayConfigurationTransactionService(
            adapter, displayValidator, adapter, adapter, adapter,
            displayStabilizer, _log, new WindowsDisplayConfirmationService());
    }

    private void DisposeDisplayEngine()
    {
        _displayTransaction = null;
        _displayAdapter = null;
    }

    private void EnsureAudioEngine()
    {
        if (_audioEngine is not null && _audioProvider is not null) return;
        try
        {
            var provider = new WindowsCoreAudioEndpointProvider();
            _audioProvider = provider;
            _audioProviderDisposable = provider;
        }
        catch (Exception ex)
        {
            _log.Warn("Audio", "Core Audio 初始化失败，音频步骤将优雅降级：" + ex.Message);
            _audioProvider = new UnavailableAudioEndpointProvider();
            _audioProviderDisposable = null;
        }
        _audioEngine = new AudioConfigurationEngine(_audioProvider, _log);
    }

    private void DisposeAudioEngine()
    {
        _audioProviderDisposable?.Dispose();
        _audioProviderDisposable = null;
        _audioProvider = null;
        _audioEngine = null;
    }

    private void EnsureWindowEngine()
    {
        if (_windowPlatform is not null && _windowEngine is not null && _windowEvents is not null) return;
        var platform = new WindowsWindowPlatform(() => Monitors);
        var events = new WindowsWindowEventSource();
        if (!events.IsActive)
        {
            events.Dispose();
            throw new InvalidOperationException("WinEventHook 注册失败，Window Engine 未启动。");
        }
        _windowEventHandler = (_, e) => _lastWindowEvent = $"{DateTime.Now:HH:mm:ss} WinEvent 0x{e.EventType:X} hwnd=0x{e.WindowHandle.ToInt64():X}";
        events.EventReceived += _windowEventHandler;
        _windowPlatform = platform;
        _windowEvents = events;
        _windowEngine = new WindowLayoutEngine(platform);
    }

    private void DisposeWindowEngine()
    {
        if (_windowEvents is not null && _windowEventHandler is not null) _windowEvents.EventReceived -= _windowEventHandler;
        _windowEventHandler = null;
        _windowEvents?.Dispose();
        _windowPlatform?.Dispose();
        _windowEvents = null;
        _windowPlatform = null;
        _windowEngine = null;
    }

    private void EnsureDesktopEngine()
    {
        if (_desktopIconEngine is not null && _desktopIconProvider is not null) return;
        var provider = new WindowsShellDesktopIconProvider(() => Monitors);
        _desktopIconProvider = provider;
        _desktopIconEngine = new DesktopIconLayoutEngine(provider);
    }

    private void DisposeDesktopEngine()
    {
        _desktopIconProvider = null;
        _desktopIconEngine = null;
    }

    private void EnsureAutomationEngine()
    {
        if (_automationEngine is not null && _legacyTriggerEngine is not null) return;
        _automationEngine = new AutomationEngine(
            new DelegateAutomationActionExecutor(async (action, context, token) =>
                (await RunAutomationActionAsync(action, context, token)).Success), _log);
        _legacyTriggerEngine = new TriggerEngine(RunLegacyActionAsync);
    }

    private void DisposeAutomationEngine()
    {
        _automationEngine = null;
        _legacyTriggerEngine = null;
    }

    private void RecordModulePerformance()
    {
        try
        {
            var record = new ModulePerformanceRecord
            {
                CapturedAt = DateTime.UtcNow,
                Mode = Settings.Modules.Mode,
                StartupMilliseconds = (long)Math.Max(0, (DateTime.UtcNow - _createdAt).TotalMilliseconds),
                Modules = Modules.Snapshot(Settings.Modules).Select(x => new ModulePerformanceSample
                {
                    Id = x.Id,
                    State = x.State,
                    ProcessId = x.ProcessId,
                    WorkingSetBytes = x.Resources.WorkingSetBytes,
                    PrivateBytes = x.Resources.PrivateBytes,
                    HandleCount = x.Resources.HandleCount,
                    CpuSeconds = x.Resources.CpuSeconds,
                    ThreadCount = x.Resources.ThreadCount
                }).ToList()
            };
            ModulePerformance.Records.Add(record);
            if (ModulePerformance.Records.Count > 100) ModulePerformance.Records.RemoveRange(0, ModulePerformance.Records.Count - 100);
            Store.Save("module-performance.json", ModulePerformance);
        }
        catch (Exception ex) { _log.Warn("Module", "性能记录保存失败：" + ex.Message); }
    }

    private bool IsModuleRunning(SyncWallpaperModule module) => Modules.GetState(module) == ModuleLifecycleState.Running;

    private void RequireModule(SyncWallpaperModule module)
    {
        if (!IsModuleRunning(module)) throw new InvalidOperationException($"{module} 未启用。请先在设置中选择标准/完整模式，或在自定义模式启用此模块。");
    }

    public async Task SetModuleEnabledAsync(SyncWallpaperModule module, bool enabled)
    {
        await Modules.SetEnabledAsync(module, enabled, Settings.Modules).ConfigureAwait(false);
        RecordModulePerformance();
        Store.Save("settings.json", Settings);
        RaiseChanged();
    }

    public async Task ApplyModuleModeAsync(ModuleMode mode)
    {
        Settings.Modules.ApplyPreset(mode);
        await Modules.StopAllAsync().ConfigureAwait(false);
        await Modules.StartEnabledAsync(Settings.Modules).ConfigureAwait(false);
        RecordModulePerformance();
        Store.Save("settings.json", Settings);
        RaiseChanged();
    }

    public void Start()
    {
        Paths.Ensure();
        Modules.StartEnabledAsync(Settings.Modules).GetAwaiter().GetResult();
        RecordModulePerformance();
        SeedLocalLibraryAndProfiles();
        if (MonitorProfiles.Profiles.Count == 0)
        {
            var monitors = _discovery.Discover().ToList();
            if (monitors.Count > 0)
            {
                MonitorProfiles.Profiles.Add(_monitorConfiguration.CaptureProfile("当前显示器布局", monitors));
                Store.Save("monitor-profiles.json", MonitorProfiles);
            }
        }
        if (DisplayConfigurations.Profiles.Count == 0 && IsModuleRunning(SyncWallpaperModule.DisplayEngine))
        {
            try
            {
                var snapshot = _displayAdapter!.Capture();
                var profile = snapshot.Profile;
                profile.Name = "当前显示配置";
                _displayRepository.Save(profile);
                DisplayConfigurations.Profiles.Add(profile);
            }
            catch (Exception ex) { _log.Warn("Display", "保存初始显示配置失败：" + ex.Message); }
        }
        StatusText = "监测中";
        _coordinator.Start();
        RaiseChanged();
    }

    private void SeedLocalLibraryAndProfiles()
    {
        foreach (var name in new[] { "本体.jpg", "本体.png", "横屏1.jpg", "横屏1.png", "竖屏1.jpg", "竖屏1.png" })
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "wallpaper", name);
            if (File.Exists(path) && Library.Assets.All(a => !a.OriginalFileName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                try { _library.Import(path, Path.GetFileNameWithoutExtension(name)); }
                catch (Exception ex) { _log.Write("Library", "导入初始壁纸失败：" + ex.Message); }
            }
        }
        Library = _library.Load();
        var needsInitialProfile = Profiles.Profiles.Count == 0 || Profiles.Profiles.All(p => p.Roles.All(r =>
            string.IsNullOrWhiteSpace(r.Fingerprint.MonitorDevicePath) ||
            r.Fingerprint.MonitorDevicePath.StartsWith(@"\\.\DISPLAY", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(r.Fingerprint.StableId) ||
            string.Equals(r.Fingerprint.ManufacturerName, "UNKNOWN", StringComparison.OrdinalIgnoreCase)));
        if (!needsInitialProfile) return;
        var monitors = _discovery.Discover().ToList();
        if (monitors.Count == 0) return;
        Profiles.Profiles.Clear();
        var profile = new WallpaperProfile
        {
            Name = monitors.Count == 1 ? "Laptop Only" : "Three Monitor Setup",
            Combination = monitors.Count == 1 ? DisplayCombinationKind.LaptopOnly : monitors.Count == 3 ? DisplayCombinationKind.ThreeMonitorSetup : DisplayCombinationKind.Custom,
            ExpectedMonitorCount = monitors.Count,
            AutoApply = true,
            MinimumConfidence = 80,
            Priority = monitors.Count == 1 ? 100 : 90
        };
        foreach (var monitor in monitors)
        {
            var role = monitor.IsInternal ? ("Laptop", "笔记本本体", "本体") :
                (monitor.Width >= monitor.Height ? ("Landscape", "横屏1", "横屏1") : ("Portrait", "竖屏1", "竖屏1"));
            var asset = Library.Assets.FirstOrDefault(a => a.DisplayName.Equals(role.Item3, StringComparison.OrdinalIgnoreCase))
                ?? Library.Assets.FirstOrDefault(a => a.OriginalFileName.Contains(role.Item3, StringComparison.OrdinalIgnoreCase));
            profile.Roles.Add(new MonitorRoleBinding
            {
                Role = role.Item1,
                DisplayName = role.Item2,
                Fingerprint = monitor.Clone(),
                WallpaperAssetId = asset?.Id ?? string.Empty,
                WallpaperPath = asset is null ? string.Empty : Path.Combine(Paths.Root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar)),
                FitMode = WallpaperFitMode.Fill,
                AllowAutoRebind = false,
                LastKnownMonitorDevicePath = monitor.MonitorDevicePath
            });
        }
        Profiles.Profiles.Add(profile);
        Settings.ActiveProfileId = profile.Id;
        Store.Save("profiles.json", Profiles);
        Store.Save("settings.json", Settings);
    }

    private async Task OnStableDisplaysAsync(DisplaySnapshot snapshot)
    {
        Monitors = snapshot.Monitors;
        if (!string.IsNullOrWhiteSpace(_discovery.LastError))
            _log.Write("Display", "原生显示路径读取失败，临时使用 Screen 兼容数据：" + _discovery.LastError);
        _log.Write("Display", $"显示器组合已稳定：{Monitors.Count} 台", monitors: Monitors.Count);
        foreach (var monitor in Monitors)
            _log.Write("Display", $"{monitor.DisplayLabel} {monitor.MonitorDevicePath} {monitor.Width}x{monitor.Height} @ {monitor.DesktopX},{monitor.DesktopY}");

        if (_automationModuleRunning && _automationEngine is not null)
        {
            LastAutomationResults = await _automationEngine.FireAsync(
                new TriggerDefinition { Type = AutomationTriggerType.DisplayConfigurationStable },
                Triggers.AutomationRules,
                new AutomationExecutionContext { TriggerType = AutomationTriggerType.DisplayConfigurationStable, ActiveDisplayProfileId = LastMatch?.Profile?.Id });
            if (_legacyTriggerEngine is not null)
                await _legacyTriggerEngine.FireAsync(TriggerEvent.DisplayConfigurationChanged, Triggers, activeProfile: LastMatch?.Profile?.Id);
        }
        if (!Settings.AutoMatchEnabled)
        {
            StatusText = "已暂停"; LastMessage = "自动匹配已暂停"; RaiseChanged(); return;
        }
        await MatchAndApplyAsync();
    }

    public async Task DetectAsync()
    {
        _coordinator.Signal();
        await Task.Delay(50);
        Monitors = _discovery.Discover();
        RaiseChanged();
    }

    public async Task ReapplyAsync()
    {
        if (Monitors.Count == 0) Monitors = _discovery.Discover();
        await MatchAndApplyAsync(manual: true);
    }

    private async Task MatchAndApplyAsync(bool manual = false)
    {
        if (Monitors.Count == 0) { StatusText = "未发现显示器"; LastMessage = "没有活动显示路径"; RaiseChanged(); return; }
        LastMatch = _matcher.Match(Monitors, Profiles.Profiles);
        _log.Write("Match", LastMatch.Message, LastMatch.Profile?.Name, Monitors.Count, LastMatch.Confidence);
        if (LastMatch.Status == MatchStatus.Ambiguous)
        {
            StatusText = "需要确认"; LastMessage = LastMatch.Message; RaiseChanged(); return;
        }
        if (LastMatch.Status == MatchStatus.NoMatch)
        {
            StatusText = "保持当前壁纸"; LastMessage = LastMatch.Message; RaiseChanged(); return;
        }
        if (!LastMatch.CanAutoApply)
        {
            StatusText = "需要确认";
            LastMessage = string.IsNullOrWhiteSpace(LastMatch.Message) ? "匹配置信度不足，未自动应用壁纸" : LastMatch.Message;
            RaiseChanged();
            return;
        }
        try
        {
            var result = await RunOnDispatcherAsync(() => _apply.ApplyAsync(LastMatch, Library.Assets, Paths, generation: _coordinator.Generation, manual: manual));
            LastMessage = result.Message; StatusText = result.Success ? "运行中" : "应用未完成";
            if (result.Success)
            {
                LastAppliedAt = DateTime.Now;
                LastMatch.Profile!.LastAppliedAt = DateTime.UtcNow;
                LastMatch.Profile.LastSuccessfulMatchAt = DateTime.UtcNow;
                foreach (var role in LastMatch.Profile.Roles)
                {
                    if (LastMatch.RoleMatches.TryGetValue(role.Role, out var monitor))
                    {
                        role.LastSuccessfulMatchAt = DateTime.UtcNow;
                        role.LastKnownMonitorDevicePath = monitor.MonitorDevicePath;
                    }
                }
                Store.Save("profiles.json", Profiles);
            }
        }
        catch (Exception ex) { StatusText = "应用失败"; LastMessage = ex.Message; _log.Write("Error", ex.ToString()); }
        RaiseChanged();
    }

    public DisplayConfigurationProfile CaptureDisplayProfile(string name)
    {
        RequireModule(SyncWallpaperModule.DisplayEngine);
        var profile = _displayAdapter!.Capture().Profile;
        profile.ProfileId = Guid.NewGuid().ToString("N");
        profile.Name = name;
        _displayRepository.Save(profile);
        DisplayConfigurations.Profiles.Add(profile);
        _log.Info("Display", $"已保存显示配置：{name}");
        RaiseChanged();
        return profile;
    }

    public async Task<DisplayConfigurationApplyResult> ApplyDisplayProfileAsync(string profileId, bool requireConfirmation = true)
    {
        if (!IsModuleRunning(SyncWallpaperModule.DisplayEngine))
            return LastDisplayTransaction = new() { Status = DisplayConfigurationTransactionStatus.PrecheckFailed, Message = "Display Engine 未启用，未执行任何显示配置变化。" };
        var profile = DisplayConfigurations.Profiles.FirstOrDefault(x => x.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return LastDisplayTransaction = new() { Status = DisplayConfigurationTransactionStatus.PrecheckFailed, Message = "找不到显示配置。" };
        LastDisplayTransaction = await _displayTransaction!.ApplyAsync(profile, new DisplayConfigurationApplyOptions { RequireConfirmation = requireConfirmation });
        LastMessage = LastDisplayTransaction.Message;
        _log.Info("Display", LastDisplayTransaction.Message);
        RaiseChanged();
        return LastDisplayTransaction;
    }

    public DisplayConfigurationProfile CopyDisplayProfile(string profileId, string name)
    {
        var copy = _displayRepository.Copy(profileId, name);
        DisplayConfigurations.Profiles.RemoveAll(x => x.ProfileId.Equals(copy.ProfileId, StringComparison.OrdinalIgnoreCase));
        DisplayConfigurations.Profiles.Add(copy);
        RaiseChanged();
        return copy;
    }

    public bool DeleteDisplayProfile(string profileId)
    {
        var removed = _displayRepository.Delete(profileId);
        if (removed) DisplayConfigurations.Profiles.RemoveAll(x => x.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        RaiseChanged();
        return removed;
    }

    public DisplayValidationResult? ValidateDisplayProfile(string profileId)
    {
        if (!IsModuleRunning(SyncWallpaperModule.DisplayEngine) || _displayAdapter is null) return null;
        var profile = DisplayConfigurations.Profiles.FirstOrDefault(x => x.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        return profile is null ? null : new DisplayConfigurationValidator(_displayAdapter).Validate(profile, _displayAdapter.Capture());
    }

    public AudioProfile CaptureAudioProfile(string name)
    {
        RequireModule(SyncWallpaperModule.AudioEngine);
        var profile = new AudioProfile { Name = name };
        foreach (var role in new[] { AudioEndpointRole.Console, AudioEndpointRole.Multimedia, AudioEndpointRole.Communications, AudioEndpointRole.Recording })
        {
            var endpoint = _audioProvider!.GetDefault(role);
            if (endpoint is not null) profile.Assignments.Add(new AudioRoleAssignment { Role = role, Endpoint = endpoint, Mode = AudioStepMode.Optional });
        }
        AudioProfiles.Profiles.Add(profile);
        Store.Save("audio-profiles.json", AudioProfiles);
        _log.Info("Audio", $"已保存音频配置：{name}");
        RaiseChanged();
        return profile;
    }

    public async Task<AudioConfigurationResult> ApplyAudioProfileAsync(string profileId, AudioStepMode mode = AudioStepMode.Optional)
    {
        if (!IsModuleRunning(SyncWallpaperModule.AudioEngine)) return LastAudioResult = new() { Message = "Audio Engine 未启用，未执行音频切换。" };
        var profile = AudioProfiles.Profiles.FirstOrDefault(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        LastAudioResult = profile is null
            ? new AudioConfigurationResult { Message = "找不到音频配置。" }
            : await _audioEngine!.ApplyAsync(profile, mode);
        LastMessage = LastAudioResult.Message;
        RaiseChanged();
        return LastAudioResult;
    }

    public bool DeleteAudioProfile(string profileId)
    {
        var removed = AudioProfiles.Profiles.RemoveAll(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Store.Save("audio-profiles.json", AudioProfiles);
        RaiseChanged();
        return removed;
    }

    public void CaptureWindowProfile(string name)
    {
        RequireModule(SyncWallpaperModule.WindowEngine);
        var windows = _windowEngine!.Capture();
        var profile = new WindowPositionProfile { Name = name };
        foreach (var window in windows)
        {
            var monitor = Monitors.FirstOrDefault(x => x.MonitorDevicePath.Equals(window.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
            if (monitor is null) continue;
            profile.Windows.Add(new WindowPlacement
            {
                ProcessPath = window.Identity.ExecutablePath,
                ProcessName = window.Identity.ProcessName,
                AppUserModelId = window.Identity.AppUserModelId,
                IsUwp = window.Identity.IsUwp,
                IsElevated = window.Identity.IsElevated,
                WindowClass = window.Identity.WindowClass,
                MonitorDevicePath = monitor.MonitorDevicePath,
                SavedMonitorX = monitor.DesktopX,
                SavedMonitorY = monitor.DesktopY,
                SavedMonitorWidth = monitor.Width,
                SavedMonitorHeight = monitor.Height,
                Left = window.PhysicalBounds.Left,
                Top = window.PhysicalBounds.Top,
                Width = window.PhysicalBounds.Width,
                Height = window.PhysicalBounds.Height,
                Dpi = window.Dpi,
                ShowState = window.ShowState,
                Maximize = window.IsMaximized,
                Minimize = window.IsMinimized
            });
        }
        WindowProfiles.Profiles.Add(profile);
        Store.Save("window-profiles.json", WindowProfiles);
        _log.Info("Window", $"已保存窗口布局：{profile.Name}，{profile.Windows.Count} 个窗口");
        RaiseChanged();
    }

    public int ApplyWindowProfile(string id)
    {
        if (!IsModuleRunning(SyncWallpaperModule.WindowEngine)) return 0;
        var profile = WindowProfiles.Profiles.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return 0;
        LastWindowRestore = _windowEngine!.Restore(profile, Monitors, new WindowRestoreOptions { StartUnlaunchedApplications = profile.RestoreUnlaunchedApplications });
        _log.Info("Window", $"已恢复 {LastWindowRestore.Applied} 个窗口，跳过 {LastWindowRestore.Skipped} 个");
        RaiseChanged();
        return LastWindowRestore.Applied;
    }

    public bool DeleteWindowProfile(string profileId)
    {
        var removed = WindowProfiles.Profiles.RemoveAll(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Store.Save("window-profiles.json", WindowProfiles);
        RaiseChanged();
        return removed;
    }

    public DesktopIconProfile CaptureDesktopIconProfile(string name)
    {
        RequireModule(SyncWallpaperModule.DesktopEngine);
        var profile = _desktopIconEngine!.Capture(name);
        DesktopIconProfiles.Profiles.Add(profile);
        Store.Save("desktop-icons.json", DesktopIconProfiles);
        _log.Info("Desktop", $"已保存桌面图标布局：{profile.Positions.Count} 个项目");
        RaiseChanged();
        return profile;
    }

    public DesktopIconRestoreResult ApplyDesktopIconProfile(string id)
    {
        if (!IsModuleRunning(SyncWallpaperModule.DesktopEngine)) return LastDesktopRestore = new() { Reasons = new[] { "Desktop Engine 未启用。" } };
        var profile = DesktopIconProfiles.Profiles.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return LastDesktopRestore = new() { Reasons = new[] { "找不到桌面图标配置。" } };
        var bounds = VirtualDesktopBounds();
        LastDesktopRestore = _desktopIconEngine!.Restore(profile, bounds);
        RaiseChanged();
        return LastDesktopRestore;
    }

    public bool DeleteDesktopIconProfile(string profileId)
    {
        var removed = DesktopIconProfiles.Profiles.RemoveAll(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed) Store.Save("desktop-icons.json", DesktopIconProfiles);
        RaiseChanged();
        return removed;
    }

    private CoreRect VirtualDesktopBounds()
    {
        if (Monitors.Count == 0) return new CoreRect(0, 0, 1, 1);
        var left = Monitors.Min(x => x.DesktopX); var top = Monitors.Min(x => x.DesktopY);
        var right = Monitors.Max(x => x.DesktopX + x.Width); var bottom = Monitors.Max(x => x.DesktopY + x.Height);
        return new CoreRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    public async Task FireTestTriggerAsync()
    {
        if (!IsModuleRunning(SyncWallpaperModule.Automation) || _automationEngine is null)
        {
            LastMessage = "Automation 未启用，未执行测试触发器。";
            RaiseChanged();
            return;
        }
        LastAutomationResults = await _automationEngine.FireAsync(
            new TriggerDefinition { Type = AutomationTriggerType.Manual, Value = "test" },
            Triggers.AutomationRules,
            new AutomationExecutionContext { TriggerType = AutomationTriggerType.Manual, Value = "test", ActiveDisplayProfileId = LastMatch?.Profile?.Id });
        if (_legacyTriggerEngine is not null)
            await _legacyTriggerEngine.FireAsync(TriggerEvent.DisplayConfigurationChanged, Triggers, activeProfile: LastMatch?.Profile?.Id);
        RaiseChanged();
    }

    private async Task<AutomationExecutionResult> RunAutomationActionAsync(ActionDefinition action, AutomationExecutionContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            switch (action.Type)
            {
                case AutomationActionType.ApplyDisplayProfile:
                    if (!IsPersistentChangeApproved(context)) return new() { Success = false, Message = "自动化显示配置需要交互式确认，已跳过。" };
                    // Display mode changes are never silent automation actions;
                    // the confirmation window is the explicit user gate.
                    var display = await ApplyDisplayProfileAsync(action.Argument, true);
                    return new() { Success = display.Status == DisplayConfigurationTransactionStatus.Applied, Message = display.Message };
                case AutomationActionType.ApplyWallpaperProfile:
                    Settings.ActiveProfileId = action.Argument; Store.Save("settings.json", Settings); await MatchAndApplyAsync();
                    return new() { Success = true, Message = "壁纸配置已执行。" };
                case AutomationActionType.ApplyAudioProfile:
                    if (!IsPersistentChangeApproved(context)) return new() { Success = false, Message = "自动化音频切换需要显式确认，已跳过。" };
                    var audio = await ApplyAudioProfileAsync(action.Argument);
                    return new() { Success = audio.Success, Message = audio.Message };
                case AutomationActionType.RestoreWindowProfile:
                    if (!IsPersistentChangeApproved(context)) return new() { Success = false, Message = "自动化窗口移动需要显式确认，已跳过。" };
                    return new() { Success = ApplyWindowProfile(action.Argument) >= 0, Message = "窗口布局恢复动作已执行。" };
                case AutomationActionType.StartProgram:
                    if (!IsSafeLaunchPath(action.Argument)) return new() { Success = false, Message = "程序路径未通过安全检查。" };
                    Process.Start(new ProcessStartInfo { FileName = action.Argument, Arguments = action.Argument2 ?? string.Empty, UseShellExecute = true });
                    return new() { Success = true, Message = "程序启动已请求。" };
                case AutomationActionType.CloseProgram:
                    foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(action.Argument))) try { if (process.MainWindowHandle != IntPtr.Zero) process.CloseMainWindow(); } catch { }
                    return new() { Success = true, Message = "普通程序关闭请求已发送。" };
                case AutomationActionType.Wait:
                    if (int.TryParse(action.Argument, out var milliseconds)) await Task.Delay(Math.Clamp(milliseconds, 0, 120000), token);
                    return new() { Success = true, Message = "等待动作完成。" };
                case AutomationActionType.Notify:
                    LastMessage = action.Argument; RaiseChanged(); return new() { Success = true, Message = action.Argument };
                case AutomationActionType.WriteLog:
                    _log.Info("Automation", action.Argument); return new() { Success = true, Message = "日志动作完成。" };
                case AutomationActionType.CustomShellCommand:
                    _log.Warn("Automation", "自定义 shell 命令默认禁用。");
                    return new() { Success = false, Message = "自定义 shell 命令默认禁用。" };
                default:
                    return new() { Success = false, Message = "动作类型暂未实现。" };
            }
        }
        catch (Exception ex)
        {
            _log.Error("Automation", ex.ToString());
            return new() { Success = false, Message = ex.Message };
        }
    }

    private async Task RunLegacyActionAsync(FunctionAction action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch (action.Type)
        {
            case FunctionActionType.LoadWallpaperProfile:
                Settings.ActiveProfileId = action.Argument; Store.Save("settings.json", Settings); await MatchAndApplyAsync(); break;
            case FunctionActionType.LoadMonitorProfile:
                var profile = MonitorProfiles.Profiles.FirstOrDefault(p => p.Id.Equals(action.Argument, StringComparison.OrdinalIgnoreCase));
                if (profile is not null) _monitorConfiguration.TryApply(profile, Monitors);
                break;
            case FunctionActionType.RunProcess:
                if (IsSafeLaunchPath(action.Argument)) Process.Start(new ProcessStartInfo { FileName = action.Argument, Arguments = action.Argument2 ?? string.Empty, UseShellExecute = true });
                break;
            case FunctionActionType.ShowNotification:
                LastMessage = action.Argument; RaiseChanged(); break;
        }
    }

    private static bool IsSafeLaunchPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || !File.Exists(path)) return false;
        if (path.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return false;
        return !path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPersistentChangeApproved(AutomationExecutionContext context)
        => context.Data.TryGetValue("AllowPersistentChanges", out var value) && value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static async Task<T> RunOnDispatcherAsync<T>(Func<Task<T>> action)
    {
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true) return await action();
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        return dispatcher is null ? await action() : await (await dispatcher.InvokeAsync(action));
    }

    public void SetStartup(bool enabled)
    {
        var executable = Environment.ProcessPath ?? string.Empty;
        _startup.SetEnabled(enabled, executable);
        Settings.StartWithWindows = enabled;
        Store.Save("settings.json", Settings);
        RaiseChanged();
    }

    public void ApplyManualDisplayAssignments(IReadOnlyList<ManualDisplayAssignment> assignments)
    {
        if (assignments.Count == 0) return;
        var profile = Profiles.Profiles.FirstOrDefault(x => string.Equals(x.Id, Settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase))
            ?? Profiles.Profiles.FirstOrDefault(x => x.ExpectedMonitorCount == assignments.Count)
            ?? ProfileTemplates.Custom("手动确认配置", assignments.Select(x => x.Role));
        if (!Profiles.Profiles.Contains(profile)) Profiles.Profiles.Add(profile);
        profile.Roles.Clear();
        profile.ExpectedMonitorCount = assignments.Count;
        profile.Combination = assignments.Count == 1 ? DisplayCombinationKind.LaptopOnly : assignments.Count == 3 ? DisplayCombinationKind.ThreeMonitorSetup : DisplayCombinationKind.Custom;
        profile.ModifiedAt = DateTime.UtcNow;
        foreach (var assignment in assignments)
        {
            var monitor = Monitors.FirstOrDefault(x => (!string.IsNullOrWhiteSpace(assignment.StableId) && string.Equals(x.StableId, assignment.StableId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(assignment.MonitorDevicePath) && string.Equals(x.MonitorDevicePath, assignment.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)));
            if (monitor is null) continue;
            var role = string.Equals(assignment.Role, "Custom", StringComparison.OrdinalIgnoreCase) ? $"Custom-{monitor.WindowsDisplayName.Trim('\\', '.', 'D', 'I', 'S', 'P', 'L', 'A', 'Y')}" : assignment.Role;
            profile.Roles.Add(new MonitorRoleBinding
            {
                Role = role, DisplayName = role, Fingerprint = monitor.Clone(), WallpaperPath = assignment.WallpaperPath,
                AllowAutoRebind = false, LastKnownMonitorDevicePath = monitor.MonitorDevicePath, Notes = "用户在 A/B/C 识别界面确认"
            });
        }
        Settings.ActiveProfileId = profile.Id;
        Store.Save("profiles.json", Profiles); Store.Save("settings.json", Settings);
        LastMessage = $"已保存 {profile.Roles.Count} 个手动角色绑定"; RaiseChanged();
    }

    public void ImportWallpaper(string path)
    {
        try { _library.Import(path); Library = _library.Load(); _log.Write("Library", $"已导入壁纸：{Path.GetFileName(path)}"); RaiseChanged(); }
        catch (Exception ex) { _log.Write("Error", "导入壁纸失败：" + ex.Message); }
    }

    public string StartupText => _startup.IsEnabled ? "已启用" : "未启用";
    public IReadOnlyList<DiagnosticEvent> RecentLogs => _log.Recent;
    public DiagnosticLaboratorySnapshot CaptureDiagnosticSnapshot()
    {
        var displays = _discovery.Discover();
        IReadOnlyList<AudioEndpointReference> audio = Array.Empty<AudioEndpointReference>();
        if (IsModuleRunning(SyncWallpaperModule.AudioEngine) && _audioProvider is not null)
        {
            try { audio = _audioProvider.Enumerate(); }
            catch { }
        }
        var defaults = new Dictionary<AudioEndpointRole, AudioEndpointReference?>();
        foreach (var role in new[] { AudioEndpointRole.Console, AudioEndpointRole.Multimedia, AudioEndpointRole.Communications, AudioEndpointRole.Recording })
        {
            try { defaults[role] = IsModuleRunning(SyncWallpaperModule.AudioEngine) && _audioProvider is not null ? _audioProvider.GetDefault(role) : null; }
            catch { defaults[role] = null; }
        }

        var windows = Array.Empty<WindowPositionSnapshot>();
        if (IsModuleRunning(SyncWallpaperModule.WindowEngine) && _windowPlatform is not null)
            try { windows = _windowPlatform.Enumerate().ToArray(); } catch { }
        var desktopItems = 0;
        if (IsModuleRunning(SyncWallpaperModule.DesktopEngine) && _desktopIconProvider is not null)
            try { desktopItems = _desktopIconProvider.Capture().Count; } catch { }
        var explorerRunning = Process.GetProcessesByName("explorer").Length > 0;
        var transaction = LastDisplayTransaction;
        return new DiagnosticLaboratorySnapshot
        {
            WindowsVersion = RuntimeInformation.OSDescription,
            SoftwareVersion = typeof(AppRuntime).Assembly.GetName().Version?.ToString() ?? "开发构建",
            Displays = displays,
            AudioDevices = audio,
            AudioDefaults = defaults,
            WindowCount = windows.Length,
            ElevatedWindowCount = windows.Count(x => x.Identity.IsElevated),
            WindowListenerStatus = _windowEvents is null ? "Window Engine 未启用" : _windowEvents.IsActive ? _lastWindowEvent : "WinEventHook 未建立",
            ExplorerStatus = explorerRunning ? $"Explorer 运行中；桌面 Shell 项目 {desktopItems} 个" : "未检测到 Explorer",
            ComInitializationStatus = $"Apartment={Thread.CurrentThread.GetApartmentState()}；AudioProvider={_audioProvider?.GetType().Name ?? "未启用"}",
            LastSystemEvent = _coordinator.LastSystemEvent,
            LastTransaction = transaction is null ? "尚未执行显示事务" : $"{transaction.Status}：{transaction.Message}",
            LastRollback = transaction is null ? "尚未发生回滚" : transaction.RollbackAttempted ? $"尝试={transaction.RollbackAttempted}，成功={transaction.RollbackSucceeded}" : "本次未触发回滚",
            DesktopShellItemCount = desktopItems,
            Modules = Modules.Snapshot(Settings.Modules),
            PerformanceHistory = ModulePerformance.Records.TakeLast(20).ToArray(),
            Resources = _resourceDiagnostics.Capture()
        };
    }
    public void OpenFolder(string path) { Paths.Ensure(); Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }); }

    public void Dispose()
    {
        _coordinator.Dispose();
        _explorerRecovery.Dispose();
        Modules.StateChanged -= Modules_StateChanged;
        Modules.Dispose();
        Store.Save("settings.json", Settings);
        Store.Save("profiles.json", Profiles);
        Store.Save("monitor-profiles.json", MonitorProfiles);
        Store.Save("window-profiles.json", WindowProfiles);
        Store.Save("triggers.json", Triggers);
        Store.Save("audio-profiles.json", AudioProfiles);
        Store.Save("desktop-icons.json", DesktopIconProfiles);
        Store.Save("module-performance.json", ModulePerformance);
        Store.Save("module-runtime.json", ModuleRuntime);
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
