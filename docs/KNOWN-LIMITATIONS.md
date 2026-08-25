# 已知限制

- 仅支持 Windows 10/11。
- EDID、WMI、ContainerId 和接口字段的可用性取决于显示器、转接器和显卡驱动。
- 两台同型号显示器没有可靠序列号且身份仍有歧义时，需要重新进行 A/B/C 人工确认。
- `IDesktopWallpaper` 的位置模式是全局设置；屏序通过按目标尺寸预渲染减少多屏宽高比差异。
- Span 为尽力而为，需在目标 Windows 版本与实际布局上验证。
- Windows 10、更多混合 DPI 组合、真实睡眠唤醒和 Explorer 强制重启仍需持续硬件验证。
- 应用未签名；首次运行可能出现 SmartScreen 提示。
