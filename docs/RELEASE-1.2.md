# 屏序 SyncWallpaper 1.2.0-beta.8

这是 1.2 系列首次公开发布，集中带来**通用壁纸组合、秘境界面、透明镂空图标和全页面滚动体验更新**。本版本沿用 Beta 预发布渠道，界面为中文。

## 到新地方，少配置一次

- 可以从空白开始创建多套通用组合，添加「横屏1、横屏2……」「竖屏1、竖屏2……」，位置数量没有固定上限，每个位置自行选择壁纸和适应方式。
- 先匹配原来手动设置过的特定显示器组合；没有安全、完整的特定匹配时，再使用符合条件的通用组合。
- **横屏数、竖屏数必须分别完全相同**。例如，通用组合要求 2 横屏，就不会应用到 1 横屏 + 1 竖屏。没有符合条件的组合时，保持所有屏幕的当前壁纸。
- 横竖屏分别按桌面位置从左到右、同列从上到下编号，包含笔记本屏幕。删除中间位置不重排编号；未设置壁纸的位置保留当前壁纸。
- 通用匹配成功后，托盘用独立的菱形状态标记表示。

## 全新的秘境界面

- 深墨绿、古金与暖白配色，搭配静态雾光、月蚀光环和星轨背景；旧主题仍可在设置中选择。
- 七个主页面、组合编辑器、选图、预览与提示窗口统一风格。
- 概览直接展示屏幕排列与当前壁纸；组合列表清楚区分特定匹配、通用匹配和数量不符。
- 软件与托盘图标改为透明镂空双屏轮廓，修复紧凑尺寸下图标被裁切的问题。
- 标题、操作按钮、搜索和筛选固定在顶部，下方内容独立滚动；改善鼠标滚轮、下拉框滚动和小窗口布局。

## 可靠性与数据保留

- 应用前核验整套显示器布局，处理断开、布局变化和 Explorer 恢复；事务失败时尝试恢复已改动的壁纸并报告结果，恢复失败则停止自动切换。
- 所有目标图片先完成渲染，再开始写入桌面，避免后续图片损坏造成半套壁纸应用；损坏的预览图片不会使主界面退出。
- 升级保留现有组合、壁纸和设置；卸载默认保留安装目录里的用户数据。
- 发布包不含开发机上的个人组合、私人壁纸、日志、缓存或测试资料。软件不会自动下载或安装更新。

## 验证

- Windows 本地 Release 构建通过，0 警告、0 错误；188 项自动测试全部通过。
- 6 项隔离卸载检查通过，覆盖默认保留、显式保留、路径边界和文件校验值不变。
- 已实测七个页面的固定操作区、独立滚动、鼠标滚轮与最小窗口布局。
- GitHub 发布流程会重新构建、运行测试和数据保留检查，生成两个 ZIP 与 SHA-256 校验文件。

## 下载与升级

- **推荐：`SyncWallpaper-1.2.0-beta.8-win-x64-selfcontained.zip`**，自带所需运行环境。
- `SyncWallpaper-1.2.0-beta.8-win-x64.zip` 为较小的普通包，需要 .NET 8 Desktop Runtime（x64）。
- 两种下载均提供单独的 SHA-256 文件和汇总 `SHA256SUMS.txt`。

新用户解压后运行 `App/SyncWallpaper.App.exe`。升级前先从托盘退出旧程序：便携版将发布包内容覆盖到原目录（含新版卸载脚本），保留 `Config`、`Wallpapers` 等数据目录；脚本安装的版本按[升级说明](https://github.com/skyXIANGTIAN13152/SyncWallpaper/blob/v1.2.0-beta.8/docs/UPGRADE.md)操作。[首次安装说明](https://github.com/skyXIANGTIAN13152/SyncWallpaper/blob/v1.2.0-beta.8/docs/INSTALLATION.md)也随包提供。

Windows 10/11 x64；本版本未进行代码签名。Windows 10、真实睡眠唤醒、Explorer 强制重启和更多混合 DPI 硬件组合仍需持续验证。

## English summary

The first public 1.2 release adds generic profiles that match exact landscape/portrait counts, a unified Mystery theme, transparent hollow icons, and fixed page actions above independently scrolling content. Specific profiles retain priority, incompatible layouts leave current wallpapers unchanged, and existing profiles and wallpaper files are preserved. This release has a Chinese UI; the English user guide is included.
