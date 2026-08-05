# Profile matching

配置档案保存 `SchemaVersion`、期望显示器数量、优先级、最小置信度、自动应用开关和逻辑角色。内置模板为 `Laptop Only`、`Three Monitor Setup`，也可以创建 `Custom`。

角色是 `Laptop`、`Landscape`、`Portrait` 或自定义名称。每个角色绑定稳定 MonitorIdentity、壁纸资产/路径、填充模式、是否允许自动重新绑定、最后成功匹配时间和备注。

匹配器先按显示器数量筛选，再对角色与实际显示器做一对一分配。稳定 serial/container/path 为 Exact/Strong；只有硬件拓扑或几何线索时为 Probable；无法区分时为 Ambiguous/Unknown。最高得分与第二名接近，或同型号无序列号出现多个相同分配时，不会猜测。

旧 `SchemaVersion=1` 文件会先在内存迁移到 V2，保存前保留角色和资产引用。迁移是幂等的，未知字段不会被主动删除。
