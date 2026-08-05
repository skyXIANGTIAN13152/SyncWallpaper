# RC1 最终 27 项报告

1. 实际开始时间：2026-08-05 12:38:04 +10:00（2026-08-05 02:38:04Z）。
2. 实际结束时间：以最终提交前的最后一次门禁时间为准；最终回复会给出精确值。
3. 实际工作时长：从上述开始时间按墙上时钟计算；不把加速测试折算为时间。
4. 真实 soak 经过时间：60.6 秒 active monotonic，13 个样本，sleep excluded 0 秒；12 小时资格为 false。
5. 加速压力测试事件数量：100,000，稳定输出 1 次，174.5 ms。
6. 当前分支：release/1.0.0-rc1。
7. 当前 commit hash：以最终回复给出的 git HEAD 为准。
8. commit 列表：554f123 Beta 1.0.0 baseline；1e29f6f RC1 harden topology and wallpaper transactions；3fe829e RC1 add hardware validation and recovery safeguards；3e21828 RC1 add recovery tools and release packaging；后续门禁提交。
9. 新增和修改文件：硬件验收工具、脱敏/快照模型、TopologyCoordinator、壁纸状态机、挂起恢复、Explorer 被动恢复、混合 DPI、安全模式、5 点备份、安装脚本、RC1 文档和测试。
10. 构建结果：Release 和 Debug 全量构建均 0 警告、0 错误；解决方案含 13 个项目。
11. 单元测试结果：125/125 通过。
12. 集成测试结果：13 通过、2 Skip。
13. 被跳过测试及准确原因：桌面 Shell 视图当前会话不可读；真实 SetDisplayConfig 原样回环需用户设置 SYNCWALLPAPER_REAL_DISPLAY_TEST=1，默认避免桌面变更。
14. 真实硬件矩阵：

| 类别 | 状态 |
| --- | --- |
| 当前 3 屏只读 QueryDisplayConfig / WMI / SetupAPI 身份 | Passed |
| 当前前后快照比较（0 项差异） | Passed |
| 同型号无序列号人工 A/B/C | Not Run |
| 物理热插拔、接口交换、DP/HDMI/USB-C | Not Run |
| 睡眠/唤醒、锁屏插拔、Explorer 重启 | Not Run |
| Win10、真实混合 DPI、长时 12 小时 | Environment Unavailable / Not Run |
| 三张真实壁纸应用回读和恢复按钮 | Not Run |
15. 后台内存：最终 1 分钟 soak 自身 Working Set 34,770,944–52,678,656 bytes；self-contained snapshot 约 37,654,528 bytes。
16. UI 打开内存：Not Run；未在无人值守阶段擅自打开用户 UI。
17. 空闲 CPU：soak 自身 CPU 0.08–0.66 秒/60.6 秒；没有高频轮询。
18. 线程数：记录于诊断 JSON；长时 UI/硬件线程矩阵仍未验证。
19. 句柄数：自身 284–361；测试宿主独立记录。
20. 12 小时前后资源变化：Not Run；短测不冒充 12 小时。
21. 发布包完整路径：artifacts/publish/win-x64 与 artifacts/publish/win-x64-selfcontained。
22. 安装包完整路径：artifacts/publish/SyncWallpaper-1.0.0-rc.1-win-x64.zip；当前用户安装脚本为 install.ps1。
23. SHA-256：同名 zip.sha256 文件；发布包 unsigned，manifest signed=false。
24. 已知限制：真实 Explorer/睡眠/Win10/混合 DPI/物理热插拔、完整 Taskbar/Shell/Remote/Online 功能未达到 Verified。
25. 尚未完成或无法执行的项目：需要用户物理操作或不同 Windows 会话的所有门禁继续保持 Not Run，不自动改变系统状态。
26. 从 Beta 1.0.0 到 RC1 的核心变化：正式 Git/分支、事务状态与 generation、单一拓扑协调、独立 21 步硬件验收、脱敏导出、被动 Explorer 恢复、挂起恢复模型、混合 DPI 验证、5 点配置恢复、安全模式、安装/升级/卸载和 SHA256 发布。
27. 是否建议用户开始真实日常试用：可以在保留现有壁纸/配置备份的前提下进行“只读与静态壁纸”小范围试用；不应把 RC1 当作 DisplayFusion Pro 完整替代，也不应在未完成人工门禁前启用高风险外围模块。
