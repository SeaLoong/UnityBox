# Avatar Security System (ASS)

ASS 提供独立 Unity 动画/配置核心，以及可选 VRChat 构建适配。安装 VRChat 适配依赖后，
它在 Avatar 构建时生成手势密码、可选身体锁定、倒计时与视听反馈。
它控制 Avatar 的本地使用流程，不提供资源所有权验证，也不能保证阻止缓存提取、
修改构建资源或重新上传。名称混淆与已保存的解锁参数都不是安全认证。

本文描述 `refactor` 分支当前代码；历史版本行为见 [CHANGELOG](../CHANGELOG.md)，
开发细节见 [技术参考](ASS_TechnicalDoc.md)。本轮已通过真实 Unity/SDK/NDMF 引用下的
Roslyn 类型检查；云端缺少 Unity 许可，Editor 导入、IL 后处理、EditMode 测试与 VRChat
实际运行仍未验证，不能将类型检查等同于兼容性保证。

## 安装与现有项目迁移

- 核心仅需要 Unity 2022.3；导入源码或本地 UPM 包即可使用配置、Controller/Clip 工具与核心测试。
- VRChat 上传功能另外安装 SDK Avatars ≥ 3.7.0；包清单不再自动安装 SDK。
- 在 VCC / ALCOM 添加 `https://sealoong.github.io/UnityBox/vpm.json`，安装
  `top.sealoong.unitybox.avatar-security-system`。
- NDMF 为可选集成，需要 SDK 和 NDMF ≥ 1.8.0（使用 `PlatformFinish`）。缺少其中任一依赖时，
  对应适配装配不参与编译；纯 Unity 核心仍可用。旧 NDMF 不会禁用 SDK 独立回退。
- 源码安装时保留 `.meta`。当前仓库缺少部分原始元数据，包括
  `Runtime/Component.cs.meta`；需要从原开发项目恢复，不能用新 GUID 修复旧场景引用。

新建或 Reset 的组件默认 `disableDefense=true`、`defaultEnableDefense=false`。
已有场景中的序列化配置不会自动变成新默认值，升级后应检查原配置。

## 无 SDK 的功能范围

`Runtime` 中的配置与路径/Bundle 探针只引用 Unity；`Editor` 中的配置快照、动画参数与
转场工具、Clip/Controller 资源管理及图构造不依赖 VRChat。仓库探针的菜单位于
`Tools/UnityBox/ASS`；实验脚本不随正式 VPM 包发布。

手势、计时和锁定图调用方必须显式传入 `IAnimatorBehaviourAdapter`，提供状态进入时的
参数赋值、音频和层权重操作；不传会立即报错。包内可用实现是可选的
`VrcAnimatorBehaviourAdapter.Instance`，由上传处理器注入。核心没有新增通用 Unity
密码运行时，也不会在缺少 SDK 时输出省略关键行为却声称可用的控制器。

普通 Unity 项目可直接调用 `ASSConfigData.FromComponent`、`Utils.CreateLayer`、
`Utils.CreateTransition`、`Utils.AddIsLocalCondition` 等 API。`IsLocal` 和手势在核心中是
普通 Animator 参数；不提供网络或手势输入服务。完整 Avatar 功能请按下方 VRChat 流程使用。

## 推荐使用流程（VRChat）

1. 在带 `VRCAvatarDescriptor` 的 Avatar 根添加
   `UnityBox/Avatar Security System (ASS)`。构建器也会查找子对象中的组件，但只读取
   找到的第一份配置，因此每个 Avatar 只保留一个 ASS 组件。
2. 设置 `gesturePassword` 和 `useRightHand`；新组件默认序列为 `[1, 2, 3, 4]`，左手。
3. 按需要启用 `disableRootChildren`（Inspector 中的 Avatar Lock）。此字段为 true
   表示生成锁定层，而不是禁用整个 ASS。
4. 保持 `disableDefense=true`、`defaultEnableDefense=false`，先测试密码、锁定与反馈。
5. 若需要 Unity Play Mode 预览，启用 `enabledInPlaymode`。预览仍需要实际驱动
   `GestureLeft`/`GestureRight` 和 `IsLocal` 的测试环境；只进入 Play Mode 不会自动模拟 VRChat。
6. 在测试 Avatar 上运行 SDK/NDMF 构建，检查 Console 和最终 FX，再进行游戏内验证。

普通模式下，将密码列表留空会跳过主功能生成；仅取消 MonoBehaviour 的 enabled
开关并不是当前实现支持的卸载方法。移除组件或恢复未生成的项目备份更明确。

## 手势与计时

| 值 | 手势 | 值 | 手势 |
| --- | --- | --- | --- |
| 0 | Idle | 4 | Victory |
| 1 | Fist | 5 | RockNRoll |
| 2 | HandOpen | 6 | HandGun |
| 3 | Fingerpoint | 7 | ThumbsUp |

- `gestureHoldTime` 是每个正确手势在 Holding 状态连续保持的最短时间，默认 0.15 秒。
  Holding 中离开当前值会回到输入等待，不能把多次短暂命中累计为一次保持。
- 非最后一位确认后等待下一位。`gestureMaxHoldTime` 控制 Confirmed 状态的等待超时，
  默认 3 秒；它不是跨所有状态累计计算的全局手势保持时间。
- Confirmed 中既不是刚确认的手势，也不是下一位时进入 ErrorTolerance；
  `gestureErrorTolerance` 默认 0.3 秒，及时切回下一位可继续，否则回到输入等待。
- Idle(0) 是普通可选密码值，不会在所有状态中被特殊忽略。
- 当前状态机没有通用的任意后缀匹配算法；不要依赖任意错误前缀都能无缝接到密码。
- `countdownDuration` 默认 30 秒，Inspector 范围为 10–30 秒。倒计时只在本地且尚未
  解锁时开始。已正确解锁不会再进入超时失败状态。
- 隐藏字段 `warningThreshold` 默认 10 秒，`OnValidate` 将它限制为不超过倒计时的一半，
  因此短倒计时不一定在最后 10 秒报警。

成功后会设置已保存且同步的解锁 Bool。再次加载时，已有 true 值可以直接跳过密码。
它可能因客户端保存状态变化而丢失，也可以被拥有构建资源的人修改，不能区分资源所有者。
Inspector 的 Weak/Medium/Strong 仅按长度和不同手势数量分类，不是安全强度评估。

## 功能与配置

| 字段 | 新组件默认值 | 当前行为 |
| --- | --- | --- |
| `gesturePassword` | `[1, 2, 3, 4]` | 允许 0–7；普通模式空列表跳过生成，越界值中止生成 |
| `useRightHand` | false | false 使用 GestureLeft，true 使用 GestureRight |
| `gestureHoldTime` | 0.15 | 连续正确 Holding 最短秒数 |
| `gestureMaxHoldTime` | 3 | 非最终步骤确认后的等待上限 |
| `gestureErrorTolerance` | 0.3 | Confirmed 后错误输入的恢复窗口 |
| `countdownDuration` | 30 | 普通模式的总输入期限 |
| `disableRootChildren` | true | 生成本地身体锁定层 |
| `disableOverlay` | false | true 时不生成全屏遮罩，密码逻辑仍运行 |
| `disableWarningSound` | false | true 时不生成警告音频层/对象，成功反馈独立 |
| `enabledInPlaymode` | false | false 时跳过 Play Mode 构建生成 |
| `disableDefense` | true | 跳过主动负载生成 |
| `defaultEnableDefense` | false | 遗留高级模式；跳过密码与倒计时，依赖已保存参数，不推荐作为验证方式 |
| `lightweightDefense` | false | 遗留负载配置；不是性能或客户端稳定性保证 |
| `disableObfuscation` | false | 禁用或启用名称/生成资源混淆 |
| `enablePlayableLayerObfuscation` | true | 允许后置处理 Playable Controller；检测 Contact 时跳过 |
| `enableDecoyLayers` / `enableDecoyStates` | true / true | 可生成额外混淆层/状态，增加图规模 |
| `writeDefaultsMode` | On | 隐藏兼容字段；当前生成器统一使用 WD On，旧值在 OnValidate 中归一化 |

### 身体锁定

本地尚未解锁时，普通根子对象通过缩放隐藏。包含 VRC Contact 组件或相关 rootTransform
引用的分支改为关闭 Renderer，避免整体缩放 Contact 结构。ASS 自身反馈对象不作为身体隐藏。

解锁先经过一次性的 `UnlockRestore`，再进入不写用户对象曲线的稳定状态，并自释放
对应 ASS FX 层权重。该设计需要与最终合并后的 FX、WD 策略及衣柜/Contact 插件共同验证；
不能保证任意第三方状态机重排都兼容。当前实现也不能承诺主动重锁已经释放的层。

### 视听反馈

PasswordInput 层负责普通输入流程中的 Overlay 与成功音效；Countdown 负责进度，
Audio 层负责最后一段时间的警告音效。关闭 Lock 不会关闭这些功能。
`warningBeep`、`successSound` 为隐藏字段；为空时才从 Resources 取默认资源，构建不再
更改源音频或 Logo 的导入设置。缺少 Shader/资源可能产生降级或缺失反馈，应检查日志。

### 主动负载与遗留默认模式

Defense 代码仍包含可能显著影响客户端的 GPU 负载。本轮没有增强该机制；Play Mode
直接跳过主动负载生成，即使 `enabledInPlaymode` 为 true 也只预览其他功能。
`IsLocal` 条件是运行逻辑，不是对其他玩家或合法使用者完全无影响的保证。

`defaultEnableDefense=true` 在 OnValidate 时会强制取消 `disableDefense`，并跳过密码与
倒计时。它与关闭 Avatar Lock 的组合仍有密码 Animator 参数注册缺口，不作为支持组合。
不要把已保存参数、名称混淆或负载是否触发解释为资源所有权验证。

## 构建与资源生命周期

- 每次 SDK 构建都由早期回调重新捕获配置并清除上次完成标记，主处理以 `callbackOrder=-1024` 执行。
- `ASSComponent` 在检测到 `com.vrchat.base >= 3.7.0` 时保留真实 SDK `IEditorOnly` 标记，
  避免 SDK 在 preprocess 之前的非法组件检查；无 SDK 时没有该类型依赖。可选 SDK 最终回调
  仍显式清理构建克隆配置，NDMF 在 RemoveEditorOnly 前捕获并清理。源配置与序列化字段保留。
- NDMF 在 Resolving 捕获配置，在 PlatformFinish 生成主功能，并声明位于
  `jp.lilxyzw.lilycalinventory` 之后。只有该 Avatar 的 NDMF 生成成功完成，SDK 主回调才跳过。
  仅捕获配置不算完成；安装 NDMF 但关闭 ApplyOnBuild/ApplyOnPlay 时仍走 SDK 独立回退。
  状态按弱对象引用保存，失败/后处理会清理，每次新构建重新开始。
- Playable 混淆的 SDK 回调位于 `int.MaxValue-1`。这些数值只是本插件注册顺序，
  不保证所有版本的 VRCFury/NDMF/其他插件都在预期位置执行。
- 独立构建复制需要修改的 Controller 和 ExpressionParameters，并保存 Descriptor
  原引用/开关；后处理恢复引用，再回收生成目录。无资产路径的第三方内存 Controller 不会被当成过期产物清空。
- NDMF 与独立路径都会复制将被修改的持久化 Controller；不会因为安装 NDMF 就假定
  上游已经克隆了它们。晚期钩子新插入的持久化 Controller 也先复制，再校正索引/处理名称。
  内存 Controller 仍由调用方管理。ASS 不强制启用 AnimatorServices；提交时序和连续多
  Avatar 构建仍需实测。
- 输出位于 `Assets/UnityBox/AvatarSecuritySystem/Generated`。这是共享临时目录，
  不应保存用户资产。NDMF 后处理不立即删除它，以免构建对象仍持有引用。
- Descriptor 原本关闭自定义动画层时，残留的 base/special Controller 不参与复制或处理。
  生成需要启用 FX 时，先将构建克隆的旧槽位恢复默认语义，避免顺便启用残留 Gesture/Action 等层；
  SDK 独立路径的原引用和关闭开关仍由快照恢复。缺少 FX 槽位时会补建。
- 复制包含多个 Controller 的同一资产文件时按 local file ID 选择目标，不再取文件中的第一个。
  Controller/Motion 复制或解析失败会中止，不能回退并递归修改源 BlendTree。
- FX AnimatorOverrideController 当前明确报错，应先在上游得到实际 AnimatorController。

这些行为以源码为准，不构成“编辑时零影响”或“始终自动清理”的承诺。应保留项目备份，
检查连续构建后原始控制器、表达式参数及其它 Avatar 引用是否保持完整。

## 测试与排查

- 无 SDK：`Tests/Editor/AnimationCoreTests.cs`，装配
  `UnityBox.AvatarSecuritySystem.Editor.Tests`，覆盖配置复制、输入类型、Holding 出口/时间、
  本地超时请求、实际层索引、缺少适配器时拒绝生成及保存 Controller 的副本隔离。
- 有 SDK：`Tests/Editor/VRChat/BuildAssetIsolationTests.cs`，装配
  `UnityBox.AvatarSecuritySystem.VRChat.Tests`，另覆盖表达式参数、上传资源隔离、
  SDK Behaviour 配置、组件清理、NDMF 实际完成判定、失败重试及晚期控制器隔离。
  当前声明 15 个核心与 27 个 SDK 用例；没有 SDK 时不编译 SDK 集成用例。

安装 Unity Test Framework 后在 Test Runner 的 EditMode 运行；UPM/VPM 安装时在项目
manifest 的 `testables` 中加入 `top.sealoong.unitybox.avatar-security-system`。
核心图测试的记录适配器只验证生成器请求，不冒充 SDK，也不证明 VRChat 运行时表现。
Unity 编译/测试结果应以实际项目的 Test Runner 报告为准。

建议按以下顺序排查：

1. 检查 Console 编译错误、VRChat/NDMF 版本、组件与脚本 GUID。
2. 保持主动负载关闭，检查密码、手势源、IsLocal，以及最终 FX 是否包含 ASS 层。
3. 测试错误输入、连续保持、成功与超时边界、已保存解锁和远端状态。
4. 连续构建两次并更换 Avatar，确认原资产引用不变；含 Contact/衣柜时检查解锁后控制权。
5. 若仅第三方组合失败，记录完整依赖版本与最终 FX，不根据固定 callbackOrder 推断插件已运行。

ASS 动画路径探针属于仓库实验，正式包不包含其脚本、固定 GUID fixture 或二进制。
其历史结果只能说明特定 AssetBundle 与字符串替换行为，不能证明通用防热上传。

## 许可证

[MIT License](../../../../LICENSE)。
