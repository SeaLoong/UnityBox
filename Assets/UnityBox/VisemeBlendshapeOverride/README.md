# Viseme Blendshape Override

`Viseme Blendshape Override` 会在构建时读取 VRChat 内建的 `Viseme` Animator 参数，并生成一层 FX Animator，按每个 viseme 的独立权重驱动自定义嘴型 BlendShape。
此外也支持按内建 `Voice` 参数对输出做二次缩放，让嘴型不只是“固定缩小”，而是会随说话强度变化。
现在每个 viseme 还可以选择是否使用**独立的 Voice 响应区间**。

## 设计目标

- 替代 Avatar Descriptor 的默认 `VisemeBlendShape` 固定 100 权重行为。
- 保留 VRChat 官方的 `Viseme` 参数输入来源。
- 允许每个 viseme 单独限制输出到 BlendShape 的权重范围。
- 可选按 `Voice` 参数对口型强度做线性混合。
- 提供一键初始化的嘴型权重预设（保守 / 均衡 / 强调）。
- 每个 viseme 可选择自己的 `Voice Min / Voice Max` 响应区间。

## 实现策略

1. 读取 Avatar Descriptor 现有的 Face Mesh / Viseme 映射，或使用组件上的手动覆盖配置。
2. 在 FX Controller 中生成一层 `UB Viseme Blendshape Override`。
3. 使用内建 `Viseme` 参数的整型值切换不同状态；每个状态只写嘴型相关的 `blendShape.*` 曲线。
4. 若启用 `Voice Modulation`，状态内部会再用 `Voice` 参数通过 1D BlendTree 将输出从 `0` 线性放大到设定权重。
5. 每个 viseme 可选择沿用全局 `Voice Min / Voice Max`，或改成自己的独立响应区间。
6. 在构建时把 Avatar Descriptor 的 Lip Sync 模式切到 `VisemeParameterOnly`，让 VRChat 继续更新 `Viseme` 参数，但不再直接写默认 BlendShape。

## 使用方式

1. 把 `VisemeBlendshapeOverrideComponent` 挂到 Avatar Root。
2. 若你已经在 Avatar Descriptor 中配置过 viseme，可直接保留“Follow Avatar Descriptor mapping”。
3. 先用工具栏里的权重预设初始化（推荐从“均衡”开始）。
4. 如需更自然的口型动态，保持 `Voice Modulation = Linear`，并根据需要调整全局 `Voice Min / Voice Max`。
5. 若某些口型需要更快或更慢地响应，再把该 viseme 切成独立区间，单独调它的 `Voice Min / Voice Max`。
6. 再逐个 viseme 微调 `Weight`，例如 `20~40`。
7. 构建 / 上传头像即可。

## 说明

- 删除该组件后，重新构建会清理本插件生成的 FX 层与相关子资产。
- 如果你想完全恢复 VRChat 默认的 viseme 绑定，还需要手动把 Avatar Descriptor 的 Lip Sync 模式切回 `VisemeBlendShape`。
- `Voice Modulation` 默认采用线性输出：`Voice <= Min` 时为 0，`Voice >= Max` 时达到该 viseme 的完整配置权重。
- 当某个 viseme 关闭 `Use Global Voice Range` 时，它会改用自己的独立 `Voice Min / Voice Max`；适合做“某些口型更灵敏、某些口型更克制”的精细调参。