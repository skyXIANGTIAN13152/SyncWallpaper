# 本轮更新检查器验收报告

## 版本与范围

- 分支：`release/1.0.0-rc2`
- 基线 commit：`c12796e`
- 当前程序集版本读取：`AssemblyInformationalVersion`，由 `Directory.Build.props` / SDK 生成；当前发布版本为 `1.0.0-rc.2`。
- 本轮只实现 GitHub Releases 版本检查和必要的数据绑定，没有重做现有 UI 视觉，也没有增加动态壁纸、音频或任务栏功能。

## GitHub 配置

唯一配置位置：`src/SyncWallpaper.Update.Core/UpdateModels.cs` 的 `ProjectLinks.GitHubOwner` 与 `ProjectLinks.GitHubRepository`，当前为 `skyXIANGTIAN13152/SyncWallpaper`。更新检查默认关闭，开启后只查询 GitHub Releases。

## 新增与修改

新增：

- `src/SyncWallpaper.Update.Core/`：GitHubReleaseChecker、SemVer、Stable/Beta 选择、URL 白名单、Release Notes 纯文本限制、每周调度策略。
- `tests/SyncWallpaper.Update.Tests/`：9 项模拟 HTTP/版本/策略安全测试。
- `docs/UPDATE-CHECKER.md`、`.github/workflows/release.yml`、`tools/release.ps1`。

修改：

- `AppRuntime`、`MainWindow`、`App.xaml.cs`：设置页和托盘手动检查/每周选项、结果展示和安全打开 Release 页面。
- `AppSettings`：只增加更新检查开关、渠道和本地时间戳，默认关闭。
- `publish.ps1`、`install.ps1`、`docs/UPGRADE.md`、README/隐私/安全/用户指南/CHANGELOG：明确用户主动安装边界和包内容。
- `SyncWallpaper.sln`：加入 Update.Core 与 Update.Tests。

## 已停用的自动更新入口

仓库中没有 `SyncWallpaper.Updater` 项目或主程序调用。没有自动下载、自动关闭、自动替换、自动解压、自动安装、健康检查、更新 staging、更新备份、更新事务恢复或版本自动回滚路径。普通 `ConfigurationStore` 配置 `.bak` 恢复和壁纸/显示配置事务回滚未删除。

## API、渠道与隐私

- Stable 查询 `/releases/latest`，忽略 draft/prerelease。
- Beta 查询 Release 列表，忽略 draft，接受 prerelease，按 SemVer 取最高版本。
- 请求只使用 HTTPS、15 秒超时、取消令牌、2 MiB 响应上限和复用 HttpClient；不读取 asset、不上传用户数据。
- 自动检查默认关闭；开启后最多每 7 天一次，使用 UTC；手动检查不受周期限制，同一时间只允许一个请求。
- Release Notes 作为限制长度的纯文本，不执行 HTML/脚本、不加载资源、不自动打开正文链接。
- 浏览器只打开 host 为 `github.com`、HTTPS、路径属于配置仓库 `/owner/repository/releases/` 的 URL。

## 测试与构建

- Debug/Release 核心单元：`129` 通过；集成 `13` 通过、`2` 跳过；更新检查 `9` 通过。
- Release 构建：0 错误、0 警告；测试未执行任何真实显示模式变更。
- Debug/Release solution build：0 error、0 warning。
- `tools/release.ps1`：restore、Debug build/test、Release build/test、framework-dependent publish 全部通过。
- 单元测试使用模拟 `HttpMessageHandler`，不依赖真实 GitHub 网络。

## 发布包

当前生成的包（未配置正式签名）：

- `artifacts/publish/SyncWallpaper-1.0.0-rc.2-win-x64.zip`
  - SHA-256：`494e0d98010c3846b1004892dda4f3fe95e2ecb758a505d5655503263e1e35be`
- `artifacts/publish/SyncWallpaper-1.0.0-rc.2-win-x64-selfcontained.zip`
  - SHA-256：`44e34a6603fce7b992c9a0cfaf83c906eb9d7163020c24b977d459aab7affec1`
- `artifacts/publish/SHA256SUMS.txt` 同时列出两种 ZIP 的 SHA-256。

包内容为 App、Host、Diagnostics、HardwareValidation、docs、许可证、CHANGELOG、安装脚本和 manifest；没有 `SyncWallpaper.Updater.exe`，已对发布目录执行文件名检查。当前仓库没有 MSI/Setup 生成器，安装版仍由用户主动运行安装脚本或未来正式安装器完成。

## 已知限制与发布前事项

1. 已填写 `ProjectLinks` owner/repository；真实 API 的网络可用性仍取决于用户环境，单元测试覆盖 200、404、403/限流和异常 URL。
2. GitHub Actions 当前上传包和校验和，不包含自动安装器；正式签名/SignPath 尚未配置。
3. 真实 Release 页面、TLS/DNS 和默认浏览器属于用户环境，单元测试不替代网络现场验证。
4. 当前版本为 `1.0.0-rc.2`；本轮包含公开仓库发布元数据和多组合壁纸功能。
