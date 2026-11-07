# 一次确认整项目升级与源配置验证

当前版本为 ACC **0.5.3**、AO **0.3.3**、ASS **0.9.4**、Viseme **0.2.3**。
各插件的 Tools 菜单在确认一次后处理整个 Assets，恢复代码仍属于原有 Editor 程序集。
完整菜单与固定组件身份见[组件引用说明](../../ComponentReferences.md)。

## 当前操作与验证

`ComponentReferenceUpgrade.Execute(Func<bool>)` 是真实菜单调用路径：取消即结束，确认后执行
`MigrationEngine.UpgradeProjectAssets()`；无需选择资源、单独分析面板或逐 GUID 指定类型。
识别结合明确类型、已知 GUID、ACC 完整字段结构与 Outfit/Prefab 上下文，同 GUID 引用自动关联。
备份后只替换失效脚本 GUID，保留字段、fileID、资产 GUID、对象引用和 UTF-8/BOM/换行。
未知与重复组件保留；干净已保存编辑上下文在处理后恢复，未保存编辑与依赖保持。

ACC 保留完整 Avatar 换装面板，移除普通 Controller 面板和输出选项；Open 缺少 SDK/MA 时
提示安装依赖。底层动画算法、原 Avatar 配置与已有生成资产保留。

全库当前执行、图形与原生源码指纹见[验证索引](../../VerificationResults.json)。
真实旧资源在不同 Assets 子目录中重放，调用整项目菜单路径并验证确认/取消、自动关联、
逐批备份、其它插件 GUID、原始文件和重复组件保全；记录位于 [WholeAssetsUpgrade 归档](../WholeAssetsUpgrade/README.md)。

## 备份

每包批次保存到 `UserSettings/UnityBox/<插件目录>/ComponentReferenceMigration/<id>/Assets/...`。
旧备份继续保留，升级不会自动恢复或清理它们。

## 历史：前版选择资源 API 验证

[SingleCore 原始记录](SingleCore/summary.json)属于0.5.2/0.3.2/0.9.3/0.2.2，执行了明确标注
过滤条件的恢复专项；其原始 XML、源码、metadata 和 DLL 指纹保留。下面15次 Editor 流程
同样属于该版本，记录底层 Scan/Apply 选择路径的真实行为，不作为当前 Tools 菜单操作说明。

## 历史：0.5.2 / 0.3.2 / 0.9.3 / 0.2.2 原始旧包升级

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
