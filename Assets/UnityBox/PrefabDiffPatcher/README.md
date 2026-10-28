# Prefab Diff Patcher

比较基础版本 A 和提供差异的版本 B，把选中的对象属性、组件和子树保存到独立 Prefab。
需要 Unity 2022.3；核心只使用 Unity API。VPM 包名：
`top.sealoong.unitybox.prefab-diff-patcher`。

## 使用

1. 打开 `Tools > UnityBox > Prefab Diff Patcher`，指定 A 和 B，点击 **Analyze Diff**。
2. 按对象壳、Transform/RectTransform、其它组件选择差异；父节点批量选择后仍可逐项调整。
3. 选择 `Assets` 内独立的 `.prefab` 输出路径，点击应用。重复保存使用原输出 GUID。

对应项选中时采用 B，未选中时保留 A。A 独有的对象保留；B 独有的对象按子树选择加入。
组件在 B 中删除且该组件差异被选中时，结果移除该组件。根对象壳固定使用 A，根组件可独立选择。

## Prefab 与场景对象输入

| A 的类型 | 输出方式 | 保留内容 |
| --- | --- | --- |
| Prefab 资产根对象，包含 Variant | 实例化 A 一次，在实例上应用选中差异，保存为 A 的 Variant | 基础资产继承、未选中的覆盖、嵌套 Prefab 连接 |
| 普通场景对象或选中的子对象 | 按选择组合成独立普通 Prefab | 对象/组件数据、局部变换、内部引用 |

资产路径的处理先创建完整 A 实例，再在现有层级上修改选中项。深层差异不会反复实例化上层的
整棵子树。保存前记录实例属性覆盖；重新加载输出后，选中的值和嵌套资产连接仍保持。
工作副本在自有 Preview Scene 中保存和清理，活动场景、已保存场景的干净状态和场景根对象保持原值。
输入 A/B 的源资产保持原值，输出文件独立保存。

## 对象身份与序列化

- 对象按相对路径匹配，同名兄弟按出现次序区分；路径中的保留字符会转义。组件使用完整
  类型名及该类型的出现次序匹配。改名或移动分别表现为两侧的不同路径。
- 对象壳和 Transform 选择独立。Transform 复制局部位置、旋转和缩放；RectTransform
  还复制 anchor、pivot、sizeDelta、anchoredPosition3D，保留输出父子关系。
- 数组、嵌套字段、精确曲线/梯度内容与引用一起比较。同一逻辑骨骼的本地引用按对应路径
  比较；引用指向不同骨骼时产生差异。
- 组合后内部引用指向输出对象；未选入的 B 独有目标对应空引用；外部资产引用继续指向
  原资产。`ExposedReference` 同时保留绑定键和默认目标。
- `SerializeReference` 支持共享节点和循环图。实现 `ISerializationCallbackReceiver` 的
  自定义序列化组件在引用重映射后重建托管缓存，再保存其序列化字段。
- 临时副本保持可编辑，Light、Renderer 等内置组件的选中值可以正常覆写。输出保存前
  清除临时隐藏标记。Missing Script 输入会给出对应修复提示。

## 测试与性能

`UnityBox.PrefabDiffPatcher.Editor.Tests` 包含普通选择与引用回归、真实资产 Variant/嵌套
连接重载、RectTransform 布局、循环托管引用、自定义序列化、输出 GUID 和路径处理。
性能类别 `UnityBoxPerformance` 测量 32/256 节点比较，以及 16/64 层深度下场景组合与
单次实例化 Variant 的保存流程；计时包含 Prefab 保存与导入，每个场景预热后采样 9 次。

在 Test Runner 的 **EditMode** 运行本程序集。UPM/VPM 安装时，在 manifest 的 `testables`
中加入本包名。场景与临时资产由夹具创建和清理，源场景保持原样。
测量结果与复现命令见[性能报告](../../../Docs/Performance.md)，回归结果见
[测试指南](../../../Docs/Testing.md)。

本包在 Unity 2022.3.22f1 的执行记录与程序集结果统一保存在
[验证结果](../../../Docs/VerificationResults.json)，性能场景由独立运行采样。
