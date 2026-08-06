# Contributing to SyncWallpaper

感谢你对屏序 SyncWallpaper 的兴趣。项目面向 Windows 10/11、.NET 8 和 WPF；提交改动前请先阅读 `README.md`、`docs/ARCHITECTURE.md`、`docs/SECURITY.md` 与 `docs/KNOWN-LIMITATIONS.md`。

## 开始开发

1. 安装 .NET 8 SDK 和 Windows 桌面开发工作负载。
2. 克隆仓库并在根目录执行 `dotnet restore`。
3. 使用 `dotnet build SyncWallpaper.sln -c Release` 构建。
4. 使用 `dotnet test SyncWallpaper.sln -c Release` 运行测试。

不要把 `Config`、`Wallpapers`、`Logs`、`Cache`、`Backups`、`Thumbnails`、`artifacts` 或 `vendor` 中的本机数据提交到 Git。设备路径、EDID 序列号、日志和壁纸文件必须使用脱敏样例。

## 提交改动

- 一个提交尽量只解决一个问题，并在提交说明中说明行为变化。
- 新功能需要单元测试；涉及 Windows API、显示器、音频、Explorer 或进程边界时，同时更新对应文档和已知限制。
- 不要把真实显示器配置、用户壁纸或个人路径写进测试夹具、报告或截图。
- 修改显示配置、音频默认设备或 Shell 行为时，必须保留显式确认门禁和安全回滚。

## Pull Request

Pull Request 请说明改动动机、影响范围、测试命令、Windows 版本，以及尚未验证的真实硬件场景。不要声称已达到 DisplayFusion Pro 对标，除非 `docs/DisplayFusionParityMatrix.md` 中的对应项目已经达到 `Verified`。

安全问题请不要公开创建 Issue，按照仓库的 Security policy 私下报告。
