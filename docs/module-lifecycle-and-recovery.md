# 模块生命周期与恢复协议

## 状态机

`SyncWallpaper.Core.ModuleManager` 是所有模块的唯一生命周期入口。公开方法不会直接修改状态；所有转换都经过受限转换表：

| 当前 | 允许的下一状态 | 触发条件 |
| --- | --- | --- |
| Stopped | Starting | 启动请求 |
| Starting | Running / Faulted | 启动成功 / 异常、取消或超时 |
| Running | Stopping / Faulted | 停止请求 / 控制器故障或进程退出 |
| Stopping | Stopped / Faulted | 停止成功 / 停止异常或超时 |
| Faulted | Starting / Stopping / Stopped | 恢复重试、停止请求、手动重新启用 |

每次转换记录 UTC 时间和原因。每个注册项有 `SemaphoreSlim` 串行化启动与停止，重复调用是幂等的，不会创建第二个宿主进程。依赖按拓扑顺序启动，检测到循环或未注册依赖时，依赖方进入 `Faulted`。

## 超时和清理

默认启动和停止超时为 8 秒，可由 `ModuleLifecycleOptions` 调整。超时会取消控制器令牌并进入 `Faulted`，不会无限等待或无限重试。独立宿主先发送带请求 ID 的 `stop`，最多等待 2 秒响应和 3 秒进程退出，最后只终止自己创建的进程树。同进程模块的 `DelegateModuleController` 必须在 `StopAsync` 释放 Hook、Timer、COM 和后台任务。

## 故障退避

意外控制器故障在 5 分钟窗口内计数。默认允许一次 1 秒退避恢复；再次故障后设置 `FaultDisabled=true`，配置自动关闭并保持 `Faulted`，等待用户手动重新启用。手动启用会清除故障禁用标记并重新走 `Stopped → Starting`。恢复任务受生命周期检查保护，核心退出后不会重新启动宿主。

## 持久化

`Config/module-runtime.json` 只保存：用户是否启用、最后一次成功启动时间、最后故障时间/错误、崩溃次数、最后恢复点和 crash-loop 禁用标记。故意不保存 PID；PID 只在当前进程内存和 UI 快照中出现。重启时先注册核心，随后按依赖顺序启动启用模块；持久化为 crash-loop 禁用的模块不自动启动。

## IPC 健康

`HostProcessModuleController` 与 `SyncWallpaper.Host.exe` 使用版本化的逐行 JSON：协议版本、请求 ID、模块实例 ID、消息类型、成功标志、错误码/消息和时间戳。宿主启动先发送 `ready`，之后每 2 秒发送 `heartbeat`。版本或实例不匹配、乱码消息、未知请求都被拒绝或记录而不会使核心退出；超过 10 秒没有心跳会报告故障。请求和响应均有 5 秒上限，连接断开会转为 `Faulted`。

## 关闭验收

关闭模块后应同时观察：状态为 `Stopped`、控制器未运行、进程 ID 为空、Hook 状态已注销、重复启停的句柄不持续增长、关闭后对应系统事件不再触发模块动作。`SyncWallpaper.Diagnostics verify` 默认只执行安全路径，真实显示/音频变更永远需要用户单独确认。
