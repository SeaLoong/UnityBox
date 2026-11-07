# UnityBox 组件引用与手动迁移

UnityBox 八个插件共有六种会随 Avatar/Prefab 保存的自有生产组件。旧发布包缺少脚本
`.meta` 时，Unity 会在各项目生成不同的 GUID；后来安装带固定 metadata 的包，已有
Scene/Prefab 仍可能引用原 GUID。组件名称相同也需要由 `m_Script` 指向实际已安装脚本。

本轮保留六种组件的类名、命名空间、Runtime 程序集、公开序列化字段与既有资源 GUID。
用户从插件菜单确认后，升级会扫描整个 `Assets`，只替换能够确认属于该插件的失效
脚本 GUID，使用当前已安装类型的真实身份。
组件内部的旧字段兼容和构建配置规范化在独立快照中处理，源组件保留已存值。

## 八插件审查范围

| 插件 | 自有 Avatar/Prefab 组件 | 旧身份来源 | 当前处理 |
| --- | --- | --- | --- |
| Advanced Costume Controller | `ACCOutfitMarker`、`ACCPartGroupMarker`、`ACCVariantMaterialOverride` | 公开 0.4.13 ZIP 无 `.meta`；0.5.0 起固定身份 | 0.5.3 保留三类及字段；旧引用可手动批量重连；Inspector 仅在显式操作时提交编辑 |
| Animator Optimizer | `AnimatorOptimizerComponent` | 0.2 是开发快照，未发布旧 VPM ZIP；早期 Runtime 配置脚本未携带 `.meta` | 0.3.3 保留类型、字段和 0.3.0 固定身份；构建规范化独立配置快照 |
| Avatar Security System | `ASSComponent` | 公开 0.7.24 ZIP 无 `.meta`；0.8.0 起固定身份 | 0.9.7 继续保留字段和固定身份；通过本包自带的手动入口修复旧引用，无启动/导入迁移 |
| Viseme Blendshape Override | `VisemeBlendshapeOverrideComponent` | 公开 0.1.14 ZIP 无 `.meta`；0.2.0 起固定身份 | 0.2.3 保留组件和字段兼容；旧 Voice 与值域在构建快照解析 |
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

## Tools 升级入口

正常更新对应插件即可使用组件升级。命令编译在该包原有的 Unity-only Editor 程序集中，
每个命令只处理所属插件的组件。

| 插件与版本 | 升级菜单 | 范围 |
| --- | --- | --- |
| ACC 0.5.3 | `Tools/UnityBox/Advanced Costume Controller/Upgrade Component References` | 三种 ACC 标记 |
| AO 0.3.3 | `Tools/UnityBox/Animator Optimizer/Upgrade Component References` | AnimatorOptimizerComponent |
| ASS 0.9.7 | `Tools/UnityBox/ASS/Upgrade Component References` | ASSComponent |
| Viseme 0.2.3 | `Tools/UnityBox/Viseme Blendshape Override/Upgrade Component References` | VisemeBlendshapeOverrideComponent |

ACC 与 AO 的主面板分别从同组的 **Open** 打开。ASS 与 Viseme 继续在组件 Inspector
配置，不增加独立配置面板。

曾手动导入旧版独立工具的项目，可自行移除旧 `Assets/UnityBox/ComponentReferenceMigration`
目录；当前命令随插件运行。旧版共用备份目录保留，不随代码移除。这是旧安装的可选
清理，正常插件更新无需执行此步骤。

## 一次确认升级整个 Assets

1. 点击上表所属插件的 **Upgrade Component References**。
2. 确认一次本次升级，命令扫描整个 `Assets` 下的 Scene、Prefab 和 `.asset`，自动匹配
   能确定身份的所属组件并执行批次；取消则不写资源。不需要先选择资源或打开恢复面板。
3. 查看 Console 汇总与批次报告，核对改写数量、备份目录和本插件保留项目。
   未识别类型及其它插件引用保持原状，不逐条输出其它脚本；本插件同对象重复的新旧
   组件或多个旧同类组件保留并报告，不删除或合并。

组件名称、类型标识和源 Prefab 身份能够确认的引用自动匹配。ACC Part Group 的通用
`Mode` / `GroupName` 字段还需要完整 schema、有效枚举值及已核实的 Outfit/Prefab 来源
上下文；确认后的旧 GUID 在本批自动关联同身份引用。界面不要求逐 GUID 选择类型，
也不逐资源或逐组件再次确认。

确认后的全项目升级针对相关资源，暂时卸载干净且已保存的 Scene 或干净 Prefab Stage，
完成后恢复原编辑场景。dirty、未命名场景及它们的依赖保留并跳过，包括尚未保存的新
嵌套 Prefab 和对象资源引用。命令不自动保存、丢弃编辑，也不随项目启动或资源导入执行。

执行前核对源文件、身份来源与目标脚本；只替换失效 `m_Script` 的固定长度 GUID 字节，
保留组件 fileID、其它字段、Prefab 覆盖、对象/资源引用、UTF-8 BOM、换行与项目 `.meta`。

## 脚本与测试 API

`UpgradeProjectAssets` 是上述全项目流程。底层 `Scan(paths)` 和 `Apply(plan)` 保留供
脚本及隔离测试调用，按传入路径建立计划和执行；它们不等同于用户菜单的整个 Assets
流程。底层调用保留自身的源 SHA/目标身份核对及打开资源保护，不自动保存或重载。
正常使用直接点击 Tools 菜单即可。

## 备份与恢复

每个插件分别保存本包的迁移批次。在写入任何目标前，先备份本批全部目标原始字节及
现有对应 `.meta`：

| 插件 | 备份目录 |
| --- | --- |
| ACC | `UserSettings/UnityBox/AdvancedCostumeController/ComponentReferenceMigration/<id>/Assets/...` |
| AO | `UserSettings/UnityBox/AnimatorOptimizer/ComponentReferenceMigration/<id>/Assets/...` |
| ASS | `UserSettings/UnityBox/AvatarSecuritySystem/ComponentReferenceMigration/<id>/Assets/...` |
| Viseme | `UserSettings/UnityBox/VisemeBlendshapeOverride/ComponentReferenceMigration/<id>/Assets/...` |

同批保存处理报告和源内容 SHA256。只读扫描不创建备份或旧身份缓存。
该目录记录本批次修改前的资源，不包含整个工程、历史包版本或后续编辑。

恢复时保留当前项目副本，按报告比较对应文件与备份，选择需要恢复的目标；恢复流程
不由启动、导入或再次分析触发。0.9.0 的旧 ASS 备份仍位于
`UserSettings/UnityBox/ASSMigration/`；旧版共用目录
`UserSettings/UnityBox/ComponentReferenceMigration/` 也作为历史记录保留，与当前各插件的
批次目录分开。

## 源配置与构建

- **ACC 0.5.3**：打开材质变体 Inspector 不初始化或清空原配置；选择外部来源先保留在
  Editor 临时状态，显式应用、分析或刷新操作才提交并记录 Undo。无效材质槽在绘制时
  保留存储值；切到 PartGroup Exclude 保留原 GroupName。
- **Animator Optimizer 0.3.3**：导入和打开 Inspector 保留原 layerIndices、字符串和预算
  字段；配置捕获和转换 Options 使用独立规范化副本。用户明确改选层或编辑字段仍可 Undo。
- **Viseme 0.2.3**：加载和 Inspector 绘制保留原 bindings，不自动补行；构建计划用深拷贝
  快照解析旧 Voice 和有效值。Inspector 的 **Edit settings** 只追加用户选中的缺失行并支持
  Undo；旧公开 authoring API 保留显式编辑语义，脚本调用方负责自己的 Undo。
- **ASS 0.9.7**：源字段在打开、导入和配置捕获后保持；新组件/显式 Reset 默认保持 0.5s、
  容错 0.3s。Inspector 提交时间编辑时保证保持至少比容错长0.01s，并约束最大等待；
  旧非法值只在显式编辑或“修正时间”时提交并支持 Undo/Redo。生成继续规范化独立副本。全部新生成状态统一 WD On，原用户状态 WD
  保留；历史隐藏 `writeDefaultsMode` 值不决定生成模式，不增加原 FX 匹配选项。
  普通 Player/Avatar 自动构建继续处理构建副本；TimeUp→Release 使用有效 TimeUp 条件，
  Countdown/Audio 的本地 Restart 优先恢复计时与警告音。成功/超时退出恢复进度默认值，
  超时关闭 Overlay 和音频对象，相关输出见[WD On 验证](Verification/ASSWriteDefaultsOn/README.md)。

一次 Tools 菜单确认授权处理整个 Assets 内本插件可确认的旧引用。普通打开、导入和
构建配置读取保持原状；只有这次显式升级流程重连旧脚本引用并执行必要的干净场景重载。

## ASS 混淆与资源身份

词池和命名映射保持0.9.6原样，本次仅修复 UI 时间约束与混淆开关输出。
SDK 与普通 Unity 共用 ASS 现有 Editor 程序集内的诱饵生成，绑定使用不存在的元数据路径。
混淆只作用于构建副本，原 Controller、Clip 和对象身份保持；不通过组件升级命令改写名称。
Contact 保护继续保留第三方 Playable 图名，ASS 自有图与诱饵照常生成。完整 NDMF 上传
依赖晚期 SDK 回调完成 Playable 改名，单独 bake/preview 不额外全图改名。
开关、生成图和路径隔离的输出见[时间与混淆记录](Verification/ASSTimingObfuscation/README.md)。

## ASS 音频资源

0.9.5 为四个自带 MP3 补齐音频导入 metadata 并启用后台加载，源 MP3 内容保持。
正常更新后构建自动加载这些资源。此修复保留 `ASSComponent` 及其脚本身份，组件引用
升级菜单继续只处理失效 `m_Script`；不扫描或改写用户其它音频。

关闭警告音只控制倒计时提示，关闭覆盖只控制画面，密码成功音仍然生成。成功音也会被
SDK 检查，因此两个选项同时关闭也需要正确的导入设置。音频与 SDK 检查记录见
[音频上传验证](Verification/ASSAudioUpload/README.md)。

## 验证记录

当前 Tools 交互、全 Assets 范围、确认/取消、场景状态保护、自动识别及源参数检查见
[组件引用验证记录](Verification/ComponentReferences/README.md)。原五种消费工程和六类旧版本
API 恢复记录按各自版本与源指纹保留，集中于[Component References 验证记录](Verification/ComponentReferences/README.md)。
[0.9.1 导入保全记录](Verification/ASSImportSafety/README.md)和
[0.9.0 自动迁移归档](Verification/ASSUpgrade/README.md)保留对应历史版本的执行范围。
