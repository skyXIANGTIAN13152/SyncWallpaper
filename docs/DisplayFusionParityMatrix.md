# SyncWallpaper / DisplayFusion Pro 对标矩阵

基线日期：2026-08-05；RC1 更新：2026-08-05  
范围：当前工作区中已经存在的 SyncWallpaper 原型，以及开源参考审计提出的目标架构。  
结论先行：当前项目还不能声称完全对标 DisplayFusion Pro。只有核心功能在本表和质量门禁中达到 Verified，才可以使用该表述。

## 状态定义

| 状态 | 判定 |
| --- | --- |
| NotStarted | 尚无可用实现，或只有需求/占位模型 |
| Prototype | 有模型、界面或单一路径原型，但缺少完整错误处理、隔离或系统版本验证 |
| Implemented | 功能路径已实现并接入模块，但尚未通过本矩阵要求的自动化和真实环境门禁 |
| Tested | 已有针对性自动测试和至少一次真实功能验证，仍有系统组合未覆盖 |
| Verified | 自动测试、真实功能验证、Win10/Win11、混合 DPI、睡眠唤醒、Explorer 重启等相关门禁均通过，限制已记录 |

矩阵中的“未测”不是“失败”；它表示没有证据可以把状态提升到更高等级。自动测试数量是该行的专用测试数，不把无关测试充当覆盖证据。

## 当前总体基线

- 当前单元测试总数为 125：新增 Monitor Identity V2、角色/模板、Schema 迁移、虚拟拓扑稳定器、同型号歧义、壁纸事务状态、拓扑合并、脱敏、快照比较、挂起恢复、混合 DPI、配置恢复、Explorer 退避和安全模式测试；最近一次构建和测试为 125/125 通过。
- 独立集成测试项目包含 15 个测试：13 个通过，2 个 Skip。除显示路径、Core Audio、默认角色和资源探针外，新增了独立宿主真实启动→PID→stop→退出、异常退出隔离和畸形 IPC 消息容错验证；Shell 桌面视图因当前会话不具备可读视图而 Skip，CCD 原样回环测试只有设置 SYNCWALLPAPER_REAL_DISPLAY_TEST=1 才执行。
- 加上默认集成运行，当前 RC1 有 138 个通过（125 单元 + 13 集成）和 2 个环境 Skip；剩余未验证重点是 Win10、模式变化/音频切换/窗口恢复/Explorer/睡眠和长时间资源稳定性。
- 其中已有三屏真实运行验证：MonitorDiscoveryService 能取得原生显示路径和 EDID/WMI 信息，WallpaperApplyService 能在当前 Windows 11 机器上设置并再次读取三个屏幕的壁纸状态。
- 当前显示器匹配已经实现“序列号/厂商/产品代码 → monitorDevicePath → 硬件拓扑字段 → 几何”的分层策略；相同型号、没有可区分字段时返回 Ambiguous，不按 Windows 临时编号猜测。
- 阶段 1.5 100 次只读资源探针和发布版 60 秒 smoke 已有记录；本轮安全 soak 已完成，`artifacts/diagnostics/soak-60m.json` 的 `ActualDurationSeconds=3601.5955`、`SampleCount=61`、`Cancelled=false`。这些采样不代表模块按进程分摊，也不构成 Verified 承诺。

## DisplayFusion 功能矩阵

| DisplayFusion 功能名称 | 屏序对应模块 | 当前状态 | 自动测试数量 | 手动测试状态 | Windows 10 | Windows 11 | 混合 DPI | 睡眠唤醒 | Explorer 重启 | 已知限制 |
| --- | --- | --- | ---: | --- | --- | --- | --- | --- | --- | --- |
| 壁纸配置 | SyncWallpaper.WallpaperEngine | Tested | 3 + 5 状态/规划 | 当前机器三屏实际应用并校验；RC1 事务含显式状态、generation、快照/重试/回滚路径 | 未测 | 当前机器已验证 | 未测 | 未测 | 未测 | Span 依赖 IDesktopWallpaper 全局位置；真实 Explorer 重启和热插拔仍待手动 |
| 显示器配置（分辨率/刷新率/旋转/HDR/DPI） | SyncWallpaper.DisplayEngine | Implemented | 18 | 已完成预检查→拓扑/基本模式阶段→稳定等待→位置/旋转/最终模式阶段→验证→失败/取消回滚→15 秒确认模型；当前 Win11 已执行“原样配置”SetDisplayConfig 验证，未执行改分辨率实测 | 未测 | 枚举与原样 CCD 验证已通过 | 未测 | 未测 | 未测 | HDR/DPI 仍需会话级验证；非原样模式的旋转/源模式映射仍需专门硬件验证 |
| 显示器身份 V2 / 同型号识别 | SyncWallpaper.DisplayEngine + Core | Tested | 16 | 当前会话 QueryDisplayConfig/WMI 只读通过；同型号无序列号会显示歧义；硬件验收中心输出脱敏快照，主程序可打开 A/B/C 覆盖层 | 未测 | 当前会话已验证 | 未测 | 未测 | 未测 | SetupAPI/ContainerId 依驱动可用；A/B/C 角色确认仍需真实人工完成 |
| 显示器拆分 | SyncWallpaper.DisplayEngine | Prototype | 0 | 只有 MonitorSplit 数据模型和分区解析原型 | 未测 | 未测 | 未测 | 未测 | 未测 | 没有虚拟工作区、窗口路由和任务栏联动 |
| 多显示器任务栏 | SyncWallpaper.TaskbarHost | Prototype | 1（宿主生命周期） | 已验证独立 `SyncWallpaper.Host.exe` 的启动/退出；完整任务栏 UI 未执行 | 未测 | 进程边界已验证 | 未测 | 未测 | 未测 | 任务按钮、分组、通知区、自动隐藏和 Explorer 重建仍未完成 |
| 任务栏快捷方式 | SyncWallpaper.TaskbarHost | NotStarted | 0 | 未验证 | 未测 | 未测 | 未测 | 未测 | 未测 | 尚无固定项目、ShellLink 和拖放模型 |
| 窗口吸附（FancyZones 类） | SyncWallpaper.WindowEngine | NotStarted | 0 | 未验证 | 未测 | 未测 | 未测 | 未测 | 未测 | WindowManagement 当前只支持捕获/应用位置，不支持区域编辑和实时吸附 |
| 窗口位置配置 | SyncWallpaper.WindowEngine | Implemented | 12 | 已实现多层身份、混合 DPI、可见区域保护、高权限跳过和安全启动开关；当前 Win11 未自动移动用户窗口 | 未测 | 代码与假平台测试通过，真实窗口移动待手动确认 | 未测 | 未测 | 未测 | AppUserModelID 读取和高权限窗口需要更多真实样本 |
| 窗口规则 | SyncWallpaper.WindowEngine + SyncWallpaper.Automation | NotStarted | 0 | 未验证 | 未测 | 未测 | 未测 | 未测 | 未测 | 没有完整的进程/标题/类名过滤、优先级和冲突解释 |
| 全局热键 | SyncWallpaper.Automation | Prototype | 0 | 有 GlobalHotkeyService；未覆盖冲突、注销和权限 | 未测 | 仅注册路径检查 | 未测 | 未测 | 未测 | 不是完整的 DisplayFusion Functions/命令热键架构 |
| 触发器 | SyncWallpaper.Automation | Implemented | 11 | 已实现优先级、条件、冷却/防抖、防递归、取消、超时和动作计划；当前 Win11 仅执行手动测试入口 | 未测 | 单元测试通过，真实窗口/进程事件待验证 | 未测 | 未测 | 未测 | 系统事件适配和动作失败后的 UI 解释仍需补全 |
| 标题栏按钮 | SyncWallpaper.ShellHost | NotStarted | 0 | 未验证 | 未测 | 未测 | 未测 | 未测 | 未测 | 没有安全的标题栏 Hook；ShellHost 进程隔离尚未实现 |
| 桌面图标配置 | SyncWallpaper.DesktopEngine | Implemented | 5 | Shell 视图接口、稳定解析名、越界修正和缺失跳过已实现；当前 Shell 视图集成测试 Skip | 未测 | 适配器构建通过，当前桌面视图未取得项目 | 未测 | 未测 | 未测 | 不使用 Progman/WorkerW 坐标；不同 Windows build 可能拒绝 IFolderView 服务 |
| 音频设备配置（播放/通信/录音） | SyncWallpaper.AudioEngine | Implemented | 8 | Core Audio COM 端点枚举和 Console/Multimedia/Communications/Recording 默认角色读取已在当前 Win11 通过；设备切换、回读和回滚尚未实测 | 未测 | 端点枚举和默认角色读取通过；设备切换未实测 | 未测 | 未测 | 不适用 | PolicyConfig 非公开接口；调用失败时只能明确降级 |
| 多屏屏保 | SyncWallpaper.ScreenSaverHost | Prototype | 1（宿主生命周期） | 独立宿主边界已验证；尚未完成 /s、/p、/c 和电源生命周期 | 未测 | 进程边界已验证 | 未测 | 未测 | 未测 | 输入退出和每屏壁纸/空白策略未完成 |
| Monitor Fading | SyncWallpaper.DisplayEngine + SyncWallpaper.WindowEngine | Prototype | 0 | 有叠加层原型，未验证焦点/性能/恢复 | 未测 | 未测 | 未测 | 未测 | 未测 | 需要与 DWM、全屏应用、HDR 和睡眠唤醒协调 |
| 鼠标跨屏与防卡边 | SyncWallpaper.WindowEngine | NotStarted | 0 | 未验证 | 未测 | 未测 | 未测 | 未测 | 未测 | 没有虚拟桌面边界、负坐标、旋转屏幕和边缘阻尼策略 |
| Alt+Tab 管理 | SyncWallpaper.ShellHost + SyncWallpaper.WindowEngine | NotStarted | 0 | 未验证 | 未测 | 未测 | 未测 | 未测 | 未测 | 需要系统级窗口过滤和版本适配，不能先假设 Shell 行为稳定 |
| 锁屏壁纸 | SyncWallpaper.ScreenSaverHost + SyncWallpaper.WallpaperEngine | NotStarted | 0 | 用户已明确不需要快捷键/锁屏功能，但此行保留用于完整对标 | 未测 | 未测 | 未测 | 未测 | 未测 | 不纳入近期实施；若未来启用，需要单独处理 Winlogon/锁屏权限 |
| 远程控制 | SyncWallpaper.Remote | NotStarted | 0 | 未验证 | 未测 | 未测 | 未测 | 未测 | 不适用 | 没有本地认证、版本协商、只读权限和安全传输设计 |
| 配置导入/导出 | SyncWallpaper.Core + SyncWallpaper.Remote | Prototype | 1（损坏配置恢复） | 已验证原子保存、备份回退和导入路径；完整 UI/版本迁移未完成 | 未测 | 存储路径已验证 | 未测 | 未测 | 未测 | 尚无 schema 版本、冲突预览、跨机器显示器确认和插件数据迁移 |

## 质量门禁矩阵

这些类别是从参考项目的实现和测试结构转换出的下一阶段最低门禁。它们不是“功能已经完成”的声明。

| 测试类别 | 对应模块 | 当前自动测试 | 当前手动状态 | Windows 10 | Windows 11 | 混合 DPI | 睡眠唤醒 | Explorer 重启 | 已知限制/下一步 |
| --- | --- | ---: | --- | --- | --- | --- | --- | --- | --- |
| 显示器配置匹配 | DisplayEngine | 5 | 序列号、路径、歧义和临时编号已有单测 | 未测 | 单测+当前枚举 | 未测 | 未测 | 未测 | 需要真实 EDID 缺失和多 GPU 样本 |
| SetDisplayConfig 事务 | DisplayEngine | 18 | 假适配器覆盖两阶段应用、预检查、验证失败、应用/取消后的回滚成功/失败；Win11 原样 CCD 回环已通过 | 未测 | 原样配置验证通过 | 未测 | 未测 | 未测 | 改变旋转/分辨率的源模式映射仍需专门实测 |
| 混合 DPI 坐标转换 | DisplayEngine + WindowEngine | 14 | 假平台与布局验证器覆盖物理像素、相对坐标、区域重叠和 DPI 缩放 | 未测 | 未测 | 单测覆盖，真实混合 DPI 未测 | 未测 | 未测 | 需要 Per-Monitor V2、旋转屏和负坐标样本 |
| 窗口坐标恢复 | WindowEngine | 12 | 假平台覆盖普通窗口、最大化、可见区域、高权限和安全启动 | 未测 | 单测通过，未移动真实用户窗口 | 未测 | 未测 | 未测 | 要处理窗口创建时序、边框、最小化和进程退出 |
| 相同型号显示器 | DisplayEngine | 3（相关匹配测试） | 无序列号且路径相同会返回歧义；尚未做屏上 A/B/C 引导 | 未测 | 单测已验证 | 未测 | 未测 | 未测 | 需要人工确认界面和确认结果持久化 |
| Explorer 重启 | TaskbarHost + ShellHost | 0 | 未测 | 未测 | 未测 | 未测 | 未测 | 未测 | TaskbarCreated/进程监控、重建和主程序隔离尚未实现 |
| 睡眠唤醒 | DisplayEngine + WallpaperEngine | 0 | 未测 | 未测 | 未测 | 未测 | 未测 | 未测 | 需要处理显示路径瞬时消失、黑屏和恢复竞态 |
| 音频设备断开 | AudioEngine | 8 | 假 Provider 覆盖缺失/禁用/Optional/Required；当前 Core Audio 枚举和默认角色读取通过，真实断开未测 | 未测 | 设备集成通过（枚举/默认角色），断开未测 | 未测 | 未测 | 不适用 | 需要 Core Audio 通知、默认角色回退和不阻塞显示事务 |
| 任务栏窗口过滤 | TaskbarHost | 0 | 未测 | 未测 | 未测 | 未测 | 未测 | 未测 | 普通 Win32/UWP/隐藏/无标题/虚拟窗口规则未定义 |
| UWP 窗口 | WindowEngine + TaskbarHost | 0 | 未测 | 未测 | 未测 | 未测 | 未测 | 未测 | AppUserModelID、窗口句柄和启动时序需要独立适配 |
| 高权限窗口 | WindowEngine + ShellHost | 0 | 未测 | 未测 | 未测 | 未测 | 未测 | 未测 | UIPI/管理员权限下不能假设普通 SendMessage 或 SetWindowPos 可用 |
| 触发器优先级 | Automation | 11 | 假执行器覆盖优先级、条件、冷却、防抖、防递归、取消和 StopProcessing | 未测 | 单元测试通过 | 未测 | 未测 | 未测 | 需要系统事件适配、执行日志和真实进程/窗口样本 |
| 配置损坏恢复 | Core | 3 | 已验证主配置损坏回退、5 个恢复点轮转、路径/大小/深度限制 | 未测 | 已验证 | 不适用 | 不适用 | 不适用 | 需要 schema 迁移、部分文件损坏和并发写入测试 |
| ShellHost 崩溃隔离 | ShellHost | 0 | 未测 | 未测 | 未测 | 未测 | 未测 | 未测 | 必须证明主程序、壁纸和显示配置仍有效，并自动禁用模块 |
| Windows 更新后功能降级 | ShellHost + Plugin.Abstractions | 0 | 未测 | 未测 | 未测 | 未测 | 未测 | 未测 | 需要 build/能力探测矩阵，禁止未知系统版本强行注入 |
| 按需模块生命周期与关闭验证 | Core + App + Host | 19（生命周期、IPC、故障注入、2 宿主集成） | 轻量发布 smoke、一次退避恢复和持久化禁用已自动验证；标准/完整重复启停待手动 | 未测 | 默认轻量、IPC ready/heartbeat、独立宿主启动/退出/异常隔离已测 | 未测 | 未测 | 宿主异常退出已测；Explorer 重启待测 | 同进程资源是共享进程基线；真实任务栏/Shell Hook 未实现 |
| 事件稳定与 50,000 信号压力 | Core + Windows | 5 + sim-soak | 虚拟 50,000 信号、手动优先、过期取消和重复签名去重均有测试；真实热插拔/睡眠仍按清单手动 | 未测 | 原生消息、只读快照和协调器已测 | 未测 | 未测 | TaskbarCreated 已接入消息源，重建未测 | 不宣称长时间硬件 soak；最多等待 10 秒 |

## 模块化重构目标

| 目标模块 | 责任 | 当前落点 | 目标进程 | 故障策略 |
| --- | --- | --- | --- | --- |
| SyncWallpaper.DisplayEngine | QueryDisplayConfig、EDID/WMI 身份、模式/DPI/HDR/旋转、配置事务 | MonitorDiscoveryService、MonitorConfigurationService | 主进程服务 | 事务失败只回滚本次应用并保留上次配置 |
| SyncWallpaper.WallpaperEngine | 图片库、渲染缓存、按逻辑角色应用、恢复 | WallpaperLibraryService、WallpaperRenderService、WallpaperApplyService | 主进程服务 | 资产丢失时保留最后成功状态并记录告警 |
| SyncWallpaper.WindowEngine | 窗口枚举、位置、区域、规则、混合 DPI | WindowManagementService（原型） | 主进程服务，必要时受控代理 | 单窗口失败不影响显示/壁纸事务 |
| SyncWallpaper.Automation | 触发器、动作、热键、优先级和审计 | TriggerEngine、GlobalHotkeyService（原型） | 主进程服务 | 事件去重、超时和取消；坏规则隔离 |
| SyncWallpaper.AudioEngine | Core Audio 默认播放/通信/录音和通知 | Implemented | 主进程服务 | 设备断开只标记音频步骤失败，不阻塞显示和壁纸；PolicyConfig 失败优雅降级 |
| SyncWallpaper.TaskbarHost | 独立多屏任务栏、任务按钮、分组、固定项、缩略图、时钟、通知区、自动隐藏 | `SyncWallpaper.Host.exe` 生命周期骨架；完整 UI 待实现 | 独立进程 | 崩溃后模块进入 Faulted，主进程继续运行；完整 Explorer 重建待实现 |
| SyncWallpaper.ShellHost | 标题栏按钮、Alt+Tab 等 Shell 邻接能力 | `SyncWallpaper.Host.exe` 生命周期骨架；尚未注入 Explorer | 独立进程 | 默认最小权限；未来 Hook/版本不兼容时自动禁用并保留日志 |
| SyncWallpaper.DesktopEngine | 官方 Shell 视图接口的桌面图标保存/恢复 | Implemented | 主进程服务（后续可受控代理） | Shell 视图拒绝或 Explorer 重启时跳过缺失项目并记录原因 |
| SyncWallpaper.ScreenSaverHost | 多屏屏保和未来可选的锁屏壁纸 | `SyncWallpaper.Host.exe` 生命周期骨架；屏保协议待实现 | 独立屏保进程 | 输入/电源事件异常时退出，不修改永久配置 |
| SyncWallpaper.Remote | 本地/远程控制、认证、导入导出 | `SyncWallpaper.Host.exe` 生命周期骨架；协议待实现 | 独立服务或受控 CLI | 只读默认、写入需授权、版本协商 |
| SyncWallpaper.Plugin.Abstractions | 插件契约、能力探测、生命周期、版本 | 待实现 | 由主进程加载受限宿主 | 插件崩溃/超时自动禁用，不影响核心模块 |

TaskbarHost 和 ShellHost 的独立进程要求是架构门禁，不是将来“有空再做”的优化。任何任务栏、标题栏或 Explorer Hook 崩溃，都必须满足：主程序不崩溃；壁纸状态不失效；显示器配置文件不丢失；错误模块自动禁用且日志可追踪。

## 分阶段实施与进入下一模块的门禁

| 阶段 | 范围 | 进入下一模块前必须完成 |
| --- | --- | --- |
| 阶段 1 | 显示器完整配置、音频配置、窗口位置、FancyZones 类区域、触发器/动作、桌面图标 | 显示器/音频/窗口/自动化/桌面图标模块已完成代码和单元测试；CCD 原样验证和 Core Audio 端点枚举已通过；桌面 Shell 因环境 Skip；FancyZones 类区域仍待实现 |
| 阶段 2 | 独立多屏任务栏、任务按钮/分组/固定项/缩略图/时钟/通知区/自动隐藏 | TaskbarHost 进程隔离、Explorer 重启和崩溃隔离通过后再扩展 UI |
| 阶段 3 | 标题栏按钮、高级窗口 Hook、Monitor Fading、Alt+Tab、鼠标控制、多屏屏保 | ShellHost 能力探测、未知 Windows 版本自动降级、高权限窗口和睡眠唤醒门禁 |
| 阶段 4 | 远程控制、在线壁纸、插件、企业部署和配置迁移 | 权限模型、版本协商、迁移回滚、插件隔离和安全审计 |

每个模块的完成记录至少要包含：构建结果、自动测试结果、真实功能验证、矩阵状态、工作集/CPU 采样、已知限制和下一步。本轮阶段 1 已把显示器配置、音频配置、窗口位置、触发器/动作和桌面图标基础能力提升到 Implemented；只有当前 Windows 11 的安全验证通过部分标记为 Tested，未标记 Verified。
