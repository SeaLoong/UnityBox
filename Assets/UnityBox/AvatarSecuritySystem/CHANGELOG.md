# Changelog

历史条目保留发布时的行为；当前使用流程见 Editor/README.md 和技术参考。

## [Unreleased]

### Added

- 独立 Unity Animator 运行后端、StandaloneSecurityBuilder 与 Inspector 生成按钮。
- Unity 场景构建处理器在构建副本移除作者配置，保留实际运行驱动和源场景配置。
- StandaloneSecurityInput 提供 SetGesture、解锁/超时查询及 Restart。
- 无曲线生命周期层，支持解锁后重新锁定与超时后的新一轮输入。
- AnimatorOverrideController 展开及独立内存 Controller 持久化副本。
- 实际 Animator 时间线测试、多 Avatar 资源生命周期用例及 16/64/256 子对象性能样本。

### Fixed

- 生成层显式使用 syncedLayerIndex=-1，修复默认同步到第0层而导致功能状态机不独立的问题。
- 持久化完整生成状态图；已经拥有资源路径的对象不再重复 AddObjectToAsset。
- 每个 Avatar 使用自己的 Build_<id> 输出目录，SDK 后处理只回收对应目录。
- 构建快照保存/恢复对应 Avatar 的名称映射和资源跟踪，支持交错的多 Avatar 后期处理。
- 生命周期 LayerControl 的兄弟层目标在层插入后按最终名称刷新。
- 旧层清理将内部键与显示名成对管理，包含生命周期层，重新生成和普通用户层共存时继续正确执行。
- Contact 根分支索引由一次扫描生成，锁定与恢复图复用索引。
- 补齐 Editor 脚本、程序集及正式包目录的固定 metadata，保留已有资源 GUID。
- 从原场景三处 ASS 配置恢复 Runtime/Component.cs.meta 的原 GUID
  1c51e6a139920bb4ca12700dc82627ff，保持原场景内容和引用。

### Integration

- 普通 Unity 核心与 VRChat/NDMF 可选适配保持独立；包安装不强制获取 SDK。
- SDK 使用原生 ParameterDriver、PlayAudio、LayerControl，并本地注册 Restart 请求。
- 新组件/Reset 默认关闭遗留主动负载；普通 Unity Builder 生成手势工作流。
- 测试、实际构建和性能结果汇总到仓库 Docs/Testing.md 与 Docs/Performance.md。

## [0.7.23] - 2026-08-11

### Fixed

- 正式恢复 ASS 全部生成状态使用 WD On；WD On 与 VRCFury SPS 的兼容性已通过 Himaku A/B 验证
- 恢复 PasswordInput、Countdown、Audio、Lock 终态的自禁用，并在层追加后按 StateMachine 反查索引、后置构建阶段再次刷新索引
- 保留包含 VRC Contact 的 Avatar 跳过 ASS Playable 混淆，以及 Lock 对 Contact 分支的 Transform 保护

## [0.7.21] - 2026-08-10

### Fixed

- 未启用 Avatar 锁定时不再生成 `ASS_Lock` 层；密码输入、Overlay 和音频反馈仍由各自功能层生成
- Lock 层移除 Overlay/音频绑定并固定 WD Off，仅在锁定/恢复阶段写入显式缩放，减少对其它插件动画属性的影响
- 成功音效和警告音效对象默认关闭；PasswordInput/Audio 层按需启用，成功音效播放完成后进入 Completed 状态关闭对象
- 混淆伪状态和诱饵层统一使用 WD Off，避免伪装状态通过未绑定属性写入默认值
- 移除 PasswordInput/Countdown/Audio/Lock 完成状态的 `VRCAnimatorLayerControl` 自释放，避免构建链重排时误关 Contact/衣柜等第三方 FX 层；完成状态改为仅清理自身反馈曲线
- 锁定时保护包含 VRC Contact Sender/Receiver 或其 `rootTransform` 骨骼引用的分支：仅关闭 Renderer，不把 Contact Transform 缩放为 0，避免接触驱动插件失效
- 检测到 VRC Contact 时跳过 ASS Playable Layer 混淆，避免破坏 VRCFury 为 HapticContacts/WorldScaleDetector 生成的 `AllReceiversOff` 恢复图

## [0.7.20] - 2026-08-08

### Fixed

- 修复 0.7.19 中解锁动画持续写入根子对象 `m_IsActive`，导致 ACC、衣柜或部件切换功能被固定值覆盖的问题
- 解锁流程拆分为一次性的缩放恢复状态和空的 WD Off 稳定状态：只在恢复采样中写入 `localScale`，随后将控制权交还给外部动画层

## [0.7.19] - 2026-08-08

### Fixed

- 修复未解锁状态下输入正确密码后，部分 Avatar 根级子对象仍保持隐藏的问题
- 解锁动画显式恢复 WD On 路径下被锁定层写入的 `m_IsActive` 与 `localScale`

## [0.7.17] - 2026-07-11

### Changed

- 防御共享材质名称改为走混淆映射，开启混淆时不再生成可识别的 `ASS_SharedDefenseMat`
- 提高防御粒子 aggressive profile：增强粒子速度、发射范围、噪声、纹理帧动画、力场、速度关联缩放/旋转与渲染尺寸等配置
- `IsLocal` 参数为 Float / Int 时改为兼容性提示，说明过渡条件已自动适配，不再输出警告

## [0.7.16] - 2026-07-11

### Fixed

- 修复 NDMF 子程序集直接引用 `ASSComponent` 导致缺少 Runtime 程序集引用的编译错误
- NDMF 配置捕获改为通过 `ASSConfigData.FromAvatar()` 间接读取，避免 NDMF 插件源码直接依赖 Runtime 类型

## [0.7.15] - 2026-07-11

### Changed

- 将 ASS 构建配置读取改为构建早期快照的纯数据流：
  - NDMF 路径在 `BuildPhase.Resolving` 捕获配置，并在 `PlatformFinish` 直接传入生成流程
  - 无 NDMF 路径新增早期 VRCSDK preprocess 快照，避免后续 `IEditorOnly` 清理导致配置缺失
  - 各生成器改为依赖 `ASSConfigData`，不再要求构建后期 `ASSComponent` 仍存在

### Fixed

- 修复 NDMF / VRCSDK 构建链中 `ASSComponent : IEditorOnly` 被提前移除后，ASS 主功能完全跳过的问题
- 修复二次上传时临时 `Generated` 控制器引用可能残留，导致后续构建找不到有效 FX / Playable Controller 的问题
- 修复关闭 Overlay 后仍生成指向缺失 Overlay 对象的动画曲线，导致后续功能异常的问题
- 防御生成失败时不再静默继续构建，避免上传成功但防御层实际缺失

## [0.7.12] - 2026-07-06

### Changed

- 调整 NDMF 构建路径职责：
  - `NDMFPlugin` 仅负责 ASS 主功能层生成（`ProcessAvatar`）
  - Playable 混淆改为独立后置流程执行，避免与下游处理链交叉覆盖
- 新增独立的 `PlayableObfuscationProcessor`，并将混淆回调顺序设置为 `int.MaxValue - 1`，使其稳定处于上传前的尾部阶段
- 统一 NDMF 场景的执行策略，并保留 `AfterPlugin("jp.lilxyzw.lilycalinventory")` 兼容约束

### Fixed

- 修复 NDMF/VRCF 组合场景下，Playable 混淆可能早执行后又被覆盖，导致 FX 等层看似“混淆失效”的问题
- 修复混淆流程误复用 `ShouldProcessAvatar` 造成的错误短路：即使密码/手势逻辑不生成，也能按开关正确执行 Playable 混淆

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
