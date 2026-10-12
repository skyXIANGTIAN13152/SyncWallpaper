# 安装

发布包提供 framework-dependent x64 和 self-contained x64 两种 ZIP。前者需要 .NET 8 Desktop Runtime，后者自带运行时。

Portable：解压后运行 `App\SyncWallpaper.App.exe`。已有安装请按[升级说明](UPGRADE.md)覆盖发行文件并保留数据目录。

1.3 开始，发行包的 `App` 目录包含按需运行的更新助手。应用内确认更新后会下载自带运行时的 x64 包，因此 framework-dependent 安装也可以升级为自包含安装，无需重新配置组合和壁纸。

当前用户安装：

```powershell
.\install.ps1 -PackagePath . -CreateShortcuts -StartWithWindows
```

安装脚本默认写入 `%LocalAppData%\Programs\SyncWallpaper`，不需要管理员权限。`-StartWithWindows` 只创建屏序自身的当前用户 Run 项。

对于可写的解压目录或标准安装目录，程序根据 `package-manifest.json` 将运行数据保存在安装根目录：`App` 和 `Diagnostics` 是程序文件，`Config`、`Wallpapers`、`Cache`、`Thumbnails` 和 `Logs` 是运行数据。若目录不可写，才使用 `%LocalAppData%\SyncWallpaper`；也可通过 `SYNCWALLPAPER_DATA_ROOT` 指定数据位置。覆盖升级必须保留实际使用的数据目录。

安装脚本只接受当前用户 `%LocalAppData%\Programs` 下的目标目录。其他位置可直接解压运行。卸载默认保留安装目录里的运行数据，详见 [卸载说明](UNINSTALL.md)。

安装不会修改分辨率、刷新率、HDR、DPI、音频、窗口、任务栏、Explorer、服务、计划任务或电源设置。
