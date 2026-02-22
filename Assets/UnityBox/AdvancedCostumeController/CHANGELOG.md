# Changelog

## [0.1.1] - 2026-02-23

### Fixed

- 修复服装变体未能正确识别的问题：移除 `Scanner.FindOutfits` 中对兄弟节点的 `HasMeshChild` 额外检查，恢复与原始实现一致的变体检测逻辑。深层嵌套 Mesh 的变体现在可以被正确发现。

## [0.1.0] - Initial Release

### Added

- 从 CostumesRoot 层级结构自动扫描服装、变体和部件
- 编辑器窗口预览与勾选控制
- 自动生成 Modular Avatar 菜单和 Animator Controller
- 部件独立开关控制（Parts Control）
- 混搭模式（Custom Mixer）
- 默认服装自动识别（按关键词匹配）
- 忽略名称过滤
