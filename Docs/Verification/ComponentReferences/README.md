# 各插件内置组件恢复与源配置验证

当前版本为 ACC **0.5.2**、AO **0.3.2**、Viseme **0.2.2**、ASS **0.9.3**。恢复功能
编译在所属插件原有的 Unity-only Editor 程序集中，随正常插件安装和更新交付。
六种组件的类名、Runtime 程序集、固定 GUID 与旧身份来源见
[组件引用说明](../../ComponentReferences.md)。

## 五种消费工程

| 消费工程 | 自带的恢复范围 | Project 菜单所有者 |
| --- | --- | --- |
| 仅 ACC | 三种 ACC 标记 | Advanced Costume Controller |
| 仅 AO | AnimatorOptimizerComponent | Animator Optimizer |
| 仅 ASS | ASSComponent | Avatar Security System |
| 仅 Viseme | VisemeBlendshapeOverrideComponent | Viseme Blendshape Override |
| 四包共同安装 | 四个独立入口，共六种组件 | 各入口只处理所属插件类型 |

每个工程使用正常包准备流程和所选包的实际依赖，检查恢复源码随该包进入原 Editor
程序集。Project 入口为 `Assets/UnityBox/<所有者>/Repair Component References`，
窗口带入当前选择；原 Tools 插件窗口入口保持。各消费组合的实际命令、源码指纹、
测试程序集、退出码与数量按执行记录列出，全库索引见[VerificationResults.json](../../VerificationResults.json)。

无 SDK 的四个单插件消费者分别执行包内恢复专项：ACC 13 项，AO、ASS、Viseme 各 11 项，
全部通过。[专项原始记录](SingleCore/summary.json)保留明确的测试过滤条件、原始 XML、
所选包及其真实源码/metadata/DLL 指纹；完整功能套件另按核心、SDK、完整集成范围计数。

## 手动操作契约

在 Project 选择 Prefab、Scene、`.asset` 或文件夹，打开所属插件的恢复窗口。
可先 **Analyze**，再点击 **Migrate Selected Assets** 一次处理本批可确定引用，不逐项确认。
ACC 的 Mode/GroupName 是弱身份字段，状态为 Ambiguous 时，即使只有一个候选，也按旧
GUID 选择一次类型；同次扫描内同旧 GUID 共用选择。其它插件类型不进入本包迁移目录。

分析只读，启动和导入没有迁移回调。执行检查源 SHA 与目标脚本身份，先备份本批全部
目标原字节和对应 metadata，再只替换失效脚本 GUID。fileID、其它字段、引用、Prefab
覆盖、BOM、中文/emoji 和换行保持。分析不创建备份或旧身份缓存。

未知、有效外部组件、新旧同类重复或多个旧同类重复保留并报告，不自动删除或合并。
已打开 Scene/PrefabStage 及它们的依赖报告跳过，包括只在内存中新增、尚未保存的嵌套
Prefab 和对象资源引用。窗口不替用户保存、关闭或重载；图形用例以关闭 autoSave 的
Prefab Stage 检查对象身份、dirty 状态和未保存编辑。

## 分包备份

批次路径为 `UserSettings/UnityBox/<插件目录>/ComponentReferenceMigration/<id>/Assets/...`，
插件目录分别为 AdvancedCostumeController、AnimatorOptimizer、AvatarSecuritySystem 和
VisemeBlendshapeOverride。同批报告记录源 SHA 与处理项，四包的批次保持独立。
旧共用备份及旧 ASSMigration 目录继续保留，不由新入口自动恢复或删除。

## 真实旧版本升级

[六组件真实升级归档](LegacyUpgrade/README.md)使用公开 ACC 0.4.13、ASS 0.7.24、
Viseme 0.1.14 ZIP，以及明确的未发布 AO 0.2 开发源。旧包导入产生真实脚本 GUID，
保存场景、Prefab、嵌套/Variant/实例覆盖及非默认配置，再完整更新所属插件。
归档按 `Scopes/acc`、`ass`、`viseme`、`ao`、`all` 分别保存阶段记录；四包组合分别执行
四个所属插件批次，每个批次只调用一次 Apply。

五种组合的三个原生阶段均以退出码 0 完成，共 15 个 Editor 进程：

| 安装组合 | 更新后 Missing | 直接修复引用 | 逐次比较组件 | 持久化属性 | 父对象引用 | Inspector |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 四包共存 | 42 | 12 | 49 | 2,097 | 119 | 24 |
| 仅 ACC | 21 | 6 | 28 | 472 | 63 | 12 |
| 仅 ASS | 7 | 2 | 14 | 203 | 21 | 4 |
| 仅 AO | 7 | 2 | 14 | 477 | 21 | 4 |
| 仅 Viseme | 7 | 2 | 14 | 966 | 14 | 4 |

Missing 数包含继承实例；修复原 Prefab 来源后，Variant 和嵌套实例沿原引用恢复。
重复组件另外逐字节保留，不计入可解析组件的属性比较数。每包批次的备份与该批写入前
的原件一致，后执行的插件保留前一批的修复及其余插件的旧 GUID。

升级后对比资源原字节、脚本解析状态和有效 VRC Descriptor；迁移阶段从各包内入口或
同包 API 手动执行，逐项核对可解析组件的持久化属性、组件 fileID、资源 GUID、引用的
GlobalObjectID，以及音频、材质、Renderer 和跨组件引用。每次恢复只写选定且可确定的
资源，重复与未选资源继续保留。实际源码、归档哈希和各阶段计数由该归档给出。

## 源配置、构建与测量

ACC Inspector 的外部来源选择、显式提交和 Undo；AO 配置/Options 副本；Viseme Voice
深拷贝解析和显式行编辑；ASS 时间副本分别检查源字段与原文件。普通 Player/Avatar
构建处理自己的场景和配置副本，保持已有源 Controller 和配置。

全库功能、图形、Linux Player、串行生成/更新与 ASS 包内恢复扫描按独立运行记录，
结果、协议和内容指纹见[测试指南](../../Testing.md)、[验证索引](../../VerificationResults.json)、
[性能报告](../../Performance.md)和[原始测量](../../PerformanceResults.json)。
其余四个无自有 Avatar 组件的包保持既有发布内容与版本。
