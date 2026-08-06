# 屏序 SyncWallpaper

一个原生 .NET 8 / WPF 的 Windows 壁纸恢复工具（Release Candidate 1.0.0-rc.2）：依据当前活动显示路径与稳定硬件指纹，识别显示器组合，为逻辑角色恢复对应壁纸。

## 已实现

- QueryDisplayConfig + DisplayConfigGetDeviceInfo 读取活动路径、monitorDevicePath、EDID 厂商/产品、output technology、connector instance、adapter/target、分辨率、旋转和桌面坐标。
- WmiMonitorID 后台补充 InstanceName、序列号、厂商、产品代码和友好名称。
- 分层匹配：厂商 + 产品 + EDID 序列号 → monitorDevicePath → 适配器/target/接口实例 → 分辨率/方向/桌面坐标；同分或同型号无可靠序列号时返回 Ambiguous，保持现有壁纸并要求确认。
- 逻辑角色使用 Laptop、Landscape、Portrait，不保存 Windows 临时显示器编号；内置屏/外接屏会在首次运行时从桌面 wallpaper 文件夹建立示例档案。
- 事件驱动防抖：原生显示/设备/电源/DPI/Explorer 消息与系统事件触发 2 秒稳定窗口，再连续读取两次相同状态（最多 10 秒）。
- IDesktopWallpaper 逐显示器设置并回读验证，失败最多重试两次；缺失文件/无匹配/歧义不会写入黑色或随机壁纸。
- 每显示器尺寸的渲染缓存，支持填充、适应、拉伸、居中、平铺和 Span，缓存键含原图哈希、尺寸、模式、背景色和渲染版本，并有大小上限。
- JSON 原子保存与 .bak 恢复，壁纸托管库、SHA-256、软删除保护、日志轮换（单文件 1 MB、保留 7 天）。
- 深色宇宙观测台 UI：概览、显示器配置、观测档案库、规则与匹配、运行记录、设置、关于；系统托盘；默认关闭窗口只隐藏到托盘。
- 多组合壁纸档案：在“规则与匹配”中可为不同显示器拓扑保存多套命名组合，每套组合独立保存稳定显示器身份、逻辑角色和壁纸；用户可手动应用所选组合，自动匹配仍会按当前拓扑和匹配优先级选择。
- 仅管理 SyncWallpaper 自身的当前用户启动项，启动参数为 --background；不修改其他软件或系统服务。
- 已加入完整套件的数据模型与本地基础服务：显示器配置档案、分割区域、窗口位置档案、触发器/函数定义、全局热键注册、窗口移动/布局捕获、屏幕淡化，以及独立宿主生命周期骨架。
- 采用按需模块架构：核心宿主只负责配置、托盘、日志、单实例、显示器变化协调、壁纸自动匹配和模块管理；默认安装为轻量模式，不启动任务栏、Shell、远程或在线壁纸进程。
- 模块状态具有 `Stopped / Starting / Running / Stopping / Faulted` 生命周期；设置页可选择轻量、标准、完整或自定义模式，并查看每个模块的状态、PID、工作集、CPU、句柄、依赖和最近错误。
- `TaskbarHost`、`ShellHost`、`ScreenSaverHost`、`RemoteHost`、`OnlineWallpaperProviders` 使用独立宿主进程；关闭时进程退出。Display/Audio/Window/Automation/Desktop 同进程模块按需创建，停止时注销 WinEvent Hook、释放 Core Audio COM、清理定时器和后台任务。
- 模块宿主使用版本化 JSON IPC（请求 ID、实例 ID、ready/heartbeat、超时、乱码/版本拒绝）；崩溃按一次退避恢复，连续故障进入 crash-loop 禁用并等待手动重新启用。`Config\module-runtime.json` 不保存 PID。
- 发布包包含 `SyncWallpaper.Diagnostics.exe`，提供只读 `snapshot`、默认安全的 `verify` 和只启动自有测试宿主的 `soak`（JSON/CSV 资源报告）。

参考项目：LGDMps/Wallpaper_Watcher（https://github.com/LGDMps/Wallpaper_Watcher）提供轻量 JSON + IDesktopWallpaper 监控思路，本项目用原生 C# / WPF 重做并加入分层指纹、角色配置、缓存和 UI。

## 开源审计与对标矩阵

- [开源参考审计](docs/OpenSourceReferenceAudit.md)：逐仓库记录许可证、关键源码路径、Windows API、合规边界和可转化测试。
- [DisplayFusion 对标矩阵](docs/DisplayFusionParityMatrix.md)：逐项记录模块、状态、自动/手动测试、Windows 版本与已知限制。
- [阶段 1 验证报告](docs/Stage1ValidationReport.md)：记录当前 Windows 11 的实际环境、测试结果、回滚证据、资源采样和未验证项。
- [阶段 1.5 手动验证清单](docs/Stage1ManualTestChecklist.md)：按风险等级列出真实 Windows 操作、确认门禁和 Skip 原因。
- [阶段 1.5 资源报告](docs/Stage1ResourceReport.md)：记录 100 次只读探针、发布版 smoke 以及尚未执行的 30 分钟长稳测试。
- [按需模块架构说明](docs/Stage1ModuleArchitecture.md)：模块职责、模式预设、进程边界、关闭验证和功能损失。
- [Beta 架构](docs/ARCHITECTURE.md)、[Monitor Identity V2](docs/MONITOR-IDENTITY.md)、[Profile matching](docs/PROFILE-MATCHING.md)、[Wallpaper transaction](docs/WALLPAPER-TRANSACTION.md)。
- [中文使用指南](docs/USER-GUIDE.zh-CN.md)、[English guide](docs/USER-GUIDE.en.md)、[真实硬件清单](docs/REAL-HARDWARE-TEST-CHECKLIST.md)、[已知限制](docs/KNOWN-LIMITATIONS.md)、[性能](docs/PERFORMANCE.md)、[隐私](docs/PRIVACY.md)。
- [Beta 1.0.0 验证报告](docs/Beta1ValidationReport.md)：本轮实际构建、测试、诊断和发布结果。
- [模块生命周期与恢复协议](docs/module-lifecycle-and-recovery.md)：状态机、超时、退避、持久化和 IPC 健康。
- [故障注入测试](docs/fault-injection-testing.md)：集中故障点、自动测试边界和真实环境安全门禁。
- [真实 Windows 验证记录](docs/manual-real-environment-verification.md)：本轮只读验证、跳过项和风险操作清单。
- [GitHub Release 更新检查器](docs/UPDATE-CHECKER.md)：请求、Stable/Beta、URL 安全、隐私和手动安装边界。
- [本轮发布验收报告](docs/RELEASE-REPORT.md)：构建、测试、包内容、Updater 停用状态和待填写仓库配置。
- [无人值守可靠性报告](docs/overnight-reliability-report.md)：基线、改动、测试、soak 和发布顺序。
- [TaskbarHost 阶段性审计](docs/taskbar-host-audit.md)：只验证进程边界和崩溃隔离，不虚报 Explorer 级任务栏功能。

审计快照位于 vendor/reference-audit，仅供研究，不参与编译；在对标矩阵核心功能全部达到 Verified 前，不声称完全对标 DisplayFusion Pro。

## 构建、测试、运行

在仓库根目录执行：

    .\build.ps1
    .\run.ps1
    .\run.ps1 --background
    .\publish.ps1

也可以直接执行：

    dotnet restore
    dotnet build -c Release
    dotnet test -c Release

发布输出：

    artifacts\publish\win-x64\App\SyncWallpaper.App.exe

    artifacts\publish\win-x64\Host\SyncWallpaper.Host.exe

    artifacts\publish\win-x64\Diagnostics\SyncWallpaper.Diagnostics.exe

publish.ps1 生成 framework-dependent 包；publish-selfcontained.ps1 生成 self-contained 包。发布目录同时带有 AppIcon.ico、SVG、许可证、变更记录、第三方声明、安装脚本和 docs，不包含测试数据、密钥、用户配置、用户日志或开发机绝对路径，也不包含 Updater.exe。
两种发布 ZIP 的校验和写入 `artifacts\publish\SHA256SUMS.txt`；GitHub Actions 只上传 ZIP、校验和、变更记录和许可证文件。

## 更新方式

屏序不会自动下载或安装更新。软件默认不联网；在“设置 → 更新检查”点击“检查更新”时，或用户主动打开“每周自动检查”后，才会查询配置中的 GitHub Releases API。发现新版本后只展示版本号、发布日期和安全的纯文本更新说明，点击“前往 GitHub Release”打开官方 Release 页面，由用户自行下载和安装。

公开仓库为 [skyXIANGTIAN13152/SyncWallpaper](https://github.com/skyXIANGTIAN13152/SyncWallpaper)，集中配置位置为 `src/SyncWallpaper.Update.Core/UpdateModels.cs` 中的 `ProjectLinks.GitHubOwner` 与 `ProjectLinks.GitHubRepository`。更新检查默认关闭，用户主动开启后只查询 GitHub Releases，不会自动下载或安装。

Portable 更新：退出屏序，确认设置页显示的数据目录，下载新的 Portable ZIP，解压到新目录并启动。安装版更新：下载新版安装程序，退出屏序后运行安装程序覆盖升级；用户配置继续保留。应用本身不会调用 Updater、下载 asset、关闭进程、替换文件、执行安装程序或自动回滚。

## RC2 验收、安装与恢复

发布包包含 SyncWallpaper.HardwareValidation.exe，提供 21 步只读验收向导、脱敏身份诊断、快照比较和人工确认门禁；另外提供 framework-dependent、自包含、portable ZIP、SHA256 和当前用户安装/升级/卸载脚本。参见 docs/RC1ReleaseCandidateReport.md（历史基线）、docs/HARDWARE-VALIDATION.md、docs/INSTALLATION.md、docs/UPGRADE.md、docs/UNINSTALL.md、docs/SECURITY.md 和 docs/TROUBLESHOOTING.md。

## 数据目录

可写的 Portable/项目目录会优先使用程序所在的 `SyncWallpaper` 文件夹；当前目录不可写时才回退到 `%LocalAppData%\SyncWallpaper`。设置页会显示实际数据目录：

`Wallpapers`、`Config`、`Backups\Deleted`、`Logs`、`Thumbnails`、`Cache\Rendered` 都位于该目录。`Config\module-performance.json` 保存每次模式/模块启停后的真实性能快照；`Config\module-runtime.json` 保存故障恢复元数据，不保存 PID。旧的 `%LocalAppData%\SyncWallpaper` 会在首次启动时迁移到新的数据目录；迁移完成后旧目录可以安全清除，当前 D 盘工作目录中的壁纸和配置不依赖它。

安全诊断：

    artifacts\publish\win-x64\SyncWallpaper.Diagnostics.exe snapshot
    artifacts\publish\win-x64\SyncWallpaper.Diagnostics.exe verify
    artifacts\publish\win-x64\SyncWallpaper.Diagnostics.exe soak --duration-minutes 60 --interval-seconds 60

RC2 沿用安全的只读 Real-Time Soak 与 Accelerated 验收工具；诊断输出位于被忽略的 `artifacts/diagnostics`，不会上传用户配置或硬件报告。12 小时必须由用户运行 `realtime-soak --duration-minutes 720` 后才可标记合格。soak 只启动屏序自己创建的测试宿主，报告包含 Working Set、Private Bytes、句柄、线程、GDI/USER、CPU、宿主状态/错误和睡眠排除时间，不会改动显示、音频、窗口或 Explorer。

首次运行会尝试导入桌面 wallpaper\本体.*、横屏1.*、竖屏1.*。壁纸被复制到托管目录后，原文件移动不会影响托管副本。
//
## 手动验证

1. 启动软件，确认概览显示当前活动显示器与匹配置信度。
2. 在“观测档案库”导入一张图片，确认文件进入 Wallpapers。
3. 在“规则与匹配”查看证据链，确认没有使用显示器编号。
4. 插拔 HDMI/DP/USB-C 或睡眠唤醒，等待防抖结束，检查日志和壁纸路径。
5. 用相同型号无序列号显示器制造同分映射，软件应显示“需要确认”并保持当前壁纸。

## DisplayFusion Pro 对标路线

当前版本已经完成壁纸/显示器识别基础层，并提供以下模块的本地基础实现。要达到 DisplayFusion Pro 的完整产品级对标，还需要继续完善 Explorer 级集成和逐项自动化验收：

- 多显示器任务栏、任务栏布局和托盘行为。
- 窗口标题栏按钮、窗口移动/调整大小、跨屏定位与保存/恢复窗口布局。
- 触发器系统（显示器连接、电源、登录/解锁、进程、网络等）与条件/动作编辑器。
- 全局快捷键、窗口规则、应用启动与脚本动作。
- 屏幕分割、虚拟显示器布局、屏幕截图和颜色/分辨率快捷切换。
- 屏保、锁屏、显示器关闭/唤醒和多屏幻灯片。

这些能力会复用现在的稳定显示器指纹、配置存储、事件防抖和日志体系；在模块完成前不会在界面或文档中声称已经具备 DisplayFusion 的全部能力。

## 按需模块与关闭后的功能损失

| 模块 | 默认轻量模式 | 关闭后失去的功能 |
| --- | --- | --- |
| 壁纸自动匹配（核心） | 开启且不可关闭 | 无；这是屏序的核心服务 |
| Display Engine | 关闭 | 分辨率/刷新率/旋转/HDR/DPI 配置档案、CCD 两阶段应用和显示配置回滚 |
| Audio Engine | 关闭 | 播放、通信、录音默认设备读取/切换和音频配置绑定 |
| Window Engine | 关闭 | 窗口枚举、位置保存/恢复、混合 DPI 修正和窗口区域基础能力 |
| Automation | 关闭 | 触发器、动作、规则优先级、全局热键和自动化审计 |
| Desktop Engine | 关闭 | 官方 Shell 桌面图标位置与视图设置保存/恢复 |
| Taskbar Host | 关闭 | 独立多显示器任务栏、任务按钮、分组、固定项、缩略图、时钟和通知区 |
| Shell Host | 关闭 | 标题栏按钮、Alt+Tab 管理以及未来的 Explorer 邻接功能 |
| ScreenSaver Host | 关闭 | 多屏屏保宿主；不会影响 Windows 自己的屏保 |
| Remote Host | 关闭 | 远程/本地 IPC 控制、认证和远程导入导出 |
| Online Wallpaper Providers | 关闭 | 在线壁纸目录和下载提供器 |

完整模式会开启全部可选模块；标准模式只开启 Display、Audio、Window、Automation、Desktop；自定义模式逐项控制。高风险独立宿主仍须在实际发布目录存在时才会启动，宿主崩溃只会让该模块进入 `Faulted`，核心壁纸服务继续运行。

## 已知限制

- Windows 原生 IDesktopWallpaper 的位置模式是全局设置；本项目通过逐显示器精确尺寸预渲染降低差异，但系统可能仍受桌面合成器策略影响。
- “识别显示器”当前提供状态提示和识别入口；A/B/C 的跨屏覆盖层与角色编辑器需要在后续 UI 迭代中补齐，匹配层已经拒绝自动猜测。
- 托盘图标为程序运行时生成的轻量图标，XAML 内置矢量 Logo；独立多尺寸安装包图标可在后续安装器阶段补充。
- 阶段 1 已加入 `SyncWallpaper.DisplayEngine`、`AudioEngine`、`WindowEngine`、`Automation` 和 `DesktopEngine`；其中显示器事务、音频端点枚举/配置流程、窗口位置恢复、触发器动作和桌面图标基础恢复仍受真实硬件与 Shell 会话验证范围限制。
- Windows 默认音频端点的 `PolicyConfig::SetDefaultEndpoint` 属于非公开接口，已隔离在 Windows 专用适配器中；兼容性检测或调用失败时会优雅降级，不阻塞壁纸/显示器核心流程。默认端点切换前应先保存当前音频配置。
- HDR、DPI、旋转和非原样刷新率/源模式映射、真实混合 DPI、睡眠唤醒、Explorer 重启及高权限/UWP 窗口仍未达到 `Verified`。
- 多显示器任务栏、标题栏按钮、Explorer 注入、远程控制、在线壁纸和完整屏保等高风险功能尚未实现；在对标矩阵核心功能全部达到 `Verified` 前，不声称完全对标 DisplayFusion Pro。
- 应用依赖 Windows 10/11 的 WMI 与显示配置 API，其他平台不支持。
