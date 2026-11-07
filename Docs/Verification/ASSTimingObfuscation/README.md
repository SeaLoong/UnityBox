# ASS 0.9.7：时间 UI 约束与混淆输出

提交编辑时保持时间至少比容错长0.01s；减少保持时间时保留合法容错并抬高保持值，
增加容错时限制到保持−0.01。最大保持按同一有效值契约调整。默认保持0.5s、容错0.3s。
构建一直使用规范化副本；旧非法配置在 Inspector 绘制时保留原值，显式修正与编辑支持 Undo/Redo。

## 混淆输出

普通 Unity 和 SDK 共享同包 Unity-only Editor 内的诱饵图生成：声明条件参数、生成 Clip、
诱饵层及 ASS 层假状态。开关关闭时不生成对应内容，开启时在实际 Controller 中持久化输出。
全部新增状态保持 WD On，用户 Controller、Clip、字段、GUID 与已有动画输出保留。
诱饵层持久化 ASS 自有标记；把生成图赋回后重复生成或关闭选项，会在新副本清理旧诱饵层。

词池和 Clip/State 名称映射保持0.9.6原样，本轮不添加冒犯性或其它新词表。
元数据曲线置于 Avatar 上不存在的专属根路径，避免控制真实对象。

完整 Playable 改名由晚期 SDK 回调执行。存在 Contact 时继续保留第三方图名称，ASS 自有
参数/对象/状态和诱饵仍混淆；NDMF 实际上传会接上此回调，单独预览/手动 bake 不额外改全图。
原项目中的名字保持，查看正常编辑资源与查看构建副本属于不同阶段。

## 验证范围

使用 Unity2022.3.22f1、UTF1.1.33、SDK3.8.2；完整组合增加 NDMF1.11、MA1.18。
各独立工程只选择当前 ASS 包，执行完整功能程序集，排除性能和图形分类；各范围重复用例分别计数。

- SerializedObject 编辑实际源字段、非法旧值显式修正、Undo/Redo与无操作 Inspector 绘制保全。
- 四种诱饵层/状态组合、禁用主混淆、保存重载后的实际名字、Clip曲线、合法条件和WD On。
- 重建的开启、关闭诱饵、关闭混淆三种组合；持久所有权标记与同名用户层保全。
- SDK late callback 的有/无 Contact 组合，以及 NDMF 实际 Finish 后 late callback。
- 原 Controller/Clip/Mesh/metadata 字节与原生 Light/表情等输出保全。
- 真实图形 Inspector 绘制与 Prefab 字节、实际 Player 构建运行。
- 独立串行比较启用/关闭诱饵的真实 Animator.Update，包含预热、样本、图规模与输出断言。

[执行与源码指纹](evidence.json)记录命令、包版本、NUnit/XML/DLL/源码 SHA、Player、图形和性能测量。

## 执行结果

| ASS 专项范围 | 实际结果 |
| --- | --- |
| ass-obfuscation-core | 102/102 通过，1 个测试程序集 |
| ass-obfuscation-sdk | 145/145 通过，2 个测试程序集 |
| ass-obfuscation-full | 150/150 通过，3 个测试程序集 |
| ass-obfuscation-graphics | 3/3 通过，1 个测试程序集 |
| ass-obfuscation-serial | 8/8 通过，1 个测试程序集 |
| Linux Player | 2 个场景、252 帧、13 次输出检查通过；源配置 1 个保留 |
| 打包工具 | 23/23 通过；8 个当前 ZIP 与源码及 metadata 一致 |

## 历史快照

0.9.6的WD On/重复超时修复见[原记录](../ASSWriteDefaultsOn/README.md)，0.9.5音频修复见
[音频记录](../ASSAudioUpload/README.md)。全库八插件完整基线对应7cd4b40，保留各自实际版本。
