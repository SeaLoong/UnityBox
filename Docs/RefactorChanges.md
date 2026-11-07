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
0.9.0 曾加入自动恢复，0.9.1 已撤回；当前六类旧引用通过独立手动工具统一处理。

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
正常打开/导入不改写旧引用；用户选定资源后在 Component References 中一次批量迁移，
无逐项确认。完整矩阵与操作见[组件引用说明](ComponentReferences.md)。

外部 Editor 扩展按 [程序集映射](DependencyArchitecture.md) 增加 Core、VRC/VRChat 或 MA
引用。普通 Animator 的参数由项目脚本提供，Avatar 参数由相应平台输入服务提供。

## 六类组件与独立迁移工具

ACC 0.5.1、AO 0.3.1、Viseme 0.2.1 和 ASS 0.9.2 保留原生产组件类、Runtime 程序集、
已发布字段和固定 GUID。BCG、Collider、Logger、Prefab Patcher 没有自有 Avatar 组件，
版本和包内容保持。旧包 metadata 缺失与 AO 0.2 开发快照的身份差异统一由
`Tools/UnityBox/Component References` 手动处理。

工具只写用户选定的资源范围；依赖只读追溯，已打开场景/Prefab Stage 及依赖报告跳过，
也包括编辑中尚未保存的新嵌套 Prefab 和对象资源引用，不替用户保存、关闭或重载。
一键执行整批可确定引用，不弹逐项确认；同一歧义 GUID
选择一次目标类型，未知和重复新旧组件保留。写前校验源 SHA 并备份原字节与对应 `.meta`，
结果位于 `UserSettings/UnityBox/ComponentReferenceMigration/<id>/Assets/...`。

这套 Editor 工具不新增 VPM 包；单包用户导入一次独立 unitypackage。
安装、详细操作与备份边界见[组件引用手册](ComponentReferences.md)，
源参数与真实升级结果见[当前记录](Verification/ComponentReferences/README.md)。

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

普通 Unity 入口共用 Scanner、模型和 AnimationBuilder，支持服装、变体、Parts、两种
Mixer 与材质动画。Avatar 入口进一步安装 MA 菜单、骨架、FX、同步参数和图标。
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

ASS 0.9.2 提供密码结果、计时、Overlay、声音和 Restart。Avatar 对象锁定及对应选项
已经移除，历史 disableRootChildren/writeDefaultsMode 字段隐藏保留，生成阶段只管理
ASS 自己的反馈对象。新增状态统一 WD Off，用户已有层及其 WD 保留。

普通 Unity 保存角色配置后正常构建 Player 即可：构建场景副本自动复制原 Controller/AOC、
清除旧 ASS 生成层（包含遗留锁层）、加入手势/计时/反馈图、配置 Animator 和运行时输入，
最后移除副本中的编辑配置。Inspector 移除新增顶部 HelpBox，保留插件介绍与字段说明，
不再有手动生成按钮；源场景与 Controller 继续用于
编辑。SDK 与 NDMF 沿用 Avatar 的自动构建和结果交接，原资产在各自生命周期中还原。

默认 hold=0.5s、tolerance=0.3s，统一规则保证 hold 严格大于 tolerance。
每位必须连续正确保持；中断重计、容错过期清序列、重复相邻位先换姿势释放，超时与
Restart 优先处理。时间约束只在生成配置副本中规范化，例如副本中的 0.15s/0.3s
使用 0.15s/0.14s；源组件保留原有字段，打开、加载和导入不自动改值。
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
首条编译错误定位所属类型；需要重连时，使用独立 Component References 一次手动
处理选定资源，不启动旧 ASS 自动迁移，也不自动恢复备份。

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
