# ASS 0.9.8：保持时间允许等于容错时间

Inspector 和共享构建规范化统一使用 `hold >= tolerance`，不再要求额外 0.01s 间隔。
保持范围为 0.05–1s，容错范围为 0–保持时间，最大等待下限仍为
`max(1s, hold+tolerance+0.05s)`。默认保持0.5s、容错0.3s。

`0.3s / 0.3s` 及 `1s / 1s` 保持相等。旧 `0.15s / 0.3s` 在独立构建副本中
使用 `0.15s / 0.15s`。打开、绘制、导入源组件保留原值；显式编辑或点击“修正时间”
才写入配置，并支持 Undo/Redo。

## 验证范围

使用 Unity2022.3.22f1、UTF1.1.33、SDK3.8.2；完整组合增加NDMF1.11、MA1.18。
独立工程只选择当前ASS包，完整执行各依赖范围的功能程序集，排除性能和图形分类。
不同范围的重复用例分别计数。

- 在60／90／120Hz实际执行生成的Animator，检查相等时值的Clip长度、连续保持及中断重置。
- 实际SerializedObject等值编辑、最大范围、无需修正提示、Undo/Redo、保存重载和源配置保全。
- 完整核心、SDK、NDMF功能回归，以及真实Inspector图形、Linux Player与串行性能。
- 原场景、Runtime metadata、音频、混淆词池与另七个插件保持。

[执行与源码指纹](evidence.json)记录实际命令、依赖版本、XML/DLL/源码SHA、Player和性能记录。

## 防御统计说明

本版没有修改防御生成器、开关、构建回调或负载参数。审查当前代码和SDK面板调用：
粒子系统数量、最大粒子容量、运行中的当前粒子数分别计数；粒子生成还受已有用量和网格
预算约束，PhysX／Cloth输出关闭。SDK源Avatar面板与最终构建副本统计的是不同对象。
普通粒子的真实SDK3.8.2调用结果如下，所有系统均停止发射，实时粒子数为0：

| 场景 | SDK粒子系统数 | SDK最大粒子总数 |
| --- | ---: | ---: |
| 源对象，全部活动 | 2 | 18 |
| 源对象，关闭一个子对象 | 2 | 18 |
| 创建独立副本后统计源对象 | 2 | 18 |
| 独立副本添加一个普通小系统 | 3 | 21 |

源字段与源统计保留；关闭子对象仍计入静态容量。探针先调用与SDK EnvConfig相同的
`AvatarPerformanceStats.Initialize()`，再调用真实`CalculatePerformanceStats`。
[探针源码](stats-audit/AvatarParticleStatsAudit.cs)、[实际结果](stats-audit/result.json)与
[执行指纹](stats-audit/run.json)一起归档。该探针使用小型停止对象，不调用防御生成器。

## 执行结果

| ASS 专项范围 | 实际结果 |
| --- | --- |
| ass-equal-core | 108/108 通过，1 个测试程序集 |
| ass-equal-sdk | 151/151 通过，2 个测试程序集 |
| ass-equal-full | 156/156 通过，3 个测试程序集 |
| ass-equal-graphics | 3/3 通过，1 个测试程序集 |
| ass-equal-serial | 8/8 通过，1 个测试程序集 |
| Linux Player | 2 个场景、252 帧、13 次输出检查通过；源配置 1 个保留 |
| 打包工具 | 23/23 通过；8 个当前 ZIP 与源码及 metadata 一致 |

## 历史快照

[0.9.7时间UI与混淆记录](../ASSTimingObfuscation/README.md)保留该版0.01s间隔规则和
原始执行结果。[0.9.6 WD On](../ASSWriteDefaultsOn/README.md)与
[0.9.5音频上传](../ASSAudioUpload/README.md)保留各自版本。
