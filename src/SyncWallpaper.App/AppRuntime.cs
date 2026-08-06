using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using SyncWallpaper.AudioEngine;
using SyncWallpaper.Automation;
using SyncWallpaper.Core;
using SyncWallpaper.DesktopEngine;
using SyncWallpaper.DisplayEngine;
using SyncWallpaper.WindowEngine;
using SyncWallpaper.Windows;
using SyncWallpaper.Update.Core;
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
    public UpdateCheckResult? LastUpdateResult { get; private set; }
    public string CurrentVersion => CurrentVersionProvider.GetInformationalVersion(typeof(App).Assembly);
    public string StatusText { get; private set; } = "正在启动";
    public string LastMessage { get; private set; } = "等待显示器检测";
    public DateTime? LastAppliedAt { get; private set; }
    /// <summary>True only while a stable display topology is being classified/applied.</summary>
    public bool IsRecognizing { get; private set; }
    public TrayIconState TrayVisualState => IsRecognizing
        ? TrayIconState.Recognizing
        : !Settings.AutoMatchEnabled || StatusText is "已暂停" or "安全模式"
            || StatusText.StartsWith("验证模式", StringComparison.Ordinal)
            ? TrayIconState.Paused
            : StatusText is "需要确认" or "保持当前壁纸" or "应用未完成" or "应用失败" or "未发现显示器"
                ? TrayIconState.Error
                : TrayIconState.Normal;
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
    private bool _sessionAutoMatchSuppressed;
    private readonly DateTime _createdAt = DateTime.UtcNow;
    private string _lastWindowEvent = "尚未收到窗口事件";
    private readonly HttpClient _updateHttpClient;
    private readonly GitHubReleaseChecker _releaseChecker;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly CancellationTokenSource _updateLifetime = new();

    public AppRuntime()
    {
        var dataRoot = ResolveStorageRoot();
        MigrateLegacyStorage(dataRoot);
        Paths = new DataPaths(dataRoot);
        Store = new ConfigurationStore(Paths);
        Settings = Store.Load("settings.json", new AppSettings());
        var loadedProfiles = Store.Load("profiles.json", new ProfilesDocument());
        var loadedProfilesSchema = loadedProfiles.SchemaVersion;
        Profiles = ProfileSchemaMigrator.Migrate(loadedProfiles);
        Library = Store.Load("library.json", new LibraryDocument());
        var libraryChanged = RestoreMissingAssetsFromSourceFolders(Library, Paths.Root);
        var profilesChanged = NormalizeWallpaperBindings(Profiles, Library, Paths.Root);
        if (libraryChanged) Store.Save("library.json", Library);
        if (profilesChanged) Store.Save("profiles.json", Profiles);
        if (!string.Equals(Settings.DataRoot, Paths.Root, StringComparison.OrdinalIgnoreCase))
        {
            Settings.DataRoot = Paths.Root;
            Store.Save("settings.json", Settings);
        }
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
        _updateHttpClient = new HttpClient();
        _releaseChecker = new GitHubReleaseChecker(
            _updateHttpClient,
            ProjectLinks.RepositorySettings,
            CurrentVersion,
            TimeSpan.FromSeconds(15));
        if (loadedProfilesSchema < ProfileSchemaMigrator.CurrentSchemaVersion)
        {
            try { Store.Save("profiles.json", Profiles); _log.Info("Config", $"profiles.json 已迁移到 SchemaVersion={Profiles.SchemaVersion}"); }
            catch (Exception ex) { _log.Warn("Config", "保存迁移后的 profiles.json 失败：" + ex.Message); }
        }
        _library = new WallpaperLibraryService(Store);
        Library = _library.Refresh().Document;
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

    public void Start(bool suppressAutoMatch = false)
    {
        _sessionAutoMatchSuppressed = suppressAutoMatch;
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
        StatusText = _sessionAutoMatchSuppressed ? "验证模式（不自动应用壁纸）" : "监测中";
        _coordinator.Start();
        if (Settings.AutomaticUpdateCheckEnabled && ProjectLinks.IsConfigured)
            _ = ScheduleAutomaticUpdateCheckAsync();
        RaiseChanged();
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool manual = true, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = new UpdateCheckSettings
        {
            AutomaticCheckEnabled = Settings.AutomaticUpdateCheckEnabled,
            Channel = ParseUpdateChannel(Settings.UpdateChannel),
            LastSuccessfulCheckUtc = Settings.LastUpdateSuccessfulCheckUtc,
            LastAttemptUtc = Settings.LastUpdateAttemptUtc
        };
        if (!manual && !UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, now))
        {
            return LastUpdateResult ?? new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null, null, null, null, null,
                "自动更新检查尚未到期。", "七天检查间隔尚未到期。");
        }

        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = DateTimeOffset.UtcNow;
            Settings.LastUpdateAttemptUtc = now;
            TrySaveSettings();
            RaiseChanged();
            var result = await _releaseChecker.CheckAsync(settings.Channel, cancellationToken).ConfigureAwait(false);
            LastUpdateResult = result;
            UpdateCheckScheduler.RecordResult(settings, result, DateTimeOffset.UtcNow);
            Settings.LastUpdateAttemptUtc = settings.LastAttemptUtc;
            Settings.LastUpdateSuccessfulCheckUtc = settings.LastSuccessfulCheckUtc;
            TrySaveSettings();
            if (result.IsSuccess)
                _log.Info("Update", result.UserMessage ?? "GitHub Release 检查完成。");
            else if (manual)
                _log.Warn("Update", result.TechnicalMessage ?? result.UserMessage ?? "GitHub Release 检查失败。");
            RaiseChanged();
            return result;
        }
        catch (OperationCanceledException)
        {
            var cancelled = new UpdateCheckResult(UpdateCheckStatus.Cancelled, null, null, null, null, null, null,
                "已取消更新检查。", "调用方取消了请求。");
            LastUpdateResult = cancelled;
            RaiseChanged();
            return cancelled;
        }
        finally { _updateGate.Release(); }
    }

    public bool OpenReleasePage(Uri? releaseUrl = null)
    {
        var url = releaseUrl ?? ProjectLinks.LatestRelease;
        if (!ReleaseUrlValidator.IsAllowed(url, ProjectLinks.RepositorySettings)) return false;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url!.ToString(), UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            _log.Warn("Update", "打开 GitHub Release 失败：" + ex.Message);
            return false;
        }
    }

    public void SaveUpdateSettings()
    {
        Settings.UpdateChannel = ParseUpdateChannel(Settings.UpdateChannel).ToString();
        TrySaveSettings();
    }

    private async Task ScheduleAutomaticUpdateCheckAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), _updateLifetime.Token).ConfigureAwait(false);
            if (!_updateLifetime.IsCancellationRequested)
                await CheckForUpdatesAsync(false, _updateLifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.Warn("Update", "自动更新检查失败（已静默处理）：" + ex.Message); }
    }

    private static UpdateChannel ParseUpdateChannel(string? value)
        => string.Equals(value, nameof(UpdateChannel.Beta), StringComparison.OrdinalIgnoreCase)
            ? UpdateChannel.Beta : UpdateChannel.Stable;

    private void TrySaveSettings()
    {
        try { Store.Save("settings.json", Settings); }
        catch (Exception ex) { _log.Warn("Config", "保存更新检查设置失败：" + ex.Message); }
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
        var monitors = _discovery.Discover().ToList();
        if (monitors.Count == 0) return;
        if (!needsInitialProfile)
        {
            if (EnsureLaptopFallbackProfile(monitors))
            {
                Store.Save("profiles.json", Profiles);
                _log.Info("Config", "已从现有 Laptop 角色建立单屏回退配置");
            }
            return;
        }
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
        EnsureLaptopFallbackProfile(monitors);
        Settings.ActiveProfileId = profile.Id;
        Store.Save("profiles.json", Profiles);
        Store.Save("settings.json", Settings);
    }

    private bool EnsureLaptopFallbackProfile(IReadOnlyList<MonitorIdentity> monitors)
    {
        if (Profiles.Profiles.Any(p => p.Enabled && p.ExpectedMonitorCount == 1 && p.Roles.Count == 1
            && string.Equals(p.Roles[0].Role, "Laptop", StringComparison.OrdinalIgnoreCase))) return false;
        var laptop = Profiles.Profiles.SelectMany(p => p.Roles)
            .FirstOrDefault(r => string.Equals(r.Role, "Laptop", StringComparison.OrdinalIgnoreCase));
        var internalMonitor = monitors.FirstOrDefault(m => m.IsInternal);
        if (laptop is null || internalMonitor is null) return false;
        var fingerprint = laptop.Fingerprint is { StableId: { Length: > 0 } } ? laptop.Fingerprint.Clone() : internalMonitor.Clone();
        var binding = new MonitorRoleBinding
        {
            Role = "Laptop",
            DisplayName = "笔记本本体",
            Fingerprint = fingerprint,
            WallpaperAssetId = laptop.WallpaperAssetId,
            WallpaperPath = laptop.WallpaperPath,
            FitMode = laptop.FitMode,
            BackgroundColor = laptop.BackgroundColor,
            AllowAutoRebind = laptop.AllowAutoRebind,
            LastKnownMonitorDevicePath = laptop.LastKnownMonitorDevicePath,
            Notes = "从现有多屏配置生成的单屏回退绑定"
        };
        Profiles.Profiles.Add(new WallpaperProfile
        {
            Name = "Laptop Only",
            Combination = DisplayCombinationKind.LaptopOnly,
            ExpectedMonitorCount = 1,
            AutoApply = true,
            AllowCompatibleMatch = true,
            MinimumConfidence = Math.Min(laptop.Fingerprint.HasUsableSerial ? 90 : 80, 100),
            Priority = Profiles.Profiles.Max(p => p.Priority) + 100,
            Roles = new() { binding }
        });
        return true;
    }

    private async Task OnStableDisplaysAsync(DisplaySnapshot snapshot)
    {
        IsRecognizing = true;
        RaiseChanged();
        try { await ProcessStableDisplaysAsync(snapshot); }
        finally
        {
            IsRecognizing = false;
            RaiseChanged();
        }
    }

    private async Task ProcessStableDisplaysAsync(DisplaySnapshot snapshot)
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
        if (_sessionAutoMatchSuppressed || !Settings.AutoMatchEnabled)
        {
            StatusText = _sessionAutoMatchSuppressed ? "验证模式" : "已暂停";
            LastMessage = _sessionAutoMatchSuppressed ? "验证模式不会自动应用壁纸；请使用明确的手动按钮" : "自动匹配已暂停";
            RaiseChanged(); return;
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
        // The identification dialog intentionally allows the user to confirm only the
        // logical role.  Its wallpaper fields may be left blank when the role already
        // has a configured library asset; preserve that binding instead of turning a
        // confirmation into an accidental wallpaper reset.
        var existingRoles = profile.Roles
            .Where(x => !string.IsNullOrWhiteSpace(x.Role))
            .GroupBy(x => x.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
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
            existingRoles.TryGetValue(role, out var existing);
            var wallpaperPath = assignment.WallpaperPath;
            // An explicitly selected file is authoritative; only inherit the
            // previous asset when the dialog left the path blank.
            var wallpaperAssetId = string.IsNullOrWhiteSpace(wallpaperPath) ? existing?.WallpaperAssetId ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(wallpaperPath) && !string.IsNullOrWhiteSpace(wallpaperAssetId))
            {
                var asset = Library.Assets.FirstOrDefault(x => string.Equals(x.Id, wallpaperAssetId, StringComparison.OrdinalIgnoreCase));
                if (asset is not null)
                    wallpaperPath = asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase)
                        ? asset.ExternalPath ?? string.Empty
                        : Path.Combine(Paths.Root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            var displayName = role switch
            {
                "Laptop" => "笔记本本体",
                "Landscape" => "横屏1",
                "Portrait" => "竖屏1",
                _ => existing?.DisplayName ?? role
            };
            profile.Roles.Add(new MonitorRoleBinding
            {
                RoleId = existing?.RoleId ?? Guid.NewGuid().ToString("N"),
                Role = role, DisplayName = displayName, Fingerprint = monitor.Clone(),
                WallpaperAssetId = wallpaperAssetId, WallpaperPath = wallpaperPath,
                FitMode = existing?.FitMode ?? WallpaperFitMode.Fill,
                BackgroundColor = existing?.BackgroundColor ?? "#050B18",
                AllowAutoRebind = false, LastKnownMonitorDevicePath = monitor.MonitorDevicePath,
                Notes = "用户在 A/B/C 识别界面确认"
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

    /// <summary>
    /// Saves the currently connected display topology as a new wallpaper
    /// combination.  The monitor fingerprints are copied from the current
    /// native topology and the wallpaper bindings are copied from the current
    /// match/active profile.  A topology which is still ambiguous is refused
    /// rather than being silently assigned by geometry.
    /// </summary>
    public WallpaperProfile SaveCurrentWallpaperProfile(string? name)
    {
        if (Monitors.Count == 0) Monitors = _discovery.Discover();
        if (Monitors.Count == 0)
            throw new InvalidOperationException("当前没有活动显示器，无法保存壁纸组合。");
        if (LastMatch?.Status == MatchStatus.Ambiguous)
            throw new InvalidOperationException("当前显示器组合仍有歧义。请先完成 A / B / C 显示器识别，再保存壁纸组合。");

        var sourceProfile = LastMatch?.Profile
            ?? Profiles.Profiles.FirstOrDefault(x => string.Equals(x.Id, Settings.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
        var assignments = new List<(MonitorIdentity Monitor, string Role, MonitorRoleBinding? Source)>();
        var usedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var monitor in Monitors)
        {
            var role = FindMappedRole(LastMatch, monitor);
            if (string.IsNullOrWhiteSpace(role))
                role = monitor.IsInternal ? "Laptop" : monitor.Width >= monitor.Height ? "Landscape" : "Portrait";

            // Geometry is only a fallback for unique roles.  Two same-shaped
            // displays without an explicit role must be confirmed by the user.
            if (!usedRoles.Add(role))
                throw new InvalidOperationException("当前有多个显示器无法唯一分配逻辑角色。请先打开“显示器识别”手动确认后再保存。");
            var source = sourceProfile?.Roles.FirstOrDefault(x => x.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
            assignments.Add((monitor, role, source));
        }

        var now = DateTime.UtcNow;
        var profile = new WallpaperProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"{Monitors.Count}屏组合 {now.ToLocalTime():MM-dd HHmm}" : name.Trim(),
            Combination = Monitors.Count == 1 ? DisplayCombinationKind.LaptopOnly : Monitors.Count == 3 ? DisplayCombinationKind.ThreeMonitorSetup : DisplayCombinationKind.Custom,
            ExpectedMonitorCount = Monitors.Count,
            CreatedAt = now,
            ModifiedAt = now,
            AutoApply = true,
            AllowCompatibleMatch = true,
            MinimumConfidence = 80,
            Priority = Profiles.Profiles.Count == 0 ? 100 : Profiles.Profiles.Max(x => x.Priority) + 10
        };

        foreach (var assignment in assignments)
        {
            var source = assignment.Source;
            var asset = source is null || string.IsNullOrWhiteSpace(source.WallpaperAssetId)
                ? null
                : Library.Assets.FirstOrDefault(x => x.Id.Equals(source.WallpaperAssetId, StringComparison.OrdinalIgnoreCase));
            var wallpaperPath = asset is not null
                ? ManagedAssetPath(asset, Paths.Root)
                : source?.WallpaperPath ?? string.Empty;
            profile.Roles.Add(new MonitorRoleBinding
            {
                Role = assignment.Role,
                DisplayName = source?.DisplayName ?? RoleDisplayName(assignment.Role),
                Fingerprint = assignment.Monitor.Clone(),
                WallpaperAssetId = asset?.Id ?? source?.WallpaperAssetId ?? string.Empty,
                WallpaperPath = wallpaperPath,
                FitMode = source?.FitMode ?? WallpaperFitMode.Fill,
                BackgroundColor = source?.BackgroundColor ?? "#050B18",
                AllowAutoRebind = source?.AllowAutoRebind ?? false,
                LastKnownMonitorDevicePath = assignment.Monitor.MonitorDevicePath,
                Notes = "用户保存的显示器组合"
            });
        }

        Profiles.Profiles.Add(profile);
        Settings.ActiveProfileId = profile.Id;
        Store.Save("profiles.json", Profiles);
        Store.Save("settings.json", Settings);
        LastMessage = $"已保存壁纸组合“{profile.Name}”：{profile.Roles.Count} 台显示器";
        _log.Info("Config", LastMessage);
        RaiseChanged();
        return profile;
    }

    /// <summary>Applies one saved wallpaper combination to the current topology.</summary>
    public async Task<bool> ApplyWallpaperProfileAsync(string profileId)
    {
        if (Monitors.Count == 0) Monitors = _discovery.Discover();
        var profile = Profiles.Profiles.FirstOrDefault(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null) { LastMessage = "找不到所选壁纸组合"; RaiseChanged(); return false; }
        if (Monitors.Count == 0) { StatusText = "未发现显示器"; LastMessage = "没有活动显示器"; RaiseChanged(); return false; }

        // Selecting a saved profile is an explicit user choice.  Give it the
        // highest priority so an identical topology does not immediately
        // switch back to another saved wallpaper set.
        profile.Priority = Profiles.Profiles.Count == 0 ? 100 : Profiles.Profiles.Max(x => x.Priority) + 1;
        profile.ModifiedAt = DateTime.UtcNow;
        Settings.ActiveProfileId = profile.Id;
        Store.Save("profiles.json", Profiles);
        Store.Save("settings.json", Settings);

        LastMatch = _matcher.Match(Monitors, new[] { profile });
        _log.Write("Match", $"用户选择壁纸组合：{profile.Name}；{LastMatch.Message}", profile.Name, Monitors.Count, LastMatch.Confidence);
        if (LastMatch.Status == MatchStatus.Ambiguous || LastMatch.Status == MatchStatus.NoMatch || !LastMatch.CanAutoApply)
        {
            StatusText = LastMatch.Status == MatchStatus.Ambiguous ? "需要确认" : "保持当前壁纸";
            LastMessage = LastMatch.Message;
            RaiseChanged();
            return false;
        }

        try
        {
            var result = await RunOnDispatcherAsync(() => _apply.ApplyAsync(LastMatch, Library.Assets, Paths, generation: _coordinator.Generation, manual: true));
            LastMessage = result.Message;
            StatusText = result.Success ? "运行中" : "应用未完成";
            if (result.Success) RecordSuccessfulWallpaperMatch(LastMatch);
            RaiseChanged();
            return result.Success;
        }
        catch (Exception ex)
        {
            StatusText = "应用失败";
            LastMessage = ex.Message;
            _log.Write("Error", ex.ToString());
            RaiseChanged();
            return false;
        }
    }

    /// <summary>Renames a saved wallpaper combination without changing its bindings.</summary>
    public bool RenameWallpaperProfile(string profileId, string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("组合名称不能为空。", nameof(name));
        var profile = Profiles.Profiles.FirstOrDefault(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null) return false;
        if (profile.Name.Equals(trimmed, StringComparison.Ordinal)) return true;
        profile.Name = trimmed;
        profile.ModifiedAt = DateTime.UtcNow;
        Store.Save("profiles.json", Profiles);
        LastMessage = $"已重命名壁纸组合为“{profile.Name}”";
        _log.Info("Config", LastMessage);
        RaiseChanged();
        return true;
    }

    /// <summary>Deletes a saved wallpaper combination and repairs the active-profile pointer.</summary>
    public bool DeleteWallpaperProfile(string profileId)
    {
        var index = Profiles.Profiles.FindIndex(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        var removed = Profiles.Profiles[index];
        Profiles.Profiles.RemoveAt(index);
        if (string.Equals(Settings.ActiveProfileId, removed.Id, StringComparison.OrdinalIgnoreCase))
        {
            Settings.ActiveProfileId = Profiles.Profiles
                .Where(x => x.Enabled)
                .OrderByDescending(x => x.Priority)
                .ThenByDescending(x => x.ModifiedAt)
                .Select(x => (string?)x.Id)
                .FirstOrDefault();
            LastMatch = null;
        }
        Store.Save("profiles.json", Profiles);
        Store.Save("settings.json", Settings);
        LastMessage = $"已删除壁纸组合“{removed.Name}”";
        _log.Info("Config", LastMessage);
        RaiseChanged();
        return true;
    }

    private static string? FindMappedRole(MatchResult? match, MonitorIdentity monitor)
    {
        if (match is null) return null;
        return match.RoleMatches.FirstOrDefault(x =>
            (!string.IsNullOrWhiteSpace(monitor.MonitorDevicePath) && x.Value.MonitorDevicePath.Equals(monitor.MonitorDevicePath, StringComparison.OrdinalIgnoreCase))
            || (!string.IsNullOrWhiteSpace(monitor.StableId) && x.Value.StableId.Equals(monitor.StableId, StringComparison.OrdinalIgnoreCase))
            || (monitor.HasUsableSerial && x.Value.HasUsableSerial && x.Value.SerialKey.Equals(monitor.SerialKey, StringComparison.OrdinalIgnoreCase))).Key;
    }

    private static string RoleDisplayName(string role) => role switch
    {
        "Laptop" => "笔记本本体",
        "Landscape" => "横屏1",
        "Portrait" => "竖屏1",
        _ => role
    };

    private void RecordSuccessfulWallpaperMatch(MatchResult match)
    {
        LastAppliedAt = DateTime.Now;
        if (match.Profile is null) return;
        match.Profile.LastAppliedAt = DateTime.UtcNow;
        match.Profile.LastSuccessfulMatchAt = DateTime.UtcNow;
        foreach (var role in match.Profile.Roles)
        {
            if (!match.RoleMatches.TryGetValue(role.Role, out var monitor)) continue;
            role.LastSuccessfulMatchAt = DateTime.UtcNow;
            role.LastKnownMonitorDevicePath = monitor.MonitorDevicePath;
        }
        Store.Save("profiles.json", Profiles);
    }

    public WallpaperLibraryRefreshResult RefreshLibrary()
    {
        try
        {
            var result = _library.Refresh();
            Library = result.Document;
            LastMessage = result.MissingCount == 0 && result.RecoveredCount == 0
                ? $"档案库已刷新：{Library.Assets.Count(x => !x.IsMissing)} 个壁纸可用"
                : $"档案库已刷新：隐藏 {result.MissingCount} 个不存在文件，恢复 {result.RecoveredCount} 个文件";
            _log.Write("Library", LastMessage);
            RaiseChanged();
            return result;
        }
        catch (Exception ex)
        {
            _log.Write("Error", "刷新档案库失败：" + ex.Message);
            return new WallpaperLibraryRefreshResult(Library, 0, 0);
        }
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

    private static string ResolveStorageRoot()
    {
        var configured = Environment.GetEnvironmentVariable("SYNCWALLPAPER_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && CanWriteToDirectory(configured))
            return Path.GetFullPath(configured);

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var isProjectRoot = File.Exists(Path.Combine(directory.FullName, "SyncWallpaper.sln"));
            var isPackageRoot = File.Exists(Path.Combine(directory.FullName, "package-manifest.json"));
            if ((isProjectRoot || isPackageRoot) && CanWriteToDirectory(directory.FullName))
                return directory.FullName;
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncWallpaper");
    }

    private static bool CanWriteToDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probe = Path.Combine(path, ".syncwallpaper-write-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static void MigrateLegacyStorage(string targetRoot)
    {
        var legacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SyncWallpaper");
        if (string.Equals(Path.GetFullPath(legacyRoot), Path.GetFullPath(targetRoot), StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(legacyRoot)) return;

        foreach (var sourceDirectory in Directory.EnumerateDirectories(legacyRoot))
        {
            var destinationDirectory = Path.Combine(targetRoot, Path.GetFileName(sourceDirectory));
            CopyMissingFiles(sourceDirectory, destinationDirectory);
        }
    }

    private static void CopyMissingFiles(string sourceDirectory, string destinationDirectory)
    {
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var relative = Path.GetRelativePath(sourceDirectory, sourceFile);
                    var destinationFile = Path.Combine(destinationDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                    if (!File.Exists(destinationFile)) File.Copy(sourceFile, destinationFile);
                }
                catch { /* A locked log/cache file must not block startup migration. */ }
            }
        }
        catch { /* The normal local-app-data fallback remains available. */ }
    }

    private static bool RestoreMissingAssetsFromSourceFolders(LibraryDocument library, string root)
    {
        var folders = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "wallpaper"),
            Path.Combine(root, "wallpaper")
        };
        var changed = false;
        foreach (var asset in library.Assets)
        {
            var destination = ManagedAssetPath(asset, root);
            if (string.IsNullOrWhiteSpace(destination) || File.Exists(destination)) continue;
            foreach (var folder in folders)
            {
                var source = Path.Combine(folder, asset.OriginalFileName);
                if (!File.Exists(source)) continue;
                try
                {
                    if (!string.Equals(FileUtilities.Sha256(source), asset.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, true);
                    changed = true;
                    break;
                }
                catch { }
            }
        }
        return changed;
    }

    private static bool NormalizeWallpaperBindings(ProfilesDocument profiles, LibraryDocument library, string root)
    {
        var changed = false;
        foreach (var role in profiles.Profiles.SelectMany(x => x.Roles))
        {
            var asset = library.Assets.FirstOrDefault(x => !string.IsNullOrWhiteSpace(role.WallpaperAssetId)
                && x.Id.Equals(role.WallpaperAssetId, StringComparison.OrdinalIgnoreCase));
            var originalName = asset?.OriginalFileName ?? Path.GetFileName(role.WallpaperPath);
            var assetPath = asset is null ? string.Empty : ManagedAssetPath(asset, root);
            if (string.IsNullOrWhiteSpace(originalName)) continue;

            if (asset is null || !File.Exists(assetPath))
            {
                var replacement = library.Assets
                    .Where(x => string.Equals(x.OriginalFileName, originalName, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(ManagedAssetPath(x, root)))
                    .OrderByDescending(x => x.ImportedAt)
                    .FirstOrDefault();
                if (replacement is not null && !replacement.Id.Equals(role.WallpaperAssetId, StringComparison.OrdinalIgnoreCase))
                {
                    asset = replacement;
                    role.WallpaperAssetId = replacement.Id;
                    changed = true;
                }
            }

            if (asset is not null)
            {
                var expectedPath = ManagedAssetPath(asset, root);
                if (!string.Equals(role.WallpaperPath, expectedPath, StringComparison.OrdinalIgnoreCase))
                {
                    role.WallpaperPath = expectedPath;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private static string ManagedAssetPath(WallpaperAsset asset, string root)
        => asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase)
            ? asset.ExternalPath ?? string.Empty
            : Path.Combine(root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        _updateLifetime.Cancel();
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
        _updateHttpClient.Dispose();
        _updateGate.Dispose();
        _updateLifetime.Dispose();
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
