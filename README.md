# 屏序 SyncWallpaper

屏序是一款专注于 Windows 多显示器壁纸的开源桌面程序。它识别当前连接的物理显示器和布局，匹配用户保存的壁纸组合，并在开机、插拔显示器、睡眠唤醒或 Explorer 恢复后自动还原每块屏幕的壁纸。

当前版本：`1.3.0-beta.1` — 自动检查更新与确认后升级（Beta 预发布）。软件只做多显示器壁纸，不提供副屏任务栏、窗口管理、音频切换、屏保、远程控制、在线壁纸或显示参数修改。

[下载当前版本](https://github.com/skyXIANGTIAN13152/SyncWallpaper/releases/tag/v1.3.0-beta.1) · [本次更新说明](docs/RELEASE-1.3.md) · [完整更新说明](CHANGELOG.md)

1.3 新增每日检查新版本：发现更新后先询问，确认后下载、校验并替换程序文件，随后重新启动。原有壁纸组合、壁纸原图和设置保留。1.2 已发布版中的通用组合、秘境界面、镂空图标和独立滚动体验继续保留。

## 核心能力

- 使用 `QueryDisplayConfig` 和 `DisplayConfigGetDeviceInfo` 读取所有活动显示路径。
- 使用 WMI `WmiMonitorID` 补充显示器厂商、产品代码、序列号、实例名和友好名称。
- 完整保留只读显示信息：monitorDevicePath、EDID 厂商/产品/序列号、adapter/source/target、接口类型、connectorInstance、分辨率、原生分辨率、刷新率、DPI/缩放、HDR/色彩、方向/翻转、桌面坐标、主屏和内置屏状态。
- 上述信息只用于识别、展示和匹配。屏序不会修改分辨率、刷新率、HDR、DPI、方向或 Windows 显示布局。
- 分层身份匹配：厂商 + 产品代码 + EDID 序列号 → monitorDevicePath → WMI InstanceName → 显卡/target/接口/connectorInstance → 几何辅助证据。
- 两台同型号显示器仍无法唯一确认时不自动猜测；A/B/C 识别界面让用户把物理屏幕绑定到 Laptop、Landscape、Portrait 或自定义逻辑角色。
- 壁纸档案库把导入图片托管到 `Wallpapers`，刷新后会隐藏已经从磁盘删除的条目，并在文件恢复后重新显示。
- 壁纸组合可新建空白档案、编辑、命名、重命名、删除、手动应用；同一显示器拓扑可保存多套壁纸方案。
- 通用组合可从 0 个位置开始添加「横屏1、横屏2……」「竖屏1、竖屏2……」，不设位置数量上限。没有完整、安全匹配的特定组合时，只有横屏数和竖屏数分别完全一致的通用组合才会接替；例如 2 横屏组合不能用于 1 横屏加 1 竖屏。
- 横竖屏分别按桌面位置从左到右、同列从上到下编号，包含笔记本屏幕。每个方向的最高编号代表所需数量，空缺或未选壁纸的位置保留当前壁纸。没有匹配的特定或通用组合时，所有屏幕保留当前壁纸；通用组合应用成功后托盘显示古金双屏轮廓与暖白菱形。
- 显示器组合变化后自动重新匹配并应用；手动应用记录实际选择，下一次自动检测重新按匹配规则选择。
- 通过 `IDesktopWallpaper` 逐显示器设置并回读验证；失败有限重试，缺失图片时保持原壁纸。特定组合身份歧义或证据太弱时不猜测，可由已配置的通用组合接替。
- 开机自启使用当前用户 Run 项，仅管理屏序自身；关闭主窗口后保留轻量托盘监测。
- Explorer 重启、锁定/解锁、睡眠/唤醒和 HDMI/DP/USB-C 拓扑变化采用事件驱动防抖，不进行高频轮询。
- 设置页提供低资源模式；渲染缓存有容量上限和清理策略。
- 新安装默认每天检查官方 GitHub Releases；升级保留原有自动检查开关。发现新版本先询问，用户确认后才下载、校验并自动替换程序；也可在设置或托盘中手动检查。

## 界面范围

主程序只保留七个页面：

1. 概览
2. 显示器识别
3. 壁纸档案库
4. 壁纸组合
5. 运行记录
6. 设置
7. 关于

默认「秘境」主题采用深墨绿、古金和暖白，背景带有静态雾光、月蚀光环和星轨。软件图标采用透明镂空的横竖双屏轮廓；托盘通过勾、菱形、短横、暂停和感叹号区分状态。

在 **设置 → 外观主题** 中可切换「秘境」「极光科技」「石墨暗色」「云白亮色」，立即生效并自动保存。
七个页面的标题、操作按钮和搜索筛选固定在顶部，下方内容独立滚动；组合编辑器和长正文弹窗也保留可见的操作区。鼠标滚轮可滚动正文，关闭状态的下拉框不会因滚轮而误改选择。
主窗口、组合编辑器和显示器确认窗口共用控件配色；阅读区域不受桌面壁纸影响，窗口外侧保留透明圆角。组合编辑器提供最大化／还原按钮和标题栏双击切换，初始尺寸适应所在屏幕的工作区。
主题切换只改变界面，不修改显示器配置、壁纸组合、自动匹配或开机启动状态。

## 安全匹配原则

Windows 的显示器编号 1、2、3 不是永久身份，重新插拔后可能变化，因此不会写入壁纸组合。特定组合以序列号优先于接口；没有序列号的相同型号显示器如果交换接口，硬件身份仍有歧义时需要重新确认。

特定组合按硬件身份匹配；通用组合使用用户明确设置的横竖数量和位置规则，不依赖显示器型号、序列号或 Windows 临时编号。自动匹配始终优先考虑完整且安全匹配的特定组合；手动“应用选中组合”也要求通用组合的横竖数量分别完全一致。

主要档案状态：

- `已匹配`：当前物理拓扑与档案唯一对应，具备完整壁纸绑定。
- `未匹配`：屏幕数量、身份、角色或壁纸不完整，或者存在歧义。
- `通用匹配`：当前使用数量完全一致的通用组合；`后备待用`：数量一致且已有壁纸，等待需要时匹配；`数量不符`：当前横竖屏数与组合要求不同；`未配置`：通用组合尚未选择壁纸。

详细规则见 [显示器身份](docs/MONITOR-IDENTITY.md) 和 [组合匹配](docs/PROFILE-MATCHING.md)。

## 数据目录

标准发行版在可写的解压或安装根目录中保存运行数据，目录结构如下：

```text
SyncWallpaper\
├─ App\                       主程序与按需运行的更新助手
├─ Diagnostics\               只读诊断工具
├─ Config\
│  ├─ settings.json            设置
│  ├─ profiles.json            壁纸组合
│  └─ library.json             壁纸档案索引
├─ Wallpapers\                托管壁纸原图
├─ Cache\Rendered\            每显示器渲染缓存
├─ Thumbnails\                缩略图
└─ Logs\                      本地日志
```

程序不创建配置回滚历史或壁纸回收备份。配置保存使用同目录临时文件原子替换；成功后临时文件消失。若程序所在目录不可写，才回退到 `%LocalAppData%\SyncWallpaper`。也可设置环境变量 `SYNCWALLPAPER_DATA_ROOT` 指定数据目录。

## 构建与测试

需要 Windows 10/11 和 .NET 8 SDK：

```powershell
.\build.ps1
.\run.ps1
.\publish.ps1
.\publish-selfcontained.ps1
```

解决方案包含 Core、Windows、App、Diagnostics、Update.Core、Updater 及其测试。自动测试覆盖身份分层、同型号歧义、拓扑稳定、特定/通用组合优先级、完整横竖数量、位置编号、空白位置保留、配置迁移、布局变化时的壁纸事务、档案库刷新、启动恢复、图标状态、更新调度与包校验，以及真实 Windows 只读显示器枚举和句柄稳定性。

发布输出：

```text
artifacts\publish\win-x64\App\SyncWallpaper.App.exe
artifacts\publish\win-x64\App\SyncWallpaper.Updater.exe
artifacts\publish\win-x64\Diagnostics\SyncWallpaper.Diagnostics.exe
```

发布 ZIP 只包含程序、更新助手、文档和安装脚本，不包含用户配置、壁纸、日志、密钥或开发机绝对路径。更新助手按需运行，不常驻。

## 只读诊断

```powershell
SyncWallpaper.Diagnostics.exe snapshot
SyncWallpaper.Diagnostics.exe wallpaper-snapshot
SyncWallpaper.Diagnostics.exe monitor-soak --iterations 1000
```

诊断只读取显示器、当前壁纸和自身资源，不修改 Windows 显示设置或壁纸。

## 文档

- [架构](docs/ARCHITECTURE.md)
- [中文使用指南](docs/USER-GUIDE.zh-CN.md)
- [English user guide](docs/USER-GUIDE.en.md)
- [显示器身份](docs/MONITOR-IDENTITY.md)
- [组合匹配](docs/PROFILE-MATCHING.md)
- [壁纸事务](docs/WALLPAPER-TRANSACTION.md)
- [真实硬件检查清单](docs/REAL-HARDWARE-TEST-CHECKLIST.md)
- [故障排查](docs/TROUBLESHOOTING.md)
- [隐私](docs/PRIVACY.md)
- [安全](docs/SECURITY.md)
- [性能](docs/PERFORMANCE.md)
- [安装与升级](docs/INSTALLATION.md)

## 开源与更新

许可证见 [LICENSE](LICENSE)。公开仓库：[skyXIANGTIAN13152/SyncWallpaper](https://github.com/skyXIANGTIAN13152/SyncWallpaper)。

自动检查启用后，每次成功检查至少间隔 24 小时，失败至少间隔 1 小时再试。发现符合所选渠道的新版本时先询问；确认后下载相应 x64 发行包，核对 SHA-256、版本与包结构，再关闭旧程序、替换程序文件并重新启动。拒绝或稍后更新不会改变当前安装。更新保留 `Config`、`Wallpapers`、`Cache`、`Thumbnails` 和 `Logs`，无需重新设置组合。详见 [升级说明](docs/UPGRADE.md) 与 [更新机制](docs/UPDATE-CHECKER.md)。

## 已知限制

- 仅支持 Windows 10/11。
- `IDesktopWallpaper` 的桌面位置模式为全局设置，屏序通过按目标尺寸预渲染降低多屏比例差异。
- 通用组合不支持 Span 跨屏模式，以免改变未分配屏幕的全局壁纸显示方式。
- 某些显示器或转接器不提供可靠 EDID 序列号；同型号设备仍有歧义时必须手动确认。
- Windows 10、真实睡眠唤醒、Explorer 强制重启和更多混合 DPI 硬件组合仍需持续验证。
