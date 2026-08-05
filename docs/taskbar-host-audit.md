# TaskbarHost 阶段性审计

本轮只审计并验证安全边界，不把 Explorer 注入或自绘任务栏误报为已完成。

## 已确认

- `TaskbarHost` 在轻量/标准模式不会创建进程；完整/自定义启用时才启动 `SyncWallpaper.Host.exe`。
- 宿主启动完成会返回带协议版本、请求 ID、模块实例 ID的 `ready`，随后以心跳维持健康状态。
- stop 请求、进程退出等待、PID 清理、断开/异常退出和核心故障隔离已有自动测试。
- 宿主进程当前没有 Explorer Hook、窗口过滤、任务按钮、分组、固定项、缩略图、时钟、通知区或自动隐藏行为；这正是安全的降级状态。

## 最小循环（待任务栏功能开发阶段）

| 环节 | 当前证据 | 状态 |
| --- | --- | --- |
| Host lifecycle / IPC | 模块生命周期/IPC 针对性单测 + Windows 宿主启动、异常退出、协议拒绝、畸形消息集成测试 + diagnostics handshake | Tested |
| Explorer restart detection | 尚未监听 `TaskbarCreated` 或 Explorer PID | NotStarted |
| 任务栏发现与主/副屏 | 只有 MonitorDiscoveryService，未创建任务栏窗口 | NotStarted |
| DPI/自动隐藏/位置 | 没有任务栏窗口实现 | NotStarted |
| HWND 重建与 Hook 释放 | 没有 Hook；宿主退出路径已覆盖 | Prototype（边界） |
| 崩溃隔离/禁用/恢复 | ModuleManager Faulted、退避和持久化禁用已覆盖 | Tested |

进入阶段 2 前必须在交互式 Windows 会话中完成：监听 Explorer/TaskbarCreated、枚举主副屏任务栏、处理混合 DPI 和自动隐藏、重建 HWND、释放 Hook，并在 Explorer 重启后证明核心壁纸和显示配置不受影响。无人值守诊断不会杀掉或重启 Explorer，因此本轮不把这些项目标为 Verified。
