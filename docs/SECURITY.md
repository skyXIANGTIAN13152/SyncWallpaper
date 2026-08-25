# 安全边界

- 显示器 API 只读。项目不调用 `SetDisplayConfig` 或 `ChangeDisplaySettingsEx` 修改显示参数。
- 壁纸只应用到当前活动且已唯一匹配的 monitorDevicePath；歧义、弱身份、缺失资产或验证失败均保持现状。
- 配置文件名禁止路径穿越，JSON 最大 10 MiB、最大深度 32；保存使用临时文件、磁盘刷新和原子替换。
- 不生成配置历史、壁纸删除备份、更新 staging 或自动回滚包。
- 默认不联网；GitHub 请求使用 HTTPS、15 秒超时、取消令牌和 2 MiB 响应上限。
- 浏览器只允许打开配置仓库的 `https://github.com/.../releases/` 页面；Release Notes 只显示为限长纯文本。
- 不加载插件、不运行脚本、不注入 Explorer、不创建额外宿主进程、不请求管理员权限。
- 日志与只读诊断默认保存在本机，不自动上传。

安全问题请通过 GitHub Security Advisories 私下报告，不要公开粘贴原始设备路径、序列号、日志或配置。
