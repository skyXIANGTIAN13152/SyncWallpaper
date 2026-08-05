# 真实 Windows 环境验证记录

环境：Windows 11（`10.0.26200`）、Windows x64、.NET SDK `8.0.423`，本机当前活动显示器 3 台。所有下列读取均为只读，未执行分辨率、刷新率、旋转、HDR、DPI、默认音频设备、窗口位置、Explorer 重启或系统电源操作。

## 已执行

| 验证 | 结果 | 证据 |
| --- | --- | --- |
| 解决方案 restore/build | 通过 | `build.ps1`，当前 12 个项目均生成 |
| 单元测试 | 通过 | 95/95，0 失败，0 跳过 |
| Windows 集成测试 | 通过/跳过 | 13 通过、2 跳过；跳过项为桌面 Shell 无可读项目和需手动确认的 CCD 无变化回环 |
| CCD 无变化回环 | 未在本轮无人值守执行 | 环境变量 `SYNCWALLPAPER_REAL_DISPLAY_TEST=1` 才允许，避免真实显示配置变化 |
| 只读显示器身份 | 通过 | 10 次读取稳定；包含 monitorDevicePath、适配器/target、分辨率、旋转、桌面坐标 |
| Core Audio 只读枚举 | 通过 | 集成测试和诊断快照均未写入默认角色 |
| Window/Shell 只读探针 | 通过/按环境跳过 | 100 次资源探针；桌面图标项目为空时保持 Skip |
| 独立宿主 IPC | 通过 | `SyncWallpaper.Diagnostics verify` 的 ModuleLifecycle/IpcHandshake |
| 发布版 60 秒 smoke | 通过 | `artifacts/diagnostics/soak-final-smoke.json`，7 个样本；包含 GDI/USER、宿主状态、趋势和阈值字段 |
| 安全 60 分钟 soak | 通过 | `artifacts/diagnostics/soak-60m.json`，实际 3601.5955 秒、61 个样本、未取消、无宿主错误；只启动屏序测试宿主 |

## 安全诊断入口

发布后运行：

```powershell
artifacts\publish\win-x64\SyncWallpaper.Diagnostics.exe snapshot
artifacts\publish\win-x64\SyncWallpaper.Diagnostics.exe verify
artifacts\publish\win-x64\SyncWallpaper.Diagnostics.exe soak --duration-minutes 60 --interval-seconds 60
```

`snapshot` 读取当前状态并生成 JSON；`verify` 列出 11 个验证项，默认跳过真实变更；`soak` 只启动屏序自己创建的测试宿主并采集资源，不杀 Explorer、不重启系统、不写显示/音频配置。

## 本轮未执行/不应无人值守执行

- 插拔 HDMI/DP/USB-C、睡眠唤醒、锁屏/解锁、注销/重启。
- 真实显示配置应用、回滚、HDR/DPI/旋转/刷新率改变。
- 真实默认播放/通信/录音设备切换。
- 自动化规则中的持久显示/音频/窗口动作默认需要 `AllowPersistentChanges=true` 的交互式上下文；无人值守上下文会记录并跳过。
- 任务栏、标题栏、Explorer Hook、UWP/高权限窗口真实注入。
- Explorer 终止或重启。

这些项目必须在用户可见的交互会话中先保存快照、明确确认、设置超时并验证回滚，不能被“测试通过”替代。
