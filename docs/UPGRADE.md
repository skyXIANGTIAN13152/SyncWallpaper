# 升级与恢复

屏序只负责发现 GitHub Release，不负责下载、安装、替换程序、关闭主程序或自动回滚。设置页和托盘中的“检查更新”只查询版本信息；用户点击后会打开官方 Release 页面。

## Portable

1. 退出屏序。
2. 在设置页确认实际数据目录，需要时先复制该目录中的 `Config` 和 `Backups`。
3. 从 GitHub Release 下载新的 Portable ZIP。
4. 解压到新目录并启动新的 `App\SyncWallpaper.App.exe`。

## 安装版

1. 从 GitHub Release 下载新版安装程序或发布包。
2. 退出当前屏序。
3. 用户主动运行安装程序/`install.ps1` 完成覆盖安装。
4. 用户配置、壁纸资产和现有开机自启选择位于设置页显示的数据目录，不随程序包替换。

`install.ps1` 仅是用户主动执行的安装脚本；主程序不会调用它，也不会启动 `SyncWallpaper.Updater.exe`。正式发布目录不包含 Updater.exe、更新 staging、更新备份或更新事务恢复工具。

## 配置恢复

配置文件每次原子保存都会保留最多 5 个恢复点，加载顺序是当前文件 → `.bak` → `.bak.1` 至 `.bak.4`。JSON 深度和大小有上限，损坏或超大文件会回退到下一个恢复点；这部分是普通配置保护，与版本更新无关。

开发/诊断环境也可使用 `ConfigurationStore.Restore`，不会覆盖未知未来 schema 的原始备份。
