# Blendshape Controller Generator

> 需要 Modular Avatar

此工具是一个 Unity 编辑器窗口，用来为指定 `SkinnedMeshRenderer` 上的 blendshape 批量生成动画片段（.anim）并创建一个 Animator Controller（每个 blendshape 一个 Layer），方便在运行时或 Animator 中通过 float 参数控制表情权重。

## 使用方法

1. 打开：Tools → UnityBox → Blendshape Controller Generator
2. 将 Avatar（根 GameObject）拖入 Avatar 字段。
3. 使用 Scan 自动查找 Avatar 下的 SkinnedMeshRenderer，或使用 Add 手动添加。
4. 展开某个 Mesh，选择需要导出的 Blendshape，确认 Param Prefix（或使用默认），预览会显示最终的参数名。
5. 点击 Generate 生成 AnimationClips 与 Animator Controller，同时还会在 Avatar 下生成 MA 菜单组件。

## 常见问题

- 如果生成的绑定路径不正确，请确认 Avatar 是目标 SkinnedMeshRenderer 的祖先（工具使用 Avatar 到目标的相对路径进行绑定）。
