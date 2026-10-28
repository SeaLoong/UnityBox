# Viseme Blendshape Override

生成由 `Viseme` Int（0–14）与可选 `Voice` Float（0–1）驱动的口型 BlendShape Animator。
核心只需要 Unity 2022.3，不做语音识别或自动采集麦克风；VRChat 适配使用平台提供的输入。
包内 [README](../Assets/UnityBox/VisemeBlendshapeOverride/README.md) 说明程序集及 API。

## 依赖与入口

| 内容 | 条件 |
| --- | --- |
| 配置、Inspector、Controller/Clip 生成 | Unity 2022.3 |
| Descriptor 默认值、SDK 构建回退 | VRChat Avatars ≥3.7 |
| NDMF 虚拟 FX | 上述 SDK + NDMF ≥1.7 |
| 包名 | `top.sealoong.unitybox.viseme-blendshape-override` |

包不强制安装 SDK/NDMF，缺少时只排除平台适配和对应测试程序集。NDMF 1.5 不具备此处使用
的 Animator Services API。组件入口为 Add Component > UnityBox > Viseme Blendshape Override。

## 普通 Unity 使用

1. 在计划放 Animator 的根对象添加组件，指定该根或其子对象的 SkinnedMeshRenderer。
2. 逐行选择实际存在的 BlendShape，设置 Weight、Voice Mode 及 Custom Settings。
3. 没有 Voice 输入时选择 Disabled；有输入则按实际数值检查 Min/Max。
4. 点击 **Generate Controller Asset**，选择新资产位置；按钮不覆盖已有 Controller。
5. 将结果分配给根对象 Animator，由项目脚本设置 Viseme/Voice。

代码调用 `TryCreateBuildPlan(root, out plan)` 后用 `BuildController(controller, plan)`。
BuildController 明确修改调用者提供、保存在 Assets 中的控制器，重建保留名称的层和子
资产。需保留原控制器时先复制。单纯添加组件不会自动提供输入服务。

## VRChat 配置

在 Descriptor 所在 Avatar Root 添加并启用组件。Renderer 留空时优先取 Descriptor Face
Mesh；编辑器默认选择随后还可查找 Body/其它候选。显式指定的 Renderer 不因 Descriptor
之后更改而自动替换；它必须属于当前 Avatar 且 sharedMesh 有所选 BlendShape。

| 映射值 | 行为 |
| --- | --- |
| Platform default（旧界面 Avatar Descriptor） | 保持空映射，每次构建读取平台默认；无适配时无输出 |
| None | 显式禁用该行，不回退 |
| 具体名字 | 固定覆盖；Mesh 缺少该名字时警告并视为无映射 |

打开 Inspector 不固化继承值。显式 Renderer 与 Descriptor Face 不同，继承名字仍来自
Descriptor，需要确认目标 Mesh 也有这些名字。禁用组件、无效 Renderer 或全部无映射会
跳过，只有成功生成后才切换构建对象 Lip Sync 为 VisemeParameterOnly。

## Weight、Voice 与输出

Weight 范围0–100，默认100；Voice默认Linear，Min=0、Max=1。这是插件缩放阈值，不是
VRChat音频检测器配置。Disabled 使用固定Weight；Linear 在Min以下输出0、Min到Max
线性增加、Max以上输出Weight。每行Custom Settings关闭时继承全部全局设置，开启后
可指定独立Weight，并选择Global/Disabled/Linear。非法阈值限制在0–1且保持Min<Max。

生成层名 `Viseme Blendshape Override`，包含15状态。每个状态归零本插件控制的其它
嘴型，再输出当前映射权重；未映射的BlendShape不写入。Voice状态用1D BlendTree。
没有额外平滑滤波。Write Defaults Auto根据已有FX做启发式选择，也可指定On/Off；同一
BlendShape被其它动画驱动时，检查最终层顺序、权重与WD，不能仅凭生成成功判定兼容。

## NDMF 与 SDK 交接

NDMF在Transforming阶段排在MA之后，使用ObjectPathRemapper解析虚拟路径；缺FX时
创建控制器及可接收提交的Descriptor条目。没有配置、禁用配置或无有效plan时不激活
AnimatorServices，也不改Descriptor；有效plan才按需启用。复用其它插件已激活的服务
不会将其关闭，仅释放本插件本次启用的上下文。

SDK不再仅凭“已安装NDMF”跳过。关闭NDMF Apply on Build/Play或本次没有执行NDMF时，
SDK仍处理；NDMF和SDK只有成功后才消费构建对象配置，以此防止重复生成。回退失败返回
false并保留配置供重试。直接ProcessAvatar API保留配置，供调用者明确再次生成。

SDK回退复制持久化AnimatorController或创建新FX，保存状态、过渡、树和Clip子资产。
同一资产文件内存在多个Controller时按所选Controller的localFileID映射副本，不再误用
主Controller。AnimatorOverrideController不支持，明确报错，不能丢弃覆盖关系。输出每次独立保存在
`Assets/UnityBox/VisemeBlendshapeOverride/Generated/`，不覆盖或自动删除旧结果。

Descriptor原customizeAnimationLayers=false时，数组里残留的Controller/Mask不是有效
输入。SDK回退或本插件首次启用NDMF服务前，先在构建对象恢复这些层的默认状态，再增加
口型FX，避免同时激活旧动画。源Controller/Mask资产不被改写。这是修复后有意的行为变化，
不是将原来关闭的自定义层自动迁移为启用。

安装真实SDK Base≥3.7时组件保留其IEditorOnly标记；适配也显式清理构建副本配置。
这些步骤不意味着普通Unity源对象会自动删除组件，直接构建API也不是可撤销场景预览。

## 升级与清理

原组件GUID、字段和口型0–14序列化值保留。公开代码枚举现为本包的
`VisemeBlendshapeOverrideComponent.Viseme`；Descriptor入口搬到VrcProcessor/VrcUtils。
外部调用者可能需要更新类型或asmdef引用。历史每行Voice格式没有通用整数迁移，需复核
Custom Settings；不能把口型枚举值保持误解为所有旧字段都自动转换。

禁用/删除组件不清理Generated，也不恢复旧版本已修改的源Avatar。若仍引用旧插件FX或
Lip Sync仍是VisemeParameterOnly，先恢复需要的源设置；确认无场景/Prefab/其它引用后
再删除旧资产。

## 回归与实测

当前声明39用例：核心9、SDK25、NDMF5。分别位于Tests/Editor/VisemeCoreTests.cs、
Tests/Editor/VRChat/VisemeRegressionTests.cs和Tests/Editor/NDMF；需要真实依赖和
Unity Test Framework，包安装时加入manifest testables。覆盖映射、曲线、资产保存、
回退/去重/重试、原无效层以及动画服务生命周期。

真实Unity2022.3.22f1已执行上述39项EditMode用例，全部通过、零跳过。完整环境为
SDK3.8.2、MA1.18、NDMF1.11；七插件完整报告为
`/workspace/unitybox-setup/unity/refactor-native-vrc-symbol-fix-20261029.json`，
本次XML运行ID为`20261028T171628-fc5a70ec`（UTC）。此前23程序集Roslyn类型检查保留
为历史静态证据，不能代替该原生执行结果。实际项目仍需验证Viseme0–14与Voice的游戏内
输入、最终NDMF/MA合并和其它口型/优化器组合。总范围见[Testing](Testing.md)。
