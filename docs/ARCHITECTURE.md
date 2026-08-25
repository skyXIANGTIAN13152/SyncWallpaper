# 壁纸专版架构

屏序只有一个常驻主进程 `SyncWallpaper.App.exe`。它负责单实例、托盘、配置、日志、显示器变化协调、壁纸档案、组合匹配和壁纸事务。产品不再包含模块管理器或任何额外功能宿主进程。

```text
Windows 显示/设备/电源/Explorer 事件
                    │
                    ▼
         DisplayChangeCoordinator
          两次稳定快照 + 最新事件优先
                    │
          ┌─────────┴─────────┐
          ▼                   ▼
 MonitorDiscoveryService   ProfileMatcher
 QueryDisplayConfig/WMI    唯一身份与角色分配
          │                   │
          └─────────┬─────────┘
                    ▼
          WallpaperApplyService
          渲染 → 应用 → 回读验证
```

## 项目边界

- `SyncWallpaper.Core`：纯数据模型、身份与组合匹配、拓扑稳定、配置原子保存和壁纸事务状态。
- `SyncWallpaper.Windows`：只读显示器发现、Windows 消息、WMI、Explorer 恢复、壁纸库/渲染/应用、启动项和日志。
- `SyncWallpaper.App`：WPF 主界面、托盘和壁纸专用运行时。
- `SyncWallpaper.Diagnostics`：只读显示器/壁纸快照与检测压力测试。
- `SyncWallpaper.Update.Core`：仅检查 GitHub Release，不下载或安装。

## 显示 API 边界

分辨率、刷新率、DPI、HDR、方向、桌面坐标和接口信息均保留，因为它们是显示器识别和故障解释的重要证据。它们只从 Windows 读取；解决方案中没有 `SetDisplayConfig`、`ChangeDisplaySettingsEx` 或显示参数写入服务。

## 事件与并发

`WM_DISPLAYCHANGE`、`WM_DEVICECHANGE`、`WM_POWERBROADCAST`、`WM_DPICHANGED`、`WM_SETTINGCHANGE`、`TaskbarCreated` 和 `SystemEvents` 会进入同一个协调器。普通变化先等待 2 秒并取得两次相同快照；新事件会取消旧事务。登录启动使用一次立即检测，使已经连接的匹配组合无需等待下一次插拔事件就能应用。

## 数据

运行数据只包括 `Config`、`Wallpapers`、`Cache`、`Thumbnails` 和 `Logs`。配置使用临时文件写完并刷新磁盘后原子替换，不生成历史恢复点或壁纸删除备份。

## 故障隔离

发现、匹配和应用均为有界操作。歧义、弱身份、缺失图片或 Explorer 暂时不可用时保持当前壁纸；回读失败会尝试恢复本次事务开始前的壁纸。UI 或更新检查异常不会改变组合档案。
