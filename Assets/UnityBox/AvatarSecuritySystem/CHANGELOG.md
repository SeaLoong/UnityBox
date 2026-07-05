# Changelog

## [Unreleased]

## [0.7.11] - 2026-07-05

### Changed

- 将 `Assets/UnityBox/AvatarSecuritySystem/Generated` 调整为构建期临时工作目录：
  - 构建开始前自动清理历史生成物
  - 构建后处理阶段自动回收生成目录
  - 整体行为更贴近 NDMF / VRCF 的临时产物模式，减少历史残留导致的体积膨胀
- 优化 `Utils.AddSubAsset()`：
  - 改用 `AssetDatabase.AddObjectToAsset(asset, controller)` 对象重载
  - 移除每次 `LoadAllAssetsAtPath` 的全量扫描，降低大 Controller 场景下的额外内存与遍历开销

### Fixed

- 修正文档与实现不一致的历史遗留问题：修复前 `callbackOrder` 实际按是否存在 NDMF 动态选择（无 NDMF 为 `-1026`；有 NDMF 时晚于 NDMF Optimize `-1025`），而文档所述为固定 `-1026`；本次改为文档所述的固定值（见下方最终方案）
- 修正存在 NDMF 时"混淆所有 Playable Layer"功能实际未生效的问题：`Obfuscator` 内部另一份基于反射的 NDMF 检测从未被赋值，导致 `requireGeneratedPath` 恒为 `true`，NDMF 克隆出的控制器（不在 Generated 目录下）被直接跳过重命名
- 移除运行期反射检测 NDMF 的动态 `callbackOrder`：VRCSDK 回调固定为 `-1024`（仅用于无 NDMF 场景，与 VRCFury 自身的 `RemoveEditorOnlyObjectsHook` 相同 `callbackOrder`，但两者处理内容互不影响，相对顺序不影响结果）；存在 NDMF 时改为通过 NDMF 官方 Plugin API 注册到 NDMF 概念中真正的最后一个 BuildPhase（`PlatformFinish`，在 `Optimizing` 之后），不再依赖 VRCSDK 的 `callbackOrder` 数轴
- 同步更新 README 与技术文档中的构建管线执行顺序说明

## [0.7.1] - 2026-07-05

### Changed

- 默认开启 **Obfuscate all Playable Layers（混淆所有 Playable Layer）** 选项（新建组件与 Reset 均为启用）

## [0.7.0] - 2026-07-04

### Added

- 新增 **Lightweight Mode（轻量模式）** 配置项，用于在尽量贴近绿模的前提下保留防御效果
- 轻量模式支持根据 Avatar 现状继承高开销粒子特性：
  - 若 Avatar 已有 Light，则复用已有 Light
  - 若 Avatar 已有粒子 `Collision` / `Trails`，则 ASS 生成粒子继承对应模块

### Changed

- 防御负载改为以共享 `UB_Defense` 材质的粒子系统为主，不再默认生成 PhysX / Cloth / 独立 `ShaderDefense` 网格树
- 粒子系统采用**最少系统数策略**：
  - 非溢出模式通常仅生成 1 个粒子系统
  - 溢出模式在 Avatar 已有粒子时生成 1 个满系统，否则生成 2 个满系统以保持溢出
- 普通模式下粒子光源优先复用 1 个外部 Light，没有可复用光源时仅补 1 个 fallback Light
- 轻量模式调整为“**不主动新增**高开销特性，但若 Avatar 已经在使用则直接继承”
- 轻量模式、溢出模式及相关提示框/Tooltip 文案全面优化，补充绿模导向说明
- README 与技术文档已同步到当前实现口径

### Removed

- 删除 `Defense.cs` 中已不再使用的旧 PhysX / Cloth / 独立 `ShaderDefense` 生成死代码路径
