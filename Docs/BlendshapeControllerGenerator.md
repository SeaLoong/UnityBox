# Blendshape Controller Generator

详细文档 — 基于 `Assets/SeaLoong's UnityBox/Editor/BlendshapeControllerGenerator.cs` 的实现。

此工具是一个 Unity 编辑器窗口，用来为指定 `SkinnedMeshRenderer` 上的 blendshape 批量生成动画片段（.anim）并创建一个 Animator Controller（每个 blendshape 一个 Layer），方便在运行时或 Animator 中通过 float 参数控制表情权重。

## 快速概览

- 菜单位置：Tools → SeaLoong's UnityBox → Blendshape Controller Generator
- 默认输出目录：`Assets/SeaLoong's UnityBox/GeneratedBlendshapes/<MeshName>/`
- 生成内容：每个选中 Blendshape 生成一个 `.anim`（线性从 0 到 100，持续 1 秒，循环），并在 Controller 中为该 Blendshape 创建一个 Layer 与对应的 float 参数（前缀 `BS_`）。

## GUI 字段说明

- Target SkinnedMeshRenderer：要读取 blendshape 的目标 `SkinnedMeshRenderer`（拖拽场景中的对象）。
- Renderer Path：用于动画曲线绑定的 Transform 路径（相对于 Animator 所绑定的根）。默认会在选择 Renderer 时自动填充（函数 GetTransformPath）。路径示例：`Body/Head`。
- Output Folder：生成文件的根目录（必须在项目 `Assets/` 内，脚本会阻止选择项目外的目录）。默认：`Assets/SeaLoong's UnityBox/GeneratedBlendshapes`。
- Controller FileName：生成的 `.controller` 文件名（可留空使用 Mesh 名称作为 Controller 名称）。
- Refresh Blendshapes：读取目标 Mesh 的 `blendShapeCount` 并列出所有名称供勾选。
- 全选 / 全不选：对列出的 blendshape 进行快速批量选择。

## 生成的文件与命名规则

- Controller：`<OutputFolder>/<MeshName>/<ControllerName>.controller`
- Animation Clips：`<OutputFolder>/<MeshName>/<MeshName>__<SafeBlendshapeName>.anim`

说明：SafeBlendshapeName 会用下划线替换非字母数字字符（函数 SanitizeName）。Clip 的曲线属性绑定为 `blendShape.<BlendshapeName>`，绑定目标由 `Renderer Path` 指定的 Transform 路径和 `SkinnedMeshRenderer` 类型决定。

示例目录结构：

```text
Assets/SeaLoong's UnityBox/GeneratedBlendshapes/MyMesh/
 - MyMesh.controller
 - MyMesh__Smile.anim
 - MyMesh__Angry.anim
```

## 实现细节

- 在生成时，脚本会先确保目标目录存在；如果不存在，会通过 `AssetDatabase.CreateFolder` 按层级递归创建。
- Controller 是通过 `AnimatorController.CreateAnimatorControllerAtPath(controllerPath)` 创建的。默认生成的 Base Layer 会被移除（controller.RemoveLayer(0)）。
- 每个选中的 blendshape：
  - 创建一个 `AnimationClip`，添加两帧关键帧：time=0 -> value=0，time=1 -> value=100。
  - 将曲线通过 `EditorCurveBinding` 绑定到 `SkinnedMeshRenderer` 的 `blendShape.<name>` 属性（binding.path = Renderer Path）。
  - 将 `AnimationClip` 存为 asset（`AssetDatabase.CreateAsset`）。
  - 在 Controller 中添加一个 float 参数 `BS_<SafeName>`（若不存在则添加）。
  - 为该 blendshape 新建一个 `AnimatorStateMachine`（并使用 `AssetDatabase.AddObjectToAsset` 将其序列化到 Controller 资源中），在其中添加 State 并把 Motion 设置为上面生成的 clip；同时启用 `timeParameterActive` 并把 `timeParameter` 指向上面添加的 float 参数名（这可以让时间参数或外部控制影响 State 时间）。
  - 新建一个 `AnimatorControllerLayer`，将该 state machine 作为 layer 的 state machine，并把默认权重设为 1，然后 `controller.AddLayer(layer)`。

## 可定制点

- 动画时间与曲线：当前固定为 1 秒且线性（0 -> 100）。可修改 `Generate()` 中创建 `AnimationCurve` 的部分来改变时间、插值或目标值范围（例如改为 0->1）。
- 多 Blendshape 组合：脚本当前为每个 blendshape 创建独立 layer；如果希望用单个 layer 通过参数混合多个 blendshape，需要改写 State / Layer 的逻辑并添加 BlendTree 或自定义驱动。

## 常见问题与排查

- 列表为空或无法读取 blendshape：

  - 确认 `Target SkinnedMeshRenderer` 已赋值，且其 `sharedMesh` 不为 null。
  - 在 Mesh 导入设置中，确保该 Mesh 包含 morph targets（BlendShapes）。

- 动画没有影响 Mesh：

  - 检查 `Renderer Path` 是否正确（路径必须匹配 Animator 层级中用于绑定的根到目标 renderer 的 Transform 路径）。
  - 确认运行时使用的 Animator 组件确实引用了生成的 Controller，且 Controller 的 layer 权重为 1。

- Controller 或 Clip 未创建 / 覆盖问题：
  - 如果目标路径已有同名 Controller，脚本会提示是否覆盖；覆盖会删除原资源并创建新资源。请在覆盖前做好备份。

## 兼容性与注意事项

- 使用了 `AnimationUtility.SetAnimationClipSettings`、`AnimationUtility.SetEditorCurve`、`AnimatorController`（UnityEditor.Animations）等编辑器 API，需在 Unity 编辑器环境下运行（Editor 文件夹）。
- 生成的 AnimationClip 使用的是编辑器 API 写入 asset，可能会触发版本控制下的大量改动，建议在生成前确认要输出到的目录。

## 快速操作示例

1. 在场景中选中带有 Blendshapes 的模型（或拖拽该模型的 `SkinnedMeshRenderer` 到窗口的 Target 字段）。
2. 点击 `Refresh Blendshapes`，确认 `Renderer Path`（必要时手动调整）。
3. 选择需要生成的 Blendshape，确认 Output Folder 与 Controller FileName。
4. 点击 `Generate Controller`，等待窗口提示完成。

生成后的 Controller 与 Clip 可以直接在 Animator 窗口中打开或在运行时通过参数 `BS_<Name>` 控制对应的 State 时间/播放。
