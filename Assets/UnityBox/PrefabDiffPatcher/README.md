# Prefab Diff Patcher

比较 A（保留的基础版本）与 B（提供差异的版本），按选择组合成独立输出 Prefab。
需要 Unity 2022.3；本包不要求 VRChat SDK、Modular Avatar 或 NDMF。
VPM 包名为 `top.sealoong.unitybox.prefab-diff-patcher`。

## 操作

1. 打开 `Tools > UnityBox > Prefab Diff Patcher`，指定 A 和 B。
2. 点击 **Analyze Diff**，检查对象壳、Transform/其它组件及子对象的差异。
3. 使用批量选择或逐项勾选。部分选择状态会保留；选择父节点会选择整棵差异子树，
   随后可取消其中的项。根对象壳固定使用 A，根组件可单独或批量选择。
4. 将结果保存到 `Assets` 内独立的 `.prefab` 路径。
5. 在输出副本中检查引用、变换和组件行为，再决定是否用于实际场景。

## 匹配与合并语义

- 对象按相对路径匹配，同名兄弟按出现次序区分；路径中的保留字符会转义。
  组件按完整类型及该类型的出现次序匹配。改名/移动不会自动视为同一个对象；
  同名兄弟或同类型组件重新排序后，需要人工核对。
- 对应且选中的项采用 B；A 独有的对象保留。B 缺少某个对象不等于删除 A 的对象。
  选中“B 已删除”的对应组件会移除结果中的该组件。
- 对象壳与组件选择互相独立。选中对象壳不会顺带应用未选中的 Transform。
  选中 Transform/RectTransform 时复制局部属性，保留输出的父子关系。
- 比较包括数组、嵌套字段、引用身份和精确序列化内容。组件中的本地引用在组合后
  重映射到输出；未引入的 B 对象引用会置空，外部资产引用保持原资产。
  应同时选入被引用的必要对象，避免得到空的骨骼、约束或组件引用。
- 输入含 Missing Script 时会停止，不会静默生成丢失脚本的数据。

## 输出与限制

A/B 输入不会被直接修改，禁止把输出写回它们对应的源 Prefab。覆盖已有独立输出时
会先提示确认并保留 GUID；资产保存不受场景 Undo 保护，应通过版本控制恢复旧输出。
输出路径不能离开 `Assets`，也不能用相对路径穿越绕过输入保护。

输出是重新组装的 Prefab，不保证保留输入的嵌套 Prefab 或 Variant 继承结构。
工具不能推断重复对象/组件的语义身份，也不保证第三方自定义序列化组件、复杂
SerializeReference 循环图、RectTransform 布局在所有场景中都得到预期结果。
对真实项目先在副本上操作并检查结果。

## 单元测试与验证状态

安装 Unity Test Framework，在 Test Runner 的 **EditMode** 运行
`UnityBox.PrefabDiffPatcher.Editor.Tests` 程序集中的
`UnityBox.PrefabDiffPatcher.Tests.PrefabDiffRegressionTests`。该程序集仅依赖本包和
Unity Test Framework，独立 Unity 项目可运行。
VPM 安装时，将本包名追加到 `Packages/manifest.json` 的 `testables`。

测试覆盖 Transform 应用、未选项保留、克隆局部坐标、骨骼数组/嵌套引用重映射、
未引入对象引用清理、精确数据比较、同名及特殊字符路径、输出 GUID 和输入路径保护。
源码位于 `Tests/Editor/PrefabDiffRegressionTests.cs`。
测试覆盖不能代替真实 Prefab 保存验证；Unity 执行结果以实际 Test Runner 报告为准，
源码和配置检查不代表测试通过。
