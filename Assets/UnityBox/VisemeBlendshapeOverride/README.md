# Viseme Blendshape Override

`Viseme Blendshape Override` 会在构建时读取 VRChat 内建的 `Viseme` Animator 参数，并生成一层 FX Animator，用自定义 BlendShape 驱动口型。
此外也支持按内建 `Voice` 参数对输出做二次缩放。

## 设计目标

- 替代 Avatar Descriptor 的默认 `VisemeBlendShape` 固定 100 权重行为。
- 保留 VRChat 官方的 `Viseme` 参数输入来源。
- 允许使用一个全局 `Weight` 作为默认输出权重。
- 可选按 `Voice` 参数对口型强度做线性混合。
- 每个 viseme 可通过 `Custom Settings` 覆盖默认 `Weight / Voice` 设置。

## 实现策略

1. 读取 Avatar Descriptor 现有的 Face Mesh / Viseme 映射，并允许逐 viseme 手动覆盖。
2. 在 FX Controller 中生成一层 `UB Viseme Blendshape Override`。
3. 使用内建 `Viseme` 参数的整型值切换不同状态；每个状态只写嘴型相关的 `blendShape.*` 曲线。
4. 若启用 `Voice Mode = Linear`，状态内部会再用 `Voice` 参数通过 1D BlendTree 将输出从 `0` 线性放大到设定权重。
5. 未启用 `Custom Settings` 的 viseme 会直接使用全局 `Weight / Voice` 设置。
6. 启用 `Custom Settings` 的 viseme 可单独指定 `Weight`，并选择 `Voice Mode = Global / Disabled / Linear`。
7. 在构建时把 Avatar Descriptor 的 Lip Sync 模式切到 `VisemeParameterOnly`，让 VRChat 继续更新 `Viseme` 参数，但不再直接写默认 BlendShape。

## 使用方式

1. 把 `VisemeBlendshapeOverrideComponent` 挂到 Avatar Root。
2. `Renderer` 默认会优先选择名为 `Body` 的 `SkinnedMeshRenderer`。
3. 把 `Weight` 调到你想要的默认整体强度。
4. 如需更自然的口型动态，保持 `Voice Mode = Linear`，并根据需要调整全局 `Voice Min / Voice Max`。
5. 若某些口型需要不同的行为，展开对应 viseme 并打开 `Custom Settings`。
6. 在 `Custom Settings` 中可单独设置 `Weight`，以及 `Voice Mode = Global / Disabled / Linear`。
7. 构建 / 上传头像即可。

## 说明

- 删除该组件后，重新构建会清理本插件生成的 FX 层与相关子资产。
- 如果你想完全恢复 VRChat 默认的 viseme 绑定，还需要手动把 Avatar Descriptor 的 Lip Sync 模式切回 `VisemeBlendShape`。
- 默认全局 Voice 范围为 `Min = 0`、`Max = 1`。
- 当某个 viseme 启用 `Custom Settings` 时，它会改用自己的 `Weight`，并允许对 `Voice Mode / Voice Min / Voice Max` 做独立覆盖。
- VRChat 的 viseme 名本身沿用了官方历史命名（例如 `PP / FF / aa / oh`），UI 中已统一按小写显示，底层仍保留原始枚举语义以保证兼容性。