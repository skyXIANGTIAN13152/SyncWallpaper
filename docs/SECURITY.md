# RC2 安全与隐私审计

- 配置、日志、壁纸和诊断优先只写可写的程序/项目数据目录；目录不可写时回退到当前用户 LocalAppData。
- 日志会将用户目录替换为 <user>；硬件报告对序列号、ContainerId、monitorDevicePath、InstanceName、AdapterId 和 StableId 使用一致性 SHA-256 哈希。
- 默认不联网、不下载在线壁纸、不运行远程控制、不加载插件。
- 默认不请求管理员权限；独立宿主通过版本化 JSON IPC 通信，协议错误、心跳超时和崩溃会隔离到模块。
- 配置文件名禁止路径穿越，JSON 最大 10 MiB、最大深度 32；保存使用临时文件和持久化刷新。
- 壁纸应用只接受已知活动 monitorDevicePath，未知身份、同分歧义、缺失资产和回读失败均保持现状，不写入黑色/纯色兜底。
- 安装脚本只允许 %LocalAppData%\Programs 下的程序目录；卸载默认不删除数据。

## 更新检查安全边界

- 产品只实现 GitHub Releases 版本检查，不包含自动下载、自动安装、独立 Updater、健康检查、staging/backup/rollback 更新事务或自动回滚入口。
- API 请求仅使用 HTTPS、15 秒超时、CancellationToken 和 2 MiB 响应上限；稳定渠道查询 `/releases/latest`，Beta 渠道读取 Release 列表并按 SemVer 选择最高的非草稿版本。
- `draft` 永远忽略；Stable 忽略 `prerelease`；版本只来自程序集 informational metadata，不维护第二份版本号；远端低于或等于当前版本不会提示。
- 浏览器打开前只接受 host 为 `github.com`、scheme 为 `https` 且路径属于集中配置仓库 `/owner/repository/releases/` 的 URL。API URL、asset 直链、HTTP、其他域名和其他仓库均不会打开。
- Release Notes 作为纯文本显示，限制长度，不渲染 HTML/脚本，不加载外部资源，不执行其中命令。网络失败只更新更新检查结果和本地日志，不改变壁纸服务状态。
- `ProjectLinks` 固定指向公开仓库 `skyXIANGTIAN13152/SyncWallpaper`；用户数据、壁纸、日志和硬件报告不会提交到仓库，也不会上传到 GitHub。

仍未完成的安全门禁：真实 Explorer 重启、高权限窗口、Windows 10/11 全矩阵、长时硬件 soak 和远程/插件模块（均默认关闭）。
