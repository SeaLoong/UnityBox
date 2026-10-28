# refactor 变更与升级方式

本文记录 `master` 基线 `e9f0b36` 至 `refactor` 的功能变化和升级行为。
Animator Optimizer 位于 `feat/animator-optimizer`；八插件验证工程从两个 checkout 组合源包。
执行记录见 [验证结果](VerificationResults.json)，测量数据见 [性能报告](Performance.md)。

## 分支和用户文件

原 `75396e7 chore: TEMP` 的文件变更以 `bee33c6` 保留，原提交位于
`backup/temp-75396e7`；随后完成共享容器归属和全仓功能修复。
原场景、实验 bundle 与已知 GUID 保留。ASS 组件 metadata 使用场景中三次出现的原 GUID
`1c51e6a139920bb4ca12700dc82627ff`，恢复后继续对应 `ASSComponent`。

工作和原始验证记录放在持久化 `/workspace`。此前完成的 `bc3d256`、`d95284e` 已分别推送
到 `refactor` 与优化器分支；优化器 rebase 前的 `c24813b` 位于远端
`backup/animator-optimizer-before-rebase`。后续完成的功能、测试和文档继续保存在对应工作分支。

## 共同架构

七个包都提供 Unity 核心，真实 SDK、MA 和 NDMF 适配位于独立程序集。包清单按工具职责
声明依赖，普通项目直接使用生成、比较、快照和日志功能。Runtime 类型及序列化字段保留；
SDK Base 可用时配置组件实现真实 `IEditorOnly`。

ASS 与其余六包为原先缺失的脚本、程序集和目录补齐稳定 metadata；新增缺失项按仓库与
资产路径确定身份，已有 `.meta` 保持原值。此后新克隆和安装直接使用仓库 GUID，ASS
恢复的原 Runtime 脚本 GUID 继续关联用户场景组件。

外部 Editor 扩展按 [程序集映射](DependencyArchitecture.md) 增加 Core、VRC/VRChat 或 MA
引用。普通 Animator 的参数由项目脚本提供，Avatar 参数由相应平台输入服务提供。

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
完整操作见 [服装手册](AdvancedCostumeController.md)。

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
继承、显式 None 与每行自定义 Voice；旧 Voice 字段按原枚举含义迁移到当前序列化结构。

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

普通 Unity 新增完整 `StandaloneSecurityBuilder.Build(root, config, path)` 工作流，生成
Controller、持久化状态图及运行时动作；Inspector 提供对应入口。`StandaloneSecurityInput`
提供 `SetGesture`、`IsUnlocked`、`HasTimedOut` 和 `Restart`。

密码按连续 Holding 执行，错误输入回到当前步骤的等待状态；成功与超时按明确条件转移。
生命周期层保持监督，密码由 true 变回 false 时重启锁、计时与密码层；`Restart` 同时清除
密码结果并重新启动输入流程。解锁状态释放对象动画，用户脚本可继续控制原对象缩放。

生成层独立求值，状态图保存只将无资产路径的对象加入目标资源，子资产持久化后可重载和
运行。内存/AOC 构建副本保留有效 Clip 和图身份。每 Avatar 构建使用独立目录及混淆上下文，
SDK 后处理恢复原引用并按本次输出归属清理。NDMF 物理处理边界提交上游服务输出，后续
服务可重新激活；最终提交测试检查前后插件输出、ASS 层和源图身份。

普通 Unity 构建从构建场景副本移除 ASS 编辑配置，运行时输入组件、状态动作及生成
Controller 保留；源场景继续持有原配置。旧场景三处 ASS 组件保持原 YAML 内容，由恢复的
`.meta` 重新关联脚本。
主动负载配置延续新建时关闭的默认值，普通 Unity 工作流生成手势、锁定、计时与反馈。
用户手册和 API 见 [ASS](../Assets/UnityBox/AvatarSecuritySystem/Editor/README.md)。

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
平台 bundle。归档覆盖重建，并核对 CRC、SHA256、listing 和 VCC 内容。

功能运行按普通核心、SDK/NDMF/MA、图标图形模式和独立 Player 分组；性能运行在独立串行
阶段采样。八插件使用各自规模化 fixture，Animator 优化同时比较转换前后图的输出和更新
时间。完整命令与执行数据见 [测试指南](Testing.md)、[验证结果](VerificationResults.json)
和 [性能报告](Performance.md)。
