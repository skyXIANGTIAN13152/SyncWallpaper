# Wallpaper transaction

壁纸事务使用 `IDesktopWallpaper`，逐显示器读取当前路径形成快照，再渲染目标图片、设置、回读验证。支持 JPG/JPEG/PNG/BMP，Fill、Fit、Stretch、Center、Tile 和 Span；渲染缓存键包含源哈希、尺寸、模式、背景和版本，并限制缓存目录大小。

事务规则：

- 拓扑匹配为 Ambiguous/Unknown 或 `CanAutoApply=false` 时不写入。
- 缺失图片、未知格式或不在当前活动路径的目标会保留当前壁纸。
- 单次设置最多三次有界重试，Explorer COM 暂不可用时等待并重试。
- 已经是目标路径的显示器不重复写入。
- 任一已写入显示器验证失败时，按逆序恢复事务快照。
- 不会把黑色纯色当作缺失图片的替代品。

真实 Explorer 重启、睡眠唤醒和显示器拔插验证需要用户在手动清单中执行；自动测试只使用虚拟拓扑和故障注入。
