# Viseme Blendshape Override

在 Avatar 构建时生成一层 FX Animator，以 VRChat 内建 `Viseme` 参数选择嘴型，
并可用 `Voice` 参数缩放 BlendShape 权重。组件只提供构建配置，不在 Avatar 上
运行自定义脚本。

## 依赖与安装

- Unity 2022.3、VRChat Avatars SDK >= 3.7.0。
- VPM 包：`top.sealoong.unitybox.viseme-blendshape-override`，要求 NDMF >= 1.7.0。
  1.7 是当前虚拟 Animator API 的最低版本。
- 手动复制源码、未安装兼容 NDMF 时可使用 SDK 回退路径；该路径支持持久化的
  `AnimatorController`，不支持 `AnimatorOverrideController`。不支持时会报错，
  不会静默替换成空 FX。已有 Override Controller 的项目应使用 NDMF。

## 使用

1. 在带 `VRCAvatarDescriptor` 的 Avatar Root 上添加 **UnityBox > Viseme Blendshape Override**。
2. 检查 `Renderer`。初次配置优先取 Descriptor 的 Face Mesh；没有时尝试 Avatar 内
   名为 `Body` 的对象，再取其它 `SkinnedMeshRenderer`。渲染器必须属于该 Avatar。
3. 配置全局 `Weight`（0–100）和 `Voice Mode`。默认 Weight 为 100，Voice 为 Linear，
   范围为 0–1。Disabled 使用固定权重，Linear 按 Voice 区间从 0 线性增加到该权重。
4. 每个 viseme 的 BlendShape 选择中，`Avatar Descriptor` 持续继承 Descriptor 映射；
   `None` 明确禁用这一行；选择具体名字会固定使用该 BlendShape。
5. 需要单独设置时启用该行的 `Custom Settings`。`Voice Mode = Global` 继承全局模式
   和区间，`Disabled` 只使用该行固定权重，`Linear` 使用该行自己的区间。
6. 按项目现有流程构建或上传，并在测试 Avatar 上检查口型。

## 构建与兼容边界

生成层名为 `Viseme Blendshape Override`，为 15 个 viseme 创建状态。每个状态把
本插件控制的其它嘴型写为 0，再给当前有效映射写入权重；未被插件映射的
BlendShape 不会因此加入控制范围。成功生成后，构建对象的 Lip Sync 改为
`VisemeParameterOnly`，由官方继续提供 `Viseme` 输入。

NDMF 路径在 Transforming 阶段显式排在 Modular Avatar 后，使用虚拟 FX 和虚拟
对象路径；缺失 FX 时补齐控制器及 Descriptor 层条目。它没有声明排在所有第三方
插件之后，同一嘴型仍被其它动画驱动时，需要检查最终合并结果。

SDK 回退每次创建独立控制器副本和持久化子资产，开启自定义动画层。输出位于
`Assets/UnityBox/VisemeBlendshapeOverride/Generated/`，不会自动清理旧输出；仅在确认
没有任何 Avatar 或资产引用后手动删除。源控制器不会直接添加口型层。

禁用或删除组件后，新构建跳过此功能。组件必须位于 Avatar Root；Renderer 无效或
没有有效映射时会警告并跳过。删除组件不会自动修复旧版本已修改的源 Avatar：如果
源对象已指向旧生成控制器或 `VisemeParameterOnly`，需要手动恢复原 FX 和 Lip Sync。

`Write Defaults = Auto` 根据现有 FX 状态做启发式判断；也可显式选择 On/Off。
这不保证与所有混用 Write Defaults、Animator 优化器或其它口型动画兼容。

## 测试与验证状态

安装 Unity Test Framework 后，在 Test Runner 的 **EditMode** 中运行
`UnityBox.VisemeBlendshapeOverride.Tests.VisemeRegressionTests`。
通过 VPM 安装时，保留项目现有设置并将本包名加入 `Packages/manifest.json` 的 `testables`。

测试覆盖禁用组件、Renderer 默认选择、继承/None 映射、Voice 边界、生成曲线和子资产、
重复构建、FX 条目保留、Override Controller 拒绝处理及输出目录边界。
当前云环境只完成源代码/配置和安装包检查，尚未执行 Unity 编译及这些 EditMode 测试。
仍需实际验证 NDMF/SDK 两条构建路径、最终 FX、游戏内 `Viseme`/`Voice` 输入和其它插件的组合。

更详细的调参与迁移说明见仓库中的 `Docs/VisemeBlendshapeOverride.md`。
