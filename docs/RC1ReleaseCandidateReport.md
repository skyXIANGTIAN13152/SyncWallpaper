# 屏序 SyncWallpaper 1.0.0-rc.1 验收报告

日期：2026-08-05  
分支：release/1.0.0-rc1  
基线提交：554f123（Beta 1.0.0 baseline）  
RC1 主要提交：1e29f6f、3fe829e

## 已完成

- 正式初始化 Git，忽略构建、缓存、诊断、用户数据和 vendor 参考仓库；发布历史可追溯。
- 壁纸事务增加 Preparing、WaitingForStableTopology、Applying、Verifying、Retrying、RollingBack、Completed、Failed、RollbackFailed、Cancelled、Superseded 状态，所有状态都带 generation、计时、重试和回滚结果。
- TopologyCoordinator 实现单一最新状态队列、单调 generation、过期取消、重复签名去重和手动请求优先级；与显示事件协调器连接。
- 硬件验收中心独立工具，21 步只读向导、脱敏诊断和报告导出。
- 配置文件大小/深度/文件名安全限制、最多 5 个恢复点和显式恢复 API。
- 挂起/恢复状态机、Explorer 退避模型和混合 DPI 布局验证器。
- RC1 x64 framework-dependent/self-contained/portable ZIP 发布脚本、SHA256、当前用户安装/升级/卸载脚本。

## 实际测试

- Release 全量构建：12 个原有项目 + HardwareValidation，0 警告、0 错误。
- 单元测试：125 通过。
- 集成测试：13 通过、2 Skip（当前会话桌面 Shell 视图不可读；真实显示配置回环需要显式环境变量）。
- 只读硬件验收：3 条活动显示路径，0 项前后快照差异；无任何桌面变更。
- 新增 50,000 事件拓扑合并、手动优先、身份脱敏、快照比较、事务状态、挂起恢复、混合 DPI、Explorer 退避、安全模式和 5 个恢复点测试。
- 真实时间 soak：60.6 秒、13 个样本、12 小时门槛为否；另有 100,000 次加速事件，157.3 ms，稳定输出 1 次。

## 未宣称完成

真实物理拔插/睡眠唤醒/Explorer 重启、Win10、混合 DPI 实机、12 小时实时 soak、实际壁纸三屏应用回读、任务栏/标题栏/远程/在线功能仍未达到 Verified。发布包必须标注 unsigned；本报告不声称完全对标 DisplayFusion Pro。
