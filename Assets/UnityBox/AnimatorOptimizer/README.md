# Animator Optimizer

Unity Animator 的分析、离线转换、图清理和构建优化工具。普通 Unity 项目直接使用核心；VRChat SDK、NDMF 和 Modular Avatar 集成按项目需要安装。

当前版本 **0.3.2**。

## 组件引用与配置副本

`UnityBox.AnimatorOptimizer.AnimatorOptimizerComponent` 的类名、Runtime 程序集和已发布字段
保持不变，发行脚本 GUID 为 `a157d3d35699dd84abde9e5412bd7017`。早期标为 0.2 的来源是
开发快照，没有对应的公开旧 VPM ZIP；0.3.0 是带固定身份的公开发布。
本包自带开发快照旧引用的手动恢复功能。正常更新后，在 Project 选择资源，打开
`Assets/UnityBox/Animator Optimizer/Repair Component References`。窗口带入当前选择，
只处理 AnimatorOptimizerComponent；点击 **Migrate Selected Assets** 一次执行整批，
无逐项确认。范围、备份与跳过规则见[组件引用手册](../../../Docs/ComponentReferences.md)。

打开项目或 Inspector 时保留源 `layerIndices`、字符串及预算字段。配置捕获和转换 Options
在独立副本中做字符串、范围及层索引规范化；实际用户改选层或编辑字段仍通过 Undo 提交。

## 安装与入口

| 功能 | 依赖 | 入口 |
| --- | --- | --- |
| 分析、离线保存副本、原地转换、AAP 运算 API | Unity 2022.3 | `Tools/UnityBox/Animator Optimizer` |
| Avatar 构建、Descriptor/LayerControl 适配 | SDK Base/Avatars ≥3.7 | Avatar 根对象的 Animator Optimizer 组件 |
| 虚拟图构建、手动选层、AAP | 上述 SDK + NDMF ≥1.11 | NDMF Resolving / Optimizing / PlatformFinish |
| MMD Behaviour 兼容测试 | MA 1.18、NDMF 1.11、SDK ≥3.7.3 | 可选 MA 测试程序集 |

`package.json` 安装本工具本身；SDK/NDMF 均为可选依赖。NDMF 1.11 提供原对象元数据，手动选层按源状态机身份定位虚拟层。

普通 Unity 使用流程：打开离线窗口，拖入 Controller 或 Animator，分析并选择层，保存副本，再将副本分配给 Animator。原地转换支持 Undo/Redo；保存副本保留源资产。

Avatar 使用流程：添加组件，选择 FX、指定 Animator 或 Explicit Controller，配置需要的转换，执行 SDK 或 NDMF 构建。Playable 目标读取当前自定义层；Animator 与 Explicit 目标使用明确的接收对象。

## 自动选择与运行成本

`master` 同时维护八个 UnityBox 插件。普通“推荐选择”和组件的自动模式按 Mask、BlendMode、状态 Tag 分组合并；只有实际包含两个以上源层的普通组进入自动转换。两个不同 Mask 的单层组即使总计两层，仍保持原状态机，避免生成两层新 BlendTree 却没有减少层调度。

跳过的单层继续参与绑定覆盖顺序检查。若它夹在待合并组之间并控制相同属性，该组合保持原执行顺序；其它独立多层组继续合并。SDK 核心与 NDMF 虚拟图采用同一选择规则。

手动选择保留完整的显式转换能力；离线单层转换关闭 `RequireAtLeastTwoLayers`。AAP 默认关闭，显式开启后按每个源层生成 Update/Output 两层。层数下降和运行成本分别呈现，具体选择结合[实际性能表](../../../Docs/Performance.md)中的原始/转换后数据；单个 Selector、AAP 也有独立的运行成本。

## 图处理能力

| 场景 | 生成结果 |
| --- | --- |
| 单个可达状态、静态输出相同的状态 | 直接 Motion 或共享 Clip |
| Bool 组合、有限 Int/Float 条件、AnyState 优先级 | 结构共享的 1D 决策 BlendTree |
| 混合 Bool/Int 输入、Entry/Exit 响应式图 | 有限决策图；相同子函数复用节点 |
| 带状态保持的有限条件图 | AAP StateRegister Update + Output 两层 |
| 相同 Mask/BlendMode 的静态属性层 | 合并层，保留共同 Mask 与原覆盖顺序 |
| 重复 Override 绑定 | 按输入组合生成后层优先的复合 Clip |
| 同组静态 Additive 浮点绑定 | 对相同绑定求和 |
| 统一时间基准的动态 Clip | 合成到共同时间轴，保留循环和播放设置 |
| 同构 Entry/Exit/Transition 图 | 合并互不重叠的属性 Motion，保留原状态历史 |
| 空 Clip、缺失对象/组件的绑定、空属性层 | 规范空 Motion 或删除非首层空层 |
| MMD 标记与 Dummy 层 | 复制真实 MMD 标记，固定首层和两个兼容槽位 |

Motion 分析递归处理现有 Direct、1D、2D、Freeform 树；签名包含曲线、对象引用、事件和时间设置。时间动画、Behaviour 回调、Trigger、IK、Synced Layer 及不同时间轴的状态图继续使用原生状态机，和已转换层共同运行。首层、不同 Mask 与交错 BlendMode 的位置保持原顺序。

Write Defaults 策略有严格 WD On、完整覆盖的 WD Off、显式按 WD On 处理三种。自动 WD Off 模式要求每个分支写满 Controller 的已知绑定；它适合完整覆盖的属性选择器。原生测试同时覆盖 WD On 多层合并和 WD Off 单层选择器。

## 参数与平台语义

普通 Unity 的 `UseVrChatParameterSemantics` 默认关闭。参数在 Controller 中声明，`IsLocal`、`Viseme` 等同名参数按本地类型处理。Avatar Playable Layer 自动开启 VRChat 模式；普通 Animator/Explicit 目标通过组件字段显式开启。SDK 是否安装不改变普通 Animator 的参数语义。

| 参数设置 | 输入契约与转换 |
| --- | --- |
| Bool → Float | `false/true` 对应 `0/1`；保留图的 If/IfNot 条件同步改写到 0.5 阈值 |
| Int → Float | 保留整数数值；调用方用 `SetFloat` 写整数 |
| VRChat 内建 Float 输入 | 使用原平台 Float 参数作为决策输入 |
| VRChat 内建 Bool/Int 输入 | 保留类型、名称与原生条件状态机，其它层继续合并 |
| Float 精确阶跃 | 使用相邻 IEEE-754 可表示值 |
| Float epsilon 近似 | 使用配置的边界邻域，报告列出实际采用的阈值 |
| AlwaysOne 归一 | 复用显式保持为 1 的命名权重参数，保留别名声明 |

普通 Unity 升级参数后，脚本对应改用 `Animator.SetFloat`；需要继续 `SetBool` / `SetInteger` 的项目关闭对应升级开关。空白离线窗口默认关闭 Bool/Int 升级；公共 Options 保留既有默认值。

Unity 的 BlendTree 读取 Float 参数槽。VRChat 的 `IsLocal`、`Viseme` 等 Bool/Int 平台输入继续由原生状态机处理；原来的 Bool/Int 声明与条件保持一致，其它可优化层照常生成。完全相同输出的分支折叠后不再读取该输入时，可以直接合并。

参数 Driver、Animator FloatCurve 与 Behaviour 引用参与整个 Controller 的参数检查。参数已有写入逻辑时保留其类型和原图执行顺序。未命中条件时保持状态的图使用 StateRegister；普通无历史选择器要求条件覆盖输入域。

## AAP 运算与状态保持

AAP 使用同一 Controller 内 `path=""`、`type=Animator`、`propertyName=参数名` 的 FloatCurve。生成状态使用 Write Defaults On。

| API | 运算 |
| --- | --- |
| `CreateAapValueClip` | 写入常量参数 |
| `BuildBooleanTruthTable` | AND/OR/NOT/XOR 与完整 Bool 真值表 |
| `BuildRemap` | 分段线性重映射 |
| `BuildAddition` / `BuildSubtraction` | 未归一化 Direct 树的加减 |
| `BuildMultiplication` | 声明范围内的双线性乘法 |
| `BuildOnePlusDivision` | 非负 B 下的 `A / (1 + B)` |
| `BuildStateRegisterUpdate` | 根据当前寄存器、输入和 Entry 初始化标记写入下一状态 |
| `BuildStateRegisterOutput` | 使用相同快照计算下一状态并输出对应 Motion |

StateRegister 的两层从同一个输入快照计算下一状态。Unity 在图求值结束时发布动画参数，因此 Output 同样计算下一状态，而不是读取刚写入的寄存器；首帧 Entry 和参数切换当帧的可见结果与原状态机同步。Entry 初始化标记在第一帧置 1，后续按状态转移表保留历史。

本地 Bool、有限 Int 和显式 Float 近似均可用于有序 Entry/AnyState、状态保持和批量转换。每个转换状态提供非空、时间无关 Motion。混合空 Motion 与动画 Motion 的状态图在常规、有限图和 AAP 规划前沿用原生状态机，保留首次进入前的场景姿态以及切回空状态后的上次输出；纯空层继续使用空层清理。寄存器名称避开已有参数及显式 VRChat 保留名。AAP 是显式功能开关；普通无历史层继续使用合并路径。

```csharp
var options = new AnimatorOptimizerOptions
{
    EnableAapStateRegisterOptimization = true,
    RequireAtLeastTwoLayers = false,
};
var optimized = AnimatorOptimizerConverter.CreateOptimizedCopy(
    source, "Assets/Generated/Optimized.controller", new[] { layerIndex }, options);
animator.runtimeAnimatorController = optimized;
```

## NDMF 与 SDK 构建

NDMF 在 RemoveEditorOnly 前捕获配置、Playable 来源和手动层身份。优化阶段读取当前虚拟曲线、条件和状态；其它插件此前对虚拟图的修改会进入生成结果。

AAP 虚拟编译复用核心符号编译器。生成数据复制到独立 VirtualClip/VirtualBlendTree，保留源 VirtualLayer 作为 Output 的身份，在它前面插入 Update。手动选择、后续 LayerControl 和层排序继续定位原身份；多个 AAP 层各有独立寄存器。临时物理编译资产在虚拟数据完成复制后删除，最终 NDMF 提交直接持有完整生成图。

普通 Animator 目标的 AnimatorServices 按需激活：没有优化器配置时保持项目原状；复用其它插件已有服务；本工具持有的服务在提交后释放。原本关闭的 Descriptor 自定义层状态和数组在普通 Animator 处理后恢复。

SDK 独立入口把源 Controller 或内存图复制到构建目录，处理后分配给目标；构建结束恢复引用并清理生成资源。`Execute Late Optimization` 选择 NDMF PlatformFinish 或 SDK 最晚 preprocess。普通 Unity Build 剥离配置组件，运行已分配的优化 Controller。

## 资产、Undo 与测试

发行包保留 Runtime 脚本 GUID `a157d3d35699dd84abde9e5412bd7017` 及其它已知 metadata。早期开发快照的失效 GUID 通过本包的手动恢复入口连接到项目实际安装的脚本；正常导入不重写场景或 Prefab。

离线复制按所选 Controller 的 localFileID 定位副本，支持同一文件内多个同名 Controller。Animator 图使用具体原生构造器创建，再复制序列化数据与重映射引用；源资产、共享图引用和已知 GUID 保持独立。

Undo/Redo 同时处理 Controller 参数、保留图条件、生成 State/StateMachine、Clip 和 BlendTree。Undo 删除本次生成子资产；Redo 恢复完整图并重新运行。保存后的 Controller 经过复制、重新导入和原生 Animator 逐帧对照。

实际测试覆盖 Bool/Int/Float 选择、WD On/Off、覆盖优先级、Mask、多周期动态 Clip、AAP Entry/历史/四则运算、NDMF 最终提交，以及 Linux standalone Player。功能计数和实际运行记录见 [测试指南](../../../Docs/Testing.md)，重复采样和前后性能数据见 [性能实测](../../../Docs/Performance.md)，实现改动见 [审查记录](../../../Docs/AnimatorOptimizerAudit.md)。

```bash
python3 scripts/unity/test_project.py prepare --repo /workspace/UnityBox \
  --project /workspace/unitybox-setup/unity/projects/optimizer-core \
  --profile core --package AnimatorOptimizer --offline
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/optimizer-core \
  --xdg-root /workspace/unitybox-setup/unity/interactive
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/optimizer-core \
  --xdg-root /workspace/unitybox-setup/unity/interactive --benchmark
```

程序集按 Runtime、核心 Editor、可选 VRC、可选 NDMF 分离。测试对应核心、VRC、NDMF、MA 四个装配；仅 Editor+UTF 启用的 MonoScript fixture 注册程序集提供真实 Behaviour，不进入 Player。

## 0.3.2 组件恢复

恢复功能随本包现有 Unity-only Editor 程序集交付；Project 入口只处理 AO 自有配置，
四包共同安装时各自窗口、类型范围和备份目录保持独立。
批次备份为 `UserSettings/UnityBox/AnimatorOptimizer/ComponentReferenceMigration/<id>/Assets/...`。
源配置快照、手动批量与编辑依赖保护见[组件引用说明](../../../Docs/ComponentReferences.md)。
