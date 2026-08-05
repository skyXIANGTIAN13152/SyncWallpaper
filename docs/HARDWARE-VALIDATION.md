# RC1 硬件验收中心

SyncWallpaper.HardwareValidation.exe 是独立的只读验收工具。它不会改显示模式、分辨率、旋转、DPI、Explorer、壁纸或电源，也不会模拟拔插设备。

运行：

    .\SyncWallpaper.HardwareValidation.exe --output .\hardware-validation.json

工具记录 21 个步骤：活动路径、EDID/WMI 身份、同型号歧义、稳定 ID 来源、A/B/C 识别提示、逻辑角色/壁纸确认门禁、前后快照比较和脱敏报告。需要物理拔插或实际应用壁纸的步骤会标记 NotRun，不会用“看起来成功”替代真实证据。

导出报告默认只保留：

- 厂商、产品代码、分辨率、刷新率、旋转、DPI、桌面坐标和连接状态；
- 序列号、ContainerId、monitorDevicePath、InstanceName、AdapterId、StableId 的一致性哈希；
- 每一步的 Passed / Failed / Blocked / NotRun / EnvironmentUnavailable。

同型号屏幕缺少序列号时，报告会标记 Blocked，主程序的“显示器识别”按钮负责在每个屏幕显示 A/B/C，再由用户指定逻辑角色。工具不会按 Windows 临时 DISPLAY1/2/3 猜测。

## 本机 RC1 只读结果

2026-08-05 当前会话返回 3 条 QueryDisplayConfig 活动路径：内置屏使用 ContainerId，两个外接屏使用 EDID 序列号。未执行物理断开/重连、三张壁纸应用回读或恢复按钮，因此这些项目仍为 NotRun，不提升对标矩阵状态。
