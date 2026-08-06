# 无人值守可靠性报告

## 执行基线

- 本报告记录的是 Git 初始化前的无人值守基线；当前源码已纳入 Git，并保留了同样的只读与安全门禁。
- 本地 SDK：.NET `8.0.423`，Windows x64，OS `10.0.26200`。
- 基线（任务开始前）：单元测试 74 通过；Windows 集成测试 10 通过、2 跳过；CCD 回环需显式环境变量，未作为无人值守默认测试。
- 基线命令实际使用本地 `work/dotnet-sdk/dotnet.exe`：`restore`、`build SyncWallpaper.sln -c Release`、分别运行两个测试项目；SDK 输出保存在 `artifacts/diagnostics/baseline-dotnet-info.txt`。

## P0 审计快照

| 区域 | 已实现/已验证 | 模拟或仅代码证据 | 未验证/可能失败点 |
| --- | --- | --- | --- |
| 核心启动 | `AppRuntime` 配置、托盘、日志、单实例、显示变化协调和壁纸核心；默认轻量启动已在发布 smoke 读取 | 同进程模块按需创建和关闭 | 真实登录/注销、启动项和长期 UI 会话 |
| 模块管理 | 单测覆盖模式、依赖、幂等、超时、故障恢复、持久化禁用 | 资源按当前进程/宿主采样，不能伪装成模块分摊 | Win10、睡眠唤醒、Explorer 重启、标准/完整长稳 |
| 独立 Host/IPC | 宿主 ready/heartbeat/stop、PID、崩溃隔离、版本/实例拒绝和诊断 handshake | 任务栏/Shell/远程/在线功能本体仍是安全骨架 | TaskbarCreated、Explorer 重建、真实 Hook |
| Display Engine | QueryDisplayConfig/WMI 身份；CCD 两阶段事务、验证/回滚单测；原样 CCD 仅显式 opt-in | 故障注入覆盖 Apply/Verify/Rollback | 改分辨率/旋转/HDR/DPI、拔线和睡眠 |
| Audio Engine | Core Audio 只读枚举/默认角色；快照、回读、回滚和注入测试 | 非公开 PolicyConfig 只在用户启用时调用 | 真正切换默认设备、设备断开、COM 服务异常 |
| Window/Desktop/Automation | 假平台窗口身份/DPI/可见性、桌面稳定标识、触发器优先级等单测 | 真实窗口移动、Shell 图标恢复是手动风险项 | UWP/高权限、Explorer 重启、混合 DPI 实机 |
| 性能/存储 | 原子配置+备份回退；100 次只读探针；60 秒 smoke | 60 分钟安全 soak 已完成 | 本次长稳实际 3601.5955 秒；趋势按原始样本起止值计算，未将启动暖机误报为泄漏 |

## 本轮改动

1. `ModuleManager` 增加严格状态转换、时间/原因、启动停止超时、依赖循环检测、幂等串行化、一次退避恢复和 crash-loop 手动禁用。
2. `module-runtime.json` 保存用户启用状态、成功启动、故障、崩溃计数和恢复点，不保存 PID。
3. 独立宿主改为版本化 JSON IPC：ready、请求 ID、实例 ID、心跳、超时、乱码/版本拒绝和断开隔离。
4. 新增集中式故障注入、通用配置事务管线，并把显示/音频注入点接入回滚测试。
5. 新增 `SyncWallpaper.Diagnostics.exe`：只读快照、安全 verify、资源 JSON/CSV soak；发布脚本会同时发布 App、Host 和 Diagnostics。

## 测试结果

任务完成时以最后一次命令输出和以下文件为准：

- 单元测试：目标记录 95 通过、0 失败、0 跳过。
- Windows 集成测试：目标记录 13 通过、2 跳过、0 失败。
- 诊断安全验证：8 个只读/宿主项 Passed，3 个高风险项 Skipped。
- 60 分钟安全 soak：报告为 `artifacts/diagnostics/soak-60m.json` 和同名 `.csv`，实际 `3601.5955` 秒、61 个样本、`Cancelled=false`、`HostError=null`；诊断进程与测试宿主均已退出。

### 60 分钟安全 soak 实测

原始长稳报告由增强版诊断发布前启动，因此保留其原始字段，不把后补字段伪写回历史样本。自身进程的实测汇总如下：

| 指标 | Min | Avg | Max | Start → End | 起止增量（约每小时） |
| --- | ---: | ---: | ---: | ---: | ---: |
| Working Set | 31,883,264 | 47,282,478 | 52,555,776 | 31,883,264 → 52,555,776 | +20,672,512 bytes/h |
| Private Bytes | 9,129,984 | 13,315,089 | 18,071,552 | 9,129,984 → 18,071,552 | +8,941,568 bytes/h |
| Handles | 277 | 351.15 | 363 | 277 → 363 | +86/h |
| CPU seconds | 0.109 | 0.967 | 1.734 | 0.109 → 1.734 | +1.625 s/h（约 0.027 s/min） |

测试宿主 Working Set 为 `25,608,192 → 28,774,400` bytes，Private Bytes 为 `6,508,544 → 7,454,720` bytes，句柄为 `232 → 232`，CPU 为 `0.0625 → 0.6875` 秒；61 个样本显示器数量始终为 3、无宿主错误。起始到结束的增长包含启动/JIT/首次 WMI、COM 初始化，故本次结果只能说明在安全只读场景下没有失控增长，不能单独证明无泄漏。

最终发布版另以 `artifacts/diagnostics/soak-final-smoke.json` 运行了 60.2 秒、7 个样本，验证了 GDI/USER、宿主状态、`Trend`/`Thresholds` 字段和 CSV 输出；该短 smoke 的 `ThresholdExceeded=true` 来自启动暖机，不能替代 60 分钟趋势结论。

## 资源记录格式

soak 每 60 秒记录自身和测试宿主的 Working Set、Private Bytes、Handle Count、Thread Count、GDI/USER 对象、CPU、显示器数量，并汇总 min/avg/max/start/end。报告还写入 `Trend`（起止增量和按小时/分钟归一化）与 `Thresholds`（诊断告警阈值），`ThresholdExceeded` 只表示采样趋势超过阈值，不等于确认泄漏。该工具不统计伪造的“按模块分摊”；同进程模块仍显示共享主进程资源。任何增长趋势只代表诊断进程/测试宿主，需要结合 `HostError`、样本间隔和实际时长判断。

## 发布与恢复顺序

发布目录包含 `SyncWallpaper.App.exe`、`SyncWallpaper.Host.exe`、`SyncWallpaper.Diagnostics.exe`。启动顺序是核心配置/日志/单实例 → 壁纸自动匹配 → 用户启用的同进程模块 → 依赖顺序的独立宿主。停用顺序反向执行，宿主先 stop/等待/必要时只终止自身进程树；核心不会因为 Taskbar/Shell/Remote/Online 故障退出。

## 结论边界

本报告不把“代码已实现”写成“真实功能已验证”。DisplayFusion 对标矩阵中未达到 `Verified` 的功能仍是 Prototype/NotStarted；本轮没有扩展高风险 Shell/任务栏 Hook，也没有声称完全对标 DisplayFusion Pro。
