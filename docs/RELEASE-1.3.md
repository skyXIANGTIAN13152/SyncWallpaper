# 屏序 SyncWallpaper 1.3.0-beta.1

这次更新加入**每日检查新版本、确认后自动下载与替换程序**，同时补齐组合编辑窗口的最大化／还原操作。沿用中文界面和 Beta 预发布渠道。

## 检查、确认、自动完成更新

- 新安装默认每天检查官方 GitHub Releases；已有安装保留原来的检查开关和更新渠道。设置页和托盘也可以手动检查。
- 发现新版后先展示版本和更新说明，由你选择「立即更新」或「稍后」。选择稍后不会下载或替换文件，同一版本的自动提示一天内不重复。
- 组合编辑器或其他对话框打开时延后提示，避免打断尚未保存的编辑。
- 确认后下载对应语言的 Windows x64 自包含包，显示进度，校验 SHA-256、版本和包内路径，再自动重启完成替换。下载阶段可以取消。
- 更新助手仅在更新时运行。它等待旧程序退出，保留旧程序供替换或启动失败时回滚；成功后清理本次更新临时文件。
- 原有组合、壁纸、设置及其他运行数据保留。自动更新只替换程序组件与发布清单，保留专用桌面图标。

## 更新渠道和界面修复

- Stable 只接收正式版；Beta 同时接收正式版和测试版。**想收到本次及后续 Beta 更新，请在设置中选择 Beta。**
- 两种渠道都从发布列表筛选同语言的合适版本；仓库暂无稳定版时明确显示结果，不再误报网络错误。
- 检查成功后至少间隔 24 小时，网络失败至少间隔 1 小时重试；时钟回拨不会造成重复请求。
- 组合编辑窗口新增最大化／还原按钮，支持双击标题栏切换；初始尺寸适应当前屏幕工作区，最大化保留任务栏空间。
- 自动检查开关的说明会立即随开关状态刷新。

## 下载与首次升级

- **推荐：`SyncWallpaper-1.3.0-beta.1-win-x64-selfcontained.zip`**，自带运行环境。
- `SyncWallpaper-1.3.0-beta.1-win-x64.zip` 是普通包，需要 .NET 8 Desktop Runtime（x64）。
- 两个 ZIP 均提供独立 `.sha256` 文件和汇总 `SHA256SUMS.txt`。

**从 1.2 或更早版本升级到本版，需要先手动安装一次。** 旧版没有本次新增的下载和替换能力。

从托盘退出旧版，把新发行包内容覆盖到原目录，保留 `Config`、`Wallpapers` 等数据目录；不要先删除整个旧目录。脚本安装的版本按[升级说明](https://github.com/skyXIANGTIAN13152/SyncWallpaper/blob/v1.3.0-beta.1/docs/UPGRADE.md)操作。新用户解压后运行 `App/SyncWallpaper.App.exe`。

## 验证与适用范围

- 本地自动测试覆盖更新调度、版本与渠道选择、下载取消、损坏或非法更新包、程序文件占用、替换中断、新版启动失败和回滚。
- 已验证真实更新助手的退出、替换和重新启动流程，并核对原有组合绑定、壁纸文件与专用图标保留。
- GitHub 发布流程在 Windows 上重新构建、运行测试和 6 项卸载数据保留检查，再生成安装包与 SHA-256 文件。

适用于 Windows 10/11 x64。本版尚未进行代码签名；发布包不含个人组合、壁纸、日志、缓存或临时测试数据。

## English summary

This Chinese Beta release adds daily GitHub release checks and user-confirmed download, verification, installation and restart. Existing profiles, wallpapers and settings are preserved; failed replacement or startup can roll back to the previous program. The profile editor also gains maximize/restore controls and DPI-aware initial sizing. Versions before 1.3 require one manual upgrade to receive the new in-app updater.
