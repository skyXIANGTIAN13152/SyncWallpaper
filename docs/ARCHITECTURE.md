# 屏序 Beta 1.0.0 架构

屏序由核心宿主、同进程按需模块和独立宿主进程组成。核心宿主只拥有配置、单实例、托盘、日志、显示拓扑协调、壁纸匹配和模块生命周期；Wallpaper 自动匹配始终保持开启。

## 边界

```text
Core Host (App)
 ├─ MonitorDiscovery / DisplayTopologyStabilizer
 ├─ ProfileMatcher / WallpaperTransaction
 ├─ ConfigurationStore / LogService / ModuleManager
 ├─ Display, Audio, Window, Automation, Desktop (按需同进程)
 └─ IPC ── TaskbarHost / ShellHost / ScreenSaverHost / RemoteHost
```

`SyncWallpaper.Windows` 只提供 Windows API 适配器；`SyncWallpaper.Core` 不依赖 Windows。独立宿主通过版本化 JSON 行协议通信，包含协议版本、请求 ID、模块实例 ID、ready、heartbeat、stop 和故障响应。宿主崩溃会被 ModuleManager 标记为 `Faulted` 并隔离，核心进程不会退出。

## 生命周期

每个模块状态为 `Stopped → Starting → Running → Stopping → Stopped`，启动/停止异常进入 `Faulted`。停止同进程模块必须注销事件、WinEvent Hook、Timer、COM 引用和后台任务；独立模块必须退出进程。关闭验证由 `ModuleManager.VerifyStopped` 与资源采样完成。

## 事件流

`WM_DISPLAYCHANGE`、`WM_DEVICECHANGE`、`WM_POWERBROADCAST`、`WM_DPICHANGED`、`WM_SETTINGCHANGE`、`TaskbarCreated` 及 `SystemEvents` 信号先合并，再等待 2 秒。稳定器必须获得两个相同快照，最多等待 10 秒；新版本信号会取消旧任务，重复签名不再应用壁纸。

## 安全边界

默认轻量模式不创建任何独立宿主进程。真实显示配置、音频切换、Explorer 重启、睡眠和电源操作没有后台自动路径，只能在用户明确确认后执行。诊断默认只读，日志和报告默认不上传。
