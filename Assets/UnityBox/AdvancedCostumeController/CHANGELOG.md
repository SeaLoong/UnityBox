# Changelog

## [0.3.2] - 2026-07-26

### Added

- 新增 `ACCPartGroupMarker`，支持以持久化 Group Name 将任意部件或容器组合为一个开关控制项。

## [0.3.1] - 2026-07-26

### Added

- 新增 `ACCOutfitMarker`，支持将任意对象显式识别为服装根，包括没有独立骨架或网格的原版服装容器。

## [0.3.0] - 2026-07-26

### Added

- 生成菜单根节点会添加 `ModularAvatarMenuInstaller`。
- 新增 `ACCVariantMaterialOverride`，支持为对象变体或无 Mesh 材质变体生成材质替换曲线。
- 新增部件分组、Custom Mixer 部件隔离和 Auto / English / 中文编辑器界面语言。

### Changed

- 服装扫描改为基于骨架与网格结构，不再使用 Ignore Names 名称过滤。
- `Parameter Prefix` 现同时作为主服装 Int 参数、Animator Layer 和生成资产的唯一命名空间。
- 生成资产输出改为按 Avatar 与 Parameter Prefix 隔离，并在重新生成当前命名空间时清理旧资产。
- Custom Mixer 现在要求启用 Parts Control，且进入混搭时显式激活服装本体。
- 材质替换曲线合并进服装与 Mixer 变体切换 Clip，不再创建单独材质 Layer。
- 修复仅勾选变体时主参数默认值与 Animator 默认状态不一致的问题。
- 修复层级中同名 Mixer 变体组的 Layer / AnimationClip 命名冲突。
- 输出目录现仅接受安全的 `Assets/...` 相对路径，防止生成清理越界。
- 拒绝不含字母或数字的 Parameter Prefix，以及空的 Custom Mixer Name。
- 修复对象名称含 `/` 时菜单节点在重新生成中被误当作层级路径的问题。

## [0.2.1] - 2026-06-16

### Changed

- 当时不再生成 `ModularAvatarMenuInstaller` 组件（已在 0.3.0 恢复）
- 生成的 Animator Controller 文件名改为跟随服装根对象名称
- 参数前缀默认改为跟随服装根对象名称
- 服装切换参数名默认改为跟随服装根对象名称

## [0.2.0] - 2026-02-23

### Added

- 生成路径自动追加当前 Avatar 名称作为子目录，防止切换不同 Avatar 生成时互相覆盖

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
