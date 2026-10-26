# Avatar Security System 技术参考

本文描述 `refactor` 分支当前实现，替代旧文档中的固定第三方顺序、通用兼容性、
安全强度和负载效果承诺。用户配置见 [README](README.md)。历史版本变化保留在
[CHANGELOG](../CHANGELOG.md)，历史实验见仓库 `SESSION_HANDOFF_2026-10-24.md`。

## 1. 实现范围与验证状态

ASSComponent 是 `MonoBehaviour, IEditorOnly` 配置载体；实际功能由 Editor 构建器生成
Animator、动画、材质和对象。没有运行时密码服务器或所有权验证。密码序列和解锁参数
存在于构建资源中，名称混淆不能使其成为可信认证机制。

本轮已做源码审查、C# 语法解析和包构建检查。Unity 编译、EditMode 回归、NDMF/SDK
实际构建和 VRChat 运行尚未执行。下列描述是代码行为与设计约束，不是运行通过报告。

## 2. 代码结构与职责

| 文件 | 职责 |
| --- | --- |
| `Runtime/Component.cs` | ASSComponent、默认值、输入字段校验和 IEditorOnly 标记 |
| `Inspector.cs` / `I18n.cs` | 配置 UI 与中英日文案 |
| `Processor.cs` | ASSConfigData、SDK 主入口、配置/Descriptor 快照、资产隔离、主功能协调、参数注册与清理 |
| `ConfigCaptureProcessor.cs` | 无 NDMF 时在早期 SDK preprocess 捕获配置 |
| `NDMF/NDMFPlugin.cs` | NDMF Resolving 捕获及 PlatformFinish 主功能调用 |
| `PlayableObfuscationProcessor.cs` | SDK 后期 Playable 混淆及 ASS 层索引刷新 |
| `Lock.cs` | 身体锁定、一次性恢复和终态释放 |
| `GesturePassword.cs` | 手势序列、保持/容错、成功/失败及反馈启用 |
| `Countdown.cs` | 输入期限、TimeUp 参数和独立警告音频层 |
| `Feedback.cs` | Overlay mesh/material 与音频对象 |
| `Defense.cs` | 遗留主动负载生成；新组件默认禁用，Play Mode 跳过 |
| `Obfuscator.cs` | 生成名、Controller/Motion 副本及可选图变换 |
| `Utils.cs` / `Constants.cs` | 参数、曲线、子资产、VRChat behaviour、名称和路径工具 |

Runtime 装配引用 `VRC.SDKBase`。主 Editor 装配引用 Runtime 与 VRChat SDK，NDMF 子装配
单独引用 NDMF，并使用 `NDMF_AVAILABLE` 约束。包清单只要求 SDK Avatars ≥ 3.7.0，
没有为可选 NDMF 固定兼容版本。

## 3. 配置与入口判定

`ASSConfigData.FromAvatar` 先检查 Avatar 根，再使用包含 inactive 子对象的
`GetComponentInChildren` 找第一份配置。它不会按 MonoBehaviour.enabled 过滤。
多组件行为不应作为配置合并功能使用。

`FromComponent` 复制值字段和密码列表，避免后续源列表变化影响已捕获的配置。
音频是保留的 Unity 资源引用。没有组件时重新 Capture 会移除该 Avatar 的旧快照。

普通模式下：

- 非法手势值（0–7 以外）使主处理失败。
- null/空密码列表合法但表示跳过生成。
- Play Mode 且 `enabledInPlaymode=false` 时跳过生成。
- `defaultEnableDefense` 会绕过普通密码/计时生成，但不代表该遗留模式经过完整验证。

`OnValidate` 限制警告阈值不超过总时长一半，确保 maxHold 大于 hold+tolerance，
将旧 WD 字段归一化为 On。默认防御模式会取消 disableDefense。
新组件/Reset 默认 disableDefense=true、defaultEnableDefense=false；不会自动迁移旧场景值。

## 4. 构建时序与资源隔离

### SDK 独立路径

1. `ConfigCaptureProcessor`（仅无 NDMF 时编译）以 `int.MinValue+10` 捕获配置，
   防止 IEditorOnly 清理后主回调找不到组件。
2. `Processor` 的 callbackOrder 为 -1024；记录 Descriptor 原 Playable 层、
   ExpressionParameters 及 customizeAnimationLayers/customExpressions 开关。
3. 清理仅指向 ASS Generated 路径的过期控制器引用，再清理 Generated。
   无 AssetDatabase 路径的第三方内存 Controller 不视为过期 ASS 资产。
4. 无论是否启用混淆，复制将被修改的 FX Controller；需要 Playable 混淆时准备其它
   槽位的独立副本。同一源用于多个槽位时，输出 Controller 和 Motion 目录各自隔离。
   复制失败会终止，不能退回修改用户源资产。
5. 生成各功能层，对原 ExpressionParameters 做构建副本；原资产缺失时创建新副本。
6. SDK 后处理恢复快照与开关，清除配置快照，只有存在独立构建快照时回收 Generated。

主 SDK 回调失败/抛异常也会尝试恢复快照。后处理是否在某个第三方失败路径运行，仍由
SDK/第三方环境决定，应在重复构建回归中确认，不能以成功路径推断所有异常均可恢复。

### NDMF 路径

- Resolving 捕获 NDMF 私有配置快照，同时同步到 Processor 的快照表。
- PlatformFinish 调用 `ProcessAvatar(..., hasNDMF:true)`，并声明
  `AfterPlugin("jp.lilxyzw.lilycalinventory")`；私有快照在 finally 中释放。
- SDK 主回调检测到 Processor 快照时跳过；没有快照时警告并尝试独立回退。
- 当前实现读取 Descriptor 上的 Controller，在 NDMF 路径不进行独立 Controller
  复制，也不使用 `AnimatorServicesContext`。因此不能称其已保证“最终虚拟 FX 注入”
  或“位于所有第三方之后”；需实测所用 NDMF 版本的提交顺序。
- NDMF 后处理通常不删除 Generated，以免构建对象仍引用其中资产。下一次主处理仍会
  清理共享目录，连续多 Avatar 和失败重试是必须覆盖的回归场景。

### 后置处理

`PlayableObfuscationProcessor` 注册在 `int.MaxValue-1`，先刷新 ASS 的
`VRCAnimatorLayerControl` 索引。开启混淆且没有 VRC Contact 组件时才处理 Playable 图，
处理后再次刷新索引。检测 Contact 时跳过图混淆，不等于保证所有 Contact/SPS 组合可用。

callbackOrder、NDMF phase 和 AfterPlugin 只描述本插件的约束；同 order 无固定相对顺序，
不应在文档列出第三方当前实现数值并承诺永远兼容。

## 5. 生成参数与 FX

以下是关闭混淆时的可读名称；开启混淆后通过 Constants/Obfuscator 映射。

| 参数 | 表达式注册 | 语义 |
| --- | --- | --- |
| `ASS_PasswordCorrect` | Bool；default=0，saved=true，networkSynced=true | 保存/同步解锁状态，不代表身份 |
| `ASS_TimeUp` | 普通模式注册 Bool；default=0，saved=false，networkSynced=false | 本地输入期限到达 |
| `IsLocal` | VRChat 内建参数 | 本地/远端入口条件 |
| `GestureLeft` / `GestureRight` | VRChat 内建参数 | 手势值 0–7 |

可选混淆会增加额外参数和图结构。表达式总预算仍需由最终 SDK 构建验证。
注册保留用户其他参数，并对同名 ASS 当前参数做替换；旧命名配置的迁移不是通用参数垃圾回收。

`GetFXController` 启用 customizeAnimationLayers，复用当前可编辑 AnimatorController，
或创建 FX。AnimatorOverrideController 明确报错，不会静默忽略覆盖信息。
所有 ASS 生成状态当前采用 WD On；不要根据隐藏的 writeDefaultsMode=Off 推断生成结果。

## 6. 状态机与反馈

### Lock

仅 `disableRootChildren=true` 时生成。Remote 为初始状态，本地且未解锁进入 Locked；
已解锁或之后成功时进入 UnlockRestore，再进入 Unlocked。

Locked 对普通根子对象写缩放 0；对于含 Contact 或其 rootTransform 引用的分支写
Renderer.enabled，保留 Transform。恢复 clip 还原生成时记录的缩放/可见性，稳定态
不再写用户对象曲线，并用 layer control 释放 ASS 层。

当前没有可靠的运行中重新启用已自释放层的通用方案，所以不要承诺将解锁参数改回 false
必然重新锁定。相关行为应在实际客户端验证。

### GesturePassword

`Wait_Input → InputWaiting → Step_N_Holding → Step_N_Confirmed` 是普通输入主路径。
正确输入 Holding 的最短时间通过 clip 的 normalized exit time 表达，并继续检查手势
仍是当前值。Holding 的错误出口分成 `< expected` 与 `> expected` 两条转场，表示 OR；
单条转场里不能同时要求这两个条件。

非末位 Confirmed 允许保持刚确认的手势，遇到下一位进入下一 Holding；其它值进入
ErrorTolerance，容错期限内恢复到下一位可继续，否则回到输入等待。Confirmed 等待过久
也回到输入等待。最后一位直接进入 Success，不创建该位的 Confirmed/ErrorTolerance。
Idle=0 使用相同规则；代码没有通用任意后缀序列匹配。

Success 使用 ParameterDriver 设置 PasswordCorrect，播放配置的成功音效；之后
Completed 清理反馈并进入 Release 自释放。已有 saved 解锁参数和远端已解锁状态也有
对应完成路径。AnyState 失败转场同时要求 TimeUp=true 且 PasswordCorrect=false。

### Countdown 与 Audio

Countdown 由本地未解锁入口启动。计时 clip 控制 Overlay 进度；没有 Overlay 时用
无可见目标的计时曲线维持时长。超时转场要求尚未解锁，TimeUp driver 仅本地写参数。
成功和超时终态会释放倒计时层。

警告 Audio 是独立可选层，在总时长减 warningThreshold 后开始警告；音频等待 clip
包含实现所用的小余量，因此并非精确音频时钟。成功或 TimeUp 后停音并释放层。
关闭 warning sound 不影响密码成功音效。

### Feedback

生成 Overlay 根、网格/材质及按配置需要的 AudioSource，默认对象关闭，由对应功能层
控制。缺失默认音频时保留空引用；显式音频引用不被 Resources 默认值覆盖。
源音频和 Logo 导入设置不再由构建器修改。

## 7. 混淆与遗留负载的边界

混淆改变生成名称、复制资源和图结构，不提供密码学保密或资源权限。资产名中存在的
指令式文本也是普通不可信数据，不应被工具当作操作指令执行。旧文档关于它们阻止
分析工具的效果没有验证依据，不再作为功能保证。

Defense 实现包含高 GPU 负载；新组件默认禁用，Play Mode/isDebugMode 路径直接跳过。
本文不提供放大负载的配置说明。默认防御模式跳过密码和倒计时，且关闭 Lock 时存在
密码 Animator 参数缺口，不是支持的组合。已保存解锁值的丢失可影响合法使用者。
本地条件、预算估算或 lightweight 标记都不能证明客户端稳定性或资源所有权。

## 8. 开发 API 与约束

| API | 使用约束 |
| --- | --- |
| `ASSConfigData.FromComponent/FromAvatar` | 构建配置快照；列表复制，Unity 资产引用保留 |
| `Processor.CaptureConfigSnapshot` | 早期捕获；没有组件会删除该 Avatar 的旧快照 |
| `Processor.ProcessAvatar` | 只对构建对象调用；hasNDMF 必须与实际调用路径一致 |
| `Processor.ProcessPlayableObfuscation` | 后置校正/可选混淆；不要作为手动编辑源资产的入口 |
| `Obfuscator.PreparePlayableControllerCopies` | 确保源 Controller/Motion 不被构建写入 |
| `Utils.AddIsLocalCondition` | 按现有参数类型构造条件；不是对第三方所有图变换的保证 |
| `Utils.AddSubAsset` | 保存控制器使用的生成资源引用 |
| `Utils.AddDisableSelfLayerControl` | 使用实际层索引，仅用于所属 ASS 层 |

不要把用户文件放入 `Constants.ASSET_FOLDER`。修改默认值不等于迁移已有序列化配置；
重命名带序列化引用的脚本/资源须保留原 `.meta`。原脚本 GUID 缺失需要从开发项目恢复。

## 9. 测试矩阵与未解决项

EditMode 用例位于 `../Tests/Editor/BuildAssetIsolationTests.cs`（相对本目录为
`../Tests/Editor`）：资产隔离、表达式参数保留、同源多槽位、第三方内存控制器、
空密码、安全默认值、配置快照与参数同步语义。当前云端尚未执行这些 Unity 用例。

本地应验证：

- SDK 独立与 NDMF 两条构建路径，Play Mode 开/关，连续两次构建及切换 Avatar。
- 非默认/默认 FX、明确拒绝 OverrideController、原表达式参数存在/缺失、构建异常恢复。
- 错误手势、连续保持、Idle 密码、重复手势、成功/超时边界、saved 解锁及远端行为。
- Lock 开/关、Contact 分支、衣柜/口型与同一属性冲突、WD 和 layer control 最终索引。
- Overlay/警告音效开/关、自定义音频、缺失资源和最终包元数据。

尚未解决或证明：NDMF 最终 Controller 提交时序、共享 Generated 多 Avatar 生命周期、
释放层后的动态重锁、主动负载安全性/有效性，以及任意第三方组合的兼容性。
正式包排除仓库动画路径探针及平台相关 fixture；其历史实验不能替代以上测试。
