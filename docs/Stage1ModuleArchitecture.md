# 屏序 SyncWallpaper 按需模块架构

日期：2026-08-05

## 核心宿主

`SyncWallpaper.App` 的核心宿主只保留配置加载/保存、托盘、日志、单实例、显示器变化协调、壁纸自动匹配和 `ModuleManager`。壁纸自动匹配属于核心功能，启动后始终保持启用；它不依赖 Display/Audio/Window/Desktop 等可选模块。

`ModuleManager` 在 `SyncWallpaper.Core` 中定义统一生命周期：

`Stopped → Starting → Running → Stopping → Stopped`

启动或停止异常进入 `Faulted`，记录最近错误，不把异常抛出到核心壁纸循环。每个注册项都会返回状态、进程 ID、工作集、Private Bytes、句柄、CPU、依赖、Hook 状态和时间戳。关闭检查使用 `VerifyStopped`，要求模块处于 `Stopped`、控制器不再运行且没有进程 ID。

## 模块边界

| 模块 | 进程 | 启动时机 | 关闭时清理 |
| --- | --- | --- | --- |
| Wallpaper（核心） | 主进程 | 始终 | 核心协调器最后关闭 |
| Display Engine | 主进程按需 | 标准/完整/自定义启用，或用户执行显示配置操作前显式启用 | 释放 CCD 事务服务和适配器引用 |
| Audio Engine | 主进程按需 | 启用后 | 释放 Core Audio provider/COM 对象 |
| Window Engine | 主进程按需 | 启用后 | 取消 WinEvent 订阅、`UnhookWinEvent`、释放窗口平台 |
| Automation | 主进程按需 | 启用后 | 清除规则引擎和事件动作执行器 |
| Desktop Engine | 主进程按需 | 启用后 | 丢弃 Shell provider；每次 Shell 调用内部释放 PIDL/COM |
| Taskbar Host | 独立 `SyncWallpaper.Host.exe` | 用户启用 | 向宿主发送 `stop`，超时才杀掉宿主树 |
| Shell Host | 独立 `SyncWallpaper.Host.exe` | 用户启用 | 同上；主进程不注入 Explorer |
| ScreenSaver Host | 独立 `SyncWallpaper.Host.exe` | 用户启用 | 同上；不会修改 Windows 屏保设置 |
| Remote Host | 独立 `SyncWallpaper.Host.exe` | 用户启用 | 同上；当前宿主只提供隔离边界 |
| Online Wallpaper Providers | 独立 `SyncWallpaper.Host.exe` | 用户启用 | 同上；默认不联网 |

独立宿主当前是安全生命周期骨架，尚未实现 Explorer 注入、任务栏完整功能、远程协议或在线下载。这些功能必须在独立进程中继续开发，不能把 Hook/网络/第三方提供器放回核心宿主。

## 四种模式

| 模式 | 启用模块 |
| --- | --- |
| 轻量模式（默认） | Wallpaper 核心 |
| 标准模式 | Wallpaper、Display、Audio、Window、Automation、Desktop |
| 完整模式 | 标准模块 + Taskbar、Shell、ScreenSaver、Remote、Online Providers |
| 自定义模式 | Wallpaper 固定开启，其余按逐项开关保存 |

设置从旧版本迁移时，如果没有 `Modules` 字段，会使用轻量模式，不会因为旧配置自动启动高风险宿主。

## 崩溃隔离

独立宿主通过 `HostProcessModuleController` 监听 `Exited`，并使用版本化 JSON IPC 的 `ready`、请求 ID、实例 ID和心跳。异常退出只把对应项标记为 `Faulted`，默认执行一次退避恢复；连续故障会持久化为 crash-loop 禁用并等待手动重新启用。显示器协调、壁纸配置和核心进程继续运行。同进程模块的启动/停止动作由 `DelegateModuleController` 包裹，生命周期异常同样由 `ModuleManager` 隔离。

## 真实性能记录

设置和诊断实验室均可读取每个模块的 Working Set、Private Bytes、句柄、CPU、PID 和状态。由于同进程模块共享一个 Windows 进程，显示的是共享进程基线，不伪装成精确的按模块分摊；独立宿主显示自己的 PID 和进程资源。阶段 1.5 的只读资源探针与发布版 smoke 记录见 [Stage1ResourceReport.md](Stage1ResourceReport.md)。

## 关闭验证门禁

关闭模块后必须确认：

1. 同进程模块控制器不再运行，WinEvent Hook 为“已注销”，Core Audio provider 已释放，后台对象可回收。
2. 独立模块 PID 不再存在，主进程不创建对应宿主。
3. 句柄在重复启停后不持续增长。
4. 显示器变化、音频变化、窗口事件不再触发已关闭模块的动作。

当前自动化测试覆盖模块模式、启动/停止幂等、依赖顺序、非法循环、超时、故障隔离、一次退避恢复、crash-loop 持久化禁用、IPC JSON 往返/乱码拒绝、显示/音频事务回滚、关闭验证和独立宿主真实启动/退出；Explorer 重启、任务栏完整行为和高风险 Hook 仍需后续手动确认。
