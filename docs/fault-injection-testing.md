# 故障注入测试

故障注入集中在 `SyncWallpaper.Core.FaultInjection`，生产默认使用 `NoFaultInjector`，不会改变正常启动行为。测试使用 `ConfigurableFaultInjector` 按命名故障点和次数注入，结束后由测试夹具释放宿主；没有散落的 `if (test)` 分支。

## 支持的故障点

- 进程：`ProcessStart`、`ProcessImmediateExit`、`HostNoResponse`、`StopTimeout`。
- IPC：`IpcFailure`、`IpcTimeout`、`IpcCorruptMessage`。
- 显示事务：`DisplayApply`、`DisplayVerify`、`DisplayRollback`。
- 音频事务：`AudioDeviceDisappearance`、`AudioApply`。
- 窗口/Shell：`WindowClosed`、`ExplorerHandleInvalid`。
- 配置/日志：`ConfigurationCorrupt`、`ConfigurationWrite`、`LogUnwritable`。
- 通用：`Cancellation`。

显示事务通过 `DisplayConfigurationTransactionService` 的 Prepare/快照/验证/Apply/Verify/Commit/Rollback/VerifyRollback 路径注入；音频引擎在保存默认角色快照后注入并使用未取消令牌回滚。通用 `ConfigurationTransactionPipeline<TTarget,TSnapshot>` 提供同样的有界顺序和“外部 Apply 可能部分成功”的保护。

## 覆盖的自动测试

当前自动测试覆盖：生命周期非法依赖/启动停止超时/幂等/依赖顺序、一次退避恢复和 crash-loop 禁用、持久化故障状态、IPC JSON 往返与乱码拒绝、显示 Apply 故障回滚、音频 Apply 故障回滚、通用事务取消和回滚令牌、配置模型不保存 PID。

## 使用边界

故障注入只允许单元测试、开发诊断和显式 `verify` 路径。默认关闭，不改变真实显示模式、默认音频设备、窗口位置或 Explorer。诊断工具的高风险条目会显示为 `Skipped`，除非用户在真实桌面逐项输入 `YES`；本轮无人值守任务没有执行这些条目。
