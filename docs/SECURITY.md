# RC1 安全与隐私审计

- 配置、日志、壁纸和诊断默认只写当前用户 LocalAppData。
- 日志会将用户目录替换为 <user>；硬件报告对序列号、ContainerId、monitorDevicePath、InstanceName、AdapterId 和 StableId 使用一致性 SHA-256 哈希。
- 默认不联网、不下载在线壁纸、不运行远程控制、不加载插件。
- 默认不请求管理员权限；独立宿主通过版本化 JSON IPC 通信，协议错误、心跳超时和崩溃会隔离到模块。
- 配置文件名禁止路径穿越，JSON 最大 10 MiB、最大深度 32；保存使用临时文件和持久化刷新。
- 壁纸应用只接受已知活动 monitorDevicePath，未知身份、同分歧义、缺失资产和回读失败均保持现状，不写入黑色/纯色兜底。
- 安装脚本只允许 %LocalAppData%\Programs 下的程序目录；卸载默认不删除数据。

仍未完成的安全门禁：真实 Explorer 重启、高权限窗口、Windows 10/11 全矩阵、长时硬件 soak 和远程/插件模块（均默认关闭）。
