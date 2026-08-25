using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using SyncWallpaper.Core;
using SyncWallpaper.Update.Core;
using SyncWallpaper.Windows;

namespace SyncWallpaper.App;

/// <summary>
/// The intentionally small application host. It owns only wallpaper data,
/// monitor discovery, topology coordination, logging, startup and update checks.
/// </summary>
public sealed class AppRuntime : IDisposable
{
    public readonly DataPaths Paths;
    public readonly ConfigurationStore Store;
    public readonly AppSettings Settings;

    public ProfilesDocument Profiles { get; private set; }
    public LibraryDocument Library { get; private set; }
    public IReadOnlyList<MonitorIdentity> Monitors { get; private set; } = Array.Empty<MonitorIdentity>();
    public MatchResult? LastMatch { get; private set; }
    public WallpaperTransactionStatus LastWallpaperTransaction => _apply.LastTransaction;
    public UpdateCheckResult? LastUpdateResult { get; private set; }
    public string CurrentVersion => CurrentVersionProvider.GetInformationalVersion(typeof(App).Assembly);
    public string StatusText { get; private set; } = "正在启动";
    public string LastMessage { get; private set; } = "等待显示器检测";
    public DateTime? LastAppliedAt { get; private set; }
    public bool IsRecognizing { get; private set; }
    public string StartupText => _startup.IsEnabled ? "已启用" : "未启用";
    public IReadOnlyList<DiagnosticEvent> RecentLogs => _log.Recent;
    public TrayIconState TrayVisualState => IsRecognizing
        ? TrayIconState.Recognizing
        : !Settings.AutoMatchEnabled || StatusText is "已暂停" or "安全模式"
            || StatusText.StartsWith("验证模式", StringComparison.Ordinal)
            ? TrayIconState.Paused
            : StatusText is "需要确认" or "保持当前壁纸" or "应用未完成" or "应用失败" or "未发现显示器"
                ? TrayIconState.Error
                : TrayIconState.Normal;

    public event EventHandler? StateChanged;

    private readonly MonitorDiscoveryService _discovery = new();
    private readonly ProfileMatcher _matcher = new();
    private readonly LogService _log;
    private readonly WallpaperLibraryService _library;
    private readonly WallpaperRenderService _wallpaperRenderer;
    private readonly WallpaperApplyService _apply;
    private readonly DisplayChangeCoordinator _coordinator;
    private readonly ExplorerRecoveryCoordinator _explorerRecovery;
    private readonly SafeModePolicy _safeMode = new();
    private readonly StartupService _startup = new();
    private readonly HttpClient _updateHttpClient;
    private readonly GitHubReleaseChecker _releaseChecker;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly CancellationTokenSource _updateLifetime = new();
    private bool _sessionAutoMatchSuppressed;
    private bool _disposed;

    public AppRuntime()
    {
        var dataRoot = ResolveStorageRoot();
        MigrateLegacyStorage(dataRoot);
        Paths = new DataPaths(dataRoot);
        Store = new ConfigurationStore(Paths);

        Settings = Store.Load("settings.json", new AppSettings());
        var settingsChanged = AppSettingsMigrator.Migrate(Settings);
        var startupActuallyEnabled = _startup.IsEnabled;
        if (Settings.StartWithWindows != startupActuallyEnabled)
        {
            Settings.StartWithWindows = startupActuallyEnabled;
            settingsChanged = true;
        }

        var loadedProfiles = Store.Load("profiles.json", new ProfilesDocument());
        var loadedProfileSchema = loadedProfiles.SchemaVersion;
        Profiles = ProfileSchemaMigrator.Migrate(loadedProfiles);
        Library = Store.Load("library.json", new LibraryDocument());
        var libraryChanged = RestoreMissingAssetsFromSourceFolders(Library, Paths.Root);
        var profilesChanged = NormalizeWallpaperBindings(Profiles, Library, Paths.Root);
        if (!string.Equals(Settings.DataRoot, Paths.Root, StringComparison.OrdinalIgnoreCase))
        {
            Settings.DataRoot = Paths.Root;
            settingsChanged = true;
        }

        if (libraryChanged) Store.Save("library.json", Library);
        if (profilesChanged || loadedProfileSchema < ProfileSchemaMigrator.CurrentSchemaVersion)
            Store.Save("profiles.json", Profiles);
        if (settingsChanged) Store.Save("settings.json", Settings);

        _log = new LogService(Paths);
        RemoveRetiredFeatureData();
        if (loadedProfileSchema < ProfileSchemaMigrator.CurrentSchemaVersion)
            _log.Info("Config", $"profiles.json 已迁移到 SchemaVersion={Profiles.SchemaVersion}");

        _library = new WallpaperLibraryService(Store);
        Library = _library.Refresh().Document;
        _wallpaperRenderer = new WallpaperRenderService(Paths);
        _wallpaperRenderer.ConfigureCacheLimit(Settings.LowPerformanceMode ? 128L * 1024 * 1024 : 512L * 1024 * 1024);
        _apply = new WallpaperApplyService(_wallpaperRenderer, message => _log.Write("Wallpaper", message));
        _explorerRecovery = new ExplorerRecoveryCoordinator(
            async token =>
            {
                if (!token.IsCancellationRequested) await MatchAndApplyAsync().ConfigureAwait(false);
            },
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
                TrySaveSettings();
            }
            RaiseChanged();
        };

        _coordinator = new DisplayChangeCoordinator(_discovery, OnStableDisplaysAsync, value =>
        {
            _log.Info("SystemEvent", value);
            _explorerRecovery.NotifyShellEvent(value);
        });

        _updateHttpClient = new HttpClient();
        _releaseChecker = new GitHubReleaseChecker(
            _updateHttpClient,
            ProjectLinks.RepositorySettings,
            CurrentVersion,
            TimeSpan.FromSeconds(15));

        if (Settings.SafeMode)
        {
            Settings.AutoMatchEnabled = false;
            StatusText = "安全模式";
            LastMessage = string.IsNullOrWhiteSpace(Settings.SafeModeReason) ? "上次壁纸回滚失败，自动匹配已暂停" : Settings.SafeModeReason;
        }
    }

    public void Start(bool suppressAutoMatch = false)
    {
        _sessionAutoMatchSuppressed = suppressAutoMatch;
        Paths.Ensure();
        SeedLocalLibraryAndProfiles();
        StatusText = suppressAutoMatch ? "验证模式（不自动应用壁纸）" : Settings.AutoMatchEnabled ? "监测中" : "已暂停";
        // Bypass the debounce at sign-in so an already-connected saved
        // topology begins its wallpaper transaction without another event.
        _coordinator.Signal(manual: true);
        if (Settings.AutomaticUpdateCheckEnabled && ProjectLinks.IsConfigured)
            _ = ScheduleAutomaticUpdateCheckAsync();
        RaiseChanged();
    }

    public void SetAutoMatch(bool enabled)
    {
        Settings.AutoMatchEnabled = enabled;
        if (enabled)
        {
            Settings.SafeMode = false;
            Settings.SafeModeReason = string.Empty;
            StatusText = "监测中";
            LastMessage = "自动匹配已启用，正在重新检测显示器";
            _coordinator.Signal(manual: true);
        }
        else
        {
            StatusText = "已暂停";
            LastMessage = "自动匹配已暂停；现有壁纸保持不变";
        }
        TrySaveSettings();
        RaiseChanged();
    }

    public void SetLowPerformanceMode(bool enabled)
    {
        Settings.LowPerformanceMode = enabled;
        _wallpaperRenderer.ConfigureCacheLimit(enabled ? 128L * 1024 * 1024 : 512L * 1024 * 1024);
        TrySaveSettings();
        RaiseChanged();
    }

    public void SetStartup(bool enabled)
    {
        var executable = Environment.ProcessPath ?? string.Empty;
        _startup.SetEnabled(enabled, executable);
        Settings.StartWithWindows = enabled;
        TrySaveSettings();
        RaiseChanged();
    }

    public Task DetectAsync()
    {
        _coordinator.Signal(manual: true);
        return Task.CompletedTask;
    }

    public async Task ReapplyAsync()
    {
        if (Monitors.Count == 0) Monitors = _discovery.Discover();
        WallpaperProfile? preferredProfile = null;
        if (LastMatch is { Profile: not null, CanAutoApply: true } previous
            && previous.Status is MatchStatus.Exact or MatchStatus.Compatible)
            preferredProfile = previous.Profile;
        await MatchAndApplyAsync(manual: true, preferredProfile).ConfigureAwait(false);
    }

    private async Task OnStableDisplaysAsync(DisplaySnapshot snapshot)
    {
        IsRecognizing = true;
        RaiseChanged();
        try
        {
            Monitors = snapshot.Monitors;
            if (!string.IsNullOrWhiteSpace(_discovery.LastError))
                _log.Warn("Display", "原生显示路径读取失败，临时使用兼容数据：" + _discovery.LastError);
            _log.Info("Display", $"显示器组合已稳定：{Monitors.Count} 台");
            foreach (var monitor in Monitors)
                _log.Info("Display", $"{monitor.DisplayLabel} {monitor.Width}x{monitor.Height} @ {monitor.DesktopX},{monitor.DesktopY}；{monitor.StableIdSource}");

            if (_sessionAutoMatchSuppressed || !Settings.AutoMatchEnabled)
            {
                StatusText = _sessionAutoMatchSuppressed ? "验证模式" : "已暂停";
                LastMessage = _sessionAutoMatchSuppressed ? "验证模式不会自动应用壁纸" : "自动匹配已暂停";
                RaiseChanged();
                return;
            }
            await MatchAndApplyAsync().ConfigureAwait(false);
        }
        finally
        {
            IsRecognizing = false;
            RaiseChanged();
        }
    }

    private async Task MatchAndApplyAsync(bool manual = false, WallpaperProfile? preferredProfile = null)
    {
        if (Monitors.Count == 0)
        {
            StatusText = "未发现显示器";
            LastMessage = "没有活动显示路径";
            RaiseChanged();
            return;
        }
        var match = preferredProfile is null
            ? _matcher.Match(Monitors, Profiles.Profiles)
            : _matcher.Match(Monitors, new[] { preferredProfile });
        _log.Write("Match", match.Message, match.Profile?.Name, Monitors.Count, match.Confidence);
        await ApplyMatchedWallpaperAsync(match, manual).ConfigureAwait(false);
    }

    private async Task<bool> ApplyMatchedWallpaperAsync(MatchResult match, bool manual)
    {
        LastMatch = match;
        if (match.Status == MatchStatus.Ambiguous)
        {
            StatusText = "需要确认";
            LastMessage = match.Message;
            RaiseChanged();
            return false;
        }
        if (match.Status == MatchStatus.NoMatch)
        {
            StatusText = "保持当前壁纸";
            LastMessage = match.Message;
            RaiseChanged();
            return false;
        }
        if (!match.CanAutoApply)
        {
            StatusText = "需要确认";
            LastMessage = string.IsNullOrWhiteSpace(match.Message) ? "匹配依据不足，未猜测显示器身份" : match.Message;
            RaiseChanged();
            return false;
        }
        if (!WallpaperProfileApplyPolicy.IsComplete(match.Profile))
        {
            StatusText = "保持当前壁纸";
            LastMessage = $"已匹配“{match.Profile?.Name}”，但显示器或壁纸配置尚未完整";
            RaiseChanged();
            return false;
        }
        try
        {
            var result = await RunOnDispatcherAsync(() => _apply.ApplyAsync(
                match, Library.Assets, Paths, generation: _coordinator.Generation, manual: manual)).ConfigureAwait(false);
            LastMessage = result.Message;
            StatusText = result.Success ? "运行中" : "应用未完成";
            if (result.Success) RecordSuccessfulWallpaperMatch(match);
            RaiseChanged();
            return result.Success;
        }
        catch (Exception ex)
        {
            StatusText = "应用失败";
            LastMessage = ex.Message;
            _log.Error("Wallpaper", ex.ToString());
            RaiseChanged();
            return false;
        }
    }

    public void ApplyManualDisplayAssignments(IReadOnlyList<ManualDisplayAssignment> assignments)
    {
        if (assignments.Count == 0) return;
        var profile = Profiles.Profiles.FirstOrDefault(x => string.Equals(x.Id, Settings.EditingProfileId, StringComparison.OrdinalIgnoreCase))
            ?? ProfileTemplates.Custom("手动确认配置", assignments.Select(x => x.Role));
        if (!Profiles.Profiles.Contains(profile)) Profiles.Profiles.Add(profile);

        var existingRoles = profile.Roles
            .Where(x => !string.IsNullOrWhiteSpace(x.Role))
            .GroupBy(x => x.Role, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        profile.Roles.Clear();
        profile.ExpectedMonitorCount = assignments.Count;
        profile.Combination = assignments.Count == 1
            ? DisplayCombinationKind.LaptopOnly
            : assignments.Count == 3 ? DisplayCombinationKind.ThreeMonitorSetup : DisplayCombinationKind.Custom;
        profile.ModifiedAt = DateTime.UtcNow;

        foreach (var assignment in assignments)
        {
            var monitor = Monitors.FirstOrDefault(x =>
                (!string.IsNullOrWhiteSpace(assignment.StableId) && x.StableId.Equals(assignment.StableId, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(assignment.MonitorDevicePath) && x.MonitorDevicePath.Equals(assignment.MonitorDevicePath, StringComparison.OrdinalIgnoreCase)));
            if (monitor is null) continue;
            var role = string.Equals(assignment.Role, "Custom", StringComparison.OrdinalIgnoreCase)
                ? $"Custom-{monitor.TargetId}"
                : assignment.Role;
            existingRoles.TryGetValue(role, out var existing);
            var wallpaperPath = assignment.WallpaperPath;
            var wallpaperAssetId = string.IsNullOrWhiteSpace(wallpaperPath) ? existing?.WallpaperAssetId ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(wallpaperPath) && !string.IsNullOrWhiteSpace(wallpaperAssetId))
            {
                var asset = Library.Assets.FirstOrDefault(x => x.Id.Equals(wallpaperAssetId, StringComparison.OrdinalIgnoreCase));
                if (asset is not null) wallpaperPath = ManagedAssetPath(asset, Paths.Root);
            }
            profile.Roles.Add(new MonitorRoleBinding
            {
                RoleId = existing?.RoleId ?? Guid.NewGuid().ToString("N"),
                Role = role,
                DisplayName = existing?.DisplayName ?? RoleDisplayName(role),
                Fingerprint = monitor.Clone(),
                WallpaperAssetId = wallpaperAssetId,
                WallpaperPath = wallpaperPath,
                FitMode = existing?.FitMode ?? WallpaperFitMode.Fill,
                BackgroundColor = existing?.BackgroundColor ?? "#050B18",
                LastKnownMonitorDevicePath = monitor.MonitorDevicePath,
                Notes = "用户在 A/B/C 识别界面确认"
            });
        }

        profile.AutoApply = WallpaperProfileApplyPolicy.IsComplete(profile);
        Settings.EditingProfileId = profile.Id;
        Store.Save("profiles.json", Profiles);
        TrySaveSettings();
        LastMessage = $"已保存 {profile.Roles.Count} 个显示器角色绑定";
        _log.Info("Config", LastMessage);
        RaiseChanged();
    }

    public void ImportWallpaper(string path)
    {
        var asset = _library.Import(path);
        Library = _library.Load();
        LastMessage = $"已导入壁纸：{asset.DisplayName}";
        _log.Info("Library", LastMessage);
        RaiseChanged();
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
            _log.Info("Library", LastMessage);
            RaiseChanged();
            return result;
        }
        catch (Exception ex)
        {
            LastMessage = "刷新档案库失败：" + ex.Message;
            _log.Error("Library", LastMessage);
            RaiseChanged();
            return new WallpaperLibraryRefreshResult(Library, 0, 0);
        }
    }

    public WallpaperProfile SaveCurrentWallpaperProfile(string? name)
    {
        if (Monitors.Count == 0) Monitors = _discovery.Discover();
        if (Monitors.Count == 0) throw new InvalidOperationException("当前没有活动显示器，无法保存壁纸组合。");
        if (LastMatch?.Status == MatchStatus.Ambiguous)
            throw new InvalidOperationException("当前显示器组合仍有歧义。请先完成 A/B/C 识别。");

        var sourceProfile = LastMatch?.Profile
            ?? Profiles.Profiles.FirstOrDefault(x => string.Equals(x.Id, Settings.EditingProfileId, StringComparison.OrdinalIgnoreCase));
        var assignments = new List<(MonitorIdentity Monitor, string Role, MonitorRoleBinding? Source)>();
        var usedFallbackRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in Monitors)
        {
            var mappedBinding = FindMappedBinding(LastMatch, monitor);
            var role = mappedBinding?.Role;
            if (string.IsNullOrWhiteSpace(role))
                role = monitor.IsInternal ? "Laptop" : monitor.Width >= monitor.Height ? "Landscape" : "Portrait";
            if (mappedBinding is null && !usedFallbackRoles.Add(role))
                throw new InvalidOperationException("多个显示器无法唯一分配逻辑角色。请先完成 A/B/C 识别。");
            var source = mappedBinding ?? sourceProfile?.Roles.FirstOrDefault(x => x.Role.Equals(role, StringComparison.OrdinalIgnoreCase));
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
            profile.Roles.Add(new MonitorRoleBinding
            {
                Role = assignment.Role,
                DisplayName = source?.DisplayName ?? RoleDisplayName(assignment.Role),
                Fingerprint = assignment.Monitor.Clone(),
                WallpaperAssetId = asset?.Id ?? source?.WallpaperAssetId ?? string.Empty,
                WallpaperPath = asset is not null ? ManagedAssetPath(asset, Paths.Root) : source?.WallpaperPath ?? string.Empty,
                FitMode = source?.FitMode ?? WallpaperFitMode.Fill,
                BackgroundColor = source?.BackgroundColor ?? "#050B18",
                LastKnownMonitorDevicePath = assignment.Monitor.MonitorDevicePath,
                Notes = "用户保存的显示器组合"
            });
        }

        profile.AutoApply = WallpaperProfileApplyPolicy.IsComplete(profile);
        Profiles.Profiles.Add(profile);
        Settings.EditingProfileId = profile.Id;
        Store.Save("profiles.json", Profiles);
        TrySaveSettings();
        LastMessage = $"已保存壁纸组合“{profile.Name}”：{profile.Roles.Count} 台显示器";
        _log.Info("Config", LastMessage);
        RaiseChanged();
        return profile;
    }

    public WallpaperProfile CreateBlankWallpaperProfile(string? name)
    {
        var priority = Profiles.Profiles.Count == 0 ? 100 : Profiles.Profiles.Max(x => x.Priority) + 10;
        var profile = WallpaperProfileEditingService.CreateBlank(name, priority);
        Profiles.Profiles.Add(profile);
        Settings.EditingProfileId = profile.Id;
        Store.Save("profiles.json", Profiles);
        TrySaveSettings();
        LastMessage = $"已新建空白组合“{profile.Name}”";
        _log.Info("Config", LastMessage);
        RaiseChanged();
        return profile;
    }

    public WallpaperProfile? FindWallpaperProfile(string profileId)
        => Profiles.Profiles.FirstOrDefault(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));

    public void SelectWallpaperProfileForEditing(string? profileId)
    {
        Settings.EditingProfileId = string.IsNullOrWhiteSpace(profileId) ? null : profileId;
        TrySaveSettings();
    }

    public void UpdateWallpaperProfile(string profileId, WallpaperProfileEditDraft draft)
    {
        var profile = FindWallpaperProfile(profileId) ?? throw new InvalidOperationException("找不到要编辑的壁纸组合。");
        foreach (var role in draft.Roles)
        {
            if (string.IsNullOrWhiteSpace(role.WallpaperAssetId))
            {
                role.WallpaperPath = string.Empty;
                continue;
            }
            var asset = Library.Assets.FirstOrDefault(x => x.Id.Equals(role.WallpaperAssetId, StringComparison.OrdinalIgnoreCase) && !x.IsMissing)
                ?? throw new InvalidOperationException($"逻辑角色“{role.Role}”选择的壁纸不存在，请刷新档案库后重新选择。");
            var path = ManagedAssetPath(asset, Paths.Root);
            if (!File.Exists(path)) throw new InvalidOperationException($"壁纸“{asset.DisplayName}”的文件不存在。");
            role.WallpaperPath = path;
        }

        var wasMatched = LastMatch?.Profile?.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase) == true;
        WallpaperProfileEditingService.Apply(profile, draft);
        Settings.EditingProfileId = profile.Id;
        if (wasMatched) LastMatch = null;
        Store.Save("profiles.json", Profiles);
        TrySaveSettings();
        LastMessage = profile.Roles.Count == 0
            ? $"已保存空白组合“{profile.Name}”"
            : $"已更新壁纸组合“{profile.Name}”：{profile.Roles.Count} 个逻辑角色";
        _log.Info("Config", LastMessage);
        RaiseChanged();
    }

    public async Task<bool> ApplyWallpaperProfileAsync(string profileId)
    {
        if (Monitors.Count == 0) Monitors = _discovery.Discover();
        var profile = FindWallpaperProfile(profileId);
        if (profile is null)
        {
            LastMessage = "找不到所选壁纸组合";
            RaiseChanged();
            return false;
        }
        if (Monitors.Count == 0)
        {
            StatusText = "未发现显示器";
            LastMessage = "没有活动显示器";
            RaiseChanged();
            return false;
        }

        var match = _matcher.Match(Monitors, new[] { profile });
        _log.Write("Match", $"用户选择壁纸组合：{profile.Name}；{match.Message}", profile.Name, Monitors.Count, match.Confidence);
        return await ApplyMatchedWallpaperAsync(match, manual: true).ConfigureAwait(false);
    }

    public bool RenameWallpaperProfile(string profileId, string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) throw new ArgumentException("组合名称不能为空。", nameof(name));
        var profile = FindWallpaperProfile(profileId);
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

    public bool DeleteWallpaperProfile(string profileId)
    {
        var index = Profiles.Profiles.FindIndex(x => x.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) return false;
        var removed = Profiles.Profiles[index];
        Profiles.Profiles.RemoveAt(index);
        if (LastMatch?.Profile?.Id.Equals(removed.Id, StringComparison.OrdinalIgnoreCase) == true) LastMatch = null;
        if (string.Equals(Settings.EditingProfileId, removed.Id, StringComparison.OrdinalIgnoreCase))
            Settings.EditingProfileId = Profiles.Profiles.OrderByDescending(x => x.Priority).Select(x => (string?)x.Id).FirstOrDefault();
        if (string.Equals(Settings.LastMatchedProfileId, removed.Id, StringComparison.OrdinalIgnoreCase)) Settings.LastMatchedProfileId = null;
        Store.Save("profiles.json", Profiles);
        TrySaveSettings();
        LastMessage = $"已删除壁纸组合“{removed.Name}”";
        _log.Info("Config", LastMessage);
        RaiseChanged();
        return true;
    }

    public MonitorRoleBinding? GetBindingForMonitor(MonitorIdentity monitor)
        => FindMappedBinding(LastMatch, monitor);

    public WallpaperStateSnapshot CaptureWallpaperSnapshot() => new WallpaperSnapshotService().Capture();

    public string ExportWallpaperDiagnostic()
    {
        Paths.Ensure();
        var path = Path.Combine(Paths.Logs, $"wallpaper-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        var payload = new
        {
            schemaVersion = 1,
            capturedAtUtc = DateTime.UtcNow,
            appVersion = CurrentVersion,
            status = StatusText,
            message = LastMessage,
            monitors = Monitors.Select(MonitorIdentitySanitizer.Sanitize).ToArray(),
            match = LastMatch is null ? null : new
            {
                status = LastMatch.Status.ToString(),
                profile = LastMatch.Profile?.Name,
                LastMatch.Confidence,
                LastMatch.Message,
                evidence = LastMatch.Evidence
            },
            transaction = LastWallpaperTransaction,
            wallpaper = CaptureWallpaperSnapshot(),
            systemMutation = false
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        _log.Info("Diagnostic", "已导出只读壁纸诊断：" + Path.GetFileName(path));
        return path;
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool manual = true, CancellationToken cancellationToken = default)
    {
        var settings = new UpdateCheckSettings
        {
            AutomaticCheckEnabled = Settings.AutomaticUpdateCheckEnabled,
            Channel = ParseUpdateChannel(Settings.UpdateChannel),
            LastSuccessfulCheckUtc = Settings.LastUpdateSuccessfulCheckUtc,
            LastAttemptUtc = Settings.LastUpdateAttemptUtc
        };
        if (!manual && !UpdateCheckScheduler.ShouldRunAutomaticCheck(settings, DateTimeOffset.UtcNow))
            return LastUpdateResult ?? new UpdateCheckResult(UpdateCheckStatus.UpToDate, null, null, null, null, null, null, "自动更新检查尚未到期。", "七天检查间隔尚未到期。");

        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Settings.LastUpdateAttemptUtc = DateTimeOffset.UtcNow;
            TrySaveSettings();
            RaiseChanged();
            var result = await _releaseChecker.CheckAsync(settings.Channel, cancellationToken).ConfigureAwait(false);
            LastUpdateResult = result;
            UpdateCheckScheduler.RecordResult(settings, result, DateTimeOffset.UtcNow);
            Settings.LastUpdateAttemptUtc = settings.LastAttemptUtc;
            Settings.LastUpdateSuccessfulCheckUtc = settings.LastSuccessfulCheckUtc;
            TrySaveSettings();
            if (result.IsSuccess) _log.Info("Update", result.UserMessage ?? "GitHub Release 检查完成");
            else if (manual) _log.Warn("Update", result.TechnicalMessage ?? result.UserMessage ?? "GitHub Release 检查失败");
            RaiseChanged();
            return result;
        }
        catch (OperationCanceledException)
        {
            LastUpdateResult = new UpdateCheckResult(UpdateCheckStatus.Cancelled, null, null, null, null, null, null, "已取消更新检查。", "调用方取消了请求。");
            RaiseChanged();
            return LastUpdateResult;
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

    public void OpenFolder(string path)
    {
        Paths.Ensure();
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
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
        catch (Exception ex) { _log.Warn("Update", "自动更新检查失败：" + ex.Message); }
    }

    private void SeedLocalLibraryAndProfiles()
    {
        foreach (var name in new[] { "本体.jpg", "本体.png", "横屏1.jpg", "横屏1.png", "竖屏1.jpg", "竖屏1.png" })
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "wallpaper", name);
            if (!File.Exists(path) || Library.Assets.Any(a => a.OriginalFileName.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            try { _library.Import(path, Path.GetFileNameWithoutExtension(name)); }
            catch (Exception ex) { _log.Warn("Library", "导入初始壁纸失败：" + ex.Message); }
        }
        Library = _library.Load();

        var monitors = _discovery.Discover().ToList();
        if (monitors.Count == 0) return;
        if (Profiles.Profiles.Count > 0)
        {
            if (EnsureLaptopFallbackProfile(monitors)) Store.Save("profiles.json", Profiles);
            return;
        }

        var profile = new WallpaperProfile
        {
            Name = monitors.Count == 1 ? "Laptop Only" : $"{monitors.Count}屏组合",
            Combination = monitors.Count == 1 ? DisplayCombinationKind.LaptopOnly : monitors.Count == 3 ? DisplayCombinationKind.ThreeMonitorSetup : DisplayCombinationKind.Custom,
            ExpectedMonitorCount = monitors.Count,
            MinimumConfidence = 80,
            Priority = 100
        };
        foreach (var monitor in monitors)
        {
            var role = monitor.IsInternal ? ("Laptop", "笔记本本体", "本体")
                : monitor.Width >= monitor.Height ? ("Landscape", "横屏1", "横屏1") : ("Portrait", "竖屏1", "竖屏1");
            var asset = Library.Assets.FirstOrDefault(a => a.DisplayName.Equals(role.Item3, StringComparison.OrdinalIgnoreCase))
                ?? Library.Assets.FirstOrDefault(a => a.OriginalFileName.Contains(role.Item3, StringComparison.OrdinalIgnoreCase));
            profile.Roles.Add(new MonitorRoleBinding
            {
                Role = role.Item1,
                DisplayName = role.Item2,
                Fingerprint = monitor.Clone(),
                WallpaperAssetId = asset?.Id ?? string.Empty,
                WallpaperPath = asset is null ? string.Empty : ManagedAssetPath(asset, Paths.Root),
                FitMode = WallpaperFitMode.Fill,
                LastKnownMonitorDevicePath = monitor.MonitorDevicePath
            });
        }
        profile.AutoApply = WallpaperProfileApplyPolicy.IsComplete(profile);
        Profiles.Profiles.Add(profile);
        EnsureLaptopFallbackProfile(monitors);
        Settings.EditingProfileId = profile.Id;
        Store.Save("profiles.json", Profiles);
        TrySaveSettings();
    }

    private bool EnsureLaptopFallbackProfile(IReadOnlyList<MonitorIdentity> monitors)
    {
        if (Profiles.Profiles.Any(p => p.Enabled && p.ExpectedMonitorCount == 1 && p.Roles.Count == 1 && p.Roles[0].Fingerprint.IsInternal)) return false;
        var laptopRole = Profiles.Profiles.SelectMany(p => p.Roles).FirstOrDefault(r => r.Fingerprint.IsInternal || r.Role.Equals("Laptop", StringComparison.OrdinalIgnoreCase));
        var laptop = monitors.FirstOrDefault(m => m.IsInternal);
        if (laptopRole is null || laptop is null) return false;
        Profiles.Profiles.Add(new WallpaperProfile
        {
            Name = "Laptop Only",
            Combination = DisplayCombinationKind.LaptopOnly,
            ExpectedMonitorCount = 1,
            AllowCompatibleMatch = true,
            MinimumConfidence = 80,
            Priority = Profiles.Profiles.Max(p => p.Priority) + 100,
            AutoApply = !string.IsNullOrWhiteSpace(laptopRole.WallpaperAssetId) || !string.IsNullOrWhiteSpace(laptopRole.WallpaperPath),
            Roles = new()
            {
                new MonitorRoleBinding
                {
                    Role = "Laptop",
                    DisplayName = "笔记本本体",
                    Fingerprint = laptopRole.Fingerprint.StableId.Length > 0 ? laptopRole.Fingerprint.Clone() : laptop.Clone(),
                    WallpaperAssetId = laptopRole.WallpaperAssetId,
                    WallpaperPath = laptopRole.WallpaperPath,
                    FitMode = laptopRole.FitMode,
                    BackgroundColor = laptopRole.BackgroundColor,
                    LastKnownMonitorDevicePath = laptop.MonitorDevicePath,
                    Notes = "从多屏组合生成的单屏回退"
                }
            }
        });
        return true;
    }

    private void RecordSuccessfulWallpaperMatch(MatchResult match)
    {
        LastAppliedAt = DateTime.Now;
        if (match.Profile is null) return;
        Settings.LastMatchedProfileId = match.Profile.Id;
        match.Profile.LastAppliedAt = DateTime.UtcNow;
        match.Profile.LastSuccessfulMatchAt = DateTime.UtcNow;
        foreach (var role in match.Profile.Roles)
        {
            if (!match.TryGetMonitor(role, out var monitor)) continue;
            role.LastSuccessfulMatchAt = DateTime.UtcNow;
            role.LastKnownMonitorDevicePath = monitor.MonitorDevicePath;
        }
        Store.Save("profiles.json", Profiles);
        TrySaveSettings();
    }

    private static MonitorRoleBinding? FindMappedBinding(MatchResult? match, MonitorIdentity monitor)
    {
        if (match?.Profile is null) return null;
        foreach (var binding in match.Profile.Roles)
        {
            if (!match.TryGetMonitor(binding, out var mapped)) continue;
            if ((!string.IsNullOrWhiteSpace(monitor.MonitorDevicePath) && mapped.MonitorDevicePath.Equals(monitor.MonitorDevicePath, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(monitor.StableId) && mapped.StableId.Equals(monitor.StableId, StringComparison.OrdinalIgnoreCase))
                || (monitor.HasUsableSerial && mapped.HasUsableSerial && mapped.SerialKey.Equals(monitor.SerialKey, StringComparison.OrdinalIgnoreCase)))
                return binding;
        }
        return null;
    }

    private static string RoleDisplayName(string role) => role switch
    {
        "Laptop" => "笔记本本体",
        "Landscape" => "横屏",
        "Portrait" => "竖屏",
        _ => role
    };

    private static async Task<T> RunOnDispatcherAsync<T>(Func<Task<T>> action)
    {
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true) return await action();
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        return dispatcher is null ? await action() : await (await dispatcher.InvokeAsync(action));
    }

    private static UpdateChannel ParseUpdateChannel(string? value)
        => string.Equals(value, nameof(UpdateChannel.Beta), StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Beta : UpdateChannel.Stable;

    private void TrySaveSettings()
    {
        try { Store.Save("settings.json", Settings); }
        catch (Exception ex) { _log.Warn("Config", "保存设置失败：" + ex.Message); }
    }

    private void RemoveRetiredFeatureData()
    {
        var retired = new[]
        {
            "audio-profiles.json", "desktop-icons.json", "display-configurations.json", "monitor-profiles.json",
            "module-performance.json", "module-runtime.json", "taskbar-host.json", "triggers.json",
            "window-profiles.json", "window-zones.json"
        };
        foreach (var fileName in retired)
        {
            DeleteIfPresent(Path.Combine(Paths.Config, fileName));
        }

        void DeleteIfPresent(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.Delete(path);
                _log.Info("Migration", "已移除停用功能数据：" + Path.GetFileName(path));
            }
            catch (Exception ex) { _log.Warn("Migration", $"无法移除 {Path.GetFileName(path)}：{ex.Message}"); }
        }
    }

    private static string ResolveStorageRoot()
    {
        var configured = Environment.GetEnvironmentVariable("SYNCWALLPAPER_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && CanWriteToDirectory(configured)) return Path.GetFullPath(configured);
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var isProjectRoot = File.Exists(Path.Combine(directory.FullName, "SyncWallpaper.sln"));
            var isPackageRoot = File.Exists(Path.Combine(directory.FullName, "package-manifest.json"));
            if ((isProjectRoot || isPackageRoot) && CanWriteToDirectory(directory.FullName)) return directory.FullName;
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
        if (string.Equals(Path.GetFullPath(legacyRoot), Path.GetFullPath(targetRoot), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(legacyRoot)) return;
        foreach (var sourceDirectory in Directory.EnumerateDirectories(legacyRoot))
            CopyMissingFiles(sourceDirectory, Path.Combine(targetRoot, Path.GetFileName(sourceDirectory)));
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
                    var destinationFile = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, sourceFile));
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
                    if (!File.Exists(destinationFile)) File.Copy(sourceFile, destinationFile);
                }
                catch { }
            }
        }
        catch { }
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
                    if (!FileUtilities.Sha256(source).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
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
            var asset = library.Assets.FirstOrDefault(x => !string.IsNullOrWhiteSpace(role.WallpaperAssetId) && x.Id.Equals(role.WallpaperAssetId, StringComparison.OrdinalIgnoreCase));
            var originalName = asset?.OriginalFileName ?? Path.GetFileName(role.WallpaperPath);
            if (string.IsNullOrWhiteSpace(originalName)) continue;
            if (asset is null || !File.Exists(ManagedAssetPath(asset, root)))
            {
                asset = library.Assets.Where(x => x.OriginalFileName.Equals(originalName, StringComparison.OrdinalIgnoreCase) && File.Exists(ManagedAssetPath(x, root)))
                    .OrderByDescending(x => x.ImportedAt).FirstOrDefault();
                if (asset is not null && !asset.Id.Equals(role.WallpaperAssetId, StringComparison.OrdinalIgnoreCase))
                {
                    role.WallpaperAssetId = asset.Id;
                    changed = true;
                }
            }
            if (asset is null) continue;
            var expectedPath = ManagedAssetPath(asset, root);
            if (role.WallpaperPath.Equals(expectedPath, StringComparison.OrdinalIgnoreCase)) continue;
            role.WallpaperPath = expectedPath;
            changed = true;
        }
        return changed;
    }

    private static string ManagedAssetPath(WallpaperAsset asset, string root)
        => asset.StorageMode.Equals("External", StringComparison.OrdinalIgnoreCase)
            ? asset.ExternalPath ?? string.Empty
            : Path.Combine(root, asset.ManagedRelativePath.Replace('/', Path.DirectorySeparatorChar));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _updateLifetime.Cancel();
        _coordinator.Dispose();
        _explorerRecovery.Dispose();
        TrySaveSettings();
        Store.Save("profiles.json", Profiles);
        _updateHttpClient.Dispose();
        _updateGate.Dispose();
        _updateLifetime.Dispose();
    }

    private void RaiseChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
