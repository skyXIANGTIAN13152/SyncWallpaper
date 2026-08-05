# 升级与回滚

    .\upgrade.ps1 -PackagePath . -InstallRoot "$env:LOCALAPPDATA\Programs\SyncWallpaper"

升级脚本只替换程序目录，用户配置和 %LocalAppData%\SyncWallpaper 下的 Wallpapers、Config、Backups、Logs、Cache 保留。已有开机自启选择不被改变。替换采用同目录临时副本和旧目录保护；文件被占用时不会静默删除旧版本。

配置文件每次原子保存都会保留最多 5 个恢复点，加载顺序是当前文件 → .bak → .bak.1 至 .bak.4。JSON 深度和大小有上限，损坏或超大文件会回退到下一个恢复点。

## 手动恢复

主程序设置页可显示恢复点并要求用户确认。开发/诊断环境也可使用 ConfigurationStore.Restore，不会覆盖未知未来 schema 的原始备份。
