# Viseme Blendshape Override

通过 `Viseme` Int（0–14）和可选 `Voice` Float 驱动 SkinnedMeshRenderer 口型。Unity 核心
负责生成与播放图；VRChat 适配接收 Descriptor 输入；NDMF 适配编辑并提交虚拟 FX。

## 安装

| 内容 | 依赖 |
| --- | --- |
| 组件、Inspector、生成 API、AnimatorOverrideController 展开 | Unity 2022.3 |
| Descriptor 默认映射、SDK 构建回调 | VRChat Avatars ≥3.7 |
| NDMF 虚拟 FX | 上述 SDK + NDMF ≥1.7 |

包名 `top.sealoong.unitybox.viseme-blendshape-override`。SDK/NDMF 为可选程序集，普通 Unity
工程直接使用核心。组件 GUID、已公开序列化字段和口型 0–14 的编号保持稳定。

## 普通 Unity 使用

1. 在 Animator 根对象添加 **UnityBox > Viseme Blendshape Override**。
2. 指定根对象或子对象的 SkinnedMeshRenderer，为各行选择 Mesh 中的 BlendShape。
3. 设置 Weight、Voice 和 Write Defaults，点击 **Generate Controller Asset** 保存新控制器。
4. 将控制器分配给根对象 Animator，由脚本设置 `Viseme` Int 与 `Voice` Float。

例如 Weight=80、Voice Min=0.2、Max=0.8 时，Voice 为0、0.2、0.5、0.8、1分别输出
0、0、40、80、80。Voice Disabled 固定输出 Weight；每行 Custom Settings 可覆盖权重与
Voice 模式。每个状态归零其余受控 BlendShape，再输出当前口型；多个口型可映射同一形状。

`Write Defaults` 支持 On、Off 和 Auto。Auto 读取已有及关联控制器的非 Additive/非 Direct-required
层，检测到实际 WD Off 动画状态时使用 Off，其余使用 On。生成层始终独立，
`syncedLayerIndex=-1`，直接评估自己的15个状态。

## 生成 API 与已有控制器

```csharp
var copy = VisemeControllerCopy.CreateEditableCopy(
    animator.runtimeAnimatorController, "Assets/VisemeOutput.controller");
VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out var plan);
VisemeBlendshapeOverrideProcessor.BuildController(copy, plan);
animator.runtimeAnimatorController = copy;
```

输入是启用的组件、有效 Renderer 与至少一个映射。`BuildController` 修改传入的 Assets
控制器，重建本插件命名的层和子资产；`Viseme` 必须为 Int，使用 Voice 时该参数必须为
Float。参数类型冲突在修改图前给出具体字段错误。

普通 Unity Player 构建在场景副本中移除本插件的配置组件，保留 Animator、生成控制器和
运行对象。编辑器中的源场景及配置继续保留，可在修改映射后重新生成。

`CreateEditableCopy` 接收 AnimatorController 或 AnimatorOverrideController。覆盖按 Clip
对象身份展开，重名 Clip 保持独立，互换 Clip 也保持正确。状态机、状态、过渡和树生成独立
副本；持久化 Clip/Mask 采用共享引用，内存 Clip/Mask 随新控制器持久化。

## VRChat 与 NDMF

在 Avatar Root 添加组件。Renderer 留空时使用 Descriptor Face Mesh。映射项：

- **Platform default**：每次构建读取 Descriptor 当前映射。
- **None**：该口型输出受控形状的零权重。
- **具体名字**：使用所选 Mesh 上的固定 BlendShape。

SDK 构建复制既有 FX，完整展开 AnimatorOverrideController，再附加口型层。每次构建使用
独立输出，位于 `Assets/UnityBox/VisemeBlendshapeOverride/Generated/`。成功后 Descriptor
使用 VisemeParameterOnly，构建配置被消费；参数类型等输入错误会恢复原 Descriptor 设置。
后续重复 SDK 回调保留已生成的 FX。直接 `ProcessAvatar` API 保留配置，可显式再次生成。

NDMF 在 Transforming 的 MA 之后生成虚拟 FX，只为有效配置启用动画服务。复用其它插件
已启用的服务，提交后生成的物理控制器包含后续插件的修改。ObjectPathRemapper 将生成时
的虚拟路径映射到最终对象路径，Renderer 后续改名仍能播放。SDK 随后的回调保留这份结果。
Descriptor 关闭自定义层时，构建采用其实际生效的默认层，并在新 FX 中添加口型动画。

## 数据升级与性能

旧行字段 `overrideGlobalVoiceSettings` 迁移到 Custom Settings；旧 `voiceModulationMode`
的 Disabled=0、Linear=1 转换到当前行枚举的 Disabled、Linear，保留原阈值和权重。
当前 Global/Disabled/Linear 格式按原值读取。

有序的15行映射直接按 ID 查询，构建计划只做一次归一化；生成子资产时直接检查资产路径，
每个子对象不再重复枚举整个控制器。性能用例分别测量15/150/1500形状 Mesh 的计划解析，
以及1/5/15个受控形状的15状态、15 Voice 树持久化生成，输出 median/p95 和采样信息。
复现和测量数据见仓库 [Performance](../../../Docs/Performance.md)。

## 测试

功能套件分别运行 Unity 核心、SDK 和 NDMF 适配，另有 `UnityBoxPerformance` 场景。
各程序集执行数、运行标识和源码指纹见[验证结果](../../../Docs/VerificationResults.json)。
实际 Animator 评估15个口型、5个 Voice 数值、正反切换及 WD On/Off/Auto；独立 Player
探针导出 WD On/Off 的300步、4500个权重检查。NDMF 用例实际提交图，执行 Renderer 改名、
后续虚拟层编辑和 SDK 去重交接。Prefab 导入测试检查新旧 Voice 格式迁移。

通过 UPM/VPM 安装测试时，把包名加入 manifest 的 `testables`。详细说明见
[VisemeBlendshapeOverride](../../../Docs/VisemeBlendshapeOverride.md) 和
[测试流程](../../../Docs/Testing.md)。
