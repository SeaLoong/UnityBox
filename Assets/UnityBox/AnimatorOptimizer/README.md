# Animator Optimizer

`Animator Optimizer` 是 UnityBox 的 Unity Animator 分析与优化工具。分析、清理、离线转换及窗口不需要 VRChat SDK 或 NDMF；安装可选集成后，也会在 Avatar 构建时处理组件配置。它尝试把通过静态约束检查的参数驱动状态机转换为 BlendTree。没有可表达等价形式的时间/历史语义会保留；静态检查不等于运行时等价性已经验证。

## 依赖与当前状态

- 核心仅需 Unity 2022.3；核心 EditMode 测试另需 Unity Test Framework。`package.json` 不会强制安装 VRChat SDK 或 NDMF。
- VRChat 构建集成可选，要求 `com.vrchat.base`、`com.vrchat.avatars` ≥3.7；缺席时整个 `UnityBox.AnimatorOptimizer.VRC` 程序集不参与编译。
- NDMF 集成同时要求上述 SDK 和 NDMF ≥1.9；`PlatformFinish` 需要 1.8，虚拟 BlendTree 的归一化属性需要 1.9。缺少 SDK 或 NDMF 时，仅排除该适配程序集和对应集成测试。
- 分支是 `feat/animator-optimizer`，已 rebase `master`；这不是外部的 Avatar Optimizer 插件。
- 本插件已有分析器、普通转换器、NDMF、离线窗口和 AAP 后端。**AAP StateRegister 的逐帧反馈尚未做 Unity 等价性验证，默认关闭。**Float 近似、启发式和 AlwaysOne 假设也需要显式选择。
- 无 SDK 的 Runtime、核心 Editor、核心 Tests 已使用 Unity 2022.3.22f1 自带 Roslyn 和真实 Unity/Test Framework 程序集编译通过，没有替身 SDK。Editor 启动因缺少许可失败；Unity 导入/IL 后处理、EditMode 用例执行及实际上传尚未验证。编译测试代码不等于执行测试。详见 [测试指南](../../../Docs/Testing.md) 和 [审查记录](../../../Docs/AnimatorOptimizerAudit.md)。
- 固定 SDK 3.8.2 + NDMF 1.11 的所需真实依赖闭包已通过 Roslyn 类型检查（23 个程序集，含测试），第三方源码未改动。工具不完整复现 Unity 导入器、程序集解析或 IL 后处理；此结果仍不代表 Editor 导入与测试执行通过。较早 SDK 3.7 的签名检查仅保留为相应版本的历史证据。

## 使用方式

普通 Unity 项目通过 `Tools/UnityBox/Animator Optimizer` 打开离线面板，拖入 `AnimatorController` 或 `Animator`，分析并选择 Layer，保存优化副本。也可以在组件上选择 `Animator Component` 或 `Explicit Controller`，再打开详细面板。没有 SDK 时 `Avatar Playable Layer` 不可解析，Inspector 会提示所需依赖；普通 Unity Build 只剥离配置组件，使用离线保存并分配的优化 Controller。

安装可选 VRChat SDK 后：

1. 将 `Animator Optimizer` 添加到 Avatar 根对象。
2. 默认目标是 Avatar Descriptor 的 FX Playable Layer。使用当前有效的自定义层；Descriptor 关闭 Customize 时不会启用残留 Controller。
3. 需要时可改为指定 Avatar 内的 `Animator`，或指定 Controller 与接收它的 Animator。目标不存在、来自其他 Avatar 或路径有歧义时会失败，不会自动换成其他对象；AnimatorOverrideController 不支持。
4. 选择参数升级和严格曲线冲突检查选项。
5. 构建/上传 Avatar。

也可以通过菜单打开离线面板：

`Tools/UnityBox/Animator Optimizer`

面板支持拖入 `AnimatorController` 或 `Animator`，分析 Layer，手动选择 Layer，并保存优化副本或原地修改。

## 支持的状态机形态

当前转换器不是只识别“两态 Toggle”，而是按以下顺序分析：

- 只有一个可达 Default State 的 Layer：直接提取该 Motion；其它没有 Transition 的 State 是不可达的，可以安全丢弃。
- 没有任何 Transition 的多 State 状态机：只保留 Default State Motion。
- 分析前会裁剪可证明从 Default、Entry、AnyState 和可达 State 都到不了的孤儿 State；条件含 Trigger、未知参数、时间或复杂数值关系时按“可能可达”保留，不会激进删除。
- `AnimationClip` 与 `BlendTree` Motion 会先做统一语义分析；安全的单子节点 BlendTree 会去掉包装，等价结构会使用同一签名，不同 Blend 类型、参数或时间设置不会被强行扁平化。
- 已经被 MA MergeBlendTree、VRCFury Action 或其它插件包装成 Direct/1D/2D/Freeform 的 Motion 不会因为节点多就自动跳过：优化器会递归建立 BlendTree selector、Direct child weight 和 AAP 参数闭包。可证明存在且类型安全的本地 Bool/Int 参数会升级为 Float；仅在显式 VRChat 参数语义下，全局参数（如 `Viseme`、`GestureLeft`）作为平台 opaque 参数保留；缺失参数、未知 writer 或 proxy Motion 才会阻止转换。
- Motion 等价签名包含 Clip 曲线、对象引用、事件、循环设置和时间设置；不同资源实例但内容完全相同的 Clip 可以被去重，曲线内容不同则不会仅因名称相同而合并。
- 如果所有 State 的**可观测输出**完全相同，Entry、AnyState、ExitTime、Duration、Offset 等内部状态差异不会改变最终动画效果，转换器会折叠为单一 Motion。对动态 Clip，仍要求 Motion/播放设置完全可证明等价；对静态 Clip，Speed/CycleOffset 等不会改变恒定输出的元数据差异可以忽略，但 Mirror、动态播放参数、AAP 曲线和 Behaviour 仍会阻止折叠。
- Any State 或状态间具有相同转移表的状态机：将每个 Transition 的条件当作状态谓词。
- 没有 Any State 的状态依赖图，如果所有有限 Bool/Int 样本下的源 State 都收敛到同一个 Motion，也可以消除 State 历史并生成 BlendTree；无法证明收敛时仍保留原图。
- MA ReactiveObject 常见的“Entry 条件进入属性 State、当前 State 条件失配时 Exit、Default State 兜底”形态，会逐个有限 Bool/Int/Float 代表域验证：如果每个输入组合下当前 State 的 Exit 与目标 Entry 状态变化严格一致，就会编译成无历史决策 BlendTree；若 Exit 不完整、Entry/Exit 优先级不一致或包含时间语义，则保留原图。
- 只使用 Bool 条件的复杂逻辑：枚举 Bool 参数真值表，生成嵌套 1D BlendTree，支持多个参数的 AND/OR/NOT 组合和默认分支。
- 只使用同一个 Int/Float 参数的条件分区：生成 1D BlendTree，在离散 Int 值上可以精确选择状态；Float 优先使用相邻可表示值表示阶跃，仅无法精确处理的边界需要显式允许近似。
- Bool 与 Int 混合条件：对 Bool 取 0/1，对 Int 按所有条件阈值生成左右代表值，枚举有限笛卡尔积并生成多维嵌套 1D BlendTree；组合超限或代表域未覆盖时保留原 Layer。
- 有限决策树会做结构共享：不同输入路径如果得到同一个子函数，会复用同一个 BlendTree 节点，而不是按输入组合机械复制整棵树。默认有限组合预算已提高到 4096；该预算是资源保护，不代表“树复杂所以语义不可优化”。
- Float 可以进入有限决策表；默认保留阈值及相邻可表示值，零/极端阈值等不能安全表示的情况拒绝转换。显式允许近似时才对这些边界使用 epsilon 邻域，并在报告中标记。
- 常见的两态 Bool Toggle、多个互斥 Any State 分支和同一参数的多值选择都属于以上通用路径，不再单独写死。
- 启用显式 VRChat 参数语义时，VRCFury Local/Remote Toggle 使用的 `IsLocal`，以及 `Viseme`/`GestureLeft` 等内建参数，即使未声明于 Controller 也会按平台已知 Bool/Int 类型推导；这些全局参数不会被升级或删除，只作为运行时 opaque 输入保留。
- 使用同一内容 `AvatarMask` 的 Layer 可以一起合并，生成 Layer 会继承共同 Mask；不同 Mask 会按内容分组生成独立 Layer，避免掩码影响范围被扩大。
- Layer 会按 `(BlendMode, Mask 内容)` 分组，并按每组第一次出现的原始索引插回 Controller。Override 与 Additive 组不会被强行混成一个 Layer。
- 多个 Override Layer 写同一条曲线时，如果输出都是静态 `AnimationClip`、输入域只包含 Bool/Int 且组合数在上限内，会枚举输入组合生成复合 Clip，按原 Layer 顺序执行后层覆盖前层。
- 多个 Additive Layer 在相同 Mask 组内且只包含静态浮点曲线时，会对同一绑定求和；不同组之间保持原始 Layer 顺序。
- 同组普通 Layer 会按绑定合成单一 Clip：Override 使用后层覆盖，Additive 对浮点曲线求和；静态 Clip 仍需通过绑定冲突、Mask、层权重、WD 和预算检查，动态 Clip 只有在帧率、长度、循环、State Speed/CycleOffset/Mirror 和关键点时间轴全部一致时才合并，否则不会把它们直接塞进 Direct Tree。
- 清理阶段会识别“空资源”而不只看资源名称：没有曲线/事件的 Clip、只绑定到确定不存在对象或组件的 Clip，以及只由这些 Motion 构成且没有可观察 Transition/Behaviour 的 Layer，可以被折叠或删除。首 Layer 始终保留。
- 1 帧和 2 帧的空 Clip 不能无条件互换：如果 Exit Time、Transition Duration/Offset、状态时间参数或其它时间观察者可能读取 Clip 长度，会保留原始 Clip；只有没有时间观察者时才共用规范空 Clip。AAP 的 `Animator` 曲线、Animation Event 和实际存在的对象/组件绑定不会被判为空。

## 有意跳过的情况

这些语义不能由普通 BlendTree 无损表达，插件会保留原 Layer 并给出原因：

- `StateMachineBehaviour`，尤其是 `VRCAvatarParameterDriver`、音频播放和其它参数驱动组件；
- `Exit Time`、Transition Duration、Motion Offset、时间依赖的循环反馈和 Motion Time；
- Entry/Exit 逻辑、带局部转移的嵌套 StateMachine、Synced Layer、IK Pass；不同 Mask 的 Layer 不会放进同一个生成 Layer；
- Additive Reference Pose、重复的 Additive 对象引用曲线，以及无法证明顺序等价的交错 BlendMode/Mask 组；这些情况会保留原 Layer。
- 即使 Additive Layer 的绑定互不重叠，timed Clip 只要包含 Additive Reference Pose 也会保留；合并一个带参考姿态的 Clip 到另一个 Clip 会改变参考姿态来源，不能仅因曲线时间轴相同就视为安全。
- 使用 AAP 写入 Animator 参数的原始动画。AAP 是通过 `AnimationClip` 的 `Animator` FloatCurve 写入同一 Controller 的参数，不是 `ParameterDriver`；它具有 Controller 局部性、层顺序和默认值限制，不能作为任意状态机的黑盒替代。
- 名称以 `proxy_` 开头的 VRCFury/VRChat 特殊 Clip 会被视为平台 Motion，不能按普通静态 Clip、空 Clip 或 singleton wrapper 处理；NDMF marker Clip 同样保持不可变。
- 不能证明互斥的重复动画绑定。关闭严格冲突检查可以强制尝试，但可能改变原 Layer 的覆盖优先级。
- 优先级复合不会处理有独立动画时间的 Clip、连续 Float 区间、AAP 或 Behaviour；这些情况仍保留原 Layer。BlendTree child 的排列顺序本身不是 Override 优先级模拟，因此不会依赖它碰运气。
- Direct BlendTree 的多 Clip 播放时间会互相影响；除非已合成到同一时间轴 Clip，转换器不会把不同长度/速度的动态 Motion 当作并行静态属性。
- 只有配置完全一致的已知 MA `ModularAvatarMMDLayerControl` 会被复制到生成 Layer；VRCFury `VRCAvatarParameterDriver`、`VRC_AnimatorLayerControl`、Action/Audio 等未知或有副作用 Behaviour 不会因为树可枚举就被吞掉。
- 纯组织层级的静态嵌套 StateMachine 可以解析到唯一 Default Motion；如果嵌套图内所有 State 的静态可观测输出相同，且 Entry/AnyState/State/StateMachine Transition 都留在该图内，也可以忽略内部 ExitTime/Duration 等差异折叠为单一 Motion。含 Behaviour、外部目标或 Exit 离开该图的嵌套图仍会跳过。
- 空 Layer 删除也要求没有 Entry/AnyState/State/StateMachine Transition，且所有 State 都是 Write Defaults Off、无 IK/Mirror、无非默认播放设置；含 Behaviour、Synced Layer、时间语义或 AAP 参数曲线的 Layer 会保留，避免把“没有普通 GameObject 曲线”误判成没有效果。即使关闭 MMD 选项，名称为 `MMD Dummy Layer 1/2` 的已知兼容槽位也不会被清理删除。

“所有状态机”并不等于“所有状态机都有等价 BlendTree”：有状态时间和历史依赖的图本质上不是当前参数到输出的纯函数。对这类图跳过比生成错误结果更安全。

## 参数语义

`AnimatorOptimizerOptions.UseVrChatParameterSemantics` 默认是 `false`。普通 Unity 模式要求引用的参数在 Controller 中声明：未声明的 `IsLocal` / `Viseme` 会阻止转换；已声明的同名参数按普通本地参数检查和升级。即使安装了 SDK，普通 Unity 模式也不会自动变成 VRChat 模式。

离线面板提供“VRChat 内建参数语义”开关。开启后才允许从平台类型表推断未声明的内建参数、保留内建参数类型，并在生成权重名或 AAP 寄存器时避开这些名字。仅在该 Controller 确实接收 Avatar Playable 内建参数时开启它；位于 Avatar 下的普通子 Animator 不会因此自动接收这些参数。

| 入口 | VRChat 参数语义 | Bool / Int 升级初值 |
| --- | --- | --- |
| 空白离线窗口、`SetTarget(controller, null)` | 关闭 | 均关闭，调用方接受 `SetFloat` 迁移后手动开启 |
| 直接创建核心 `AnimatorOptimizerOptions` | 关闭 | 保留 `true`，兼容现有显式 API；调用者负责脚本契约 |
| 从组件打开面板、VRChat SDK / NDMF 构建 | `AvatarPlayableLayer` 自动开启；Animator / Explicit 默认关闭，仅显式 `useVrChatParameterSemantics` 开启时启用 | 保留组件已有配置 |

SDK / NDMF 适配入口保留目标的参数语义，不会把普通子 Animator 的缺失参数自动当作 Avatar 内建参数。组件的新字段 `useVrChatParameterSemantics` 默认 `false`，Inspector 仅在 Animator / Explicit 目标显示该开关。

直接调用核心 API 处理接收 Avatar 内建参数的 Controller 时，应把同一个 `UseVrChatParameterSemantics = true` 的 options 传给分析、选集验证和转换。该选项与参数升级开关相互独立；不需要升级的 VRChat 内建参数在关闭本地升级时仍保持原类型。

## 参数升级

普通 Unity 脚本若仍调用 `Animator.SetBool` / `SetInteger`，不能假定它们会自动写入升级后的 Float 参数。调用方必须同步改用 `SetFloat` 并保持离散输入约定，或关闭对应升级选项；优化器无法证明任意外部脚本、OSC 或第三方系统的写入方式。VRChat 表达式参数映射是平台行为，不是普通 Unity 的通用保证。

### Bool → Float

勾选后，使用于被转换逻辑的本地 Animator Bool 参数会改为 Float，并把保留图中的 `If/IfNot` 以及可表示的 Bool 等值条件重写为 `Greater/Less 0.5`。VRChat 表达式 Bool 到本地 Float 的映射是 `false → 0`、`true → 1`，所以 Toggle 仍然是离散的，同时可以作为 BlendTree 参数。

如果该参数被无法检查的 Parameter Driver、其它 StateMachineBehaviour 或 Controller 内的 AAP `Animator` FloatCurve 使用，相关转换会被跳过；参数升级按整个目标 Controller 检查，而不是只看被选中的 Layer，避免把参数计算的层顺序错误地当成普通属性曲线。

### Int → Float

勾选后，使用于被转换逻辑的本地 Animator Int 参数可改为 Float，保留整数输入域。若保留图仍用该参数做 `Equals/NotEqual`，则拒绝升级，避免把无效的等值条件留在 Float 参数上。VRChat 表达式 Int 到本地 Float 是数值转换，不是 0~1 归一化；Float 在 Unity Animator 中也不是只能取 0~1。因此 ACC 一类“VRC Int + 本地 Float + 整数阈值”的结构是可行的。

### Float 条件近似

Animator Transition 的 Float 条件是阶跃判断，而 1D BlendTree 在阈值之间是线性插值。默认尝试用相邻 IEEE-754 可表示值保留阶跃边界，无法证明的零/极端阈值会保留原图。勾选近似后，这些边界才允许使用可配置的近似区间，并在报告中提示。

### AlwaysOne 与空权重

启用 `Normalize AlwaysOne Parameters` 后，插件会在满足安全条件时从默认值为 `1` 且名称含 `AlwaysOne` 的 Float 参数中选择一个 Canonical 参数，让 Direct BlendTree 权重引用统一到它，并在创建生成层权重时优先复用它。别名参数不会被删除，以免破坏 VRChat Expression 参数或其它外部系统的引用；被 AAP `Animator` FloatCurve、ParameterDriver 或无法检查的 Behaviour 使用的参数不会参与归一。

这是一项构建后清理/复用启发式，不会把“名称叫 AlwaysOne”当作绝对数学证明。若别名实际上会在运行时被外部写入，应关闭该选项。

### MMD Layer 兼容

启用 `Preserve MMD Compatibility` 后，FX Controller 的首层保持不动，并确保索引 `1`、`2` 分别是权重为 `0` 的 `MMD Dummy Layer 1`、`MMD Dummy Layer 2`。已有 Dummy Layer 会被移动而不是重复创建；空 Layer 清理也不会占用或删除这三个兼容槽位。启用该选项时，插件不会转换原始首层；如果有 Synced Layer，会在插入兼容层后重映射仍然存在的目标索引。

## AAP 运算图

`AnimatorOptimizerAapBuilder` 提供独立的 AAP/BlendTree 生成器，用于把参数运算建模成真正的动画图：

- `CreateAapValueClip` 创建 `path=""`、`type=Animator`、`propertyName=参数名` 的 Animator FloatCurve。
- `BuildBooleanTruthTable` 生成嵌套 1D BlendTree，适合 AND/OR/NOT/XOR 和任意有限 Bool 真值表。
- `BuildRemap` 使用 1D 分段线性插值实现参数重映射/比较近似。
- `BuildAddition`、`BuildSubtraction` 使用未归一化 Direct BlendTree 叠加 AAP 曲线。
- `BuildMultiplication` 使用嵌套 1D BlendTree 的权重相乘；它在声明的输入范围内精确，超出范围会按 BlendTree 端点钳制。
- `BuildOnePlusDivision` 使用 Normalize Blend Values 和一个无曲线 Dummy Motion 实现 $A/(1+B)$；要求分母输入非负。一般 $A/B$ 可先把分母变换为 $B-1$，但要自行保证归一化条件成立。

运算 API 会检查输入是否有限、范围是否合法以及除法输入的非负约束；超出声明范围的输入仍需调用方处理。

AAP 符号计划会保存编译时的参数语义，生成更新树时继续使用同一模式。普通 Unity 可用 `VelocityX` 等名字作为本地寄存器；VRChat 模式禁止覆盖这些内建参数。`BuildBooleanStateRegisterUpdate` 的原签名保留并默认使用普通 Unity 语义；VRChat 调用者使用新增重载 `useVrChatParameterSemantics: true`。`AllocateParameterName` 同样提供显式平台标记重载。

AAP 运算图必须放在同一个 Animator Controller 内，生成状态必须使用 Write Defaults On；AAP 不跨 Playable Layer Controller 传播，也不能由 `VRCAvatarParameterDriver` 设置或读取。该生成器目前是显式 API，不会默认把 AAP 计算层插入普通 Layer 合并流程。

开启组件中的 `AAP StateRegister Optimization` 后，满足限制的有状态图会使用两个生成层：第一层用 AAP FloatCurve 每帧写入下一 State 编号，第二层用该编号的 1D BlendTree 选择原 State Motion。除了 Bool，还支持有限 Int 分区以及显式允许的 Float 阈值近似；带有限 Entry 条件的图会额外生成初始化标记，首帧先按 Entry 表写入初始 State，之后再进入普通 StateRegister 反馈。它仍不支持 ExitTime、Transition Duration、Offset、Behaviour、Trigger、动态 Motion 或嵌套状态机。AAP StateRegister 的 Int/Float 输入会按阈值邻域生成多维 1D 决策树，Int 的整数输入在代表域内精确。

## 构建时机

- 离线保存复制整份资源文件后，按源 Controller 的 local file ID 选择对应副本；同一文件内多个同名 Controller 不再误选第一个。身份无法唯一解析时明确失败并清理未完成输出。构建流程对 Controller 子资产使用独立图复制，主 `.controller` 文件复制也按 local file ID 核对。
- NDMF 手动选层会把原状态机身份映射到虚拟层，避免 MA 等插件插层后旧索引指向另一层。身份缺失或不唯一时跳过并阻止 SDK 重试；手动选层的 AAP 构建路径暂不支持，会跳过并提示。自动选层的 AAP 仍可使用物理 fallback。
- 有 NDMF：在 `Resolving` 的 `RemoveEditorOnly` 之前快照组件配置、原 Playable 有效性与手动选层身份，并从构建副本剥离组件，在 `Optimizing` 阶段处理虚拟 Avatar Playable/Animator Controller；源 Controller 不会被修改。
- 原 Descriptor 关闭 Customize、目标使用默认层或没有真实自定义 Controller 时，Playable 目标保守跳过，并阻止 SDK 把后续 NDMF 状态误当作原有效源再次处理。这也意味着其它插件稍后新建的 Playable Controller 不会让原无效目标自动变成有效目标。
- NDMF Animator Services 改为通过配置检查后按需激活；没有 AO 配置时不再因安装 AO 就启用 Descriptor 自定义动画层。已由其它插件激活的服务会复用并保留；本次自行激活的服务在结束时释放。处理普通 Animator 目标时，若原 Descriptor 关闭 Customize，本次服务提交后恢复原 Playable 数组与关闭状态，避免启用无关的残留 Controller。
- NDMF 虚拟路径与普通路径同样要求动态 Motion 的时间轴可证明一致；VirtualClip 会比较帧率、循环/起止时间和关键点结构后再合并，不满足时保留原组。
- NDMF 虚拟路径即使没有足够的可合并 Layer，也会执行启用的空 Layer/AlwaysOne/MMD 收尾；AlwaysOne 归一会排除虚拟 Controller 中的 AAP `Animator` 曲线、ParameterDriver 和其它可序列化 Behaviour 引用。有限表升级也会扫描整个虚拟 Controller；AAP StateRegister 需要写入真实 Animator FloatCurve，因此仍由物理 VRCSDK fallback 处理。
- NDMF 虚拟路径也会对 MA Reactive 风格的 Entry/Exit 有限函数做同样证明；通过后生成普通决策 BlendTree，不通过则保留虚拟状态机。物理 AAP StateRegister 的首帧 Entry 初始化反馈不在 NDMF 虚拟路径伪造，仍由真实 FloatCurve fallback 负责。
- 有 SDK、无 NDMF：最早的 VRCSDK callback 快照并剥离配置，随后在 preprocess 的 EditorOnly 清理附近处理，并把 Controller 复制到 UnityBox 的临时 Generated 目录；构建结束恢复 Avatar 对源 Controller 的引用并清理临时资产。
- 开启 `Execute Late Optimization` 后，NDMF 使用 `PlatformFinish`；无 NDMF 时使用 `int.MaxValue` 的 VRCSDK preprocess callback。早期 callback 会延迟处理，NDMF 只有在真正完成虚拟优化后才会阻止物理 fallback，避免 AAP/cleanup-only 场景被“已捕获”状态误跳过。

## 后续方向

- 进一步验证现有同时间基准的动态 Clip 合并；不同时间基准仍不支持，不能把静态分析通过等同于已在运行时验证。
- 继续扩展 AAP Transition 后端：当前有限 Bool/Int/Float StateRegister、Entry 初始化和 MA Reactive Entry/Exit 已经可以生成决策树；后续可继续加入更多可证明的参数 Driver 代数（Copy/Add/Set）和分段数学运算，并记录精度与归一化前提。
- 对带局部参数谓词的嵌套 StateMachine 做受限扁平化分析；目前仅支持纯组织层级的静态嵌套图。
- 增加上传前 Controller 快照、逐状态差异报告和运行时等价性采样。

## 资产与目标边界

- SDK fallback 对源资产或内存 Controller 建立独立副本；成功后只替换目标 Avatar 的引用，构建结束恢复并清理临时资产。
- 离线窗口的“原地修改”会修改所选 Controller；用副本验证结果，查看诊断后再决定是否原地应用。转换会记录被修改的子资产以支持 Undo；生成子资产的完整撤销回收仍需验证，建议优先保存副本。
- 多个独立 Animator 的选择不能依赖“场景里第一个”；优先显式目标。构建副本中仅接受可唯一解析的相对路径。
- 老分支未提交完整 `.meta`，已有场景的组件 GUID 需从原开发项目恢复，不能用新 GUID 代替。
- 暂不支持任意嵌套状态机、Trigger/副作用 Behaviour、任意时间历史或 AnimatorOverrideController 的等价转换；拒绝这些形态是保留语义的边界，不应通过忽略报错强行优化。

## 程序集边界与回归测试

- `UnityBox.AnimatorOptimizer.Runtime` 保留原组件类型、程序集名和序列化字段；无 SDK 时没有 SDK 引用。安装 VRChat Base ≥3.7 时，仅通过版本条件实现真实 `VRC.SDKBase.IEditorOnly`，使用 SDK DLL 的自动引用，保持上传前脚本白名单兼容。没有替身 SDK 类型。
- `UnityBox.AnimatorOptimizer` 包含分析器、转换器、窗口、配置快照和普通 Animator 目标处理；通过显式平台接口接收可选 Descriptor 目标及 LayerControl 索引操作。
- `UnityBox.AnimatorOptimizer.VRC` 使用真实 SDK 类型实现 Descriptor 分配/恢复、LayerControl、四个 SDK 构建回调。NDMF 虚拟控制器代码继续放在独立 `UnityBox.AnimatorOptimizer.NDMF` 程序集中。
- `UnityBox.AnimatorOptimizer.Tests` 不引用 SDK，覆盖核心、通用 Processor、配置剥离、程序集依赖边界和显式平台参数语义；`UnityBox.AnimatorOptimizer.VRC.Tests` 使用真实 ParameterDriver、LayerControl 和 Descriptor；`UnityBox.AnimatorOptimizer.NDMF.Tests` 仅在对应 SDK/NDMF 依赖齐全时编译。
- 通过 Unity Test Runner 的 EditMode 页面运行已启用的程序集；UPM 安装时在项目 `Packages/manifest.json` 的 `testables` 中加入本包名。包归档和 Roslyn 编译检查不能替代 Unity Test Runner 执行或 Avatar 上传验证。
