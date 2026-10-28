# Animator Optimizer 审查

分支 `feat/animator-optimizer` 已从旧基线 rebase 到 `master` 的 `e9f0b36`。
原提交 `c24813b` 保存在 `backup/animator-optimizer-before-rebase`，未改写远端。
场景来自该分支，未在源码审查中重写。

2026-10-26 在原功能审查后继续拆分独立 Unity 核心和可选 SDK/NDMF 适配。
其他七包的完整修复在 `refactor`，尚未合入本分支；本文不将它们的功能或测试视作优化器证据。

## 完成程度

这不是只有窗口的空壳：已有普通状态机分析/转换、静态与受限动态 Motion 合并、
参数升级、空资源清理、Mask/MMD、同构状态图、NDMF 虚拟处理、离线面板和 AAP API。
不过 AAP StateRegister 反馈时序仍属于未完成验证的实验后端，默认关闭；任意有
副作用/历史/时间语义的图没有通用等价转换，仍应保留原图并报告原因。

## 本轮修正

- 早期按 `PlatformFinish`（1.8）、`VirtualBlendTree.NormalizedBlendValues`（1.9）
  API 存在检查，将旧的 ≥1.5 条件修到 ≥1.9。这不足以证明完整功能可用；本次原生验证发现
  源对象身份映射还需要 NDMF 1.11，当前最低版本已提高到 ≥1.11，原因与迁移影响见下文。
  测试装配仅在启用测试条件时编译，NDMF 用例单独隔离。
- Bool 升级时同步处理保留图中的等值条件；保留图的 Int 等值判断不能直接留在
  Float 上，相关转换保守拒绝。普通与虚拟路径均检查这些约束。
- 原地转换补齐子资产 Undo 与脏标记，避免撤销或重新加载后参数和条件不一致。
- 配置捕获不再受禁用根组件阻断，开始新构建清理旧状态；外部、失效或歧义 Animator
  不再静默改用另一个目标。Explicit Controller 没有接收对象时明确失败。
- NDMF 的非 Playable 目标以实际 Animator 为上下文字典键；显式源与其当前 Controller 不一致时走物理回退，避免找不到或改错虚拟图。
- SDK fallback 对内存 Controller 建立独立、可持久化副本，复用状态图复制逻辑，
  隔离瞬态 Clip/Mask；跳过 `customizeAnimationLayers=false` 时残留的自定义层，
  避免优化器启用原本关闭的配置。构建清理恢复原引用和开关。
- NDMF 手动选层按原状态机身份映射到合并后的虚拟层；无法唯一定位时跳过，
  同时阻止 SDK 使用过期索引再次处理。手动选层的 AAP 构建路径暂不支持。
- AAP 状态寄存器拒绝与输入参数同名的寄存器和无效默认状态，在写入子资产前报告错误；
  VRChat 模式额外拒绝平台内置参数名，普通 Unity 同名本地参数按显式声明处理；数值/范围校验保留。
- 生成目录及默认输出名使用跨平台单路径段，拒绝点目录和分隔符造成越界。

各项均补充针对性的 EditMode 回归。测试执行方式和验证边界见 [测试指南](Testing.md)。

## 独立核心与可选平台适配（2026-10-26）

`UnityBox.AnimatorOptimizer.Runtime` 保留原组件类型、字段和装配身份；没有 SDK 时只用
Unity。在真实 `com.vrchat.base >=3.7` 存在时，条件实现 SDK 的 `IEditorOnly`，保留上传前
白名单兼容，没有创建同名 SDK 替代类型。原有缺失 `.meta` 仍需从原项目恢复。

`UnityBox.AnimatorOptimizer` 保留分析器、Converter、AAP API、离线窗口、配置快照和
普通 Animator/Explicit Controller 处理。它通过明确的平台接口接收 ControllerTarget
（Controller、BindingRoot、Assign、CaptureRestore）和 LayerControl，不再直接依赖
Descriptor。普通 Unity 可离线保存副本并自行分配给 Animator；组件配置不会让普通 Unity
Build 自动优化，构建回调仅移除配置组件。缺 SDK 时 Avatar Playable Layer 不可解析，
Inspector 提示所需依赖，不改选另一个 Animator。

新 `UnityBox.AnimatorOptimizer.VRC` 在真实 Base/Avatars ≥3.7 时启用，容纳 Descriptor
目标分配/恢复、SDK LayerControl 和四个 SDK 构建回调。NDMF 装配当前依赖该适配与 NDMF
≥1.11（早期拆分曾声明 ≥1.9，已由本次原生验证纠正）。package 取消强制 VPM 依赖，安装插件本身不自动安装 SDK；已有平台工程可以保留
原依赖。SDK 与 NDMF 的配置捕获、构建副本剥离、晚期处理和 RestoreAndCleanup 保持。

核心测试不引用 SDK；实际 Descriptor、ParameterDriver、LayerControl 用例移到
`.VRC.Tests`，虚拟图位于 `.NDMF.Tests`，均只在依赖满足时编译。外部 asmdef 若直接使用
已迁移平台类型，需要增加 `.VRC` 引用。普通 Unity 脚本写入的 Bool/Int 若被升级成 Float，
调用方也需改为 SetFloat 并保持离散域，或关闭升级；静态分析不能证明任意外部写入方式。

## 验证证据

早期审查完成过 C# 语法、JSON、5 项 Python 打包测试及八包归档/CRC/SHA256/metadata
检查，当时没有 Editor。该阶段的 57 个 C# 文件/25 个 JSON 等计数仅描述当时快照，不能
作为后续新增适配程序集已经编译的证据。

2026-10-26 安装的 Unity 2022.3.22f1 可以执行 `-version`。可写 XDG 配置解决了只读 home
问题后，当时 Editor 启动仍 exit 1：`No valid Unity Editor license found`，因此该阶段未执行导入或
Test Runner；此限制已在 2026-10-29 完成 Personal 激活后解除。使用真实 Unity/Test Framework 引用和 Editor 自带 Roslyn 对 Runtime、核心
Editor、核心 Tests 及两个 UTF 程序集做类型检查，5 个全部通过，无 fake SDK。
2026-10-28 最终核心证据：`/workspace/unitybox-setup/unity/optimizer-subasset-core-20261028/compile-report.json`。

最终固定 SDK 3.8.2、MA 1.18、NDMF 1.11 的共享环境后，优化器实际引用的完整 C# 依赖闭包
**23 个程序集全部编译通过**，`failed=[]`，包含真实 SDK/NDMF 源码、优化器核心/适配及测试，
未修改第三方源码。2026-10-28 最终报告为 `/workspace/unitybox-setup/unity/optimizer-subasset-full-20261028/compile-report.json`。
优化器没有 MA 硬依赖，23 个程序集不包含 MA 或本分支旧七包，不能作为那些插件已通过的证据。
这补足了完整启用依赖的类型检查，不包含 Unity import、IL 后处理、build 或任何测试执行。

早期另对官方 SDK 3.7 DLL 检查 Runtime/核心/VRC/VRC.Tests 四个程序集，以及引用未修改上游
NDMF 1.11 输出的 NDMF/NDMF.Tests 两个程序集，均通过签名类型检查。报告在
`/workspace/unitybox-setup/unity/signature-optimizer-platform-target/` 的
`signature-vrc-report.json`、`signature-ndmf-report.json`。它们明确省略未构建且源码无需
直接调用的 SDK 脚本装配，不是完整 asmdef 依赖图。它们保留作历史；不能把最终 SDK 3.8.2
闭包结果追溯为早期版本已经完整编译，也不能由任一编译结果推断上传、逐帧行为或所有最低
支持版本均已验证。

当前源码声明核心 127、VRC 12、NDMF 33、MA 2 个用例，共 174；实际执行结果见下节。
此前类型检查不能代替真实 Editor 的导入与测试，也不包含上传或逐帧等价验证。
共享项目准备/运行/报告工作流见 [测试指南](Testing.md) 与 [Unity 环境](UnityEnvironment.md)。

## 普通 Unity 参数语义与 VRChat 的显式区分

仅移除 SDK 引用还不足以支持普通 Unity：旧核心按参数名称硬编码 VRChat 内建语义，
例如未声明的 IsLocal/Viseme 仍可能获得平台类型，并且同名本地参数不能正常升级。
现以 `AnimatorOptimizerOptions.UseVrChatParameterSemantics` 显式控制，默认 false，
Clone 保留其值。普通 Unity 必须声明参数；名字碰巧与 VRChat 内建参数相同不改变它的
本地类型/升级规则。VRChat 模式才推断内建输入、禁止升级内建参数，并在生成权重名称和
AAP 状态寄存器名称时避开平台保留名。

组件新增 `useVrChatParameterSemantics=false`。`ConfigData.FromComponent` 使用
“目标是 AvatarPlayableLayer，或用户显式开启该字段”的规则。普通 Animator 与
Explicit Controller 默认普通 Unity 语义，SDK/NDMF 构建也保留这一选择，不因安装 SDK
或对象位于 Avatar 下自动开启。原因是普通子 Animator 不自动获得 Avatar Playable Layer
的内建输入；误推断未声明参数可能让原本无输入的图通过分析。调用者只有在实际提供平台
输入语义时才应显式开启。

空白离线窗口及没有显式配置的 `SetTarget` 同时关闭 Bool/Int→Float 升级，避免普通
游戏脚本的 SetBool/SetInteger 在用户不知情时失效。传入的组件配置仍保留其升级选项；
旧参数升级字段和既有默认值不迁移，新增平台语义字段默认 false。公共 Options 的升级选项仍保持原 API 默认 true，
直接 API 调用者必须检查写入方是否遵守 SetFloat/离散域约定，不能把 UI 默认与 API 默认
混为一谈。

手动调用核心 API 处理 VRChat Playable Controller 时，需把同一份显式启用平台模式的
Options 传入分析、选择和转换，避免各阶段对参数采用不同假设。

AAP 符号计划记录平台模式。原 `BuildBooleanStateRegisterUpdate` 调用签名保留，默认
普通 Unity；VRChat 调用者使用新增 bool 重载并传 `useVrChatParameterSemantics: true`。
`AllocateParameterName` 同样新增显式模式重载。回归覆盖同名本地参数、未声明参数拒绝、
VRChat 内建输入推导、寄存器/权重名避让及构建入口传递；不能据此假设任意外部写入服务
或 AAP 逐帧反馈已获验证。

## NDMF 按需服务与原有效 Playable 来源（2026-10-28）

原配置无条件要求 AnimatorServicesContext；NDMF 激活服务会将 Descriptor 的
customizeAnimationLayers 设为 true，并加载数组中残留的非默认 Controller。因此原 Avatar
关闭自定义层时，仅 SDK getter 的 false 检查不够：到优化阶段开关已经变了，旧图可能被
误当作有效目标；甚至没有优化器配置也会触发这项平台状态变化。

现在提前记录原有效 Playable 选择，并使用兼容扩展声明，只有配置、阶段与来源检查通过
才按需激活服务。原 customizeAnimationLayers=false、默认层或无自定义 Controller 的
Playable 目标保持跳过，同时标记本次已处理以阻止 SDK 再次误用变更后的开关。该策略保守：
即使其它插件稍后创建新层，本次原本无效的 Playable 目标也不会重新启用自动优化。

Animator/Explicit 目标需要自行激活服务时，结束后恢复原来关闭的 Descriptor 开关和
base/special 数组，避免复活无关残留图；只释放本插件本次拥有的服务，复用其它插件已激活
的服务不将其关闭。9项新增NDMF回归覆盖无配置、无效来源、有效来源与服务拥有权/恢复。
按需服务 API 在官方 NDMF 1.9 已公开，这是当时的 API 存在证据；该检查未覆盖源对象
身份映射。当前完整适配的最低版本已改为 1.11，不再将 1.9 的单项 API 检查解释为全功能支持。

核心5、仅SDK19、SDK+NDMF23个程序集于2026-10-28最终类型检查通过，包含最后
两项复制身份回归。SDK-only报告为
`/workspace/unitybox-setup/unity/optimizer-subasset-sdk-20261028/compile-report.json`。
这些是激活之前的类型检查结果；相应用例现已进入真实 Editor 验证，当前结论见下节。

## 同文件 Controller 身份

离线复制整个资源文件后，旧代码加载主Controller，用户选择文件中的另一个同名Controller
时可能输出错误对象。现在按所选源对象的localFileID唯一匹配副本；没有唯一匹配就清理
失败输出并中止，不退回猜测主对象。构建主.controller复制入口共用精确加载逻辑。

构建子资产原本就使用CopySerialized及独立图复制，本次没有发现它取错主对象；新增回归
明确检查同一文件第二个同名Controller在离线和构建路径都保持正确身份。由错误文件结构
触发的旧静默成功现在会变成明确失败，需检查源资产而不能继续使用错误输出。

## 真实 Editor 修复与验证（2026-10-29，北京时间）

Hub 使用已有 Personal 账号完成此机器激活，Editor 日志确认 `Successfully updated license`。
此前许可阻断已解除；不需要另购许可证。固定 Editor 2022.3.22f1、UTF 1.1.33 / NUnit 1.0.6，
平台组合使用真实 SDK 3.8.2、NDMF 1.11、MA 1.18，未修改第三方源码。

| 实际范围 | 结果 | 本次 run ID |
| --- | --- | --- |
| 无 SDK 核心 | 127/127，通过，0 跳过 | `20261028T171358-c5cbfa10` |
| 仅 SDK，无 NDMF / MA | 139/139，通过，0 跳过 | `20261028T171546-8d963795` |
| SDK + NDMF，无 MA | 172/172，通过，0 跳过 | `20261028T173227-54647502` |
| SDK + NDMF + MA | 174/174，通过，0 跳过 | `20261028T173628-b739272a` |

日期按北京时间描述，run ID 使用 UTC。每个成功范围均有独立项目、本次 XML、真实退出码
与程序集发现检查；不能将不同范围的重叠用例相加成独立用例数。完整证据路径及命令见
[测试指南](Testing.md) 与 [可审查结果快照](UnityNativeResults.json)。

NDMF 的源身份获取还存在独立缺陷：旧反射代码查找的 `OriginalObjectExposed` 并不存在，
原图与虚拟图无法建立预期身份关联，导致插层后的手动选择与平台参数语义映射失效。官方
1.11 在 `VirtualNode` 提供受保护的 `OriginalObject` 元数据，1.9/1.10 不具备这项数据。
因此修正身份读取的同时，将优化器 NDMF 装配及测试的最低版本从此前声明的 1.9 提到
**1.11**；仅检查 PlatformFinish/归一化 API 不足以证明完整兼容。影响是旧 NDMF 版本不再
启用本插件虚拟适配，需升级到 1.11，或选用独立核心 / 仅 SDK 入口并重新检查构建结果。
核心和 SDK 的依赖边界不变；此要求仅属于优化器，不能套用到 refactor 的其他插件。
原始 tag 源码可核对 [1.9.0](https://github.com/bdunderscore/ndmf/blob/1.9.0/Editor/API/AnimatorServices/VirtualObjects/VirtualNode.cs)、
[1.10.0](https://github.com/bdunderscore/ndmf/blob/1.10.0/Editor/API/AnimatorServices/VirtualObjects/VirtualNode.cs) 与
[1.11.0](https://github.com/bdunderscore/ndmf/blob/1.11.0/Editor/API/AnimatorServices/VirtualObjects/VirtualNode.cs)。
新增 4 个 NDMF 用例覆盖真实 Clip 来源/可检查绑定、缺失或不兼容元数据的保守失败、
合法生成节点没有物理来源的情况，以及严格/近似两种模式下 Float 空洞保持源图。

原生执行还发现并修复了以下生产问题：

- 独立 Controller 图复制对 AnimatorStateMachine / State / Transition / BlendTree 调用通用
  `Object.Instantiate` 会触发 Unity 原生 strong-PPtr 断言。现先使用对应类型构造器创建
  backing object，再 `CopySerialized` 并按已有身份表重映射共享/嵌套引用。Behaviour 仍使用
  Instantiate，保留脚本初始化顺序；不改变用户源图。内存与子资产复制回归实际通过。
- 数值分区存在未覆盖值时，旧近似模式会选“最近分支”。例如 `<0.5` / `>0.5` 在恰好
  `0.5` 时原图保持先前状态，输入可以长期停留于此，并非仅一帧误差。普通与 NDMF 路径
  现在都拒绝这种图，即使 `AllowFloatConditionApproximation=true`。影响是旧版本部分转换
  改为明确跳过；完整覆盖的精确阶跃和显式零阈值近似仍有回归保护。

测试自身也修复了真实 Unity 资产契约：复制前持久化完整的 fixture 图；通过 AnimationUtility
设置已持久化 Clip 事件；通用 Behaviour 使用单独的原生 MonoScript 注册程序集。原来的
同名 MA 替身已删除，2 项 MMD 用例改用真实 MA 类型并按依赖隔离。因 Unity 会规范化同步层
索引、Transform 的 xyz 曲线等数据，相关 fixture 改为可实际表达的输入并检查原图保持与
输出语义。没有将失败用例改成忽略或用零测试替代执行。

测试装配保留 `TestAssemblies` 标记，移除重复的 TestRunner 显式引用；测试工程默认通过
`-assemblyNames` 限定本仓库所选套件，避免把 SDK 上游测试混入结果，空测试范围会在启动
Editor 前失败。Linux VRC 工程补充真实 Editor 内建 Android JNI 模块，满足 SDK Oculus
导入；无 SDK 核心不引入它。

## 尚未验证或不支持

- AAP StateRegister 的初始化、逐帧反馈、层顺序和实际成本未做 Unity/VRChat 实测。
- NDMF 虚拟 AAP 后端未实现；自动选层可走物理 fallback，手动选层在 NDMF 构建中
  会跳过并提示，因为跨阶段的层身份桥接尚未实现。交接仍需实机验证。
- 不能无损转换的 Trigger、未知 Behaviour、任意嵌套/历史/时间逻辑会保留原图。
- Float 近似、启发式、AlwaysOne 归一依赖显式假设，不是无条件等价保证。
- 老分支缺失的原始 `.meta` 需要从原项目恢复。旧场景也依赖未提交的 SDK/第三方资产。
- 真实 Editor 导入及下述 EditMode 范围已验证；完整运行时逐帧等价性、实际上传和性能仍需专门验证。
