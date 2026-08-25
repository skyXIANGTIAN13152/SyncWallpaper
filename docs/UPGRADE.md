# 升级

屏序只检查 GitHub Release，不自动下载、关闭程序、替换文件、运行安装器或回滚版本。

1. 从官方 Release 下载并校验 ZIP。
2. 从托盘退出屏序。
3. 只替换程序目录 `App` 和 `Diagnostics`。
4. 保留 `Config`、`Wallpapers`、`Cache`、`Thumbnails` 和 `Logs`。
5. 启动新版本，确认设置页显示正确数据目录和开机自启状态。

`upgrade.ps1` 使用同一安装脚本覆盖程序文件，不会删除运行数据。项目不创建更新 staging、更新备份或配置历史恢复点。
