# 阶段 1.5 资源与稳定性报告

日期：2026-08-05  
目标：确认按需模块基础不会因为重复只读操作造成线性句柄/GUI 对象增长，并记录轻量发布版起步资源。以下数字是观测值，不是最终产品承诺。

## 100 次只读资源探针

测试：`ReadOnlyResourceProbeRunsOneHundredIterationsAndWritesReport`。每次循环读取显示器、Core Audio 端点、窗口列表和官方 Shell 桌面视图；结束后执行 GC，并记录 Working Set、Private Bytes、GC Heap、Handle、GDI、USER、CPU。

最近一次快照：`artifacts/validation-snapshots/stage15-resource-20260805-032447.json`。

| 指标 | Before | After | Delta |
| --- | ---: | ---: | ---: |
| Working Set | 66.6 MiB | 89.1 MiB | +22.4 MiB |
| Private Bytes | 21.5 MiB | 38.7 MiB | +17.3 MiB |
| GC Heap | 5.92 MiB | 1.01 MiB | GC 后下降 |
| Handle Count | 427 | 435 | +8 |
| GDI Objects | 0 | 0 | 0 |
| USER Objects | 5 | 5 | 0 |
| 累计 CPU | 0.45 s | 1.16 s | +0.70 s |

句柄增量远低于探针门槛 500，GDI/USER 没有增长。Working Set/Private Bytes 变化包含首次 JIT、WMI、COM 和 Shell 初始化，不能据此声称 30 分钟常驻无泄漏。

## 发布版轻量 smoke

发布输出：`artifacts/publish/win-x64/SyncWallpaper.App.exe` 与 `SyncWallpaper.Host.exe`。使用旧配置（没有 `Modules` 字段，迁移为轻量模式）以 `--background` 启动，等待约 6 秒后读取：

- PID：本次临时进程
- Working Set：`82,894,848` bytes（约 79.0 MiB）
- Private Bytes：`18,870,272` bytes（约 18.0 MiB）
- Handles：385
- CPU：0.44 s
- 轻量模式未启动独立宿主；宿主文件存在但只有启用对应模块才会创建进程。
- 同次启动会把模式、启动耗时和每个模块的资源样本写入 `%LocalAppData%\SyncWallpaper\Config\module-performance.json`；轻量记录中只有 Wallpaper 为 `Running`，其余模块为 `Stopped` 且没有 PID。

该 smoke 只验证启动、显示器协调、壁纸核心和发布文件完整性；没有把关闭进程当作正常退出性能结论。

## 尚未完成的资源门禁

以下项目仍是 `NotRun`，因此不允许把资源行提升为 `Verified`：

- 30 分钟常驻（无 UI 与有 UI 两种模式）。
- 主窗口打开/关闭 50 次。
- 显示器变化 100 次、音频变化 100 次、窗口布局应用 100 次、自动化触发 100 次。
- 缩略图 100 次。
- 各同进程模块重复启停后的句柄、WinEvent Hook、COM 和 Timer 验证。
- 标准/完整模式逐项启动时间与资源基线，以及独立宿主崩溃后自动禁用的长时间记录。

## 本轮安全诊断工具

`SyncWallpaper.Diagnostics.exe` 的 `snapshot`、`verify` 和 `soak` 已加入发布包。最终发布版 60 秒 smoke 报告为 `artifacts/diagnostics/soak-final-smoke.json`：7 个样本，自身 Working Set `31,739,904–44,273,664` bytes，句柄 `270–349`，并确认逐样本记录宿主的 Working Set、Private Bytes、句柄、线程、GDI/USER 对象、CPU、模块状态和错误；JSON 汇总提供 `Trend`、`Thresholds`、`ThresholdExceeded`。该工具只启动 SyncWallpaper 自己的测试宿主，不写显示/音频/窗口/Explorer 状态。

60 分钟安全 soak 的最终结果写入 `artifacts/diagnostics/soak-60m.json` 和 `.csv`：实际 `3601.5955` 秒、61 个样本、未取消、宿主错误为空。该历史任务使用增强字段发布前的诊断程序，因此 GDI/USER/模块状态以最终发布版短 smoke 验证；长稳原始数据中的 Working Set、Private Bytes、句柄和 CPU 汇总及起止趋势已在 `docs/overnight-reliability-report.md` 记录。

## 记录格式

后续每完成一项资源测试，都要保存测试日期、Windows build、模式、模块集合、Working Set、Private Bytes、GC、Handles、GDI、USER、CPU、启动时间和是否线性增长；只要存在未解释的线性增长，状态保持 `Tested` 或更低。
