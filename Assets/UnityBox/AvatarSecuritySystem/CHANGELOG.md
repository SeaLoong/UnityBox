# Changelog

历史条目保留发布时的行为；当前使用流程见 Editor/README.md 和技术参考。

## [0.9.7] - 2026-11-08

### Fixed

- Inspector 在提交时间编辑时限制保持时间至少比容错长 0.01s，并同步约束最大等待；默认保持 0.5s、容错 0.3s。旧非法值打开和绘制时保留，使用显式“修正时间”提交并支持 Undo/Redo。
- 词池与 Clip/状态名称映射保持 0.9.6 原样；本次只修复时间 UI 约束和混淆开关对应的实际输出。
- 普通 Unity 与 SDK 构建共用本包原 Editor 程序集中的 `ObfuscationGraph`，普通 Unity 的诱饵层、诱饵状态选项现在生成对应内容。启用的诱饵图稳定分配诱饵 Clip，条件参数与 WD On 策略一致。
- 普通 Unity 对已分配的生成 Controller 再次构建时，按持久化的 ASS 层标记清理旧输出，反复生成不叠加；关闭混淆或诱饵后重建会移除旧诱饵，用户层保留。
- 元数据曲线路径选择 Avatar 中不存在的专属根，避免命中真实对象。源 Controller、Scene/Prefab、脚本 GUID 及音频设置保留。
- 保留 Contact 兼容保护：含 Contact 的 Avatar 跳过第三方 Playable 图改名，ASS 自有名称与诱饵继续混淆。完整 Playable 混淆在 VRC SDK 晚期回调处理，单独 NDMF bake/preview 不增加全图改名。
- 混淆只作用于构建副本；更新后正常构建重新生成即可。
- 时间 UI、保存重载后的诱饵输出与构建路径记录见[时间与混淆验证](../../../Docs/Verification/ASSTimingObfuscation/README.md)。

## [0.9.6] - 2026-11-08（历史发布）

### Fixed

- ASS 所有新生成状态统一使用 WD On，包括密码、计时、音频、生命周期、释放、防御、混淆、诱饵和新建默认 Idle；不根据原 FX 自动匹配，也不新增设置。
- 修复原 WD On 表情层切回空闲状态时，追加的 ASS WD Off 层使 BlendShape 无法恢复默认值的问题。修复前 Unity 对照中，权重 85 在空 Neutral 中残留；ASS 新状态使用 On 后恢复为 0。
- 修复 Countdown 的既有 `TimeUp → Release` 转场缺少 Exit Time 和条件、被 Unity 忽略的问题；现在以 TimeUp 条件触发退出。Countdown 与 Audio 的本地 Restart 优先回到 Remote，超时后的下一轮继续计时并恢复警告音。
- 成功或超时退出计时后，WD On 将进度属性恢复默认值；超时关闭 Overlay 与音频对象。原生回归覆盖两轮超时/重启及进度、声音恢复。
- 保留复制前用户状态的 On/Off 设置、Clip、绑定与源资产身份。隐藏的旧 `writeDefaultsMode` 字段仅保留序列化数据，不控制新状态。
- 更新后正常构建重建 ASS 输出即可；不扫描或改写已保存的源 Controller、Scene/Prefab，不在导入时转换 WD。0.9.5 的音频导入设置与两个反馈开关含义继续保留。
- 表情复位、原图保全、密码/倒计时/Restart 与最终 SDK/NDMF 图的执行记录见 [WD On 验证](../../../Docs/Verification/ASSWriteDefaultsOn/README.md)。

## [0.9.5] - 2026-11-08（历史发布）

### Fixed

- 为四个自带 MP3 补齐稳定的 AudioImporter metadata 并启用 `Load In Background`，修复默认 `Decompress On Load` 音频被 VRC SDK 的状态行为检查拒绝、阻止上传的问题。音频文件内容保持。
- 保留现有开关含义：“关闭警告音效”只关闭倒计时警告音，密码成功音仍然生成；“关闭覆盖界面”只控制画面。两者同时关闭时，成功音同样使用正确的后台加载设置。
- 正常更新后重新发起构建即可生成当前音频引用；不增加启动/导入扫描，不改写用户 Scene/Prefab 或其它音频的导入设置。
- 音频导入、开关组合、真实 SDK 拒绝/允许对照和 NDMF 最终 Controller 的回归范围见 [音频上传验证](../../../Docs/Verification/ASSAudioUpload/README.md)。

## [0.9.4] - 2026-11-08（历史发布）

### Component references

- 升级入口改为 `Tools/UnityBox/ASS/Upgrade Component References`：一次确认后扫描整个 Assets，自动匹配 ASSComponent，全部备份后重连并输出报告与 Console 汇总。
- 移除资源选择恢复窗口；无需选择资源、逐 GUID 指定类型或再次确认。未知、其它插件及重复组件保留并报告。
- 显式全项目升级针对相关资源暂时卸载干净且已保存的 Scene/Prefab Stage，执行后恢复；dirty、未命名场景及它们的依赖保留并跳过，不自动保存。
- 配置仍在组件 Inspector；无启动、导入或移动回调迁移，自动构建与源配置副本边界保持。

## [0.9.3] - 2026-11-07（历史发布）

### Component references

- ASS 正常插件更新内置组件恢复，位于原 Unity-only Editor 程序集中，只处理 ASSComponent。
- Project 入口为 `Assets/UnityBox/Avatar Security System/Repair Component References`，预填当前资源选择；一次手动执行整批，无逐项确认。
- 源 GUID、字段与 fileID 保留；源 SHA/目标身份核对和全部备份后才替换失效 GUID，打开资源及未保存依赖继续跳过。
- 本包备份位于 `UserSettings/UnityBox/AvatarSecuritySystem/ComponentReferenceMigration/<id>/Assets/...`，无启动/导入回调；原 Tools 窗口入口保持。

## [0.9.2] - 2026-11-07（历史发布）

### Component references

- 旧 ASS 引用接入独立 Component References Editor 工具，与 ACC 三类标记、AO 和 Viseme 共六类组件统一处理。
- 选择资源后一次手动执行整批迁移，无逐项确认；歧义 GUID 只选择一次目标类型，未知、重复与打开资源在报告中跳过。
- 当时的工具使用独立安装包分发；0.9.3 起由 ASS 包内恢复入口交付。该旧实现无启动、导入或构建自动迁移。
- ASS 本身继续保留 0.9.1 的源配置保全、无自动迁移与构建副本规范化；固定 GUID、类名、程序集和字段保持。
- 操作见仓库 `Docs/ComponentReferences.md`，执行结果见 `Docs/Verification/ComponentReferences/README.md`。

## [0.9.1] - 2026-11-07

### Removed

- 完整移除自动旧组件迁移：启动扫描、导入/移动回调、GUID 重写、自动场景重载及修复菜单/API。
- 移除组件加载时的 `OnValidate` 参数纠正；已有时值和选项不在导入、打开时自动改写。

### Changed

- 生成图继续在独立配置副本上规范化时间，源 Scene/Prefab 与序列化字段保留原值。
- 新组件及用户显式 Reset 使用保持 0.5s、容错 0.3s；自动构建、WD Off、原用户图保留等功能继续使用。
- 0.9.0 迁移备份保留供逐文件比较，恢复由用户明确选定资源和内容；本版不自动覆盖、恢复或删除。
- 已有 Missing Script 按组件实际 `m_Script` GUID 定位脚本与包，不再自动批量替换项目引用。
- Inspector 移除新增顶部自动构建 HelpBox，保留插件介绍与字段说明；锁定选项和手动 Generate 按钮继续移除。

### Verification

- 新增打开、导入与配置捕获保留源文件及已有字段的回归；执行结果见仓库 `Docs/Verification/ASSImportSafety/README.md`。

## [0.9.0] - 2026-11-07（历史发布；自动迁移已于 0.9.1 移除）

### Changed

- 移除 Avatar 对象锁定与 Inspector 对应选项，旧 disableRootChildren/WD 字段隐藏保留
  历史序列化；新增 ASS 状态统一 WD Off，保留用户图及其原 WD 设置。
- 普通 Unity 改为真实 Player 构建场景中自动生成 Controller/运行输入，再移除编辑配置。
  Inspector 手动 Generate 按钮移除，源场景与原 Controller 保持编辑身份。
- 复制已有 Controller/AOC 并清理旧 ASS 生成层，包括遗留锁层，再追加手势、计时和反馈。
- 默认保持 0.5s、容错 0.3s，统一规范化确保保持严格大于容错；连续正确姿势才累计。
  错误中断重计，容错过期清序列，重复相邻姿势先释放，超时/Restart 优先处理。
- 运行输入新增 IsPasswordCorrect；IsUnlocked 保留为密码结果的兼容别名。
- Inspector、构建校验与时间说明同步英语、简体中文、繁体中文和日语。

### Fixed

- 仅启用诱饵状态、关闭诱饵层时，也先声明转移条件所用参数；参数池没有 Bool 时
  添加本地布尔守卫，参数类型与条件保持一致。
- 修复从旧发布包升级后，原 ASS 组件变成 Missing Script 的问题。0.7.24 发布 ZIP 没有
  携带 `Runtime/Component.cs.meta`，各项目自行生成不同 GUID；0.8.0 加入固定 GUID 时
  缺少这类旧安装的引用迁移，导致它们的组件失去脚本关联。
- 首次加载 0.9.0 时自动扫描 `Assets` 内的 Scene 和 Prefab，后续导入资源继续处理。
  通过 ASS 配置字段和失效脚本引用识别旧组件，直接重连到固定脚本 GUID，保留组件
  fileID、密码、开关、音频引用和 Prefab 覆盖关系；其他类型及仍有效的脚本引用保持原值。
- 已确认的旧 GUID 记录于 `UserSettings/UnityBox/ASSMigration/LegacyGuids.json`，
  支持同次及后续导入的 stripped Prefab 实例引用，跨脚本重载继续使用。
- 修复前将原文件字节保存到 `UserSettings/UnityBox/ASSMigration/<id>/`。
  场景或 Prefab 有未保存修改时保留现场，保存后通过
  `Tools/UnityBox/Avatar Security System/Repair Legacy Components` 完成恢复。
- 继续沿用 0.8.0 的固定 GUID；此前已正确安装的组件继续使用原关联。

## [0.8.0] - 2026-11-05


### Added

- 独立 Unity Animator 运行后端、StandaloneSecurityBuilder 与 Inspector 生成按钮。
- Unity 场景构建处理器在构建副本移除作者配置，保留实际运行驱动和源场景配置。
- StandaloneSecurityInput 提供 SetGesture、解锁/超时查询及 Restart。
- 无曲线生命周期层，支持解锁后重新锁定与超时后的新一轮输入。
- AnimatorOverrideController 展开及独立内存 Controller 持久化副本。
- 实际 Animator 时间线测试、多 Avatar 资源生命周期用例及 16/64/256 子对象性能样本。

### Fixed

- 普通 Unity 状态音频支持根节点 AudioSource 的空相对路径，也继续支持子节点路径。
- 仅在状态动作需要参数赋值或 IsLocal 条件时建立参数类型表；音频与层权重动作直接执行。
- 生成层显式使用 syncedLayerIndex=-1，修复默认同步到第0层而导致功能状态机不独立的问题。
- 持久化完整生成状态图；已经拥有资源路径的对象不再重复 AddObjectToAsset。
- 每个 Avatar 使用自己的 Build_<id> 输出目录，SDK 后处理只回收对应目录。
- 构建快照保存/恢复对应 Avatar 的名称映射和资源跟踪，支持交错的多 Avatar 后期处理。
- 生命周期 LayerControl 的兄弟层目标在层插入后按最终名称刷新。
- 旧层清理将内部键与显示名成对管理，包含生命周期层，重新生成和普通用户层共存时继续正确执行。
- Contact 根分支索引由一次扫描生成，锁定与恢复图复用索引。
- 补齐 Editor 脚本、程序集及正式包目录的固定 metadata，保留已有资源 GUID。
- 从原场景三处 ASS 配置恢复 Runtime/Component.cs.meta 的原 GUID
  1c51e6a139920bb4ca12700dc82627ff，保持该仓库场景内容和引用；其它旧项目的随机
  GUID 引用由 0.9.0 的升级迁移处理。

### Integration

- 核心、SDK 与 NDMF 功能统一维护在 master，安装和验证流程使用当前仓库源码。
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
