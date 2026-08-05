# Monitor Identity V2

## 采集层

1. `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` 枚举活动路径。
2. `DisplayConfigGetDeviceInfo(DISPLAYCONFIG_TARGET_DEVICE_NAME)` 读取 monitorDevicePath、EDID 厂商/产品代码、outputTechnology、connectorInstance、targetId。
3. `DISPLAYCONFIG_SOURCE_DEVICE_NAME` 和 source mode 读取临时 Windows 显示名、sourceId、分辨率、旋转和桌面坐标。
4. `WmiMonitorID` 只读补充 InstanceName、SerialNumberID、ManufacturerName、ProductCodeID、友好名称。
5. SetupAPI `DEVPKEY_Device_ContainerId` 和只读 DISPLAY 注册表是可选补充；API 不可用时不影响显示器发现。

## 身份优先级

稳定 ID 会写入可解释前缀：

| 优先级 | 来源 | 说明 |
| --- | --- | --- |
| 1 | `edid:` | 唯一的厂商 + 产品代码 + EDID 序列号 |
| 2 | `container:` | Windows ContainerId |
| 3 | `path:` | monitorDevicePath |
| 4 | `instance:` | WMI InstanceName |
| 5 | `topology:` | adapter LUID + target + output technology + connectorInstance |
| 6 | `geometry:` | 分辨率、方向和桌面坐标；只作最后的可解释回退 |

`\\.\DISPLAY1`、`DISPLAY2` 等编号永远只是当前会话显示提示，不会保存为永久身份。重复型号且无序列号时，`DisplayIdentityMatcher` 返回 `Ambiguous`/`Unknown`，`CanAutoApply=false`，壁纸事务保持当前壁纸。

## 证据

每次匹配保留得分、身份状态、命中层级和冲突字段（serial、container、path、instance、分辨率、旋转）。UI 和诊断报告显示原始字段与来源，便于解释为何没有自动应用。
