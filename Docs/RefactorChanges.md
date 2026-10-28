# refactor 分支改动与影响

本文比较 `master` 基线 `e9f0b36` 与 `refactor` 当前源码，包含先前已提交的修复及本轮
独立 Unity 拆分。它是升级和审查清单；生成器需要重新运行后，修复才会进入已有 Avatar
的生成资产。Animator Optimizer 在独立的 `feat/animator-optimizer`，没有合并到本分支。

## 分支与用户文件

- 原 `75396e7 chore: TEMP` 的临时提交记录已从本地 `temp` 撤掉，原提交保留于
  `backup/temp-75396e7`。文件变更以 `bee33c6` 保留，随后 `6376435` 提交全仓缺陷修复。
  这不是丢弃用户修改，也没有改写远端历史。原改动的正确性分析见 [temp 审查](TempBranchReview.md)。
- 原 Avatar 场景、实验 bundle 和已知 GUID 保留；没有借修复重写大场景。已提交且已知的 metadata 内容和3处搬迁的 metadata 均保留；
  7个原本未提交 metadata 的 Editor 源码搬迁后有新记录身份，这不等于恢复了原本未知的 GUID。
  仍缺少的原始组件元数据只能从原开发项目恢复，随机新 GUID 不能修复旧引用。
- 本轮环境重启清空过 `/tmp` 的临时审查副本。源码从 `/workspace` 中保留的隔离工程快照
  恢复到 `/workspace/unitybox-refactor-review`，再按编译记录的源码 SHA256 核对。后续工作和
  最终验证使用持久目录；历史日志中的 `/tmp` 仅是当时的来源路径。
- 两分支均未自动发布。源码包版本与线上 listing 的已发布版本可能不同；已有项目不会因
  本地分支修复而自动更新。

## 共同架构与迁移

七个包的普通 Unity 核心不再硬引用 VRChat SDK、Modular Avatar 或 NDMF。
SDK、MA、NDMF 回调/菜单安装分别放入受真实包版本约束的可选程序集，包清单取消强制
安装整套 Avatar 工作流。已有 Avatar 项目保留原依赖；新普通 Unity 项目可只安装所需包。
详细边界和 API 迁移见 [依赖架构](DependencyArchitecture.md)。

Runtime 中原 MonoBehaviour 类型、序列化字段及已有脚本 GUID 保留。检测到真实 SDK Base
≥3.7 时，原配置组件继续实现 SDK 的 `IEditorOnly`，以保留上传前的组件白名单兼容。
缺 SDK 时没有同名替代类型。NDMF 的 `RemoveEditorOnly` 处理标记为 EditorOnly 的对象，
不能将它等同于自动清除所有 `IEditorOnly` 组件；各适配仍负责自己的配置捕获/剥离。

外部代码直接引用搬迁后的 Editor 类型时，可能需要在自己的 asmdef 中增加新的 Core、
VRChat/VRC 或 ModularAvatar 程序集引用。组件身份保留不等于所有 Editor 扩展代码无需调整。
缺少可选依赖时，对应程序集和集成测试不会编译；这不等于这些功能已经测试通过。

## Advanced Costume Controller

| 原触发问题 | 当前行为 | 升级后果 |
| --- | --- | --- |
| 多套独立服装共用一个父容器，组根覆盖另一套服装 | 以 Base 确定服装归属；共享/祖先冲突时回退到各自 Base，选中时激活必要组织祖先 | 需重新 Generate，旧控制器不会自动改变；不会控制 CostumesRoot 或 Avatar 根 |
| `P/B` 和 `Q/B` 被删掉组织路径后共用 Mixer 菜单 | 菜单保留真实组织路径；普通与两种 Mixer 使用一致的服装身份 | 菜单层级可能变化，旧路径自定义引用需核对 |
| Mixer 重新使用扫描时的部件分组，恢复已取消的选择 | 候选以当前预览中的服装、变体、部件勾选与临时分组为准 | 旧错误候选会消失；空候选槽位不生成 |
| 同显示名部件覆盖菜单，清洗后的参数/文件名碰撞 | 显示项加序号；被清理/截断的路径加稳定 SHA256 后缀 | 特殊字符名称的参数或目录可能改变，保存的菜单选择可能重置 |
| 材质变体转换没有匹配仍删除来源 | 转换前验证匹配槽位；失败保留组件与子对象 | 成功转换仍会清理完整变体，需保留对照时先复制；支持场景 Undo |

普通 Unity 新增 `Tools > UnityBox > Advanced Costume Controller (Unity Assets)`。真实
Scanner、模型和完整 AnimationBuilder 共用同一 Core 实现，支持服装/变体、Parts、两种
Mixer 与材质曲线；不是仅抽出命名工具。窗口可选择服装组，细粒度部件/变体模型也可经
`UnityCostumeGenerator.Generate` 传入。将输出 Controller 分配给 CostumesRoot 上的
Animator，并用项目自己的输入驱动 Float 参数。

普通入口每次创建独立 Build 目录，保留旧资产；失败只清理本次不完整生成。默认目录为
`Assets/UnityBox/Generated/AdvancedCostumeControllerUnity`，与 Avatar 清理目录隔离。
不会自动分配 Animator、安装菜单或运行 MA Setup Outfit。没有真实 SDK 时拒绝参数压缩；
即使有 SDK，网络参数同步安装仍属于 Avatar 工作流，普通游戏应保持压缩关闭。

Avatar 窗口仍负责 MA 菜单、Setup Outfit、同步参数与图标。它会清理当前场景/Avatar/
参数前缀命名空间的旧生成资产；关闭自动图标时保留 MenuIcons。不要把手工资产放入该目录。
这与普通 Unity 入口保留旧 Build 的策略不同，不能混用目录。

完整 MA 集成最低为 MA **1.18** 和 SDK Base/Avatars **3.7.3**；独立 Driver/Descriptor
适配仍支持 SDK 3.7。原 MA 1.10 声明不足以支持当前 OutfitRoot/PortableControl API。
MA 1.13/1.18 都调用 SDK 3.7.3 首次提供的双参数 `VRCConstraintSource` 构造器，故仅满足
SDK 3.7.0 并不代表 MA 集成可编译。详见 [手册](AdvancedCostumeController.md)。

## Blendshape Controller Generator

| 原触发问题 | 当前行为 | 升级后果 |
| --- | --- | --- |
| Bool 勾选 Inv 不改变开关结果 | 交换 On/Off 输出 | 重新生成后原先错误的反向设置开始生效，应复核默认外观 |
| 同名 Renderer 或清洗后同名 blendshape 相互覆盖 | Renderer 以真实相对路径区分；参数/文件名处理碰撞 | 改名或移动对象可能得到新目录/参数名，旧引用需检查 |
| `Smile` 与 `Smile/Big` 菜单叶子/目录冲突 | 保留独立控制项 | 旧菜单在重新生成后会调整 |
| 参数改型、取消部分选择后残留旧声明 | 成功生成后替换对应 Renderer 的菜单、FX 和参数；迁移旧公共根声明 | 不会删除其它控制器仍使用的声明；全部取消选择不等于卸载旧结果 |
| 取消/异常破坏旧控制器，MA 错误被吞掉 | 每次先写新 Build，成功后切换引用；失败清理本次目录并报告 | 旧资产保留供 Undo/旧引用使用，磁盘占用增加，确认无引用后手工清理 |
| 零 Value Scale 与阶梯无限切线产生 NaN | 明确处理零曲线并保留阶梯语义 | 对零值/阶梯配置重新生成 |

窗口和完整 Controller/Clip 生成可在无 SDK 项目使用，根对象字段为 Animation Root。
普通模式不自动设置场景 Animator；每个 Renderer 各有一个 Controller，项目需自行选择或
合并。真实 SDK Base/Avatars ≥3.7.3、MA ≥1.13 存在时启用可选 MA 适配；窗口的
`Install MA Controls` 可关闭菜单/FX 安装，仅生成资产。包不再强制拉取这些依赖。

曲线、Duration、Loop、Value Scale 仍存于 EditorPrefs，不是 Avatar 持久配置。
场景 Undo 不撤销资产创建/删除。参数总预算、Write Defaults 和其它口型/表情工具的绑定
冲突须检查最终 Controller。详见 [手册](BlendshapeControllerGenerator.md)。

## Viseme Blendshape Override

原来的实现有有效的口型生成逻辑，但存在依赖、映射与构建缺陷：

- NDMF 最低版本改为实际使用 Animator Services 所需的 **1.7**。虚拟路径通过
  ObjectPathRemapper 获取；没有 FX 时创建接收目标，生成顺序声明在 MA 之后。
- 跳过禁用组件；默认 Renderer 优先 Descriptor Face，再 Body/其它候选。已指定 Renderer
  不会被后续初始化自动替换；跨 Mesh 继承仍需要映射名存在。
- 空映射保持从 Descriptor 继承，显式 None 才禁用该行；打开 Inspector 不再固化继承值。
  修复 Voice 最小/最大阈值重叠，使线性区间有效。
- SDK 回退启用自定义动画层，复制持久化 FX 或创建新 FX，保存状态/过渡/BlendTree/Clip
  子资产。每次独立生成，避免改写源 FX。OverrideController 明确报错，不丢弃覆盖关系。
- 核心配置、15 个口型枚举、Renderer 验证、曲线和 Controller 生成不依赖 SDK；序列化枚举
  的整数值保持原含义。普通 Unity 调用者自行提供 Viseme/Voice 参数和目标绑定。
- Descriptor 继承、Lip Sync 切换、上传回调在 `.VRChat`，NDMF 在单独程序集；原组件类型和
  GUID 保留。包不再强制安装 SDK/NDMF。

安装 NDMF 本身不能证明本次 Avatar 已被它处理。SDK 不再仅凭包存在跳过；关闭
ApplyOnBuild/ApplyOnPlay 后仍执行回退。NDMF/SDK 成功后才消费构建配置，重复 callback
不再次生成；失败返回失败并保留配置可重试。直接 ProcessAvatar API 保留配置供明确重复生成。
无配置、禁用或无有效plan时不激活NDMF动画服务；只释放本次自有上下文。

原 customizeAnimationLayers=false 时，SDK 回退或本插件首次启用 NDMF 服务先在构建对象
恢复残留 Controller/Mask 为默认层，再添加口型FX，避免同时复活已关闭的旧图。同文件
含多个 Controller 时，复制后按所选源 Controller 的 localFileID 找到对应子资产，不再
错误返回文件的主 Controller。源资产保持不变。回归状态见 [测试指南](Testing.md)。

只有成功生成有效映射才切换 Lip Sync。禁用组件不清理以前的 Generated，也不替用户恢复
旧版本已经改过的源 Avatar。旧每行 Voice 格式没有通用枚举迁移，应逐行复核 Custom
Settings。共享绑定、Write Defaults、第三方后续优化与实际语音效果仍需实测。

## Avatar Security System

| 原触发问题 | 当前行为 | 影响和边界 |
| --- | --- | --- |
| 构建直接修改原 FX、ExpressionParameters 或误清理第三方内存 Controller | 独立路径隔离待修改资产，保存原引用/开关并在后处理恢复；无资产路径不等同过期文件 | NDMF 控制器提交时序及连续多 Avatar 仍需实测；共享 Generated 目录不能存手工资产 |
| 错误手势条件同时要求小于和大于同一值，永远不触发 | 拆为两条 OR 转场，Holding 中继续检查正确手势 | 密码行为会按连续保持执行，原来偶然“通过”的错误输入不再通过 |
| 短暂正确输入累计、成功与超时竞争 | 连续 Holding 时间明确；超时要求尚未解锁 | 需重新构建状态机，检查成功/超时边界、重复手势和 Idle |
| 空密码/非法手势继续生成 | 普通模式空密码跳过；非法输入拒绝 | 空配置不再生成一个看似可用的锁定流程 |
| 新组件默认启用高 GPU 负载，Play Mode 也可能生成 | 新建/Reset 默认关闭主动负载，Play Mode/isDebugMode 跳过 | 不迁移已有序列化设置；没有增强负载效果，也没有证明防盗或客户端稳定性 |
| 构建修改默认音频/Logo 的导入设置 | 不再改写这些源资源导入设置 | 反馈依赖现有资源可用性，缺资源应检查日志 |

配置、Controller/Motion 工具、手势/计时/锁定图移到独立核心。状态进入行为经
`IAnimatorBehaviourAdapter` 显式注入，真实 SDK 实现在 `.VRChat`。没有适配器时拒绝需要
这些行为的图构造；没有新增通用 Unity 手势输入或密码运行时，也不会省略关键 Behaviour
后声称完整可用。平台的 Contact/Descriptor/ParameterDriver/LayerControl/上传流程保持可选。

构建交接现在按每个 Avatar 的实际 NDMF 成功状态去重，配置已捕获不等于已经生成。
安装 NDMF 但关闭 ApplyOnBuild/ApplyOnPlay 时，SDK 仍以独立路径运行并保留快照/恢复保护；
完成状态用弱对象身份保存，新捕获、失败和后处理清理，避免旧实例ID误匹配下一次构建。

NDMF 自身不保证已克隆 Controller，ASS 也不强制启用 AnimatorServices；因此 NDMF 和
SDK 两条路径都复制将修改的持久化 Controller 与外部 Motion，不再假定其它插件已完成
隔离。晚期处理也保护新出现的源控制器。同文件多 Controller 的副本按原 localFileID
选取正确子资产，避免处理另一个主 Controller；Controller/Motion 复制或解析失败现在
明确中止，不能回退到源 BlendTree 后递归改写。此前默默继续的失败可能变成可见构建错误，
这是保护源资产的必要行为改变。无资产路径的内存 Controller 仍由调用方管理，不保证深复制。

原关闭 customizeAnimationLayers 时残留的 base/special Controller 不参与处理；需要生成
FX 时，先在构建克隆恢复旧槽位默认语义并补 FX，避免顺带启用关闭的 Gesture/Action。
独立后处理恢复原数组和开关。新增用例覆盖以上生命周期、源隔离、身份和失败路径；仍需
有授权的 Editor 验证实际 NDMF 提交与上传。

Defense 代码仅移动到平台适配，保持已有禁用默认和边界；`defaultEnableDefense` 与关闭
Lock 的参数注册缺口未在本轮补强。名称混淆、已保存参数与动画路径实验不构成资源所有权
验证。层自释放后通用动态重锁、NDMF 最终提交时序、共享 Generated 生命周期仍待验证。
完整配置见 [用户手册](../Assets/UnityBox/AvatarSecuritySystem/Editor/README.md) 和
[技术参考](../Assets/UnityBox/AvatarSecuritySystem/Editor/ASS_TechnicalDoc.md)。

## Prefab Diff Patcher

选择状态现在可保持部分选择，批量选择覆盖根组件；对象壳与 Transform 选择独立。
选中的 Transform/RectTransform 复制局部属性并保留输出父子关系，避免因新父对象的世界
位移改变局部坐标。比较包含数组、嵌套字段及精确序列化数据，引用按完整对象路径、组件类型
与次序映射；没有引入的 B 引用置空，外部资产保留。

输出禁止覆盖 A/B 输入或越出 Assets，覆盖独立输出先确认并保留已有 GUID。Missing Script
导致中止，避免生成静默丢脚本的结果。根对象壳仍来自 A，A 独有对象保留；B 缺少对象不等于
删除 A 对象，选中的已删除组件才移除。重名兄弟/同类组件按次序匹配，改名、移动、重排的
语义不能自动推断。

本轮独立 Unity 工作没有额外修改其生产行为；它原本就不需要 SDK。真实 Prefab 保存、
嵌套/Variant 继承、自定义序列化和复杂 SerializeReference 图仍需副本验证，输出资产保存
不受场景 Undo 保护。详见 [包说明](../Assets/UnityBox/PrefabDiffPatcher/README.md)。

## Avatar Collider Monitor

修复偏好开关，并将快照冻结为路径、实例身份、配置、世界矩阵、激活状态与 Layer，
改名/销毁对象不再改变旧历史。同一 Transform 上多个 Collider 分别记录，历史只比较同一
根实例，限制最近 1000 条；不同 Avatar 和构建克隆不被硬合并。

核心现在选择任意 Root GameObject，记录 Unity Collider/Collider2D，支持手动采样及
Unity 场景构建回调。VRC Descriptor ColliderConfig 由可选 provider 添加；SDK 早晚回调
使用实际构建对象。NDMF 四阶段观察点独立于 SDK。保留原 Component 入口，并增加
GameObject/扩展事件 API。

它不绘制碰撞体几何、不采集 PhysBoneCollider、不持续逐帧记录，也没有 VRCFury 内部
每个 feature 的可靠钩子。后处理没有构建对象时不随意选源场景 Avatar 伪造样本。历史在
内存，脚本重载会清空，暂无导出。阶段差异不能直接归因给某插件。

## Build Pipeline Logger

Shader/compute 去重状态在每次 Unity/Avatar 构建开始重置，连续构建不再缺日志；同一
构建内仍按回调类型去重，不是每个 shader 变体的完整记录。核心只包含 Unity 导入/构建
回调，SDK 和 NDMF 各有可选程序集；保留菜单、EditorPrefs 键与日志类别。

带 VRCFury/NDMF 名称的 callbackOrder 日志解释为观察位置，不承诺第三方确实执行。
同 order 没有绝对顺序，NDMF 观察点不是所有插件的全局阶段边界。Linker 尚未启用，
没有完整耗时、依赖追踪或独立文件导出；不修改传入的构建程序集或 shader 数据。

## 打包、文档与测试工具

- 发布 ZIP 保留已有 `.meta`，避免安装时改变已知 GUID；`.gitignore` 允许跟踪插件元数据。
  旧版安装包若已让Unity生成不同GUID，补回源码metadata不会自动修复所有旧项目引用，
  升级时需保留项目备份并核对脚本引用。7个原无metadata的Editor文件新增身份不声称恢复原值。
- 排除 Generated，以及 ASS 动画路径探针、固定 GUID 实验 fixture 和平台 bundle。它们
  仍保留在源码仓库，发布包不能用旧实验菜单作为生产功能。Editor 回归测试仍随包保留。
  实验菜单补上切场景/删资产前保存提示、显式Original/Modified/内存三模式及对照检查，
  并用显式AssetBundleBuild[]只打探针包；旧二进制不代表这些流程已经重跑。
- ZIP 用覆盖模式，重复构建不残留已删除文件。GitHub workflow 调用统一 Python 筛选器并先
  执行 5 项打包测试。仍按已跟踪 package.json 自动发现包，没有发布/推送步骤由本轮代执行。
- 所有使用手册改为当前实现的依赖、行为和限制；废弃 React/TypeScript 指南改为 Unity/C#
  规范。新增本清单、依赖架构、环境测试文档和 `.agents/skills/unitybox-testing/SKILL.md`。
- 安装脚本固定真实 Unity 2022.3.22f1 的镜像层及哈希；依赖锁保存真实包归档/树校验。
  prepare/run/report 工具在 checkout 外生成隔离工程，保留本次日志/退出码/XML，拒绝零测试、
  全跳过、缺 XML、许可/编译错误和旧结果充当通过。不会激活许可或修改用户场景。
- Roslyn 工具检查真实源码和实际依赖，报告源码哈希及省略的上游可选引用；它不完整复现
  Unity 导入、平台符号、程序集解析或 IL 后处理，不能替代 Editor。

## 实际验证和剩余结论

Unity 2022.3.22f1 已安装；真实测试启动 exit 1，提示 `No valid Unity Editor license found`，
没有 NUnit XML。用户已确认当前没有授权。插件 EditMode 测试、Unity 导入、IL 后处理、
实际构建/上传及逐帧行为均未获得通过结论，不再重复询问或重试许可。

2026-10-28 最终源码真实类型检查：七插件无SDK闭包19个程序集、仅SDK闭包40个、
SDK3.8.2 + MA1.18 + NDMF1.11闭包55个全部通过。未修改第三方源码。历史ACC/BGC
SDK3.7.3最低组合30个通过，只代表当时范围。当前声明177个插件回归用例（核心91、
平台86），均未在Unity执行。共享工具23项Python测试和5项打包测试属于其它层次。
报告、隔离工程和用例清单见[测试指南](Testing.md)。

升级前应保留项目备份并恢复原始 metadata；先在独立工程验证生成与资产引用，再分别覆盖
无 SDK、SDK 回退、NDMF/MA 三种入口。重新生成可能改变参数名、菜单层级、生成目录或图
语义；旧资产和保存的菜单值没有通用自动迁移。完整实测前，状态是“确认缺陷已修正并补充
回归代码、真实依赖类型检查通过，Unity 行为验证受许可阻断”。
