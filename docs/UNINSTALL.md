# 卸载

```powershell
.\uninstall.ps1
```

脚本会停止安装目录中的屏序程序，移除程序文件、当前用户启动项和屏序快捷方式。运行数据默认保留，避免意外删除壁纸；如需清理，请先确认数据目录后自行删除 `Config`、`Wallpapers`、`Cache`、`Thumbnails` 和 `Logs`。

卸载不会改变 Windows 显示参数或当前壁纸。
