# 安装

发布包提供 framework-dependent x64 和 self-contained x64 两种 ZIP。前者需要 .NET 8 Desktop Runtime，后者自带运行时。

Portable：解压后运行 `App\SyncWallpaper.App.exe`。

当前用户安装：

```powershell
.\install.ps1 -PackagePath . -CreateShortcuts -StartWithWindows
```

安装脚本默认写入 `%LocalAppData%\Programs\SyncWallpaper`，不需要管理员权限。`-StartWithWindows` 只创建屏序自身的当前用户 Run 项。

本机正式版本部署在 `D:\屏序 SyncWallpaper`；其中 `App` 和 `Diagnostics` 是程序文件，`Config`、`Wallpapers`、`Cache`、`Thumbnails` 和 `Logs` 是运行数据。覆盖升级必须保留这些数据目录。

安装不会修改分辨率、刷新率、HDR、DPI、音频、窗口、任务栏、Explorer、服务、计划任务或电源设置。
