# TaskbarHost 阶段性审计

> 当前结果更新于 2026-08-10。阶段 2 开始前的历史基线保留在本文后半部分；当前实现证据另见 [Phase2TaskbarHostReport.md](Phase2TaskbarHostReport.md) 和对标矩阵。

## 当前安全边界与验证结论

- 副屏任务栏仍是独立进程，不替换、不注入 Explorer；自动隐藏、边缘唤醒、AppBar 注册和释放都只存在于 `TaskbarHost` 生命周期内。
- 单副屏普通模式使用公开的 `SHAppBarMessage`、`ABM_NEW / ABM_QUERYPOS / ABM_SETPOS / ABM_REMOVE` 协商工作区，模块停止后恢复原工作区。
- 当前 Windows 11 三屏实测发现：两个底边 AppBar 即使提交不同显示器矩形，Shell 仍可能把两次工作区缩进叠加到同一副屏。为避免破坏桌面布局，多副屏普通模式会明确报告原因并安全回退为覆盖模式，不使用 `SPI_SETWORKAREA` 等全局修改绕过系统。
- 自动隐藏不争用 Explorer 的自动隐藏槽，也不永久缩小工作区；鼠标离开后延迟收起，仅保留可配置的底边唤醒条。系统只允许每个显示器、每个边缘存在一个自动隐藏 AppBar，因此屏序使用自身窗口状态机而不调用 `ABM_SETAUTOHIDEBAREX` 抢占该槽。
- 当前三屏真实门禁分别验证普通模式和自动隐藏模式：每个副屏一条窗口、负坐标/混合 DPI 定位、工作区不被错误修改、隐藏状态实时上报，以及停止后的窗口/Timer/Hook/进程清理。

## 通知区域可行性审计

- Windows 公共 Shell API 支持应用管理自己的通知图标，但没有提供枚举、复制或代理其他进程通知图标的稳定公共接口。
- 通过 `Shell_TrayWnd`、内部 `ToolbarWindow32`、Explorer 内存读取、DLL 注入或未记录消息镜像第三方图标，会依赖 Windows build 的内部结构，且扩大 Explorer 崩溃、权限和隐私风险；本项目禁止采用这些路径。
- 因此“完整通知区域镜像”继续标记为 `NotStarted`，不会用只显示音量/网络按钮的近似实现冒充完成。后续若实现，只能由独立宿主通过公开 API 提供屏序自己的状态图标和明确标注的系统入口，第三方图标镜像须等待受支持的系统能力。
- 参考边界：[SHAppBarMessage](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shappbarmessage)、[ABM_SETPOS](https://learn.microsoft.com/en-us/windows/win32/shell/abm-setpos)、[ABM_SETAUTOHIDEBAREX](https://learn.microsoft.com/en-us/windows/win32/shell/abm-setautohidebarex)。

## 阶段 2 开始前的历史基线

以下内容仅记录功能开发前的起点，不代表当前版本。

### 当时已确认

- `TaskbarHost` 在轻量/标准模式不会创建进程；完整/自定义启用时才启动 `SyncWallpaper.Host.exe`。
- 宿主启动完成会返回带协议版本、请求 ID、模块实例 ID的 `ready`，随后以心跳维持健康状态。
- stop 请求、进程退出等待、PID 清理、断开/异常退出和核心故障隔离已有自动测试。
- 宿主进程当前没有 Explorer Hook、窗口过滤、任务按钮、分组、固定项、缩略图、时钟、通知区或自动隐藏行为；这正是安全的降级状态。

### 当时的最小循环（待任务栏功能开发阶段）

| 环节 | 当前证据 | 状态 |
| --- | --- | --- |
| Host lifecycle / IPC | 模块生命周期/IPC 针对性单测 + Windows 宿主启动、异常退出、协议拒绝、畸形消息集成测试 + diagnostics handshake | Tested |
| Explorer restart detection | 尚未监听 `TaskbarCreated` 或 Explorer PID | NotStarted |
| 任务栏发现与主/副屏 | 只有 MonitorDiscoveryService，未创建任务栏窗口 | NotStarted |
| DPI/自动隐藏/位置 | 没有任务栏窗口实现 | NotStarted |
| HWND 重建与 Hook 释放 | 没有 Hook；宿主退出路径已覆盖 | Prototype（边界） |
| 崩溃隔离/禁用/恢复 | ModuleManager Faulted、退避和持久化禁用已覆盖 | Tested |

进入阶段 2 前必须在交互式 Windows 会话中完成：监听 Explorer/TaskbarCreated、枚举主副屏任务栏、处理混合 DPI 和自动隐藏、重建 HWND、释放 Hook，并在 Explorer 重启后证明核心壁纸和显示配置不受影响。无人值守诊断不会杀掉或重启 Explorer，因此本轮不把这些项目标为 Verified。
