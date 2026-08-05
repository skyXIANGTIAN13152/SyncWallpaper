# Beta 1.0.0 validation report

验证窗口：2026-08-05 10:24:10–10:52:41（本轮实际约 28 分 31 秒）  
版本：`1.0.0-beta.1`  
SDK：.NET 8.0.423  
系统：Windows `10.0.26200` x64  

## 已完成

- `build.ps1`：12 个项目，0 warnings，0 errors。
- `SyncWallpaper.Tests`：113/113 通过。
- `SyncWallpaper.IntegrationTests`：13 通过，2 Skip（桌面 Shell 视图和需要显式 opt-in 的 CCD 原样回环）。
- Monitor Identity V2：QueryDisplayConfig、DisplayConfig source/target、WMI、可选 SetupAPI/注册表、StableId 来源和歧义门禁。
- 事件稳定器：原生消息、2 秒起始等待、两次相同样本、10 秒上限、版本取消和签名去重。
- 壁纸事务：当前路径快照、图片格式检查、渲染缓存上限、三次验证重试、失败回滚和 Span best-effort。
- A/B/C 识别覆盖层、扩展显示器字段、逻辑角色/模板/Schema V2 迁移、Beta 文档与发布清单。

## 安全诊断

- 虚拟 10,000 事件：稳定输出 1 次，耗时约 41.4 ms；发布版多次约 35.1–50.7 ms。
- 只读快照：当前会话 3 台显示器、Explorer 运行、自身 Working Set 约 36.6 MiB（发布诊断进程约 37.3 MiB）。
- `verify`：8 个安全项通过；Explorer 重启、真实显示模式变更/回滚、真实音频默认设备切换均按风险门禁 Skip。
- 完整历史 60 分钟自有宿主 soak 仍保留在 `artifacts/diagnostics/soak-60m.json`；本轮没有伪造 2/6/24 小时真实硬件证据。

## 发布

framework-dependent x64 包位于 `artifacts/publish/win-x64`，可选 self-contained 包位于 `artifacts/publish/win-x64-selfcontained`；两者都包含 `App/`、`Host/`、`Diagnostics/`、图标、许可证、README、变更记录、第三方声明和 docs，且不包含 PDB/开发机绝对路径。

## 未验证与限制

Win10、真实混合 DPI、睡眠唤醒、热插拔后的真实壁纸回读、Explorer 重启恢复、真实显示模式改变、Core Audio 设备切换、UWP/高权限窗口、完整 Taskbar/Shell/Remote/Online 功能仍未达到 Verified。详见 `docs/KNOWN-LIMITATIONS.md` 与 `docs/REAL-HARDWARE-TEST-CHECKLIST.md`。
