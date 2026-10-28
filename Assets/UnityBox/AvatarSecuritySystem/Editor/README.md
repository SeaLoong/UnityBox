# Avatar Security System

ASS 为角色提供手势密码、身体显示锁、输入倒计时、Overlay 和声音反馈。普通 Unity 使用
原生 Animator 后端；VRChat 使用 SDK Behaviour 后端；两条入口共享同一套图生成器。

## 普通 Unity 使用

1. 给角色根对象添加 `Avatar Security System (ASS)`，配置密码、使用的手、连续保持时长和倒计时。
2. 点击 Inspector 的 **Generate Standalone Unity Controller**，选择 Assets 内的输出文件。
3. 生成器创建并分配 Animator Controller，同时配置 `StandaloneSecurityInput`。
4. 输入脚本通过下面的 API 设置手势；0–7 的数值对应 Idle、Fist、HandOpen、Fingerpoint、
   Victory、RockNRoll、HandGun、ThumbsUp。

```csharp
var input = avatar.GetComponent<UnityBox.AvatarSecuritySystem.StandaloneSecurityInput>();
input.SetGesture(1);                 // 默认左手；右手传 rightHand: true
bool unlocked = input.IsUnlocked;
bool timedOut = input.HasTimedOut;
input.Restart();                     // 重新锁定、重置计时并接受新的密码输入
```

每一位必须连续保持 `gestureHoldTime`。短暂错误输入会重置当前保持；确认后按配置的
容错时长接收下一位。成功后恢复身体显示，播放声音并释放功能层的权重。独立的空曲线
生命周期层继续运行：解锁参数变回 false 时重新启用密码、锁定和计时层；Restart 也可从
超时终态开始新一轮。解锁期间，身体缩放重新由用户自己的动画层控制。

生成的普通 Unity Controller 默认 `IsLocal=true`。应用可显式驱动 `IsLocal`，并通过
`GestureLeft`/`GestureRight` 提供整数输入。`StandaloneSecurityInput` 保存实际生成的参数名，
开启名称混淆时仍可使用同一 API。独立 Unity 入口生成手势工作流，并保持遗留主动负载关闭。

Editor 脚本入口：

```csharp
var config = ASSConfigData.FromComponent(component);
var controller = StandaloneSecurityBuilder.Build(avatar, config, "Assets/MyAvatar/ASS.controller");
animator.runtimeAnimatorController = controller;
```

`Build` 使用配置副本，保留调用方的列表、音频引用和配置值。输出控制器、空 Clip 与生成
状态均持久化为资源，适用于 Editor 手动求值及实际 Player 构建。Unity 构建场景处理器只在
构建副本中移除 ASSComponent，保留 Animator、StandaloneSecurityInput 与状态进入行为；
源场景的配置继续用于编辑和重新生成。

## VRChat 使用

安装真实 SDK Base/Avatars ≥3.7.0，在 Avatar 根配置 ASS，然后运行 SDK 构建。配置在早期
捕获，生成阶段创建 FX、参数和反馈对象，构建克隆中的配置组件随后被移除。
NDMF ≥1.8 的可选适配在 Resolving 捕获配置、PlatformFinish 生成结果；成功完成后 SDK
回调复用该次结果。新建及 Reset 的组件默认关闭遗留主动负载。

| 表达式参数 | 保存 | 网络同步 | 作用 |
| --- | --- | --- | --- |
| `ASS_PasswordCorrect` | 是 | 是 | 解锁状态 |
| `ASS_TimeUp` | 否 | 否 | 本地超时状态 |
| `ASS_Restart` | 否 | 否 | 本地重新开始请求；生命周期层消费后清零 |

`IsLocal` 和两只手的 Gesture 参数使用平台输入。成功、计时和层权重行为由真实 SDK
ParameterDriver、PlayAudio、AnimatorLayerControl 生成。生命周期层处理解锁后的重新
锁定；最终层索引刷新同时维护自释放控制与指向兄弟功能层的重新启用控制。

每个 Avatar 使用独立的 `Generated/Build_<id>` 目录；处理另一个 Avatar 或重试构建只
更新自己的输出。SDK 后处理恢复原 Descriptor、Controller、参数和开关，并回收该次 SDK
目录。NDMF 输出目录跟随构建结果保留，连续处理多个 Avatar 时原输出引用继续有效。

FX 支持 AnimatorController、内存 Controller 和 AnimatorOverrideController。Override
按 Clip 对象身份展开，保留重名 Clip、交换覆盖与嵌套图；生成结果拥有独立状态图，复制
外部 Motion 后再处理。原 Controller、Clip、BlendTree 和表达式参数保持原有内容。

## 配置与显示

- `disableRootChildren=true` 生成身体显示锁。普通根分支写缩放；含 VRC Contact 或其
  rootTransform 的分支只切换 Renderer，保留 Contact 所依赖的 Transform。
- Lock 只写自己负责的缩放/Renderer 属性；Overlay、进度和音频由各自功能层管理。
- Overlay 与警告音效独立开关。关闭警告音效仍保留密码成功声音；显式音频引用优先。
- 空密码用于关闭普通密码生成功能；移除组件可从构建配置中移除 ASS。
- 原场景配置脚本 GUID **1c51e6a139920bb4ca12700dc82627ff** 已从仓库场景三处 ASS
  序列化块恢复到 `Runtime/Component.cs.meta`。保留该 metadata 即可延续原场景引用。
- Editor 脚本、程序集及包目录补齐固定 metadata，导入不同工程时保持相同资源标识。

## 测试与性能

`StandaloneRuntimeTests` 使用真实 Animator.Update 验证连续保持、错误输入、完整序列、
解锁、功能层释放、二次锁定与输入、超时/Restart，以及解锁后用户动画控制权。另检查
原 GUID 的 MonoScript 类型、持久化图、重名 Clip 的交叉 Override 和内存 Controller 副本。
`GenerationLifecycleTests` 覆盖多 Avatar、同 Avatar 重试、SDK 回收与兄弟层控制索引。
`NdmfFinalizationTests` 执行真实插件调度和最终提交，同时保留上游虚拟编辑、ASS 生成层及
下游虚拟编辑，并检查最终原生动画输出与原资源内容。

Unity 2022.3.22f1、SDK 3.8.2、NDMF 1.11.0 的完整功能运行通过 **57/57**：核心 26、
SDK 30、NDMF 最终提交 1，运行 ID 为 `20261028T185613-8ad4eb53`。

性能测试用 16、64、256 个根子对象生成锁定/恢复图，先预热，再采集 7 个样本，输出
中位数、P95、最小/最大耗时与可用的托管分配数据。Contact 分支一次性建立索引，生成
锁定与恢复 Clip 时复用该索引，替代按每个根子对象重复扫描整棵 Avatar 的处理。

实际范围、运行 ID 与原生结果见 [测试指南](../../../../Docs/Testing.md)，测量数据与硬件
条件见 [性能记录](../../../../Docs/Performance.md)。实现结构见 [技术参考](ASS_TechnicalDoc.md)，
版本变化见 [CHANGELOG](../CHANGELOG.md)。正式包保留功能测试，排除仓库路径探针与实验 Bundle。

## 许可证

[MIT License](../../../../LICENSE)。
