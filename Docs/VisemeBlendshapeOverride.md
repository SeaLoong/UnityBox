# Viseme Blendshape Override

本插件将15个口型输入转换为 SkinnedMeshRenderer BlendShape 动画。普通 Unity 使用
`Viseme` Int 与可选 `Voice` Float；VRChat 使用 Descriptor 提供的输入；NDMF 将生成图
合并到最终 FX。Unity 核心、SDK 适配与 NDMF 适配分别编译。

## 安装与入口

| 入口 | 依赖 | 输出 |
| --- | --- | --- |
| Inspector / BuildController | Unity2022.3 | 可直接分配给 Animator 的控制器 |
| CreateEditableCopy | Unity2022.3 | AnimatorController/AOC 的独立可编辑状态图 |
| SDK 上传回调 | VRChat Avatars≥3.7 | 独立持久化 FX、VisemeParameterOnly |
| NDMF Transforming | 上述 SDK、NDMF≥1.7 | 最终提交到 Descriptor 的 FX |

包名 `top.sealoong.unitybox.viseme-blendshape-override`，SDK/NDMF 为可选依赖。

## 设置与播放

在 Animator 根对象添加组件，指定其自身或子对象的 SkinnedMeshRenderer，再为各行选择
BlendShape。点击 **Generate Controller Asset** 保存新资产，将结果分配给 Animator。
调用者设置 `Viseme` Int（sil=0，PP=1，直到ou=14）。Voice Disabled 输出固定 Weight；
Linear 按 Min/Max 缩放，区间两侧输出端点。

| Weight | Voice Min/Max | Voice | 当前形状权重 |
| --- | --- | --- | --- |
| 80 | 0.2 / 0.8 | 0、0.2 | 0 |
| 80 | 0.2 / 0.8 | 0.5 | 40 |
| 80 | 0.2 / 0.8 | 0.8、1 | 80 |

每行 Custom Settings 可以独立设置 Weight 与 Global/Disabled/Linear。输入范围由
Inspector 和构建归一化统一处理。15状态始终归零其它受控形状，映射为 None 的状态将这些
形状全部设为0；未纳入映射的形状由其它动画层管理。该组件接收参数输入，音频识别属于
提供 Viseme/Voice 参数的服务。

Write Defaults 可选 On、Off 或 Auto。Auto 检查 FX 及关联控制器（包括 AOC 的底层图），遇到有实际 Motion 的 WD Off
状态时采用 Off，忽略 Additive 和要求 WD On 的 Direct-only 层。生成层是独立 Override
层，权重1、同步索引-1；这一设置让已有 Base Layer 和口型层分别执行自己的状态图。

## API 和覆盖控制器

`TryCreateBuildPlan(root, out plan)` 解析映射与路径；`BuildController(controller, plan)`
在传入的 Assets 控制器内重建插件层及其子资产。Viseme 输入类型为 Int，Voice 输入类型
为 Float，类型冲突会报告实际参数名与所需类型，然后保持传入图。

`VisemeControllerCopy.CreateEditableCopy(runtimeController, newAssetPath)` 支持普通控制器、
AnimatorOverrideController 和内存状态图。它用对象身份解析有效覆盖，保留同名不同 Clip、
Clip互换、共享树引用及所选 Controller 身份。状态机/状态/过渡/树为独立副本；持久化
Clip/Mask 共享原引用，临时 Clip/Mask 保存到新控制器。

图节点用具体 Unity 原生构造器创建，再复制序列化字段并重映射对象引用。源图和其文件保持
原值。`BuildController` 直接编辑调用者传入的控制器；先创建副本即可保留原控制器。

普通 Unity Player 构建通过场景处理回调，仅从构建场景副本移除
`VisemeBlendshapeOverrideComponent` 配置；Animator、生成的控制器及运行对象继续保留。
源场景里的配置保持原值，可继续修改、重新生成。

## SDK 构建流程

Renderer 留空时取 Descriptor Face Mesh。空行映射在构建时读取最新 Descriptor 值；
None 明确输出0；选择的具体名字为固定映射。只有启用组件、有效 Renderer 和有效映射
进入生成流程。

SDK 为每次构建创建独立 FX，展开有效 AOC 覆盖、附加生成层，并设置 VisemeParameterOnly。
输出在 `Assets/UnityBox/VisemeBlendshapeOverride/Generated/`，源控制器和源 Clip 不改写。
构建输入错误恢复原 Controller、动画层开关和 Lip Sync，配置留在对象上以便修正后重建。
成功后消费配置，同次 SDK 再次调用保持已有 FX；直接 ProcessAvatar API 保留配置。

Descriptor 的 customizeAnimationLayers=false 表示数组中的自定义赋值处于停用状态。此时生成
使用默认层语义，再添加新的 FX，而不是激活数组内旧赋值。生成资产的保留与清理由项目的
引用管理流程负责；每次独立生成都保留前一次结果。

## NDMF 提交与后续插件

插件在 Transforming 阶段的 Modular Avatar 后运行。有效计划才激活 AnimatorServices，
使用其它插件已启用的服务时保留该服务生命周期。虚拟 FX 缺少时创建对应 Descriptor 条目，
提交时成为真实控制器。

动画绑定使用 ObjectPathRemapper 的虚拟路径。回归场景在生成后将 Face 改名为 Renamed
Face，再增加一个驱动 Transform 的虚拟层；最终提交后实际 Animator 同时执行口型和后续
层，并且 SDK 回调保持这份控制器。配置在成功生成后消费，避免同次重复输出。

## 序列化与升级

组件 GUID、公开字段和口型0–14编号保持稳定。旧 `overrideGlobalVoiceSettings` 迁移到
useCustomSettings；旧行 voiceModulationMode 的 Disabled=0/Linear=1 通过反序列化回调
转换到当前 Disabled/Linear，阈值和权重保留。当前 Global/Disabled/Linear 直接读取。
真实 Prefab YAML 导入用例覆盖两种旧值及三种当前值。

公开枚举为 `VisemeBlendshapeOverrideComponent.Viseme`；平台调用入口为
`VisemeBlendshapeOverrideVrcProcessor` / `VisemeBlendshapeOverrideVrcUtils`。

## 执行与性能

功能套件分别覆盖核心、SDK 和 NDMF；性能场景使用 `UnityBoxPerformance` 类别。
实际执行数、运行标识与源码指纹见[验证结果](VerificationResults.json)。
真实 Animator 在 WD On/Off/Auto 下对15个 ID、5个 Voice 数值进行正反切换，共6750个
权重检查。Player 探针将 WD On/Off 的300步与4500个检查写入独立 Linux Player 场景。
NDMF 用例执行最终提交、后续插件编辑、Renderer 路径变化及 SDK 交接。

性能分别测量计划解析和资产生成，使用预热、多个样本、median/p95，固定15个口型。
计划场景使用15/150/1500形状 Mesh，生成场景使用1/5/15个受控形状。模型已将有序行查询
改成直接索引，归一化复用现有列表，生成子资产采用直接路径归属检查。数据与运行配置见
[Performance](Performance.md)，实际功能结果见[Testing](Testing.md)。

复现：

```sh
python3 scripts/unity/test_project.py prepare --repo /workspace/unitybox-refactor-review \
  --project /workspace/unitybox-setup/unity/projects/viseme-check \
  --profile vrc --package VisemeBlendshapeOverride --dependency nadena.dev.ndmf --offline
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/viseme-check \
  --xdg-root /workspace/unitybox-setup/unity/interactive --exclude-category UnityBoxPerformance
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/viseme-check \
  --xdg-root /workspace/unitybox-setup/unity/interactive --benchmark
```
