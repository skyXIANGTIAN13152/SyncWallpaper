# 屏序 SyncWallpaper 阶段 1 验证报告

验证日期：2026-08-05  
验证范围：显示器配置事务、Core Audio 音频配置、窗口位置配置、触发器与动作、桌面图标基础配置，以及阶段 1.5 的按需模块生命周期、只读稳定性和资源探针。  
结论：阶段 1.5 已完成安全只读验证、独立宿主进程边界和轻量发布 smoke；改变显示器/音频/桌面/Explorer 的操作仍保持确认门禁，没有把任何模块标记为 `Verified`。

## 验证环境

- 系统：Windows 11 家庭版 中文版，x64，版本 `10.0.26200`，OS build `26200`。
- 显示器组合（Windows 当前临时编号只作为本次观测记录，不写入配置身份）：
  - `DISPLAY1`：`X=3840,Y=-201,1280×800`，非主屏。
  - `DISPLAY2`：`X=-1440,Y=0,1440×2560`，非主屏，竖屏。
  - `DISPLAY3`：`X=0,Y=0,2560×1440`，主屏。
- 上面的 `Screen.AllScreens` 数值是当前进程 DPI 上下文看到的桌面坐标；原生 CCD 日志中的 `1920×1200 / 2160×3840 / 3840×2160` 是物理像素。两组数值的差异正好体现了当前机器的混合 DPI/缩放，配置事务使用 CCD 物理坐标，窗口引擎再做显式 DPI 换算。
- 原生显示路径日志中记录的稳定标签/设备路径：
  - `TL134ADXP03`：`DISPLAY#TMA0803#5&2a56f61f&0&UID256#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}`，`1920×1200 @ 5760,-302`。
  - `VG27AQL1A`：`DISPLAY#AUS2704#5&2a56f61f&0&UID261#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}`，`2160×3840 @ -2160,0`。
  - `U27G42`：`DISPLAY#AOCB426#5&2a56f61f&0&UID257#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}`，`3840×2160 @ 0,0`。
- 系统 PnP 音频设备：`USB Audio Device`、`Realtek High Definition Audio`、`AMD High Definition Audio Device`，状态均为 `OK`。Core Audio COM 枚举在当前会话已通过；真实默认端点切换仍未执行。

## 自动化测试结果

| 测试层 | 数量 | 结果 |
| --- | ---: | --- |
| `SyncWallpaper.Tests` 单元测试 | 74 | 74 通过，0 失败（含 11 个模块生命周期/模式/故障隔离测试） |
| `SyncWallpaper.IntegrationTests` 默认运行 | 12 | 10 通过，2 Skip（Shell 桌面视图不可读；CCD 测试需显式 opt-in） |
| CCD 原样配置真实 opt-in 测试 | 1 | 1 通过；没有改变当前分辨率/旋转 |

按默认回归加上 CCD opt-in 计算，实际执行通过的测试方法为 74 + 10 + 1 = 85；剩余 1 个 Shell 桌面视图场景仍 Skip（默认运行的 CCD 门禁保留为 Skip）。

单元测试覆盖了显示器预检查、模式/刷新率拒绝、验证失败回滚、回滚失败、身份歧义、音频 Optional/Required、窗口身份和混合 DPI、自动化优先级/冷却/防抖/递归/取消/超时，以及桌面图标缺失和越界修正。Windows API 均通过 Fake/Mock 适配器测试；集成测试不支持时使用 Skip，不伪造通过。

## 真实 Windows 11 场景

| 场景 | 实际结果 | 是否改变永久状态 |
| --- | --- | --- |
| QueryDisplayConfig + WMI 显示路径读取 | 通过；取得三条活动路径、设备路径和 EDID/WMI 标签 | 否 |
| 壁纸后台 smoke | 通过；三屏壁纸均渲染并回读验证成功 | 仅恢复到原有壁纸状态 |
| SetDisplayConfig 安全回环 | 通过；保存当前快照、预检查、两阶段事务（拓扑/基本模式→稳定→最终字段）、`SDC_VALIDATE + SDC_USE_SUPPLIED_DISPLAY_CONFIG`、稳定等待和验证完成 | 否 |
| 显示器模式/旋转真实改变 | 未执行 | 否（出于安全原因） |
| Core Audio 端点枚举 | 通过；Core Audio COM 返回端点 | 否 |
| 默认播放/通信/录音设备读取 | 通过；当前会话的 Console、Multimedia、Communications、Recording 默认角色均可回读 | 否 |
| 默认播放/通信/录音设备切换并恢复 | 未执行；需要明确选择并拥有可切换的端点 | 否 |
| 三个普通窗口捕获、移动、恢复 | 未执行真实窗口移动；Fake 平台测试通过 | 否 |
| 睡眠/唤醒后的重新匹配 | 未执行 | 否 |
| Explorer 重启后的 Shell 图标接口/托盘恢复 | 未执行；当前 Shell 集成测试 Skip | 否 |

没有自动注销、重启或关机，也没有执行可能改变用户显示模式的非原样配置。

## 回滚验证

- 模拟适配器已实际覆盖：应用失败、验证不一致、确认倒计时拒绝、回滚成功、回滚失败，以及应用后取消触发回滚的路径。
- 真实 Windows 本轮只应用原样 CCD 验证，因此没有触发真实回滚；没有把“API 返回成功”当作真实功能成功。
- 15 秒确认窗口和超时回滚代码已接入显示事务，但尚未在真实模式变更上触发，以避免改变用户当前桌面。

## 资源采样

阶段 1.5 的 100 次只读资源探针记录为：Working Set `69,869,568 → 93,388,800` bytes，Private Bytes `22,503,424 → 40,624,128` bytes，句柄 `427 → 435`，GDI `0 → 0`，USER `5 → 5`，累计 CPU `0.45 → 1.16 s`。句柄增量为 8，低于 500 的探针门槛；首次 JIT/WMI/COM/Shell 初始化造成的工作集变化不能替代 30 分钟长稳结论。

发布版轻量模式 `--background` 约 6 秒采样：工作集 `82,894,848` bytes（约 79.0 MiB）、Private Bytes `18,870,272` bytes（约 18.0 MiB）、句柄 385、CPU `0.44 s`；同时检查宿主进程数为 0。旧设置迁移为轻量模式时没有创建 Taskbar/Shell/ScreenSaver/Remote/Online 宿主进程。

资源详情见 [Stage1ResourceReport.md](Stage1ResourceReport.md)，手动风险门禁见 [Stage1ManualTestChecklist.md](Stage1ManualTestChecklist.md)。

## 阶段 1.5 按需模块定型

- 核心宿主只保留配置、托盘、日志、单实例、显示器变化协调、壁纸自动匹配和 `ModuleManager`。
- 默认轻量模式只运行壁纸核心；标准模式开启 Display/Audio/Window/Automation/Desktop；完整模式再开启 Taskbar/Shell/ScreenSaver/Remote/Online；自定义模式逐项保存覆盖。
- Display、Audio、Window、Automation、Desktop 使用按需控制器。Window Engine 停止时会取消事件订阅、注销 WinEvent Hook 并释放窗口平台；Audio Engine 停止时释放 provider/COM；其余同进程服务不持有后台线程或定时器。
- TaskbarHost、ShellHost、ScreenSaverHost、RemoteHost、OnlineWallpaperProviders 使用独立 `SyncWallpaper.Host.exe`。真实集成测试已验证 RemoteHost 启动、PID 记录、发送 `stop` 和进程退出。
- 模块状态严格使用 `Stopped / Starting / Running / Stopping / Faulted`；独立宿主异常只影响对应模块，核心壁纸进程继续运行。设置页和诊断实验室显示状态、PID、Working Set、Private Bytes、句柄、CPU、依赖和最近错误。
- 资源探针期间发现并修复真实缺陷：`DwmGetWindowAttributeInt` 的 P/Invoke 入口名称错误，实际窗口枚举会抛出 `EntryPointNotFoundException`；已改为显式绑定 `DwmGetWindowAttribute`，随后 100 次窗口枚举探针通过。

## 已知限制和未验证项

1. `PolicyConfig::SetDefaultEndpoint` 是非公开 Core Audio 边界；已隔离在 Windows 适配器，失败会降级并记录，尚未在真实设备之间切换验证。
2. HDR、DPI、旋转和非原样刷新率/源模式映射尚未进行真实硬件改变测试；当前只验证了安全原样 CCD 回环。
3. 窗口 `AppUserModelID` 读取、UWP 窗口和高完整性窗口需要更多真实样本；默认不自动启动未运行程序。
4. 官方 Shell 视图接口在本次会话没有返回可读取桌面项目；Explorer 重启重连尚未验证。
5. Win10、真实混合 DPI、睡眠唤醒、显示器热拔插、驱动重启和 Explorer 崩溃隔离均未达到 `Verified` 门槛。

下一步应先在可恢复的测试显示器/音频设备上完成一次明确的模式和默认端点切换，再考虑进入 TaskbarHost/ShellHost 等高风险阶段；在这些门禁完成前，不声称完全对标 DisplayFusion Pro。
