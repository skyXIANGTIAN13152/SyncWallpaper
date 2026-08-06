# 屏序 SyncWallpaper UI 图案规范（当前实现）

本轮将“程序内部主视觉”和“Windows 图标”分开。复杂的审视之眼只出现在 WPF 程序内部；任务栏和系统托盘使用显示器拓扑图案，避免把复杂大图缩小后变成不可读的点。

## 资源结构

### 程序内部

- `src/SyncWallpaper.App/MainWindow.xaml`：深空玻璃窗口外壳、状态卡片和完整几何审视之眼。
- `EyeGradient`：青色至蓝紫色的内部 UI 眼睛渐变。
- 眼睛包含外环、眼睑轮廓、虹膜环、瞳孔和高光；它不是托盘资源。

### 任务栏应用图标

- `assets/syncwallpaper-icon.svg`
- `assets/TaskbarIconReference.png`（用户提供的图 5，作为任务栏图标的源图）
- `assets/TaskbarIconReferenceTransparent.png`（去除边缘连通黑色背景、保留屏幕内部黑色的透明版本）
- `assets/AppIcon.ico`
- `assets/AppIcon_16.png`、`AppIcon_32.png`、`AppIcon_64.png`、`AppIcon_256.png`

任务栏图标采用用户提供的图 5 语言：青色观测环、台式显示器、右下方笔记本、环上的状态亮点。它不包含眼睛、星云、文字或 HUD 刻度。

### 系统托盘图标

- `assets/TrayIcons/tray_normal.svg` / `.ico` / `_16.png` … `_64.png`
- `assets/TrayIcons/tray_paused.svg` / `.ico` / `_16.png` … `_64.png`
- `assets/TrayIcons/tray_recognizing.svg` / `.ico` / `_16.png` … `_64.png`
- `assets/TrayIcons/tray_error.svg` / `.ico` / `_16.png` … `_64.png`
- `src/SyncWallpaper.App/TrayIconState.cs`
- `src/SyncWallpaper.App/TrayIconRenderer.cs`

SVG 是便于评审和继续调整的图形源文件；运行时渲染器生成包含 16、20、24、32、48、64 像素帧的 ICO，并交给 `NotifyIcon`。托盘图标只保留圆环、显示器拓扑和状态符号。

## 四种托盘状态

| 状态 | 颜色 | 形状/附加符号 | 触发条件 |
|---|---|---|---|
| 正常监测 | 青蓝 | 完整观测环、显示器+笔记本、状态点 | 后台正常运行 |
| 自动切换暂停 | 灰蓝 | 显示器拓扑上方有暂停双竖线 | 自动匹配被关闭、验证模式或安全模式 |
| 正在识别 | 高亮青色 | 断开的扫描弧、扫描点 | 稳定显示器拓扑正在匹配/应用 |
| 歧义/错误 | 红色 | 断裂环、虚线不确定显示器、警告三角 | 匹配歧义、无匹配、壁纸事务失败等 |

颜色不是唯一信号：暂停依靠双竖线，识别依靠扫描弧，错误依靠虚线屏幕和警告三角。

## 接入点

- `App.xaml.cs` 在托盘状态变化时替换 `NotifyIcon.Icon`，旧图标立即释放。
- `AppRuntime.IsRecognizing` 在显示器拓扑稳定处理的开始/结束时短暂切换，异常通过 `finally` 恢复正常状态。
- `AppRuntime.TrayVisualState` 将暂停、歧义、错误和正常状态映射为图标状态，不改变壁纸匹配策略。
- `DisplayChangeCoordinator` 的 `SyncWallpaper.DisplayWatcher` 是隐藏的系统消息接收窗口，已移除 `WS_EX_APPWINDOW`、加入 `WS_EX_TOOLWINDOW` 并隐藏，避免它被 Windows 错误地显示为第二个任务栏缩略图。
- 关闭主窗口不会保留独立动画线程或计时器；图标是一次性位图，不使用持续高帧率渲染。

## DPI 与性能

- 托盘 ICO 已准备 16/20/24/32/48/64 像素帧，Windows 可按 100%、125%、150%、200% 缩放选择合适帧。
- 任务栏 ICO 另外包含 256 像素统一资源。
- 运行时只在状态变化时重新生成托盘图标，不在后台循环绘制。
- 图标渲染不依赖 GPU；关闭主窗口后没有图标动画资源或渲染线程需要释放。
- `tests/SyncWallpaper.Tests/TrayIconRendererTests.cs` 覆盖四种状态和全部托盘尺寸，并验证多帧 ICO 可加载。

## 生成资源

如果调整 SVG 或颜色，可执行：

```powershell
.\tools\BuildTrayIconAssets.ps1
```

脚本只生成静态 PNG/ICO，不参与主程序运行。
