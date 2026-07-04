# Changelog

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
