# 1.1.0-beta.1 壁纸专版验收

日期：2026-08-25

## 产品范围

发布版只包含多显示器发现/识别、壁纸档案库、壁纸组合、自动匹配与应用、托盘、日志、启动项、只读诊断和手动 GitHub 更新检查。

显示器的分辨率、刷新率、HDR、DPI、方向、接口和硬件身份仍完整读取；所有显示参数写入能力已经移除。

## 已移除

显示配置写入、音频、窗口区域/规则/热键、桌面图标、独立副屏任务栏、Shell、屏保、远程、在线壁纸、模块管理和通用硬件验收中心。

## 构建与测试

- Release solution build：0 warnings / 0 errors。
- Unit tests：67 passed。
- Update tests：9 passed。
- Windows integration tests：4 passed。
- 总计：80 passed / 0 failed / 0 skipped。
- 真实三屏环境曾成功读取并按稳定身份应用 3/3 台壁纸；当天日志保留完整事务记录。
- 最终部署时仅连接笔记本本体，冷启动后自动匹配并应用 1/1 台，无需手动点击。
- 1000 次只读显示器发现耗时 7.47 秒，句柄增量 4；后台静置 15 秒 CPU、句柄和 Private Bytes 均无增长。

## 发布内容

`publish.ps1` 只发布 `App` 与 `Diagnostics`。ZIP 包含许可证、说明、文档和安装脚本，不包含用户数据、额外宿主、自动安装器或配置备份。

最终发布文件：

- `SyncWallpaper-1.1.0-beta.1-win-x64.zip`
  - SHA-256：`1434208fafda535948cf737f4d83eda6d96d50ef045d4a7f401085a2c9bd5a36`
- `SyncWallpaper-1.1.0-beta.1-win-x64-selfcontained.zip`
  - SHA-256：`4a51acfb14eaebdead09794640e652fd2ce23c05c6c3ecc4ba028151bffcc2d3`

正式后台进程静置样本：Working Set 129,822,720 bytes，Private Bytes 60,276,736 bytes，Handle Count 1,060，15 秒 CPU 增量 0。

清理退役模块、旧 RC 包、旧验收快照、临时诊断工具、自包含解包目录和可再生成的 `bin/obj` 后，共释放 402,968,307 bytes（384.30 MiB）。`Config`、`Wallpapers`、`Logs` 和当前渲染缓存未删除。
