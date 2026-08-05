# 阶段 1.5 真实 Windows 手动验证清单

日期：2026-08-05  
环境：Windows 11 x64，OS build `10.0.26200`，当前三屏会话。

## 安全门禁

- [x] 创建并验证 `artifacts/validation-snapshots/` 快照目录；运行时 JSON 快照被 `.gitignore` 忽略。
- [x] 只读检查和 100 次资源探针已执行；没有重启、注销、关机或自动睡眠。
- [x] 需要改变显示器、音频、桌面图标或 Explorer 的按钮只显示风险说明，未在没有用户确认的情况下执行。
- [x] 应用发布前保留原有配置文件；显示器变更路径仍要求 CCD 快照、确认倒计时和回读验证。

## 已完成的真实验证

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 显示器 Level A 只读 | 通过 | 10 次 QueryDisplayConfig/WMI 读取签名一致；未使用 DISPLAY1/2/3 作为身份 |
| 显示器身份字段 | 通过 | monitorDevicePath、EDID/WMI、adapter/target、分辨率、方向、桌面坐标可读取 |
| Core Audio 只读 | 通过 | 播放/通信/录音端点与 Console、Multimedia、Communications、Recording 默认角色可读取 |
| 窗口只读枚举 | 通过 | 100 次资源探针覆盖 WindowsWindowPlatform；修复了 DwmGetWindowAttribute 入口错误 |
| 独立宿主边界 | 通过 | RemoteHost 宿主真实启动、记录 PID、发送 stop、进程退出；TaskbarHost 异常退出被标记 Faulted；主进程不受影响 |
| 轻量发布 smoke | 通过 | 发布版 `--background` 运行约 6 秒，工作集约 79.0 MiB，Private Bytes 约 18.0 MiB，句柄 385，CPU 0.44 秒；宿主进程数为 0 |

## 原有 Skip 的解释

1. `DesktopShellEnumerationWorksOrSkips`：当前会话的官方桌面 Shell 视图没有返回可读项目/接口权限不足。代码保留安全空结果，未移动现有图标。应在可读取的 Explorer 桌面会话中重跑；这不是功能成功的替代证明。
2. `DisplayConfigurationNoOpRoundTripRequiresManualOptIn`：为防误触，真实 CCD 原样回环只有设置 `SYNCWALLPAPER_REAL_DISPLAY_TEST=1` 才运行。当前已做过 opt-in 原样验证；默认测试仍 Skip，避免普通回归测试触碰显示配置。

## 等待用户明确确认的验证

这些项目已有诊断实验室入口和风险说明，但本轮没有自动执行：

- **显示器 Level B/C**：选择安全位置/主屏/旋转变化，保存 CCD 快照，应用后等待 15 秒，超时恢复并逐项回读。分辨率、刷新率、HDR、启用/禁用默认不执行。
- **音频切换**：用户选择目标播放/通信/录音端点后才切换；保存原角色、回读、恢复并确认。
- **窗口布局**：只创建 TestWindowA/B/C，保存、移动、恢复、关闭；覆盖最大化/最小化、缺失显示器和混合 DPI。不会触碰用户窗口。
- **临时桌面图标**：只创建 `SyncWallpaper Desktop Test.lnk`，使用官方 Shell 读取/移动/恢复后删除。
- **Explorer 生命周期**：用户确认后才结束并重新启动 Explorer；不能把 Explorer 重启当作普通自动化测试。
- **睡眠/唤醒**：软件只进入等待记录状态，由用户手动睡眠/唤醒；不自动发起电源操作。

## 进入下一模块前的判定

- 轻量模式必须始终没有 Taskbar/Shell/ScreenSaver/Remote/Online 宿主进程。
- 关闭同进程模块后，状态为 `Stopped`、Hook/COM 已注销、句柄不持续增长、对应事件不再触发动作。
- 独立宿主异常退出应只把该模块标记为 `Faulted`，核心壁纸进程继续运行。
- 未满足真实窗口、音频恢复、Explorer 重连和睡眠唤醒证据前，不把相关矩阵行提升到 `Verified`。
