# Privacy

屏序默认不联网。它不包含遥测、分析、广告或在线壁纸下载。

当用户手动点击“检查更新”，或主动开启每周自动检查时，应用才会连接公开的 GitHub Releases API 查询版本信息。请求只包含正常 HTTP 请求所需的 User-Agent、Accept 和 API 版本头；应用不会上传显示器身份、壁纸路径、配置、日志、序列号、用户 ID 或设备指纹。

应用不会自动下载 Release asset，不会执行远端文本或文件，不会自动替换程序、关闭进程、运行安装程序或回滚版本。发现更新后只打开经过白名单校验的 `https://github.com/{owner}/{repository}/releases/...` 页面，由用户自行下载和安装。

Release Notes 按不可信纯文本显示，限制长度，不渲染 HTML，不执行脚本，不加载远程图片或 iframe，也不会自动打开正文中的链接。

配置、日志、壁纸和诊断优先只写可写的程序/项目数据目录；目录不可写时回退到当前用户 LocalAppData。日志会将用户目录替换为 `<user>`，硬件报告对序列号、ContainerId、monitorDevicePath、InstanceName、AdapterId 和 StableId 使用一致性 SHA-256 哈希。日志轮换为单文件 1 MiB、保留七天；导出报告前请自行检查是否包含可识别本机的信息。应用不提权，也不修改其他软件或系统服务的启动项。
