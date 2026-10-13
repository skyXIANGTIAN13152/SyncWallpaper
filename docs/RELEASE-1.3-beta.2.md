# 屏序 SyncWallpaper 1.3.0-beta.2

通用组合与特定组合现在拥有各自的镂空图标，在组合列表和编辑窗口中都能更快分辨。本次沿用中文界面和 Beta 预发布渠道。

## 两种组合，两种标识

- **通用组合**：开放的四角边框，内含横屏与竖屏轮廓；秘境主题使用浅绿色。
- **特定组合**：闭合的金色菱形，内含显示器轮廓，突出与已识别显示器的固定关联。
- 组合类型文字与对应图标同色，其他主题也会使用适合该主题的配色。
- 组合列表与编辑器右上角类型徽标同步更新。应用图标、托盘图标及匹配规则保持原样。
- 原有组合、壁纸文件和设置继续保留。

## 下载与升级

- **推荐 EXE 安装包**：[SyncWallpaper-1.3.0-beta.2-win-x64-setup.exe](https://github.com/skyXIANGTIAN13152/SyncWallpaper/releases/download/v1.3.0-beta.2/SyncWallpaper-1.3.0-beta.2-win-x64-setup.exe)，自带运行环境，按向导安装，无需管理员权限。
- **自包含便携版**：[SyncWallpaper-1.3.0-beta.2-win-x64-selfcontained.zip](https://github.com/skyXIANGTIAN13152/SyncWallpaper/releases/download/v1.3.0-beta.2/SyncWallpaper-1.3.0-beta.2-win-x64-selfcontained.zip)。
- **普通便携版**：[SyncWallpaper-1.3.0-beta.2-win-x64.zip](https://github.com/skyXIANGTIAN13152/SyncWallpaper/releases/download/v1.3.0-beta.2/SyncWallpaper-1.3.0-beta.2-win-x64.zip)，需要 .NET 8 Desktop Runtime（x64）。
- 三个下载均附独立 `.sha256` 文件，汇总校验值见 `SHA256SUMS.txt`。

已使用 1.3.0-beta.1 的用户，可在设置中选择 **Beta** 渠道并检查更新，确认后由应用自动下载和升级。从更早版本升级，请使用安装包并核对原安装目录，或按[升级说明](https://github.com/skyXIANGTIAN13152/SyncWallpaper/blob/v1.3.0-beta.2/docs/UPGRADE.md)操作。

## 验证与适用范围

本次图标已在实际组合列表、通用组合编辑器及特定组合编辑器中验证，14 项主题检查通过。GitHub 发布流程会重新构建并运行完整自动测试、卸载数据保留检查及安装器生命周期检查，全部通过后才公开安装包。

适用于 Windows 10/11 x64，尚未进行代码签名。发行包只包含程序和文档，不包含个人组合、壁纸或运行数据。

## English summary

Generic and display-specific profiles now have distinct hollow icons in the profile list and editor badges. The Mystery theme uses sage green for generic profiles and champagne gold for specific profiles, with theme-aware alternatives elsewhere. Existing profiles, wallpapers, settings and matching rules are preserved. This Chinese Beta release includes a current-user EXE installer and two portable ZIP packages.
