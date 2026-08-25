# GitHub Releases 更新检查器

屏序的更新功能是“发现版本 + 打开 Release 页面”，不是安装器。主程序永远不下载或执行 Release asset。

## 组件

- `SyncWallpaper.Update.Core`：`GitHubReleaseChecker`、`SemanticVersion`、`ReleaseVersionComparer`、`ReleaseUrlValidator`、`ReleaseNotesSanitizer` 和每周调度策略。
- `AppRuntime.CheckForUpdatesAsync`：把检查结果写入本地设置和诊断日志；网络异常不会改变壁纸状态。
- `MainWindow` 设置页：显示当前版本、渠道、上次检查、更新说明和“前往 GitHub Release”。
- 托盘菜单：提供“检查更新”和“查看 GitHub 项目”，没有下载、安装、重启或 Updater 菜单项。

## 仓库配置

`src/SyncWallpaper.Update.Core/UpdateModels.cs` 中的 `ProjectLinks.GitHubOwner` 和 `ProjectLinks.GitHubRepository` 是唯一配置点，当前值为 `skyXIANGTIAN13152` / `SyncWallpaper`。更新检查仍默认关闭；关闭时不会联网。

## 请求与渠道

- Stable：`GET https://api.github.com/repos/{owner}/{repo}/releases/latest`，接受非 draft、非 prerelease 的 SemVer tag。
- Beta：`GET .../releases?per_page=100`，忽略 draft，允许 prerelease，按 SemVer 选择最高版本。
- 请求头包含 `Accept: application/vnd.github+json`、`X-GitHub-Api-Version: 2022-11-28` 和 `User-Agent: SyncWallpaper/{informational-version}`。
- 15 秒超时、CancellationToken、复用单一 `HttpClient`、2 MiB 响应上限；不读取或下载 Release assets。
- 当前版本来自程序集 `AssemblyInformationalVersion`，支持 `v1.2.3`、`1.0.0-beta.1`、`v1.0.0-rc.2`；同版本和降级版本均不提示。

## 周期策略

自动检查默认关闭。用户打开后最多每 7 天一次，使用 UTC；系统时钟回拨时不会立即重复请求。手动点击不受 7 天间隔限制；同一时间只保留一个检查任务。自动检查失败静默记录，不弹错误、不改变托盘核心状态。

## URL 和 Release Notes

只允许 `https://github.com`，且路径必须属于配置仓库的 `/owner/repository/releases/`。API URL、asset 直链、HTTP、其他域名、其他仓库和异常路径都不会交给浏览器。打开浏览器使用 `ProcessStartInfo.UseShellExecute`，不经过 cmd、PowerShell 或字符串命令拼接。

Release Notes 作为不可信纯文本交给 WPF `TextBlock`，上限 12,000 字符；不启用 Markdown/HTML、脚本、图片、iframe，不自动打开正文链接。

## 产品边界

当前没有 `SyncWallpaper.Updater` 产品调用，也不发布 Updater.exe。不存在更新 staging、更新备份、更新事务恢复、自动关闭主程序、自动替换文件、健康检查或自动回滚流程。产品也不生成普通配置历史备份。
