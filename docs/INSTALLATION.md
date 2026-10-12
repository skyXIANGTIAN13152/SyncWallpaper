# 安装

## EXE 安装包（推荐）

1. 从[官方 GitHub Releases](https://github.com/skyXIANGTIAN13152/SyncWallpaper/releases)下载 `SyncWallpaper-{version}-win-x64-setup.exe`。需要验证下载完整性时，使用同一 Release 中的 `SyncWallpaper-{version}-win-x64-setup.sha256`。
2. 双击安装包，核对安装目录后按向导完成安装。安装包自带 .NET 8 Desktop Runtime，无需另行安装运行时，也不要求管理员权限。
3. 从开始菜单启动“屏序 SyncWallpaper”。向导中可选创建桌面快捷方式，也可在完成页直接启动。

新用户默认安装到 `%LocalAppData%\Programs\SyncWallpaper`。安装器会优先沿用已有安装版的位置；如果能从屏序的当前用户启动项识别便携版位置，也会提供该位置。请始终核对向导里的目录。已有便携版可选择原目录，以沿用原有组合、壁纸和设置；选择其他目录不会自动搬迁旧数据。源码目录不能作为安装目标。

安装器只写入程序和发行文件，不打包、覆盖或删除 `Config`、`Wallpapers`、`Cache`、`Thumbnails`、`Logs` 等运行数据。若目标目录中的屏序仍在运行，请先保存正在编辑的组合，再从托盘退出后继续安装；关闭主窗口只会隐藏到托盘。安装器不会强行结束程序，也不允许用较低的数字版本覆盖较高版本。

安装器不修改开机自启选择；需要自启时，在屏序的“设置”中开启。安装后可在 Windows 的“设置 → 应用”中卸载，默认保留组合和壁纸，详见[卸载说明](UNINSTALL.md)。

## ZIP 便携版

Release 继续提供 framework-dependent x64 和 self-contained x64 两种 ZIP。前者需要 .NET 8 Desktop Runtime，后者自带运行时。解压后运行 `App\SyncWallpaper.App.exe`，无需运行安装器。已有安装请按[升级说明](UPGRADE.md)覆盖发行文件并保留数据目录。

1.3 开始，发行包的 `App` 目录包含按需运行的更新助手。应用内确认更新后会下载自带运行时的 x64 包，因此 framework-dependent 安装也可以升级为自包含安装，无需重新配置组合和壁纸。EXE 安装版与 ZIP 便携版共用这一更新机制。

## 脚本安装

ZIP 内仍提供当前用户安装脚本：

```powershell
.\install.ps1 -PackagePath . -CreateShortcuts -StartWithWindows
```

脚本默认写入 `%LocalAppData%\Programs\SyncWallpaper`，不需要管理员权限，只接受当前用户 `%LocalAppData%\Programs` 下的目标目录。`-StartWithWindows` 只创建屏序自身的当前用户 Run 项。其他位置可直接解压运行，或使用 EXE 安装向导选择目录。

## 数据位置

对于可写的解压目录或标准安装目录，程序根据 `package-manifest.json` 将运行数据保存在安装根目录：`App` 和 `Diagnostics` 是程序文件，`Config`、`Wallpapers`、`Cache`、`Thumbnails` 和 `Logs` 是运行数据。若目录不可写，才使用 `%LocalAppData%\SyncWallpaper`；也可通过 `SYNCWALLPAPER_DATA_ROOT` 指定数据位置。覆盖升级必须保留实际使用的数据目录。

安装不会修改分辨率、刷新率、HDR、DPI、音频、窗口、任务栏、Explorer、服务、计划任务或电源设置。
