# SyncWallpaper 开源参考审计

审计日期：2026-08-05  
审计目标：为“屏序 SyncWallpaper”建立可追溯的 DisplayFusion Pro 功能对标依据，只研究公开项目的架构、功能行为、Windows API 用法和测试思路，不把受限项目的实现代码带入本项目。

## 审计方法与合规边界

本次审计使用浅克隆、固定提交版本的方式取得源码快照。所有快照存放在 vendor/reference-audit 下，仅用于阅读和比对，不参与 SyncWallpaper 的编译。每个项目都实际检查了根目录许可证、项目文件、关键目录和关键实现文件；仓库的 README 只作为功能背景，不能替代源码检查。

| 仓库 | 审计提交 | 根许可证 |
| --- | --- | --- |
| zac15987/DisplayProfileManager | c873b66ba54b6efb111620e736878c21eda22f51 | MIT |
| microsoft/PowerToys | d04ebff5e747aed7b79d875e183701d11d966524 | MIT |
| dremin/RetroBar | dbd95f3d5d89ad7b0b888dff7af4b0dee916cec6 | Apache-2.0 |
| nikolas-sturm/WOMP | 7951cf2b955abf9a19453aeec2c6cdd13bbe611b | MIT |
| davkean/audio-switcher | cdf43922906b5fc1267b7e288cbcb4103c52589a | MIT（仓库内含改编代码声明） |
| terrymacdonald/DisplayMagician | c201d7e41fe8e8d022e495cea3ae4f1e9177ab75 | GPL-3.0 |
| Lively-wallpaper/lively | 115a02a48c83ccd97a62c79ec7b69fe2624a052e | GPL-3.0 |
| glzr-io/glazewm | 5709ad0a3c7c386bbc3e38166a865ffc12937515 | GPL-3.0 |
| valinet/ExplorerPatcher | 0a88a6e0ef6b1752fea36e581cffff1097e862b0 | GPL-2.0 |
| ramensoftware/windhawk | 61fc60dad607e6888d8de560d1b6add716f936c3 | GPL-3.0 |

许可证结论只针对审计提交的根许可证；依赖、打包 DLL、图标、样例和嵌套目录可能有单独条款。MIT/Apache 项目的代码若将来真的复制，也必须保留版权和许可证通知，本项目当前不复制其代码。GPL 项目只允许做概念、行为、API 和测试思路参考；除非整个分发物改为与其兼容的 GPL 许可证，否则不复制源代码、类层次、补丁或可识别的代码片段。

### 与当前项目的隔离措施

1. vendor/reference-audit 下的快照不是项目引用，也没有加入任何 SyncWallpaper 项目文件。
2. src 和 tests 中只使用本项目自己的模型、接口和 P/Invoke 声明；没有从 GPL 仓库复制实现。
3. 以后若需要引入 MIT/Apache 代码，必须在引入前做逐文件许可证检查，并在 NOTICE 中保留归属。
4. 任务栏、标题栏、Explorer Hook 等高风险功能采用独立进程和 IPC 设计；即使参考项目直接注入 Explorer，也不会改变本项目的隔离边界。

## 1. zac15987/DisplayProfileManager

仓库：[zac15987/DisplayProfileManager](https://github.com/zac15987/DisplayProfileManager)

### 许可证

根目录 LICENSE 为 MIT。仓库 README 还注明项目处于维护暂停状态，因此只把已存在的实现当作 API 和交互参考，不把“有功能”当作生产级验证结论。

### 技术栈

C#、WPF、.NET Framework 4.8；NLog；Newtonsoft.Json；AudioSwitcher.AudioApi；Windows 原生显示配置和全局热键 API。

### 最值得参考的模块和实际检查的文件

- src/Helpers/DisplayConfigHelper.cs：封装 GetDisplayConfigBufferSizes、QueryDisplayConfig、DisplayConfigGetDeviceInfo、DisplayConfigSetDeviceInfo、SetDisplayConfig；包含查询/应用标志、输出技术、HDR 和拓扑辅助方法。
- src/Core/ProfileManager.cs：ApplyProfileAsync 先准备显示配置，先应用主屏，再按能力选择单阶段或 ApplyStagedConfiguration；阶段一应用活动屏、模式和 HDR，等待短暂间隔后，阶段二应用完整拓扑和位置；失败时回退到单阶段。
- src/Helpers/DpiHelper.cs：DPI 读取和缩放辅助。
- src/Helpers/AudioHelper.cs：播放和录音设备配置，失败时继续处理并记录。
- src/UI/Windows/MonitorIdentifyWindow.xaml.cs：在屏幕上显示识别界面，适合处理相同型号无序列号的人工确认。
- src/Helpers/GlobalHotkeyHelper.cs 与 src/Core/HotkeyConfig.cs：托盘/热键配置和注册边界。

### 使用的 Windows API

QueryDisplayConfig、GetDisplayConfigBufferSizes、DisplayConfigGetDeviceInfo、DisplayConfigSetDeviceInfo、SetDisplayConfig、ChangeDisplaySettingsEx、EnumDisplaySettings、WPF DPI API，以及 RegisterHotKey/低级键盘相关 API；音频通过 Core Audio 封装库。

### 可合规复用的内容

可以复用其 MIT 许可下的设计经验、数据字段、两阶段提交顺序、错误回退策略、WPF MVVM 分层和识别界面行为。若未来复制具体代码，必须逐文件保留 MIT 通知；当前 SyncWallpaper 没有复制具体实现。

### 只能作为概念参考的内容

HDR、DPI 和驱动特定显示模式的应用顺序，以及“暂停若干毫秒即可稳定”的假设只能作为实验起点；不能把其 WPF 视图或 ProfileManager 类直接当作本项目实现。

### 已知限制

项目依赖 .NET Framework 4.8，显示驱动对 CCD/HDR/DPI 的支持不一致；音频默认端点的 PolicyConfig 接口属于非稳定边界；README 明确项目维护暂停，自动化覆盖不足。

### 可转化测试

- SetDisplayConfig 事务先验证后应用，阶段一失败回退阶段二的集成测试。
- HDR、刷新率、旋转和 DPI 的组合配置测试。
- 配置中断后重新枚举并恢复当前拓扑的测试。
- 相同显示器的识别界面人工确认测试。
- 播放/录音设备同时变更时的继续执行和日志测试。

## 2. microsoft/PowerToys

仓库：[microsoft/PowerToys](https://github.com/microsoft/PowerToys)

### 许可证

根目录 LICENSE 为 MIT。PowerToys 是多模块仓库，第三方目录和 NuGet 依赖仍需单独核对。

### 技术栈

C++/Win32、C#、WPF、WinUI、MSVC，模块以独立进程、共享库和 IPC 组合；FancyZones、Workspaces、PowerDisplay、Grab And Move、Always On Top、Keyboard Manager 均有独立实现和测试工程。

### 最值得参考的模块和实际检查的文件

- FancyZones：src/modules/fancyzones/FancyZones/FancyZonesApp.cpp、FancyZonesLib/FancyZones.cpp、FancyZonesWindowProcessing.cpp、FancyZonesData、MonitorUtils.cpp、WorkArea.cpp、WindowUtils.cpp、WindowKeyboardSnap.cpp、WindowMouseSnap.cpp。
  FancyZonesApp.cpp 中实际注册 WH_KEYBOARD_LL、SetWinEventHook，监听移动开始/结束、对象创建/销毁/显示/名称变化和位置变化，并在失败时调用 DisableModule。FancyZones.cpp 处理 HMONITOR、工作区、每窗口 DPI 上下文和区域命中。
- FancyZones 测试：FancyZones.UITests、FancyZonesEditor.UnitTests、FancyZones.FuzzTests，覆盖布局、编辑器和模糊输入。
- Workspaces：WorkspacesCsharpLibrary/Data/ApplicationWrapper.cs、MonitorConfigurationWrapper.cs、ProjectWrapper.cs、WorkspacesStorage.cs、Workspaces.ModuleServices/WorkspaceService.cs、WorkspacesLauncher/WindowArrangerHelper.cpp。启动器是独立进程，通过事件、命名管道和进程句柄排列窗口。
- PowerDisplay：PowerDisplay.Lib/Drivers/PInvoke.cs、Models/Monitor.cs、Models/MonitorIdentity.cs、Drivers/WMI/WmiController.cs。Monitor 的稳定身份来自 Windows device path/PnP 信息，WMI 信息与规范化设备路径配对。
- Grab And Move：GrabAndMove/main.cpp，使用键盘/鼠标低级钩子、前台窗口事件、DWM 框架边界和约 16ms 的拖动节流。
- Always On Top：AlwaysOnTop.cpp、FrameDrawer.cpp、WindowBorder.cpp、VirtualDesktopUtils.cpp、WinHookEventIDs.*，以 WinEvent 和 DWM 属性实现边框及置顶。
- Keyboard Manager：KeyboardManager.cpp、KeyboardEventHandlers.cpp、MappingConfiguration.cpp、KeyboardManagerEngineTest，处理低级输入、SendInput、抑制和状态互斥。

### 使用的 Windows API

SetWindowsHookEx、SetWinEventHook、SetThreadDpiAwarenessContext、SetThreadDpiHostingBehavior、EnumDisplayMonitors、GetMonitorInfoW、GetDisplayConfigBufferSizes、QueryDisplayConfig、DisplayConfigGetDeviceInfo、EnumDisplaySettingsW、ChangeDisplaySettingsExW；DWM 的 DwmGetWindowAttribute、DwmEnableBlurBehindWindow、DwmSetWindowAttribute；DXVA2 的物理显示器和 DDC/CI；SendInput、OpenProcess、WaitForSingleObject、命名管道和虚拟桌面 COM。

### 可合规复用的内容

MIT 许可允许在保留通知的前提下复用代码，但本项目当前只复用架构和测试思想：事件注册/注销生命周期、按显示器工作区分区、混合 DPI 上下文、独立启动器进程、失败时禁用模块，以及单元/UI/模糊测试分层。

### 只能作为概念参考的内容

PowerToys 的大型原生模块、内部 IPC 协议、插件和 UI 代码不能直接当作 SyncWallpaper 的实现；其产品级权限和安装器假设也不应直接移植。

### 已知限制

Windows 事件钩子、DPI 和高权限窗口行为随系统版本变化；部分功能需要管理员权限；PowerToys 仓库规模大、构建链复杂，单个模块的可用性不能推断为 SyncWallpaper 已完成。

### 可转化测试

- 混合 DPI 下工作区/区域坐标的正反向转换。
- 移动开始/结束、窗口创建/销毁、Explorer 重启后的钩子注销和重新注册。
- UWP、隐藏/最小化、管理员窗口的过滤。
- 键盘/鼠标事件重复、抑制、重入和进程崩溃隔离。
- Workspaces 捕获后窗口句柄失效、进程未启动、跨显示器恢复。
- 模糊测试布局 JSON 和区域边界。

## 3. dremin/RetroBar

仓库：[dremin/RetroBar](https://github.com/dremin/RetroBar)

### 许可证

根目录 LICENSE 为 Apache-2.0。

### 技术栈

WPF + WinForms，目标框架为 net6.0-windows 和 net10.0-windows，依赖 ManagedShell 0.0.358；任务栏和 Shell 对象由托盘程序内的多个控件组合。

### 最值得参考的模块和实际检查的文件

- RetroBar/Taskbar.xaml.cs：继承 ManagedShell.AppBar.AppBarWindow，组合 WindowManager、ShellManager、StartMenuMonitor、HotkeyManager，并响应显示器、DPI 和设置变化。
- Controls/TaskList.xaml.cs：调用 Tasks.CreateGroupedWindowsCollection，为每个显示器生成任务集合。
- Controls/TaskButton.xaml.cs：通过 Window.IsUWP、AppUserModelID 识别 UWP，处理分组、前置、最小化和关闭。
- Controls/TaskThumbnail.xaml.cs：封装 DwmRegisterThumbnail、DwmUpdateThumbnailProperties、DwmQueryThumbnailSourceSize、DwmUnregisterThumbnail。
- Utilities/ExplorerMonitor.cs：注册 TaskbarCreated 消息；Explorer 重启时重新打开任务栏和 ManagedShell 任务服务，并捕获错误。
- Toolbar.xaml.cs、Clock.xaml.cs 和 NotifyIcon 相关控件：快捷方式、Shell 文件夹、时钟和通知区。

### 使用的 Windows API

AppBar 窗口、RegisterWindowMessage(TaskbarCreated)、DWM 缩略图 API、Shell/Win32 窗口 API、UWP AppUserModelID、屏幕/DPI 变化消息。

### 可合规复用的内容

Apache-2.0 允许在保留 NOTICE 和许可证的前提下复用；本项目当前采用其行为和边界思路：任务栏按显示器建立独立 AppBar、任务窗口分组、UWP 识别、缩略图以及 Explorer 重启重建。

### 只能作为概念参考的内容

ManagedShell 的内部对象模型、任务按钮模板、Shell 事件订阅和具体 XAML 不直接复制；SyncWallpaper 的 TaskbarHost 必须有自己的进程边界和降级策略。

### 已知限制

ManagedShell 和 Explorer 的 Shell 合同属于不稳定边界；任务栏自动隐藏、通知区和缩略图行为受 Windows 版本、DPI 和 Explorer 生命周期影响。

### 可转化测试

- Explorer 重启后 TaskbarCreated 触发重建。
- 普通 Win32、UWP、无标题窗口和隐藏窗口的任务过滤。
- 同一 AppUserModelID 的分组、前置/最小化状态。
- DWM 缩略图句柄失效、关闭窗口和高 DPI。
- 自动隐藏、工作区变化和多显示器热插拔。

## 4. nikolas-sturm/WOMP

仓库：[nikolas-sturm/WOMP](https://github.com/nikolas-sturm/WOMP)

### 许可证

根目录 LICENSE 为 MIT。

### 技术栈

Rust workspace（womp-core、womp-cli、womp），Tauri + React + Fluent UI；配置使用 JSON/TOML，Windows 侧通过 FFI/COM 调用显示、壁纸、DPI、HDR 和音频接口。

### 最值得参考的模块和实际检查的文件

- crates/womp-core/src/ccd_manager.rs：QueryDisplayConfig、DisplayConfigGetDeviceInfo、SetDisplayConfig；先用 SDC_VALIDATE 验证数组，再用 SDC_APPLY|SDC_USE_SUPPLIED_DISPLAY_CONFIG|SDC_SAVE_TO_DATABASE|SDC_NO_OPTIMIZATION 应用；显示 DPI 使用自定义 -3/-4 类型，HDR 使用高级颜色类型 9/10，壁纸调用 IDesktopWallpaper，音频通过 PolicyConfig 设置三种角色。
- crates/womp-core/src/config_manager.rs：按 profile.toml、displays.json 组织配置，支持读取、列举、克隆、重命名、删除。
- crates/womp-core/src/serde_types/config.rs：配置中的 before/after 命令目标和参数。
- crates/womp-core/src/lib.rs：执行前置命令，加载并校验 JSON，应用布局，再执行后置命令；womp-cli/src/main.rs 提供 save/apply/list。

### 使用的 Windows API

QueryDisplayConfig、DisplayConfigGetDeviceInfo、SetDisplayConfig、IDesktopWallpaper COM、非公开 PolicyConfig::SetDefaultEndpoint、DPI/HDR CCD 类型；命令执行使用 Windows 进程和 cmd /C。

### 可合规复用的内容

MIT 允许在保留通知的前提下复用；当前只采纳其配置事务、配置文件分层、CLI 命令、壁纸/音频/显示器绑定和 before/after 动作模型。

### 只能作为概念参考的内容

自定义 CCD 结构、非公开 PolicyConfig 接口、Tauri 前端和 xcopy 克隆行为不能直接视为稳定 API；SyncWallpaper 会重新定义 C# 接口、错误模型和迁移格式。

### 已知限制

部分结构和 PolicyConfig 未公开，驱动版本可能造成失败；配置覆盖/恢复和命令失败的事务语义仍需额外验证；xcopy 不是跨版本可靠的配置迁移方案。

### 可转化测试

- SDC_VALIDATE 不通过时不得写入系统配置。
- 先/后动作失败、超时和回滚策略。
- JSON/TOML 损坏、旧版本迁移和 profile 克隆。
- DPI/HDR/壁纸/音频同时应用的幂等性。
- CLI 与 GUI 同时修改配置时的锁和冲突。

## 5. davkean/audio-switcher

仓库：[davkean/audio-switcher](https://github.com/davkean/audio-switcher)

### 许可证

根目录 LICENSE 为 MIT；AudioDeviceManager.cs 还保留了改编源代码和 NAudio 来源的说明，不能只看根许可证。

### 技术栈

较旧的 C# WinForms/MEF 代码，Windows Core Audio COM 互操作，AudioDevice/Collection/PropertyStore 模型，SynchronizationContext 事件分发。

### 最值得参考的模块和实际检查的文件

- src/AudioSwitcher/Audio/AudioDeviceManager.cs：创建 MMDeviceEnumerator、注册 IMMNotificationClient；暴露 DeviceAdded、DeviceRemoved、PropertyChanged、DefaultDeviceChanged、DeviceStateChanged，并在 Dispose 时注销。
- AudioDevice.cs、AudioDeviceCollection.cs：枚举和筛选播放/录音端点。
- IMMDeviceEnumerator.cs、IMMNotificationClient.cs、PropertyStore.cs：COM 接口和属性读取。
- PolicyConfig.cs、IPolicyConfig2/3.cs：通过 SetDefaultEndpoint 设置 Multimedia、Communications、Console 三种角色。

### 使用的 Windows API

IMMDeviceEnumerator、IMMNotificationClient、MMDevice API、设备状态和属性存储、PolicyConfig COM、Core Audio 端点角色。

### 可合规复用的内容

MIT 许可和文件内归属允许复用 API 互操作设计；本项目当前只借鉴端点生命周期、三种默认角色、通知注销和 SynchronizationContext 线程切换。

### 只能作为概念参考的内容

PolicyConfig 的 COM GUID/接口布局和旧版事件包装不应直接当作长期稳定依赖；SyncWallpaper 的 AudioEngine 需要独立的能力探测、超时和设备移除降级。

### 已知限制

仓库基于较旧 Visual Studio/框架，PolicyConfig 非公开，设备变化时默认端点可能瞬间为空；权限、会话和蓝牙设备状态都需要实机验证。

### 可转化测试

- 播放、通信、录音三个角色分别设置和读取。
- 默认设备断开、重新连接、状态 Disabled/Active。
- 回调线程、注销后的迟到事件和异常隔离。
- 蓝牙/HDMI 音频设备名称变化和同名设备。

## 6. terrymacdonald/DisplayMagician（GPL，仅概念参考）

仓库：[terrymacdonald/DisplayMagician](https://github.com/terrymacdonald/DisplayMagician)

### 许可证

根目录 LICENSE 为 GNU GPL-3.0。不能把其源代码、类名体系、补丁或可识别的代码结构复制到当前非 GPL 项目。

### 技术栈

C# .NET 10 Windows Forms + WPF；NLog、Newtonsoft.Json、System.Management、Vortice；显示驱动包装器（NVIDIA NVAPI、AMD ADL/ADLX、Intel IGCL）；AudioSwitcher.AudioApi；部分 DDC/CI 和厂商 DLL。

### 最值得参考的模块和实际检查的文件

- DisplayMagicianShared/ProfileItem.cs：ScreenPosition 保存几何位置、适配器/库/连接器、旋转、刷新率、HDR、DPI、任务栏位置和壁纸。
- ProfileRepository.cs：LocalAppData 下的 JSON 配置、活动 profile、加载/恢复。
- DisplayMagicianStartupAction.cs、ShortcutItem.cs、ShortcutManager.cs：快捷方式、应用/游戏启动和前后动作。
- Windows/CCD.cs、DdcCiHelpers.cs、GDI.cs、Wallpaper.cs、Windows/TaskbarHelper.cs：CCD、DDC/CI、GDI、壁纸和任务栏行为。

### 使用的 Windows API

QueryDisplayConfig、SetDisplayConfig、ChangeDisplaySettingsEx、GDI、IDesktopWallpaper、DDC/CI；同时使用 NVIDIA/AMD/Intel 厂商接口、任务栏和进程监控。

### 可合规复用的内容

仅可复用功能行为、配置字段候选、配置回滚思路、驱动能力矩阵和测试案例。不能复制 GPL 实现；厂商 DLL 还需遵守各自条款。

### 只能作为概念参考的内容

配置 profile 与音频/壁纸/前后置程序绑定、Surround/Eyefinity/Combined Displays 的用户流程、回滚和托盘快捷方式。

### 已知限制

驱动厂商和版本差异大，许多功能需要管理员权限；README 自身仍列出单元测试建设等待办，因此不能作为“已完全验证”的证据。

### 可转化测试

- NVIDIA/AMD/Intel 能力探测失败时的降级。
- 显示配置和音频/壁纸的回滚。
- 外部程序启动失败、超时和退出后的后置动作。
- 旋转、HDR、DPI、刷新率和拼接显示的组合。

## 7. Lively-wallpaper/lively（GPL，仅概念参考）

仓库：[Lively-wallpaper/lively](https://github.com/Lively-wallpaper/lively)

### 许可证

根目录 LICENSE 为 GNU GPL-3.0。不得复制其 WPF/UWP、workerw、屏保或插件实现。

### 技术栈

.NET Core 3.1 WPF + UWP/XAML、ModernWpf、WebView、MPV/VLC；壁纸类型和屏保/子进程拆分，另有 watchdog。

### 最值得参考的模块和实际检查的文件

- src/livelywpf/livelywpf/Core/Display/DisplayManager.cs、DisplayMonitor.cs：EnumDisplayMonitors、GetMonitorInfo、EnumDisplayDevices、DeviceId、边界/工作区和 WM_DISPLAYCHANGE。
- Helpers/DesktopWallpaper.cs：IDesktopWallpaper 接口和 CLSID C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD。
- Core/Wallpapers/PictureWallpaper.cs、IWallpaper、WallpaperFactory：图片、MPV、VLC、网页和程序壁纸的进程化。
- Services/ScreensaverService.cs、livelyScreenSaver/Program.cs：/c、/p、/s，workerw/桌面窗口管理、空白屏幕、空闲计时和原始输入。
- Core/Watchdog/WatchdogProcess.cs、PipeServer/PipeClient：子进程监控和命名管道。

### 使用的 Windows API

EnumDisplayMonitors、GetMonitorInfo、EnumDisplayDevices、GetSystemMetrics、WM_DISPLAYCHANGE、SetParent、SetWindowPos、MapWindowPoints、GetLastInputInfo、Raw Input、named pipe、IDesktopWallpaper。

### 可合规复用的内容

只采纳多显示器壁纸/屏保的生命周期、壁纸子进程、watchdog、/c /p /s 参数和显示器变化测试思路。

### 只能作为概念参考的内容

workerw/Progman 桌面挂载、屏保窗口重父级、插件进程和 Web/Mpv/VLC 嵌入。它们属于 GPL 实现且依赖旧系统行为，必须重新设计。

### 已知限制

workerw 和 Shell 私有窗口层级很脆弱，旧依赖版本可能与当前 Windows 不一致；屏保、唤醒、输入捕获和电源策略需要真实机器验证。

### 可转化测试

- 每显示器壁纸启动/停止和进程崩溃恢复。
- /c、/p、/s 屏保入口和空闲/输入退出。
- WM_DISPLAYCHANGE、睡眠唤醒、Explorer 重启后的桌面恢复。
- 不同 DPI、旋转和空显示器的窗口父子关系。

## 8. glzr-io/glazewm（GPL，仅概念参考）

仓库：[glzr-io/glazewm](https://github.com/glzr-io/glazewm)

### 许可证

根目录 LICENSE.md 为 GPL-3.0。不能复制其 Rust 窗口管理器、命令解析器或钩子实现。

### 技术栈

Rust workspace，packages/wm、wm-cli、wm-platform、wm-common 等，跨 Windows/macOS 的平台抽象、事件循环、IPC 和命令模型。

### 最值得参考的模块和实际检查的文件

- packages/wm/src/models/monitor.rs：监视器 UUID、原生属性、边界、工作区、DPI/缩放和设备路径/硬件 ID。
- native_monitor_properties.rs：HMONITOR、设备路径、硬件 ID、名称、工作区和 DPI。
- commands/monitor/update_monitor.rs、focus_monitor.rs、commands/window/set_window_position.rs、run_window_rules.rs：显示器更新、聚焦、窗口位置和规则。
- packages/wm-platform/src/platform_impl/windows/display.rs、display_listener.rs、window_listener.rs、keyboard_hook.rs、mouse_listener.rs、native_window.rs。
- events/handle_display_settings_changed.rs：WM_DISPLAYCHANGE、WM_SETTINGCHANGE、WM_DEVICECHANGE 事件。

### 使用的 Windows API

HMONITOR、MonitorFromWindow、GetMonitorInfoW、GetDpiForMonitor、WM_DISPLAYCHANGE、WM_SETTINGCHANGE、WM_DEVICECHANGE、SetWindowPos、键盘/鼠标/窗口监听和 IPC。

### 可合规复用的内容

仅复用显示器数据模型、事件驱动更新、窗口规则优先级、鼠标跨屏和命令/IPC 的概念。

### 只能作为概念参考的内容

Rust workspace、平铺窗口算法、配置语法、原生 hook 代码和平台实现只能作为行为参考。

### 已知限制

设备路径或硬件 ID 可能缺失；平铺语义与 DisplayFusion 不同；DPI 和 WM_DEVICECHANGE 顺序受系统影响。

### 可转化测试

- 显示器新增/移除/重新排列和事件去重。
- 规则优先级、窗口位置恢复和同型号无序列号人工确认。
- 鼠标在虚拟桌面边界、负坐标和混合 DPI 下移动。
- IPC 客户端断开和配置热加载。

## 9. valinet/ExplorerPatcher（GPL，仅概念参考）

仓库：[valinet/ExplorerPatcher](https://github.com/valinet/ExplorerPatcher)

### 许可证

根目录 LICENSE 为 GPL-2.0。Explorer 注入和补丁代码不能复制到 SyncWallpaper。

### 技术栈

C/C++ 原生 DLL、MSVC v145、ExplorerPatcher.sln；ep_gui、ep_setup、ep_dwm 与 ExplorerPatcher 主模块，使用 SlimDetours、Shell/WinRT/UIAutomation。

### 最值得参考的模块和实际检查的文件

- ExplorerPatcher/dllmain.c：DLL 注入 Explorer，维护任务栏显示器列表和设置状态。
- hooking.h：SlimDetours inline hook 封装。
- Taskbar10.cpp、TaskbarCenter.cpp、TwinUIPatches.cpp：任务栏和 Win11 UI 补丁。
- SettingsMonitor.c/.h：RegNotifyChangeKeyValue 监听设置变化。
- ep_gui/resources/settings.reg：设置持久化。

### 使用的 Windows API

LoadLibrary、GetProcAddress、SlimDetours/inline hook、RegNotifyChangeKeyValue、FindWindowExW、MonitorFromWindow、GetMonitorInfoW、SetWindowPos、DWM、Shell/WinRT/UIAutomation，以及 Shell_TrayWnd/Shell_SecondaryTrayWnd。

### 可合规复用的内容

只参考 Explorer 重启监听、设置变更、任务栏枚举和 Windows 更新后功能降级策略。

### 只能作为概念参考的内容

Explorer DLL 注入、私有符号补丁、SlimDetours hook、注册表模板和具体任务栏 patch 全部只能概念参考。

### 已知限制

强依赖 Windows 私有实现和版本号，更新后极易失效；注入错误可能拖垮 Explorer，且有安全、权限和稳定性风险。这正是 SyncWallpaper 必须把 ShellHost 独立进程化并支持自动禁用的原因。

### 可转化测试

- Explorer 重启后主进程和壁纸配置不受影响。
- 未知 Windows build 的能力探测和自动禁用。
- 注册表通知断开、重复通知和设置迁移。
- ShellHost 崩溃、重启次数上限和日志完整性。

## 10. ramensoftware/windhawk（GPL，仅概念参考）

仓库：[ramensoftware/windhawk](https://github.com/ramensoftware/windhawk)

### 许可证

根目录 LICENSE 为 GPL-3.0；嵌套第三方库还有各自许可证。不能复制其全局注入或模块加载实现。

### 技术栈

C++ Win32 windhawk.exe/windhawk.dll 注入引擎，Rust core/IPC/CLI，TypeScript/VSCode 前端；MinHook/SlimDetours 风格的 hook 生命周期和崩溃监视。

### 最值得参考的模块和实际检查的文件

- src/windhawk/engine/all_processes_injector.cpp、new_process_injector.cpp、dll_inject.cpp：全进程/新进程注入。
- customization_session.cpp：会话、私有命名空间和模块上下文。
- mod.cpp：动态模块加载、符号解析缓存和 hook 生命周期。
- mods_manager.cpp：模块状态管理。
- app/event_viewer_crash_monitor.cpp：崩溃监控。
- src/windhawk-core/ui/src/ipc、domain、settings、CLI：IPC、设置和命令边界。

### 使用的 Windows API

CreateRemoteThread、进程/线程句柄、LoadLibraryEx、GetProcAddress、VirtualProtect、私有命名空间、IPC、MinHook/SlimDetours 和崩溃监视。

### 可合规复用的内容

只借鉴模块状态机、崩溃计数、自动禁用、IPC 心跳和日志关联；不引入注入引擎。

### 只能作为概念参考的内容

全局进程注入、符号 hook、动态 DLL 生命周期、模块商店和前端均为 GPL 实现，不能复制。

### 已知限制

注入对安全软件、权限、系统更新和第三方进程极其敏感；失败可能造成目标进程崩溃。SyncWallpaper 的标题栏和 Shell 功能必须默认关闭危险路径，并以独立进程和超时隔离。

### 可转化测试

- Hook 子进程崩溃不影响主进程、壁纸和显示器配置。
- 连续失败后自动禁用模块，重启后状态可解释。
- IPC 心跳、版本不匹配和权限不足。
- Windows 更新/未知模块符号下的优雅降级。

## 综合结论：对 SyncWallpaper 的约束

### 可直接形成自有实现的能力边界

| 目标模块 | 主要参考 | 必须保留的独立边界 |
| --- | --- | --- |
| SyncWallpaper.DisplayEngine | DisplayProfileManager、PowerToys PowerDisplay、WOMP | C# 自有模型；QueryDisplayConfig 事务；序列号优先、无歧义才自动应用 |
| SyncWallpaper.WallpaperEngine | WOMP、Lively 的行为 | IDesktopWallpaper 和渲染缓存自有实现；壁纸状态持久化与显示配置解耦 |
| SyncWallpaper.WindowEngine | FancyZones、Workspaces、glazewm 的概念 | 窗口事件、混合 DPI 坐标和规则状态机自有实现 |
| SyncWallpaper.Automation | DisplayProfileManager、PowerToys Keyboard Manager | 触发器/动作/热键有优先级、取消和审计日志 |
| SyncWallpaper.AudioEngine | audio-switcher、WOMP | Core Audio 能力探测；设备断开后不阻塞显示/壁纸事务 |
| SyncWallpaper.TaskbarHost | RetroBar、PowerToys 的进程隔离思路 | 独立进程，Explorer 重启后可重建，崩溃只禁用任务栏模块 |
| SyncWallpaper.ShellHost | RetroBar、ExplorerPatcher、Windhawk 的风险案例 | 不复制注入代码；默认禁用高风险 hook；失败不影响主程序 |
| SyncWallpaper.ScreenSaverHost | Lively | 独立屏保入口和多屏窗口生命周期 |
| SyncWallpaper.Remote | WOMP CLI/IPC、PowerToys Workspaces IPC | 本地认证、版本协商、只读/写入权限分离 |
| SyncWallpaper.Plugin.Abstractions | PowerToys/Windhawk 的模块状态思想 | 只定义接口、能力、生命周期和故障状态，不载入 GPL 实现 |

### 本轮审计的阻断性发现

1. 现有显示器识别和壁纸应用已能在当前 Windows 11 三屏机器上运行，但还没有完整的 SetDisplayConfig 两阶段事务，也没有 Win10、混合 DPI、睡眠唤醒和 Explorer 重启验证。
2. 当前 TaskbarHost 和 ScreenSaverHost 是轻量原型；任务栏和 ShellHost 还没有达到独立进程、自动禁用和崩溃隔离的目标。
3. 现有 9 个测试主要覆盖显示器匹配和配置存储，不能证明 DisplayFusion 级别的窗口、音频、任务栏或触发器功能。
4. GPL 仓库提供了很多有价值的行为证据，但不能用其代码快速填补功能差距；实现必须按本项目自己的接口和许可证边界重写。

以上结论用于下一份对标矩阵；在矩阵核心功能达到 Verified 之前，不应对外声称“完全对标 DisplayFusion Pro”。
