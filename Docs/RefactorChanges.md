# master 变更与升级方式

本文记录从 `e9f0b36` 基线到八插件统一 `master` 的功能变化与升级行为。
原 `refactor` 七插件与 Animator Optimizer 在同一个 checkout 开发、测试和发布。
执行记录见 [验证结果](VerificationResults.json)，测量数据见 [性能报告](Performance.md)。

## 分支和用户文件

原 `75396e7 chore: TEMP` 的文件变更以 `bee33c6` 保留，原提交位于
`backup/temp-75396e7`；随后完成共享容器归属和全仓功能修复。
原场景、实验 bundle 与已知 GUID 保留。ASS 固定脚本 GUID
`1c51e6a139920bb4ca12700dc82627ff` 来自仓库样例场景，关联同名 `ASSComponent`。
旧发布包未携带脚本 metadata，各安装自行生成的 GUID 并不都与样例相同；
0.9.0 曾加入自动恢复，0.9.1 已撤回；当前六类旧引用通过所属插件的包内手动入口处理。

`master` 的 `Assets/UnityBox.unity` 原样保留用户较新的 temp/refactor 场景，
对应合并前 `409eb5b` 的 2,924,530 字节内容。优化器旧场景保留在 `53fa22f` 的提交历史中。
合并时按完整场景版本保留序列化对象和引用，功能测试使用独立 fixture。
原临时提交和优化器 rebase 前提交分别保留在 `backup/temp-75396e7`、
`backup/animator-optimizer-before-rebase`；当前开发与发布统一使用 `master`。

工作和原始验证记录放在持久化 `/workspace`。关键 NUnit XML、Player JSON 和工具检查
结果随源码归档于 `Docs/Verification/`；当前发布变化见[发布说明](ReleaseNotes.md)。

## 共同架构

八个包都提供 Unity 核心，真实 SDK、MA 和 NDMF 适配位于独立程序集。包清单按工具职责
声明依赖，普通项目直接使用生成、比较、快照和日志功能。Runtime 类型及序列化字段保留；
SDK Base 可用时配置组件实现真实 `IEditorOnly`。

各包为原先缺失的脚本、程序集和目录补齐稳定 metadata；新增缺失项按仓库与
资产路径确定身份，已有 `.meta` 保持原值。新克隆和安装使用仓库 GUID；
正常打开/导入不改写旧引用；用户从对应插件 Tools 菜单确认一次后，升级整个 Assets 的所属组件引用。完整矩阵与操作见[组件引用说明](ComponentReferences.md)。

外部 Editor 扩展按 [程序集映射](DependencyArchitecture.md) 增加 Core、VRC/VRChat 或 MA
引用。普通 Animator 的参数由项目脚本提供，Avatar 参数由相应平台输入服务提供。

## 六类组件与包内恢复

ACC 0.5.3、AO 0.3.3、ASS 0.9.7、Viseme 0.2.3 保留六种生产组件的类名、Runtime 程序集、字段与固定 GUID。
恢复实现在各插件原有 Unity-only Editor 程序集中，随正常插件更新交付。从 `Tools/UnityBox/<插件菜单>/Upgrade Component References` 确认一次后，扫描整个 `Assets`，自动识别并恢复本插件的旧脚本引用。无需选择资源、打开恢复面板或逐 GUID 指定类型。

识别结合明确类型、已知 GUID、完整字段结构与 ACC Outfit/Prefab 来源，不再要求用户逐 GUID 选择。
确认后自动建立批次并备份，再替换失效 GUID，保留 fileID、字段、引用、metadata 和文本格式。
干净已保存的场景/Prefab 编辑上下文会恢复；未保存编辑及其依赖、未知或重复组件保留。
各包备份位于 `UserSettings/UnityBox/<插件目录>/ComponentReferenceMigration/<id>/Assets/...`。
完整范围见[组件引用说明](ComponentReferences.md)，当前验证见[执行记录](Verification/ComponentReferences/README.md)。

## Animator Optimizer

优化器加入统一 `master`，普通 Unity 的 Animator/Explicit Controller 与 Avatar Playable
入口使用明确参数语义。平台 Bool/Int 条件保持原生状态机，混合空/非空 Motion 图保留姿态
历史；其它独立层按分析结果合并。AAP 的更新与输出共享当帧决策，NDMF 使用当前虚拟图，
最终提交后保持层身份和播放设置。创建的子资产参与完整 Undo/Redo。

普通 Unity 启用 Bool/Int 升级后，输入脚本对应改用 `SetFloat`；需要维持原输入 API 时关闭
参数升级。AAP 按配置启用，转换成本与控制器结构一起查看[性能报告](Performance.md)。
0.3.1 将加载时的字符串/层索引/预算纠正移到独立配置与 Options 副本，源字段保留；
显式改选层或字段编辑仍可 Undo。AO 0.2 是开发快照，未发布公开旧包。
离线生成、目标选择与输入契约见[优化器手册](../Assets/UnityBox/AnimatorOptimizer/README.md)。

## Advanced Costume Controller

| 触发场景 | 当前行为 | 升级操作 |
| --- | --- | --- |
| 多套服装共用容器，组根与祖先相互覆盖 | 以 Base 确定归属，冲突时回到各自 Base；切换时启用组织祖先 | 重新 Generate，保留真实菜单组织路径 |
| Mixer 使用过期扫描选择或同名部件 | 使用当前勾选/临时分组；同名控制项和清洗路径使用稳定区分 | 重新生成并核对已保存的菜单选择 |
| 后续生成层默认同步到第 0 层 | 所有生成层显式独立求值 | 新生成控制器直接使用独立层；原输出通过重新生成更新 |
| 重生成删除旧资产，场景 Undo 引用丢失 | 每次生成保留独立 Build 目录，场景修改形成事务 | Undo/Redo 恢复前后安装引用；旧目录按项目清理周期管理 |
| 当前参数安装超过整 Avatar 预算 | 按 MA/NDMF 的参数汇总计算并在安装前处理预算 | 调整压缩/选项后生成；失败时旧安装保持原状 |

ACC 仅提供完整 Avatar 换装面板，生成动画并安装 MA 菜单、骨架、FX、同步参数和图标。
移除普通 Controller 面板和输出选项；已有生成资产保留。底层 Scanner、模型与
AnimationBuilder 独立于 SDK，继续复用于 Avatar 生成与原生测试。缺少 SDK/MA 时 Open 提示依赖。
压缩测试覆盖本地编码、远端解码状态及实际 SDK Driver 写入值；图标测试读取真实渲染像素。

输出目录包括场景/根对象/参数命名空间及独立 Build。参数和菜单经过稳定碰撞处理，
特殊字符或重复显示名升级后按新名称生成；旧菜单保存值按新的参数表重新确认。
0.5.1 的材质变体 Inspector 只在显式应用/分析/刷新时提交，选择外部来源只改临时状态；
无效材质槽与 Exclude GroupName 保留。完整操作见 [服装手册](AdvancedCostumeController.md)。

## Blendshape Controller Generator

Float、Bool、Inv 和 Value Scale 生成相应曲线，多 Renderer 结果可合并为一个
Controller，并通过窗口分配给 Animation Root 上的 Animator。每 Renderer Controller 继续
服务于 MA 安装；组合器按参数类型和实际动画绑定处理冲突。

MA 安装/移除和 Animator 分配均纳入 Undo。参数预算使用与 MA 相同的整 Avatar 汇总；
替换生成结果时重新计算安装后预算。新 Build 写入成功后再切换引用，旧资产继续供 Undo
和已有场景引用使用。移除选中控制项通过专用动作完成。

Bool 反向、同名 Renderer、`Smile` 与 `Smile/Big` 菜单路径、零曲线和阶梯曲线均有对应
生成结果。升级时重新生成，曲线编辑选项继续使用 EditorPrefs。
手册见 [Blendshape](BlendshapeControllerGenerator.md)。

## Viseme Blendshape Override

生成层显式设置独立同步索引，15 个口型状态按 Viseme/Voice 独立求值。映射支持 Descriptor
继承、显式 None 与每行自定义 Voice；旧 Voice 在构建深拷贝快照中按原枚举含义解析，
0.2.1 的加载、绘制和计划读取保留源 bindings，显式编辑/初始化才提交并支持 Undo。

SDK 对普通 Controller、文件内子 Controller、内存图和 AnimatorOverrideController 建立
独立可编辑副本；AOC 的有效 Clip 替换和源对象身份被保留。SDK 与 NDMF 的 Auto WD 都按
关联控制器实际状态图计算，自定义控制器名称不改变该策略。NDMF 的 Controllers
通过字典条目取值枚举，覆盖 FX 以外的 Gesture 等控制器，修复直接读取 Values 集合时
遗漏关联图、导致 Auto WD 选错的情况。

NDMF 使用实际重映射路径，最终提交后可再经过其它插件处理。SDK 回调以本 Avatar 的
完成状态去重，构建失败保留原 Descriptor 与源资产。原关闭的自定义层先恢复默认选择，
再添加有效口型输出。无 SDK 项目直接生成 Controller，并由项目脚本写入 Viseme/Voice。
普通 Unity 构建移除场景副本中的口型编辑配置，保留生成 Controller 与 Animator；
源场景配置继续用于下次编辑和生成。手册见 [口型覆盖](VisemeBlendshapeOverride.md)。

## Avatar Security System

0.9.7 的 Inspector 时间编辑使用延迟提交：保持至少比容错长0.01s，容错上限为保持减0.01s，
最大保持/等待随依赖关系调整。旧非法配置在绘制时保留原值，显式“修正时间”或编辑才提交，
并支持 Undo/Redo；默认保持0.5s、容错0.3s。

词池和命名映射保持0.9.6原样，本次仅修复 UI 时间约束与混淆开关输出。
SDK 与普通 Unity 共用 ASS 内部 `ObfuscationGraph`，普通 Unity 的诱饵开关实际应用。
元数据绑定选择不存在的专属路径；原源图和真实对象保持。
普通 Unity 已分配生成 Controller 后再次 Build，会通过持久化的 ASS 专属默认状态 tag 识别并清理旧 ASS 层，
不受当前混淆或诱饵开关影响；反复生成不叠加，关闭选项后重建会移除旧诱饵并保留用户层。

源项目名字保留，混淆在构建副本中发生。Contact 兼容保护继续跳过第三方 Playable 图名，
ASS 自有图与诱饵继续混淆；完整 NDMF 上传的最终 Playable 改名依赖晚期 SDK 回调，
单独 NDMF bake/preview 不额外全图改名。详情见[时间与混淆记录](Verification/ASSTimingObfuscation/README.md)。

0.9.5 为四个自带 MP3 补齐稳定 AudioImporter metadata，启用后台加载，修复 SDK 对
`Decompress On Load` 且关闭 `Load In Background` 的状态行为音频拒绝上传的问题。
源 MP3 内容保持，更新后正常构建即可生成当前引用；不扫描或改写用户其它音频。
“关闭警告音效”仍只关闭倒计时警告音，“关闭覆盖界面”仍只控制画面；成功音仍按
密码流程生成，因此两个选项同时关闭也需要正确的音频导入设置。自定义音频通过其
Inspector 或 SDK Fix 配置后台加载。[本次验证](Verification/ASSAudioUpload/README.md)
覆盖导入、开关组合、真实 SDK 检查和 NDMF 最终图。

ASS 0.9.7 提供密码结果、计时、Overlay、声音和 Restart，全部新生成状态统一 WD On。
范围包含密码、计时、音频、生命周期、防御、混淆、诱饵和 ASS 新建的默认 Idle。
不匹配原 FX、不增加 WD 模式选项，用户已有层和状态的 On/Off 保留。历史隐藏
`writeDefaultsMode` 值继续保留，不控制新图生成；`disableRootChildren` 不恢复旧身体锁定。

旧固定 WD Off 会使原 WD On 表情的空 Neutral 停留在前一状态的权重；真实对照中的 85
在新 ASS On 图恢复为 0。更新后正常构建，在副本中重建 ASS 层并保留原 Controller、
场景与用户状态。对应输出见[WD On 验证](Verification/ASSWriteDefaultsOn/README.md)。

0.9.6 同时修复旧 Countdown 的 TimeUp→Release 转场：原结构没有 Exit Time 或条件，被
Unity 忽略，导致超时后 Restart 清除了 TimeUp 却无法再次计时。现在使用 TimeUp 条件
进入 Release，并让 Countdown/Audio 的本地 Restart 优先返回 Remote，恢复新一轮倒计时
和警告音。该修复针对旧转场结构。成功或超时退出时，WD On 将进度属性恢复为默认值；
超时 Overlay 和音频对象关闭。回归覆盖两轮超时、重启及进度和声音恢复。

普通密码、计时和音频曲线绑定 ASS 反馈对象。启用防御模式且存在可复用 Light 时，
沿用既有的 Avatar 灯光复用及父节点激活逻辑；本次调整其生成状态的 WD。

普通 Unity 保存角色配置后正常构建 Player 即可：构建场景副本自动复制原 Controller/AOC、
清除旧 ASS 生成层（包含遗留锁层）、加入手势/计时/反馈图、配置 Animator 和运行时输入，
最后移除副本中的编辑配置。Inspector 移除新增顶部 HelpBox，保留插件介绍与字段说明，
不再有手动生成按钮；源场景与 Controller 继续用于
编辑。SDK 与 NDMF 沿用 Avatar 的自动构建和结果交接，原资产在各自生命周期中还原。

默认 hold=0.5s、tolerance=0.3s，Inspector 显式编辑与生成副本均保持 hold 严格大于 tolerance。
每位必须连续正确保持；中断重计、容错过期清序列、重复相邻位先换姿势释放，超时与
Restart 优先处理。未编辑的旧非法值继续保存在源组件中；构建副本中的 0.15s/0.3s
使用 0.15s/0.14s，打开、加载和导入不自动改值。
StandaloneSecurityInput 推荐查询 IsPasswordCorrect；IsUnlocked 只作同一结果的兼容别名。
界面与校验说明同步英语、简体、繁体、日语。
仅启用诱饵状态而关闭诱饵层时，条件参数仍先注册；没有 Bool 参数池时补齐本地布尔
守卫，避免生成未声明或类型不符的转移条件。

0.9.1 完整移除 0.9.0 的旧 GUID 迁移器、启动/导入扫描、依赖资源改写、场景重载和
修复菜单/API，同时移除 `OnValidate` 对已有参数的自动纠正。原固定脚本 GUID 保持，
已有 Scene/Prefab、组件 fileID、配置及引用不在包加载时被重写。

0.9.0 自动改写前留下的 UserSettings/UnityBox/ASSMigration/<id>/Assets/... 只作历史
原文件记录，本版不自动恢复、覆盖或删除。需要调查时保留当前工程副本和备份，逐文件
比较后由用户选择恢复目标；备份不包含脚本 metadata、包版本和后来保存的编辑。
已有 Missing Script 按组件的 `m_Script` GUID 查找脚本路径和所属包，结合 Console
首条编译错误定位所属类型；需要重连时，从 `Tools/UnityBox/ASS/Upgrade Component References`
确认一次后升级整个 Assets 的 ASS 引用；不在启动或导入时迁移，不自动恢复旧备份。

完整说明见[ASS 用户手册](../Assets/UnityBox/AvatarSecuritySystem/Editor/README.md)，
当前执行范围见[组件引用记录](Verification/ComponentReferences/README.md)。

## Prefab Diff Patcher

在 A 的 Prefab 实例上应用选中变化，保存时保留 A 的 Variant 祖先和嵌套 Prefab 链接。
选择可覆盖 Transform/RectTransform、组件属性、组件集合和子树；A 独有及未选中的部分
沿用 A。外部资产引用、内部路径引用、数组、ExposedReference 绑定键和循环/共享托管引用
按对应引用身份处理，自定义序列化结果在保存重载后检查。

临时对象保留编辑能力，内置 Light/Renderer 组件可直接粘贴属性；复制子树保持原名。
逻辑 ObjectReference 归一化后停止遍历原生 PPtr ID，ExposedReference 继续比较绑定键。
输出覆盖保持输出 GUID。生成过程在专用 Preview Scene 实例化和合成对象，完成后关闭
该场景；原输入、活动用户场景、clean/dirty 状态和根对象集合保持原状。真实回归分别
保存干净用户场景并检查资产输入与场景输入，覆盖保存输出后的状态。
[Prefab 手册](../Assets/UnityBox/PrefabDiffPatcher/README.md)列出选择和保存操作。

## Avatar Collider Monitor

快照冻结 3D/2D 碰撞体配置、世界变换、活动状态和几何包围盒。SDK provider 追加真实
PhysBoneCollider 的形状、配置和解析后的 rootTransform；同一 Mesh 引用重新烘焙也可形成
几何变化记录。

预览、历史比较和统计按快照版本缓存；多根对象与 Editor 会话各自使用身份域。
历史 JSON 支持导出、替换或追加导入，SceneView 显示世界包围盒。
[碰撞体手册](../Assets/UnityBox/AvatarColliderMonitor/README.md)包含 API 和性能场景。

## Build Pipeline Logger

Unity 导入、构建、场景、脚本、Shader、UnityLinker、SDK 与 NDMF 回调统一记录序号、
耗时、类别、order 和事件名。每次构建开始独立轨迹；完成轨迹保留为 LastCompleted，后续
导入日志进入新轨迹。JSON 可分别导出当前或最后完成构建。

常用设置缓存，关闭日志时 Shader 回调走直接返回路径；同类 Shader 回调按构建去重。
独立 Linux Player 构建启用真实 Linker stripping，轨迹记录实际回调过程。
[日志手册](../Assets/UnityBox/BuildPipelineLogger/README.md)列出菜单与导出 API。

## 打包、测试与性能

VPM 筛选保留既有 `.meta`、Editor 测试和 Runtime 源码，排除 Generated、历史 ASS 探针与
平台 bundle。每个版本按自身包清单构建，固定 ZIP 时间与权限元数据；保留历史 listing
和原 ZIP。相同版本允许相同内容重建，内容改变时提升包版本。发布检查核对 CRC、逐文件
内容、SHA256、listing 和 VCC 入口。

功能运行按普通核心、SDK/NDMF/MA、图标图形模式和独立 Player 分组；性能运行在独立串行
阶段采样。八插件使用各自规模化 fixture，Animator 优化同时比较转换前后图的输出和更新
时间。完整命令与执行数据见 [测试指南](Testing.md)、[验证结果](VerificationResults.json)
和 [性能报告](Performance.md)。
