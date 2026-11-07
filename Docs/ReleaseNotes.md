# 发布与升级说明

## 2026-11-08：一次确认升级整个 Assets 与 ACC 单入口

| 包 | 原版本 | 新版本 |
| --- | --- | --- |
| Advanced Costume Controller | 0.5.2 | 0.5.3 |
| Animator Optimizer | 0.3.2 | 0.3.3 |
| Avatar Security System | 0.9.3 | 0.9.4 |
| Viseme Blendshape Override | 0.2.2 | 0.2.3 |

从 `Tools/UnityBox/<插件菜单>/Upgrade Component References` 确认一次后，扫描整个 `Assets`，自动识别并恢复本插件的旧脚本引用。无需选择资源、打开恢复面板或逐 GUID 指定类型。
明确类型、已知 GUID 和 ACC 层级/Prefab 来源构成自动识别依据，同 GUID 的其它引用在本批关联。
原文件与 metadata 先备份；干净已保存的编辑上下文临时刷新后恢复，未保存状态、未知和重复组件保留。

ACC 与 AO 的主窗口入口移到同父菜单的 `Open`。ACC 移除普通 Controller 生成面板与输出选项，
保留一次完成动画、菜单、参数和 FX 安装的 Avatar 换装流程；缺少 SDK/MA 时提示安装依赖。
底层算法继续供 Avatar 生成与测试使用，已有生成资产、Runtime GUID、字段及原场景保持。

[当前操作与备份](ComponentReferences.md) · [变更与影响](RefactorChanges.md) · [执行证据](Verification/ComponentReferences/README.md)


## 2026-11-07：各插件内置组件恢复（历史发布）

| 包 | 原版本 | 新版本 |
| --- | --- | --- |
| Advanced Costume Controller | 0.5.1 | 0.5.2 |
| Animator Optimizer | 0.3.1 | 0.3.2 |
| Viseme Blendshape Override | 0.2.1 | 0.2.2 |
| Avatar Security System | 0.9.2 | 0.9.3 |

四个插件随正常更新内置手动组件恢复，编译在各自现有的 Unity-only Editor 程序集中。
在 Project 选择资源，通过 `Assets/UnityBox/<插件名>/Repair Component References` 打开
对应窗口，选择会自动带入；原 Tools 插件窗口入口保持。

每个窗口只处理所属插件的三种或一种组件。可先 Analyze，再一次执行
**Migrate Selected Assets**，无需逐项确认。ACC 的 Mode/GroupName 弱候选按旧 GUID
选择一次类型；重复、未知与打开资源/依赖（含未保存新引用）报告跳过。
源 SHA、目标脚本核对与本批全部备份完成后才改失效 GUID，不触发自动场景重载。
备份按各插件目录隔离，原字段、fileID、metadata、BOM 和换行保持。

菜单、范围和备份路径见[组件引用手册](ComponentReferences.md)。单包四组合、四包共同
安装及真实旧版本的检查见[验证记录](Verification/ComponentReferences/README.md)。
六种生产组件的类名、Runtime 程序集、字段和固定 GUID 保持，其余四个无自有组件的包
继续保留原版本与发布内容。

## 2026-11-07：手动组件引用迁移与源配置保全（历史发布）

| 包 | 原版本 | 新版本 |
| --- | --- | --- |
| Advanced Costume Controller | 0.5.0 | 0.5.1 |
| Animator Optimizer | 0.3.0 | 0.3.1 |
| Viseme Blendshape Override | 0.2.0 | 0.2.1 |
| Avatar Security System | 0.9.1 | 0.9.2 |

其余四包保持原版本与发布内容。八插件的组件矩阵确认六种自有 Avatar/Prefab 组件，
类名、Runtime 程序集、已发布字段和固定身份继续保留。旧 ACC/ASS/Viseme 发布 ZIP
缺少 `.meta`，AO 的相同问题来自早期 0.2 开发快照；AO 0.2 没有公开 VPM ZIP。

该次发布曾提供共用的 Component References 窗口：用户选择资源后一次手动批量迁移，
无逐项确认；歧义旧 GUID 只选择一次目标类型，未知、重复新旧组件及已打开资源/依赖
只报告跳过。分析不写文件，执行先备份本批原字节并校验源 SHA，再替换失效脚本 GUID，
保留其它字段、fileID、引用与 metadata，无启动、导入或场景重载触发。

该次发布使用过独立安装包；当前版本已改为对应插件内置的恢复入口。
当前操作、备份路径与六类固定身份见[组件引用说明](ComponentReferences.md)，
原生和真实旧版本回归见[执行记录](Verification/ComponentReferences/README.md)。

- ACC：取消材质变体 Inspector 打开时初始化；外部来源先保留临时选择，显式操作提交并可 Undo。
  无效材质槽和 Exclude 下的 GroupName 保留源值。
- AO：源 layerIndices、字符串和预算不在加载时改写，配置/Options 在副本中规范化。
- Viseme：加载、绘制和构建读取保留源 bindings；旧 Voice 与有效值在深拷贝计划中解析，
  Inspector 的 Edit settings 只添加选中的缺失行并支持 Undo；旧 authoring API 的显式调用
  保留编辑语义，调用方负责 Undo。
- ASS：继续保留无自动迁移、0.5s/0.3s 默认时值与构建副本规范化；旧引用由上述手动工具处理。

## 2026-11-07：ASS 0.9.1 撤回自动迁移（历史发布）

本版完整移除 0.9.0 的自动迁移实现、项目扫描、导入/移动时 GUID 改写、自动场景重载、
修复菜单和写入 API。打开项目、导入包和加载 Scene/Prefab 时保留源资源及已有配置。
组件不再通过 `OnValidate` 自动改写时值；有效时间约束在生成所用的配置副本中处理。
默认保持 0.5s、容错 0.3s 保持不变，新组件和显式 Reset 使用这些默认值。

已有 Scene/Prefab 中的 Missing Script 按该组件的 `m_Script` GUID、所属脚本及
Console 首条编译错误定位；该版不执行项目范围的引用替换。

Inspector 移除新增的顶部自动构建 HelpBox，恢复插件介绍和字段说明的呈现；
Avatar 锁定选项与手动 Generate 按钮继续移除。

0.9.0 已留下的 `UserSettings/UnityBox/ASSMigration/<id>/Assets/...` 记录继续保留。
它们只包含相应轮次写入前的 Scene/Prefab，不含旧脚本 metadata、包版本和后续修改；
需要恢复时先保全当前项目并逐文件比较，由用户明确选择内容。本版没有自动恢复步骤。

当前执行范围与源指纹见[导入安全记录](Verification/ASSImportSafety/README.md)。
自动构建、连续手势、WD Off、四语言和用户原 Controller/AOC 保留继续使用，
完整操作见[ASS 用户指南](../Assets/UnityBox/AvatarSecuritySystem/Editor/README.md)。

## 2026-11-07：ASS 0.9.0（历史发布）

0.9.0 曾通过启动和导入回调修复旧随机脚本 GUID，相关实现已于 0.9.1 撤回。
[历史三阶段归档](Verification/ASSUpgrade/README.md)保留合成工程上的旧行为记录，
用于追溯 0.9.0 的实现；当前版本的检查以源资源保全为目标。

### 自动构建与输入流程调整

ASS 0.9.0 移除 Avatar 对象锁定及对应选项，旧字段仅作隐藏的序列化兼容。
普通 Unity 在正常 Player 构建的场景副本中自动复制原 Controller/AOC、保留用户层和
WD 设置、清除旧 ASS 层（含遗留锁层），再添加手势、计时和反馈。Inspector 手动
Generate 按钮移除；新增 ASS 状态全部 WD Off，不写用户 Avatar 对象缩放。

默认保持 0.5s、容错 0.3s，并统一保证保持时间严格大于容错。正确姿势需连续达到保持
时长，错误中断后重新计时，容错过期清当前序列，重复位需要先换姿势释放；超时和
Restart 优先。0.9.0 曾在加载时把不满足关系的旧时值规范化，例如 0.15s / 0.3s
调整为 0.15s / 0.14s；0.9.1 只在构建配置副本中处理该约束，保留源字段。运行时使用 IsPasswordCorrect；IsUnlocked 继续作为同一密码结果的兼容别名。
仅启用诱饵状态、关闭诱饵层时仍声明条件参数；没有 Bool 池时添加本地布尔守卫，
使参数类型与生成的转移条件一致。Inspector 与校验说明覆盖英语、简体、繁体、日语，使用步骤见
[ASS 用户指南](../Assets/UnityBox/AvatarSecuritySystem/Editor/README.md)。

## 2026-11-05：八插件统一主分支（历史发布）

八个插件统一维护于 `master`。本次合并包含原 `refactor` 的七插件实现、
`feat/animator-optimizer` 的优化器，以及合并后的功能修复、测试和文档。
当前执行结果见[验证记录](VerificationResults.json)，全部测量见[性能报告](Performance.md)。

### 包版本

本轮按各包 `package.json` 独立升级版本，供 VCC/ALCOM 识别更新：

| 插件 | 原版本 | 新版本 |
| --- | --- | --- |
| Advanced Costume Controller | 0.4.13 | 0.5.0 |
| Animator Optimizer | 0.2.0 开发快照 | 0.3.0 首个公开包 |
| Avatar Collider Monitor | 0.1.0 | 0.2.0 |
| Avatar Security System | 0.7.24 | 0.8.0 |
| Blendshape Controller Generator | 0.1.1 | 0.2.0 |
| Build Pipeline Logger | 0.1.0 | 0.2.0 |
| Prefab Diff Patcher | 0.1.1 | 0.2.0 |
| Viseme Blendshape Override | 0.1.14 | 0.2.0 |

Unity 核心与可选 SDK/MA/NDMF 集成通过各自程序集启用。外部 Editor 扩展若引用旧程序集，
按[程序集与依赖映射](DependencyArchitecture.md)调整引用。现有脚本 GUID、序列化字段和
用户场景按原身份保留；旧 Voice 映射由显式序列化迁移处理。

### 本轮功能改进

- **Animator Optimizer**：自动推荐按真实 Mask、BlendMode 与状态标签分组，只有至少两个
  来源层的普通合并组才进入默认多层推荐。不同分组的单层保持原状态机和参数类型；
  手动明确转换和显式启用的 AAP 继续提供。NDMF 使用相同选择规则，并保留覆盖顺序约束。
- **Advanced Costume Controller**：整 Avatar 参数检查同时核对预算与同名类型冲突。
  失败时撤销本次场景安装，继续使用此前的 Controller、菜单和资产。
- **Blendshape Controller Generator**：多个 Renderer 共用一次生成事务；后续 Renderer
  失败或用户取消时，整批恢复旧安装与 Controller 引用，清理本批新 Build。
  成功后一次 Undo/Redo 对应整个批次。
- **Viseme Blendshape Override**：重生成从旧生成层的实际引用图回收资产，排除其它层仍
  使用的 Motion。共享 Clip/BlendTree 和仅名称相似的用户资产保留，重复生成的输出稳定。
- **Avatar Security System**：运行时只在参数赋值或本地输入判断需要时解析参数类型。
  音频动作支持根节点 AudioSource 的空相对路径，也继续支持子节点路径。
- **Prefab Diff Patcher**：输出路径同时避开 A/B 本身、A 的 Variant 祖先及嵌套来源，
  在写入前处理冲突，保持来源资产内容和 GUID。
- **Avatar Collider Monitor**：历史 JSON 完整解析成功后一次提交；后段数据错误时原历史、
  导出结果和比较缓存保持一致，追加和替换导入使用同一事务边界。
- **Build Pipeline Logger**：Player 准备阶段和 preprocess 使用同一次构建轨迹，
  LastCompleted 保留完整准备、场景、Linker 和完成顺序；下一次构建重新建立轨迹。

各包手册说明具体入口、配置、API 与迁移步骤；[变更与升级影响](RefactorChanges.md)
同时记录之前分支迭代的生成层、独立 Unity 工作流、Undo/Redo 和资源持久化改进。

### 场景和源码历史

`Assets/UnityBox.unity` 保留用户后来上传的 temp/refactor 场景原字节，来源提交
`409eb5b4fe6400a7306de9535a3be0dcc835fa90`，大小 2,924,530 bytes，SHA256 为
`97bd78f2791abea3c101fe99b4cba3becc301f0e1cf7cfe7f9cbb85755baa202`。
旧优化器分支的示例场景仍保留在 `53fa22fe453bb683928e88c1ce1aa4b83a6a6e9a` 历史中。
合并不混合两份场景的对象 ID，也不重写用户配置。

### 安装和验证

更新已有项目时先在 VCC/ALCOM 查看对应的新包版本，更新后按所需工作流重新生成输出；
此前生成资产继续供场景引用与 Undo 使用。程序集依赖、参数类型与生成路径的变化见
对应手册及升级说明。

仓库脚本支持纯 Unity、仅 SDK、完整 MA/NDMF、图标像素、真实 Linux Player 和串行
性能验证。逐帧输出、保存重载、Undo/Redo、失败回滚与源资产保留均由实际用例检查。
不同依赖范围重复执行核心用例，测试数分别记录。

向 `master` 推送触发 VPM 构建与 Pages 工作流。发布按各包清单版本生成归档；
历史版本和已发布的 ZIP 保持可下载，同版本的实际内容改变需要先升级版本。
新的 ZIP 使用确定的排序、时间戳和权限规则；相同内容可重复得到相同 SHA256。
旧 ZIP 经内容与 SHA256 核对后沿用原归档，保留既有版本身份。
