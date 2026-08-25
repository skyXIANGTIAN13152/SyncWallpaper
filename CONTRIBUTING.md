# Contributing to SyncWallpaper

感谢你对屏序 SyncWallpaper 的兴趣。项目只面向 Windows 10/11 多显示器壁纸识别与恢复，使用 .NET 8 和 WPF；提交改动前请先阅读 `README.md`、`docs/ARCHITECTURE.md`、`docs/SECURITY.md` 与 `docs/KNOWN-LIMITATIONS.md`。

## 开始开发

1. 安装 .NET 8 SDK 和 Windows 桌面开发工作负载。
2. 克隆仓库并在根目录执行 `dotnet restore`。
3. 使用 `dotnet build SyncWallpaper.sln -c Release` 构建。
4. 使用 `dotnet test SyncWallpaper.sln -c Release` 运行测试。

不要把 `Config`、`Wallpapers`、`Logs`、`Cache`、`Thumbnails` 或 `artifacts` 中的本机数据提交到 Git。设备路径、EDID 序列号、日志和壁纸文件必须使用脱敏样例。

## 提交改动

- 一个提交尽量只解决一个问题，并在提交说明中说明行为变化。
- 新功能需要单元测试；涉及 Windows 显示 API、Explorer 或壁纸事务时，同时更新对应文档和已知限制。
- 不要把真实显示器配置、用户壁纸或个人路径写进测试夹具、报告或截图。
- 不接受修改分辨率、刷新率、HDR、DPI、音频、窗口、任务栏或 Shell 行为的功能；显示器 API 在本项目中只用于读取身份与状态。

## Pull Request

Pull Request 请说明改动动机、影响范围、测试命令、Windows 版本，以及尚未验证的真实显示器场景。

安全问题请不要公开创建 Issue，按照仓库的 Security policy 私下报告。
