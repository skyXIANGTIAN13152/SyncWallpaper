# 显示器身份

## 采集

1. `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` 枚举活动显示路径。
2. `DisplayConfigGetDeviceInfo(DISPLAYCONFIG_TARGET_DEVICE_NAME)` 读取 monitorDevicePath、EDID 厂商/产品代码、outputTechnology、connectorInstance 和 targetId。
3. `DISPLAYCONFIG_SOURCE_DEVICE_NAME` 与 source/target mode 读取临时显示名、sourceId、分辨率、刷新率、方向和桌面坐标。
4. WMI `WmiMonitorID` 只读补充 InstanceName、SerialNumberID、ManufacturerName、ProductCodeID 和友好名称。
5. SetupAPI ContainerId、原生分辨率、DPI、HDR/色彩、主屏与内置屏状态作为额外只读证据。

## 分层优先级

1. 厂商 + 产品代码 + 有效 EDID 序列号。
2. monitorDevicePath。
3. WMI InstanceName。
4. adapterId + targetId + outputTechnology + connectorInstance。
5. ContainerId 只作辅助证据。
6. 分辨率 + 方向 + 桌面坐标只作最后的几何证据。

序列号优先于接口。如果两台同型号显示器交换接口，具有不同有效序列号时身份仍跟随物理显示器；没有序列号且无法唯一确认时，屏序要求重新进行 A/B/C 确认。

`\\.\DISPLAY1`、`DISPLAY2` 等 Windows 临时编号只用于当前会话提示，永远不会保存成永久身份。

## 方向与翻转

原始 rotation 来自 `DISPLAYCONFIG_PATH_TARGET_INFO.rotation`。界面按 Windows 设置中的含义显示：

- 1：横向 · 未翻转
- 2：纵向 · 未翻转
- 3：横向 · 已翻转
- 4：纵向 · 已翻转

界面不显示内部数字，避免与显示器编号混淆。

## 只读保证

完整显示信息不会因壁纸专版而删减；分辨率、刷新率、HDR、DPI 和方向仍会显示和参与匹配解释。删除的只有修改这些参数的能力。
