# 屏序 SyncWallpaper

屏序是一款专注于 Windows 多显示器壁纸的开源桌面程序。它识别当前连接的物理显示器和布局，匹配用户保存的壁纸组合，并在开机、插拔显示器、睡眠唤醒或 Explorer 恢复后自动还原每块屏幕的壁纸。

当前版本：`1.1.0-beta.1`。软件只做多显示器壁纸，不提供副屏任务栏、窗口管理、音频切换、屏保、远程控制、在线壁纸或显示参数修改。

## 核心能力

- 使用 `QueryDisplayConfig` 和 `DisplayConfigGetDeviceInfo` 读取所有活动显示路径。
- 使用 WMI `WmiMonitorID` 补充显示器厂商、产品代码、序列号、实例名和友好名称。
- 完整保留只读显示信息：monitorDevicePath、EDID 厂商/产品/序列号、adapter/source/target、接口类型、connectorInstance、分辨率、原生分辨率、刷新率、DPI/缩放、HDR/色彩、方向/翻转、桌面坐标、主屏和内置屏状态。
- 上述信息只用于识别、展示和匹配。屏序不会修改分辨率、刷新率、HDR、DPI、方向或 Windows 显示布局。
- 分层身份匹配：厂商 + 产品代码 + EDID 序列号 → monitorDevicePath → WMI InstanceName → 显卡/target/接口/connectorInstance → 几何辅助证据。
- 两台同型号显示器仍无法唯一确认时不自动猜测；A/B/C 识别界面让用户把物理屏幕绑定到 Laptop、Landscape、Portrait 或自定义逻辑角色。
- 壁纸档案库把导入图片托管到 `Wallpapers`，刷新后会隐藏已经从磁盘删除的条目，并在文件恢复后重新显示。
- 壁纸组合可新建空白档案、编辑、命名、重命名、删除、手动应用；同一显示器拓扑可保存多套壁纸方案。
- 显示器组合变化后自动重新匹配。匹配成功时自动应用，不会把“手动应用”误写成当前匹配组合。
- 通过 `IDesktopWallpaper` 逐显示器设置并回读验证；失败有限重试，歧义、缺失图片或弱身份证据会保持原壁纸。
- 开机自启使用当前用户 Run 项，仅管理屏序自身；关闭主窗口后保留轻量托盘监测。
- Explorer 重启、锁定/解锁、睡眠/唤醒和 HDMI/DP/USB-C 拓扑变化采用事件驱动防抖，不进行高频轮询。
- 设置页提供低资源模式；渲染缓存有容量上限和清理策略。
- 更新检查默认不联网；用户手动检查或主动开启每周检查时，只读取官方 GitHub Releases 页面，不自动下载或安装。

## 界面范围

主程序只保留七个页面：

1. 概览
2. 显示器识别
3. 壁纸档案库
4. 壁纸组合
5. 运行记录
6. 设置
7. 关于

复杂“观测之眼”只用于程序内部视觉；Windows 任务栏和托盘使用显示器拓扑图标。

## 安全匹配原则

Windows 的显示器编号 1、2、3 不是永久身份，重新插拔后可能变化，因此不会写入壁纸组合。序列号优先于接口；没有序列号的相同型号显示器如果交换接口，将要求重新确认，而不是把壁纸猜到错误屏幕。

档案状态只显示：

- `已匹配`：当前物理拓扑与档案唯一对应，具备完整壁纸绑定。
- `未匹配`：屏幕数量、身份、角色或壁纸不完整，或者存在歧义。

详细规则见 [显示器身份](docs/MONITOR-IDENTITY.md) 和 [组合匹配](docs/PROFILE-MATCHING.md)。

## 数据目录

正式安装在 `D:\屏序 SyncWallpaper` 时，所有运行数据也位于该目录：

```text
D:\屏序 SyncWallpaper\
├─ App\                       程序
├─ Diagnostics\               只读诊断工具
├─ Config\
│  ├─ settings.json            设置
│  ├─ profiles.json            壁纸组合
│  └─ library.json             壁纸档案索引
├─ Wallpapers\                托管壁纸原图
├─ Cache\Rendered\            每显示器渲染缓存
├─ Thumbnails\                缩略图
└─ Logs\                      本地日志
```

程序不创建配置回滚历史或壁纸回收备份。配置保存使用同目录临时文件原子替换；成功后临时文件消失。若程序所在目录不可写，才回退到 `%LocalAppData%\SyncWallpaper`。也可设置环境变量 `SYNCWALLPAPER_DATA_ROOT` 指定数据目录。

## 构建与测试

需要 Windows 10/11 和 .NET 8 SDK：

```powershell
.\build.ps1
.\run.ps1
.\publish.ps1
.\publish-selfcontained.ps1
```

解决方案只包含壁纸相关项目：Core、Windows、App、Diagnostics、Update.Core 及其测试。当前自动测试共 80 项，覆盖身份分层、同型号歧义、拓扑稳定、组合编辑/匹配、壁纸事务、档案库刷新、启动恢复、图标状态、更新检查，以及真实 Windows 只读显示器枚举和句柄稳定性。

发布输出：

```text
artifacts\publish\win-x64\App\SyncWallpaper.App.exe
artifacts\publish\win-x64\Diagnostics\SyncWallpaper.Diagnostics.exe
```

发布 ZIP 不包含用户配置、壁纸、日志、密钥、开发机绝对路径、额外宿主进程或自动安装器。

## 只读诊断

```powershell
SyncWallpaper.Diagnostics.exe snapshot
SyncWallpaper.Diagnostics.exe wallpaper-snapshot
SyncWallpaper.Diagnostics.exe monitor-soak --iterations 1000
```

诊断只读取显示器、当前壁纸和自身资源，不修改 Windows 显示设置或壁纸。

## 文档

- [架构](docs/ARCHITECTURE.md)
- [中文使用指南](docs/USER-GUIDE.zh-CN.md)
- [English user guide](docs/USER-GUIDE.en.md)
- [显示器身份](docs/MONITOR-IDENTITY.md)
- [组合匹配](docs/PROFILE-MATCHING.md)
- [壁纸事务](docs/WALLPAPER-TRANSACTION.md)
- [真实硬件检查清单](docs/REAL-HARDWARE-TEST-CHECKLIST.md)
- [故障排查](docs/TROUBLESHOOTING.md)
- [隐私](docs/PRIVACY.md)
- [安全](docs/SECURITY.md)
- [性能](docs/PERFORMANCE.md)
- [安装与升级](docs/INSTALLATION.md)

## 开源与更新

许可证见 [LICENSE](LICENSE)。公开仓库：[skyXIANGTIAN13152/SyncWallpaper](https://github.com/skyXIANGTIAN13152/SyncWallpaper)。

程序不会自动下载、替换或启动更新文件。发现新版本后只打开经过白名单校验的 GitHub Release 页面，由用户决定是否安装。

## 已知限制

- 仅支持 Windows 10/11。
- `IDesktopWallpaper` 的桌面位置模式为全局设置，屏序通过按目标尺寸预渲染降低多屏比例差异。
- 某些显示器或转接器不提供可靠 EDID 序列号；同型号设备仍有歧义时必须手动确认。
- Windows 10、真实睡眠唤醒、Explorer 强制重启和更多混合 DPI 硬件组合仍需持续验证。
