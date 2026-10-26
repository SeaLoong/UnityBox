# Viseme Blendshape Override

本工具通过 VRChat 内建 `Viseme` 与 `Voice` Animator 参数，在构建时生成自定义口型
BlendShape 层。它接管已映射嘴型的输出权重，保留 VRChat 官方的口型输入来源。

## 依赖和入口

| 项目 | 当前要求 |
| --- | --- |
| Unity | 2022.3 |
| VRChat Avatars SDK | >= 3.7.0 |
| VPM 包 | `top.sealoong.unitybox.viseme-blendshape-override` |
| NDMF | VPM 安装要求 >= 1.7.0 |
| 组件入口 | Avatar Root 的 Add Component > UnityBox > Viseme Blendshape Override |

NDMF 1.5 没有当前使用的 Animator Services API。手动复制源码且没有兼容 NDMF 的
项目会走 SDK 预处理回退。回退只支持持久化 `AnimatorController`；遇到
`AnimatorOverrideController` 会终止该处理并报告错误，应改用 NDMF 构建路径。

## 配置步骤

1. 在有 `VRCAvatarDescriptor` 的 Avatar Root 上添加组件并保持启用。
2. 检查 Renderer。未指定时，编辑器初始化先找 Descriptor 的 Face Mesh，随后找 Avatar
   内名为 Body 的 SkinnedMeshRenderer，最后取其它候选。已经指定的 Renderer 不会
   因 Descriptor 后续更改而自动切换；需要时手动重新指定。
3. 设置全局 Weight 和 Voice Mode，再逐行检查 15 个 viseme 的映射。
4. 对需要不同权重/Voice 区间的行启用 Custom Settings。
5. 构建测试副本，检查最终 FX 和游戏内效果。

Renderer 必须属于当前 Avatar，并且 sharedMesh 中存在对应 BlendShape。组件放在
Root 的子对象上不会被当前构建入口发现；禁用组件会直接跳过。没有任何有效映射时
只报告警告并跳过，不会把 Descriptor 改成 VisemeParameterOnly。

## 映射语义

| 下拉框值 | 结果 |
| --- | --- |
| Avatar Descriptor | 组件存储空映射，每次构建读取 Descriptor 对应 viseme 的名字 |
| None | 显式禁用该行，不回退到 Descriptor |
| 具体 BlendShape | 使用该名字；目标 Mesh 缺少它时警告并把该行视为无映射 |

打开 Inspector 不会把继承值固定为当前名字。显式 Renderer 与 Descriptor Face Mesh
不同时，继承的名字仍来自 Descriptor，因此需要确认这些名字也存在于目标 Mesh。
UI 显示小写口型名称，内部保持 VRChat 的枚举值（例如 `PP / FF / aa / oh`）。

## 权重与 Voice

全局 Weight 范围为 0–100，默认 100。Voice 默认 Linear，区间默认 Min=0、Max=1。
该区间是插件自己的缩放阈值，不是对 VRChat 音频检测器的配置。

| Voice 设置 | 输出 |
| --- | --- |
| Disabled | 当前 viseme 使用固定 Weight |
| Linear | Voice <= Min 时输出 0；Min 到 Max 之间线性增加；Voice >= Max 时输出 Weight |
| 每行 Global | 仅在 Custom Settings 中可选，继承全局 Voice 模式与阈值 |

Custom Settings 关闭时，该行全部使用全局设置；开启时使用该行 Weight，并按其
Voice Mode 选择全局或独立的 Voice 行为。阈值会限制在 0–1 内并确保 Min < Max；
例如 Min=Max=1 会被归一化为两个不同阈值。没有额外平滑或延迟滤波。

调参应先降低整体 Weight，再检查 Voice 的可用范围。元音与辅音可以使用不同权重
和阈值，但不存在适合所有模型/麦克风的固定推荐值；以测试 Avatar 的实际输入为准。

## 构建结果

层名为 `Viseme Blendshape Override`。它使用 `Viseme` Int 参数切换 15 个状态；
启用 Voice 缩放的状态使用 `Voice` Float 参数控制 1D BlendTree。每个状态重置本插件
控制的其它嘴型，再输出当前映射权重。未被映射的 BlendShape 不加入此层的绑定。
成功生成后，构建对象的 Lip Sync 切为 `VisemeParameterOnly`。

**NDMF 路径**：在 Transforming 阶段显式排在 Modular Avatar 之后，向虚拟 FX 添加层，
通过 ObjectPathRemapper 取得对象虚拟路径。FX 不存在时会创建虚拟控制器，并保证
Descriptor 中存在可接收提交的 FX 条目。这个顺序约束不代表已在所有第三方插件之后；
后续优化器与其它口型插件的组合仍须检查最终产物。

**SDK 回退路径**：开启 Descriptor 自定义动画层，复制现有持久化 FX 或创建新 FX。
每次生成独立资产，状态、过渡、BlendTree 和动画保存为控制器子资产；不复用或覆盖
上一次生成的控制器。输出位于 `Assets/UnityBox/VisemeBlendshapeOverride/Generated/`，
特殊 Avatar 名称不会被解释成相对目录或反斜线目录。

Write Defaults 可选 Auto/On/Off。Auto 根据已有 FX 的状态判断，跳过部分必须使用
WD On 的层/Direct BlendTree 情况；它是启发式选择，不是跨插件兼容保证。
如果其它层驱动相同 BlendShape，应检查层顺序、权重和 Write Defaults。

## 禁用、迁移与清理

禁用或删除组件会让之后的构建跳过生成，不会自动清理磁盘上的旧 Generated 资产，
也不会替用户重设旧版本已经修改过的源 Avatar。若源 Avatar 仍引用旧插件控制器，
需要恢复原 FX；若 Lip Sync 已被旧版本改成 VisemeParameterOnly，恢复所需模式和映射。
确认无场景、Prefab 或其它资产引用后，再删除旧输出。

从早期序列化格式升级时，应复核每行 Custom Settings。旧的每行 Voice 枚举与当前
Global/Disabled/Linear 的数值不同，当前代码不把旧枚举整数直接迁移到新字段，
旧行会回到 Global。已有自定义 Renderer/BlendShape 名称仍需人工核对。

## 单元测试与实际项目复测

测试文件：`Assets/UnityBox/VisemeBlendshapeOverride/Tests/Editor/VisemeRegressionTests.cs`。
在 Unity Test Framework 的 **EditMode** 运行
`UnityBox.VisemeBlendshapeOverride.Tests.VisemeRegressionTests`。
VPM 安装时把本包名加入项目 `Packages/manifest.json` 的 `testables`，保留其它已有项。

回归覆盖组件禁用、Face/Body 默认选择、Descriptor 继承和显式 None、Voice 非法边界、
输出曲线、状态/过渡持久化、重复构建的资产隔离、FX 条目补全、Override Controller
拒绝处理及目录范围。当前云环境未执行 Unity 编译或 EditMode 测试。

实际项目至少验证：

1. NDMF/Modular Avatar 最终构建中，口型层存在并绑定正确 Renderer。
2. SDK 回退中原 FX 资产不被改写，连续构建和同名 Avatar 的结果相互独立。
3. `Viseme` 0–14、Voice 低于/处于/高于设定区间，以及 None 映射的行为。
4. 禁用组件后新构建停止接管口型，源 Avatar 设置符合预期。
5. 与其它 BlendShape 动画、Write Defaults 混用和 Animator 优化器的组合。
