# UnityBox 组件引用与手动迁移

UnityBox 八个插件共有六种会随 Avatar/Prefab 保存的自有生产组件。旧发布包缺少脚本
`.meta` 时，Unity 会在各项目生成不同的 GUID；后来安装带固定 metadata 的包，已有
Scene/Prefab 仍可能引用原 GUID。组件名称相同也需要由 `m_Script` 指向实际已安装脚本。

本轮保留六种组件的类名、命名空间、Runtime 程序集、公开序列化字段与既有资源 GUID。
引用迁移只替换选定资源中的失效脚本 GUID，使用当前已安装类型的真实脚本身份。
组件内部的旧字段兼容和构建配置规范化在独立快照中处理，源组件保留已存值。

## 八插件审查范围

| 插件 | 自有 Avatar/Prefab 组件 | 旧身份来源 | 当前处理 |
| --- | --- | --- | --- |
| Advanced Costume Controller | `ACCOutfitMarker`、`ACCPartGroupMarker`、`ACCVariantMaterialOverride` | 公开 0.4.13 ZIP 无 `.meta`；0.5.0 起固定身份 | 0.5.1 保留三类及字段；旧引用可手动批量重连；Inspector 仅在显式操作时提交编辑 |
| Animator Optimizer | `AnimatorOptimizerComponent` | 0.2 是开发快照，未发布旧 VPM ZIP；早期 Runtime 配置脚本未携带 `.meta` | 0.3.1 保留类型、字段和 0.3.0 固定身份；构建规范化独立配置快照 |
| Avatar Security System | `ASSComponent` | 公开 0.7.24 ZIP 无 `.meta`；0.8.0 起固定身份 | 0.9.2 继续保留字段和固定身份；通过独立手动工具修复旧引用，无启动/导入迁移 |
| Viseme Blendshape Override | `VisemeBlendshapeOverrideComponent` | 公开 0.1.14 ZIP 无 `.meta`；0.2.0 起固定身份 | 0.2.1 保留组件和字段兼容；旧 Voice 与值域在构建快照解析 |
| Blendshape Controller Generator | 无 | 旧包只有 Editor 窗口及生成逻辑 | 自有组件迁移目录为空；生成的 MA/Animator 组件使用各自原类型 |
| Avatar Collider Monitor | 无 | EditorWindow、快照与 provider，无自有 Avatar authoring 组件 | 保持 0.2.0，不把用户碰撞体或 SDK 组件归为本插件 |
| Build Pipeline Logger | 无 | 静态设置、回调和 NDMF 插件，无自有生产 MonoBehaviour | 保持 0.2.0，不迁移 Avatar 组件 |
| Prefab Diff Patcher | 无 | EditorWindow 和合成逻辑；测试 Fixture 不属于产品组件 | 保持 0.2.0，比较对象上的组件属于其原插件 |

这些旧版本的差异集中在缺失脚本 metadata，而不是本轮把六种组件的类或 Runtime
程序集改名。Viseme 更早的行字段别名、Disabled/Linear 枚举兼容继续按原含义读取；
引用工具不改写这些字段。AO 的 0.2 仅用于开发快照回归，不以公开发布 ZIP 描述。

## 六种脚本的发行身份

下表列仓库和发行包的固定 GUID；工具在扫描时解析项目当前实际安装的目标脚本。
已有有效脚本引用保留；同名类、显示名称或字段相近不直接替代类型判断。

| 类型 | Runtime 程序集 | 固定脚本 GUID |
| --- | --- | --- |
| `UnityBox.AdvancedCostumeController.ACCOutfitMarker` | `UnityBox.AdvancedCostumeController.Runtime` | `e98f908c5fb14a479a7cc0ce3492a7d5` |
| `UnityBox.AdvancedCostumeController.ACCPartGroupMarker` | `UnityBox.AdvancedCostumeController.Runtime` | `594421b5520f47b892549d6e70c660fe` |
| `UnityBox.AdvancedCostumeController.ACCVariantMaterialOverride` | `UnityBox.AdvancedCostumeController.Runtime` | `a37f4c4e3ea441078fad0c0782ac99a4` |
| `UnityBox.AnimatorOptimizer.AnimatorOptimizerComponent` | `UnityBox.AnimatorOptimizer.Runtime` | `a157d3d35699dd84abde9e5412bd7017` |
| `UnityBox.AvatarSecuritySystem.ASSComponent` | `UnityBox.AvatarSecuritySystem.Runtime` | `1c51e6a139920bb4ca12700dc82627ff` |
| `UnityBox.VisemeBlendshapeOverride.VisemeBlendshapeOverrideComponent` | `UnityBox.VisemeBlendshapeOverride.Runtime` | `2814d8bd3adc4c539c9d1825545f6316` |

ASS 的 `StandaloneSecurityInput`、`AnimatorStateEntryActions` 从 0.8.0 起新增且身份稳定，
不属于早期旧 GUID 的六类迁移候选。VRC Avatar Descriptor、MA 组件、Unity 内建组件
及用户自写脚本继续属于各自程序集，工具保留这些有效引用。

## 安装 Component References

组件引用工具是独立 Editor 工具，源码位于
`Assets/UnityBox/ComponentReferenceMigration/Editor`，没有新增第九个 VPM 包。

- 从源码使用完整 UnityBox 仓库时，工具随该目录一起导入。
- 通过 VCC/ALCOM 只安装部分插件时，下载
  [UnityBox-ComponentReferences.unitypackage](https://sealoong.github.io/UnityBox/UnityBox-ComponentReferences.unitypackage)，
  在 Unity 导入一次即可。工具只依赖 Unity Editor，可处理当前安装的一至八个 UnityBox 包。

安装后打开 `Tools → UnityBox → Component References`。
工具没有启动、资源导入或移动时自动执行的迁移回调；打开窗口和分析均只读取资源。

## 一次批量迁移

1. 在 Project 窗口选择需要处理的 Prefab、Scene、`.asset` 或文件夹，打开工具并点击
   **Use Project Selection**；也可以把资源拖入窗口。文件夹按其内部资源递归扫描。
2. 可先点击 **Analyze** 查看旧 GUID、目标组件、实际目标脚本 GUID 和处理状态。扫描范围外的
   依赖只用于身份追溯，不扩展为写入目标。
3. 状态为 **Ambiguous** 时，在该旧 GUID 的下拉框选择一次正确类型。仅有 `Mode` /
   `GroupName` 等通用字段时，即使只列出一个 ACC Part Group Marker 候选，也需要这一次
   类型选择。同次扫描内所有相同旧 GUID 共用选择；批量执行时自动重新分析，也可先点
   Analyze 查看结果。
4. 点击 **Migrate Selected Assets**。这一次手动操作会重新分析并批量处理可确定的条目，
   不要求逐组件、逐资源确认。未识别、目标脚本不可用、歧义或同对象重复新旧组件等条目列入
   报告并保留原文，不自动删除、合并或猜测目标。
5. 查看批次报告。已打开的 Scene、Prefab Stage 及它们引用的依赖会报告跳过，包括编辑中
   尚未保存的新嵌套 Prefab 和对象资源引用；需要处理时，由用户保存并关闭相应编辑内容，
   再次执行。工具不替用户保存、关闭或重载场景。

执行前会重新核对扫描时的源文件、身份来源与目标脚本；资源在扫描后发生变化时，先
重新分析。执行中只替换失效 `m_Script` 的固定长度 GUID 字节，保留组件 fileID、字段、
Prefab 覆盖、对象/资源引用、UTF-8 BOM 和换行。项目中的 `.meta` 不改写。

## 备份与恢复

每批迁移在写入任何目标前，先备份本批全部目标原始字节及现有对应 `.meta`：

```text
UserSettings/UnityBox/ComponentReferenceMigration/<id>/Assets/...
```

同批保存处理报告和源内容 SHA256。**Analyze** 不创建备份或旧身份缓存。
该目录记录本批次修改前的资源，不包含整个工程、历史包版本或后续编辑。

恢复时保留当前项目副本，按报告比较对应文件与备份，选择需要恢复的目标；恢复流程
不由启动、导入或再次分析触发。0.9.0 的旧 ASS 备份仍位于
`UserSettings/UnityBox/ASSMigration/`，与这个手动工具的新批次目录分开。

## 源配置与构建

- **ACC 0.5.1**：打开材质变体 Inspector 不初始化或清空原配置；选择外部来源先保留在
  Editor 临时状态，显式应用、分析或刷新操作才提交并记录 Undo。无效材质槽在绘制时
  保留存储值；切到 PartGroup Exclude 保留原 GroupName。
- **Animator Optimizer 0.3.1**：导入和打开 Inspector 保留原 layerIndices、字符串和预算
  字段；配置捕获和转换 Options 使用独立规范化副本。用户明确改选层或编辑字段仍可 Undo。
- **Viseme 0.2.1**：加载和 Inspector 绘制保留原 bindings，不自动补行；构建计划用深拷贝
  快照解析旧 Voice 和有效值。Inspector 的 **Edit settings** 只追加用户选中的缺失行并支持
  Undo；旧公开 authoring API 保留显式编辑语义，脚本调用方负责自己的 Undo。
- **ASS 0.9.2**：源字段在打开、导入和配置捕获后保持；新组件/显式 Reset 默认保持 0.5s、
  容错 0.3s，生成图只规范化独立配置副本。普通 Player/Avatar 自动构建继续处理构建副本。

迁移是用户选定资源后明确执行的文件操作。普通打开、导入、分析和构建配置读取与该
操作分离；手动执行一批已经覆盖该批次的可确定条目，无额外逐项审批。

## 验证记录

全六种组件的真实旧包/开发快照升级、选定范围、打开资源保护、歧义/重复跳过、备份原字节
及源参数保全检查集中于[Component References 验证记录](Verification/ComponentReferences/README.md)。
[0.9.1 导入保全记录](Verification/ASSImportSafety/README.md)和
[0.9.0 自动迁移归档](Verification/ASSUpgrade/README.md)保留对应历史版本的执行范围。
