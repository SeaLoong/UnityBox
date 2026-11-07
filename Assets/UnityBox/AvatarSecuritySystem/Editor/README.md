# Avatar Security System

Avatar Security System（ASS）提供手势密码验证、倒计时提示、Overlay 和声音反馈，
并支持 VRChat 防御配置。可以按需要调整密码、计时和反馈选项，通过参数或运行时 API
读取密码结果。普通 Unity 与 VRChat 分别在各自构建流程生成运行图，原有角色动画
继续由项目控制。当前版本为 **0.9.6**。

## 升级与已有项目

0.9.6 保持移除旧启动/导入迁移回调，提供本包自带的 Tools 升级命令。打开项目、安装或导入包、
加载 Scene/Prefab 时，ASS 不扫描或重写项目中的脚本 GUID、场景、Prefab 与已保存配置。
组件加载也不再通过 `OnValidate` 自动改写已有时值；生成时只规范化配置副本。
新组件与用户显式执行 Reset 使用默认保持 0.5s、容错 0.3s。

从仍含旧启动/导入回调的版本通过 VCC/ALCOM 完整更新后，重新启动 Unity 一次，使旧版 Editor 回调完全卸载。
如果手动覆盖源码，还需移除旧包中已删除的 `Editor/LegacyComponentMigration.cs`、
`Tests/Editor/LegacyComponentMigrationTests.cs` 及两者的 `.meta`，避免旧代码残留。
保留 `Runtime/Component.cs.meta` 和用户 Scene/Prefab 及其 metadata。

0.9.0 的启动/导入 GUID 改写和自动场景重载已撤回。已有组件显示 Missing Script 或
invalid script 时，先读取该组件的 `m_Script` GUID，核对其脚本路径、所属包与 Console
首条编译错误，再针对该引用处理。

若要调查 0.9.0 已发生的改写，保留项目当前副本及
`UserSettings/UnityBox/ASSMigration/<id>/Assets/...`。这些备份是相应轮次写入前的 Scene/Prefab
原字节，只覆盖当轮被改写的文件，不包含原脚本 `.meta`、包版本或后来保存的修改。
`LegacyGuids.json` 仅记录旧身份。需要恢复时先逐文件比较，再由用户明确选择目标；
本版不会自动恢复、覆盖或删除这些备份，也不自动重连 Missing Script。

0.7.24 发布 ZIP 没有携带脚本 `.meta`，Unity 在不同旧项目生成了不同 GUID；0.8.0 起使用
固定 GUID `1c51e6a139920bb4ca12700dc82627ff`。本版继续保留这个已发布身份和
`ASSComponent` 类型，已有项目的脚本引用继续保留。
Unity 第三方包需要保存导入资源的 `.meta`，官方[包布局说明](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-layout.html)
说明了 Editor/Runtime/Tests 的资源身份要求；发布检查拒绝缺少 `.meta` 的脚本或程序集。

0.9.1 的导入保全检查保存在[ASS 导入安全记录](../../../../Docs/Verification/ASSImportSafety/README.md)。
0.9.0 的旧自动迁移复现仅作为[历史归档](../../../../Docs/Verification/ASSUpgrade/README.md)保留。

## 升级组件引用

点击 `Tools/UnityBox/ASS/Upgrade Component References` 并确认一次，命令扫描整个 Assets，
自动匹配 ASSComponent 旧引用，完成备份、重连并输出 Console 汇总与批次报告。
无需选择资源、逐 GUID 映射或单独恢复面板；普通 ASS 配置继续使用组件 Inspector。
未知及其它类型保持原状，已识别类型的重复组件保留并报告。必要时只暂时卸载干净已保存 Scene/干净 Prefab
Stage，完成后恢复；dirty/untitled 及依赖保留并跳过，不自动保存。

本包备份保存在
`UserSettings/UnityBox/AvatarSecuritySystem/ComponentReferenceMigration/<id>/Assets/...`。
完整操作见[组件引用手册](../../../../Docs/ComponentReferences.md)，
当前交互与版本化基础记录见[验证说明](../../../../Docs/Verification/ComponentReferences/README.md)。

## 普通 Unity 自动构建

1. 在角色根对象添加 `Avatar Security System (ASS)`，配置密码、手侧、保持时长和倒计时。
2. 保存 Scene，按正常 Unity 流程构建 Player。构建场景处理器自动复制角色现有 Controller
   或 OverrideController，保留用户图与 WD 设置，再加入 ASS 的手势、计时和反馈层。
3. 运行时通过自动加入的 `StandaloneSecurityInput` 提供手势输入和查询密码结果。

```csharp
var input = avatar.GetComponent<UnityBox.AvatarSecuritySystem.StandaloneSecurityInput>();
input.SetGesture(1);                 // 默认左手；右手传 rightHand: true
bool accepted = input.IsPasswordCorrect;
bool timedOut = input.HasTimedOut;
input.Restart();                     // 清除本轮结果与计时，重新接受输入
```

手势数值 0–7 对应 Idle、Fist、HandOpen、Fingerpoint、Victory、RockNRoll、HandGun、ThumbsUp。
生成后普通 Unity 的 `IsLocal` 默认为 true；应用也可直接驱动 Animator 的
`IsLocal`、`GestureLeft`、`GestureRight` 参数。输入组件保存实际生成的参数名，名称混淆时
继续使用同一 API。`IsUnlocked` 保留为 `IsPasswordCorrect` 的兼容别名，只表示密码结果。

Inspector 保留插件介绍与字段说明，不再显示新增的顶部自动构建 HelpBox，也无手动 Generate 按钮。自动生成只作用于构建场景副本：
分配独立 Controller、配置运行时输入与状态行为，然后移除编辑配置。源场景继续保留
原 Controller 与 `ASSComponent`，供后续编辑。新生成状态均为 WD On；旧 ASS 生成层
（包括遗留锁层）在副本内清理，用户层及其 WD 设置保留。

## Write Defaults 与表情复位

0.9.6 将 ASS 新生成状态统一设置为 **WD On**：密码输入、保持、成功、计时、音频、
生命周期、释放、防御、混淆、诱饵以及新建默认 Idle 都采用同一策略。生成器直接使用
On，不按原 FX 的模式匹配，也不增加选择项。用户已有状态的 On/Off 设置继续保留。

原 WD On 表情图可通过空闲状态恢复 BlendShape 默认值。修复前的实际 Unity 对照中，
表情权重从 0 变为 85 后切回空 Neutral，追加 ASS WD Off 层会使其停留在 85；ASS 新状态
使用 On 后恢复为 0。动画路径属于 ASS 并不能隔离同一 Animator 中的默认值恢复行为。

更新插件后重新发起正常构建，ASS 在生成副本中清理旧生成层并建立新的 WD On 状态。
源 Controller、Scene/Prefab 和已有用户状态不在导入或加载时转换。隐藏的历史
`writeDefaultsMode` 值保留为序列化数据，新状态始终使用上述策略。

表情对照、原图保全及功能流程的执行记录见
[WD On 验证](../../../../Docs/Verification/ASSWriteDefaultsOn/README.md)。

## 连续保持与防快速遍历

默认 `gestureHoldTime=0.5s`、`gestureErrorTolerance=0.3s`。保持时间必须严格大于容错时间；
生成器在独立配置副本中应用时间规则；已保存字段在打开、导入和加载时保持原值。
例如保存的 0.15s / 0.3s 在构建副本中使用 0.15s / 0.14s，源组件仍保留原设置。

每一位都必须连续保持正确姿势达到保持时长。切到错误姿势会中断当前计时，恢复正确
姿势后从零重新累计；容错窗口到期会清除当前序列进度。连续快速轮换 0–7 不会累计为
有效密码。重复的相邻密码位必须先切换到不同姿势，再重新保持该姿势。

超过单步最大保持/等待窗口会返回输入入口；倒计时到期和 `Restart` 优先于接受本帧手势。
密码成功后产生结果与声音/Overlay 反馈，完成状态释放 ASS 自身功能层。结果变回 false
或调用 `Restart` 后接受下一轮输入。应用依据 `IsPasswordCorrect` 处理自己的交互状态。

成功或超时退出计时后，WD On 使进度属性恢复默认值；超时会关闭 Overlay 与音频对象。
重新调用 `Restart` 后，倒计时和警告音层优先回到本地起点，下一轮重新计时并在警告阶段
恢复提示音。0.9.6 同时修复了原有超时退出转场被 Unity 忽略、导致第二轮停止计时的问题；
两轮超时与重启、进度及声音恢复纳入原生回归。

## VRChat 自动构建

安装真实 SDK Base/Avatars ≥3.7.0，在 Avatar 配置 ASS，然后正常运行 SDK 构建。
配置在早期捕获，生成阶段复制 FX、参数和反馈对象，构建克隆中的配置随后移除。
NDMF ≥1.8 的可选适配在 Resolving 捕获配置、PlatformFinish 生成；成功完成后 SDK
回调复用该次结果。普通 Unity 自动构建与 Avatar 构建各用对应入口。

| 表达式参数 | 保存 | 网络同步 | 作用 |
| --- | --- | --- | --- |
| `ASS_PasswordCorrect` | 是 | 是 | 密码结果 |
| `ASS_TimeUp` | 否 | 否 | 本地超时状态 |
| `ASS_Restart` | 否 | 否 | 新一轮输入请求，由生命周期层消费后清零 |

`IsLocal` 和两只手的 Gesture 使用平台输入。参数、声音和层权重由真实 SDK
ParameterDriver、PlayAudio、AnimatorLayerControl 执行。每个 Avatar 使用独立的
`Generated/Build_<id>`；SDK 后处理还原原 Descriptor、Controller 和参数，NDMF 输出跟随
构建结果保留。原 Controller、Clip、BlendTree 及表达式参数保持各自资产身份。

## 音频与上传

0.9.5 为 `PasswordSuccess`、`CountdownWarning`、`StepSuccess` 和 `InputError` 四个自带
MP3 提供固定的 AudioImporter metadata，并启用 **Load In Background**。默认生成流程使用
前两种音频；更新包后重新发起正常构建即可，音频内容保持。

VRC SDK 会检查 Controller 状态行为引用的音频；`Decompress On Load` 且未启用后台加载
会阻止上传。旧包没有附带上述音频的导入设置，Unity 的默认设置正好触发这一条件。

| 选项 | 关闭的内容 | 仍保留的反馈 |
| --- | --- | --- |
| 关闭警告音效 | 倒计时警告音 | 密码成功音、未单独关闭的覆盖界面 |
| 关闭覆盖界面 | 全屏覆盖画面 | 密码成功音、未单独关闭的倒计时警告音 |

两个选项同时启用时，密码成功音仍然生成，因此它也需要符合 SDK 的音频导入要求。
本版保留上述开关含义，不新增音频开关。

通过脚本指定自定义音频时，若该音频使用 **Decompress On Load**，请在 AudioClip Inspector
启用 **Load In Background** 并应用，或使用 SDK 对应错误的 **Fix**。ASS 不扫描或修改其它
用户音频。组件引用升级菜单处理脚本引用，不用于修改音频设置。

音频导入、实际 SDK 检查及 NDMF 最终输出的记录见[音频上传验证](../../../../Docs/Verification/ASSAudioUpload/README.md)。

## 配置、显示与兼容

- 混淆只启用诱饵状态、关闭诱饵层时，仍声明条件使用的参数；参数池没有 Bool 时
  添加本地布尔守卫，参数类型与转移条件保持一致。
- 0.9.0 移除 Avatar 对象锁定功能和对应选项。普通密码/反馈层控制 ASS 自身反馈对象；
  可选防御分支沿用其对象生成与已有 Light 复用流程。
- 旧 `disableRootChildren`、`writeDefaultsMode` 隐藏保留，用于读取历史序列化数据；
  新图不依据它们生成身体锁定或强制修改用户层的 WD。
- Overlay 和倒计时警告音使用独立开关；密码成功音保留。显式音频引用优先，导入要求见上文。
- 空密码在 Avatar 构建中表示跳过 ASS。普通构建同样保留原 Animator，仅移除构建副本的
  空配置；Editor 后端 API 要求至少一位有效密码。
- Inspector 的英语、简体中文、繁体中文和日语文本随界面语言选择显示，包含保持/容错
  的时间说明和构建校验。

## 测试与性能

原生 Animator 回归覆盖连续保持、快速遍历、中断后的重新计时、重复位释放、容错与
超时边界、Restart、WD On 及表情空闲复位。构建回归检查自动生成、既有 Controller/AOC 的用户层、
源场景配置保留以及 Avatar 对象缩放不受 ASS 影响。

导入保全回归以旧 GUID、已有配置与 Scene/Prefab 为输入，检查导入和打开不会替换身份、
修改原文件或自动创建迁移记录；构建副本的时间规范化与源字段保持分别检查。
SDK 与 NDMF 测试覆盖实际 Behaviour、参数副本、多 Avatar 生命周期和最终提交。

0.9.6 的生成状态与表情复位见[WD On 验证](../../../../Docs/Verification/ASSWriteDefaultsOn/README.md)。
0.9.5 的音频上传检查见[音频上传验证](../../../../Docs/Verification/ASSAudioUpload/README.md)。
0.9.4 的历史组件引用执行范围与结果见[组件引用验证记录](../../../../Docs/Verification/ComponentReferences/README.md)。
[0.9.1 导入安全记录](../../../../Docs/Verification/ASSImportSafety/README.md)保留该历史版本的范围。
[测试指南](../../../../Docs/Testing.md)提供重跑命令；[验证结果](../../../../Docs/VerificationResults.json)
与[性能报告](../../../../Docs/Performance.md)记录对应版本、范围和内容指纹。
实现与扩展 API 见[技术参考](ASS_TechnicalDoc.md)，历史变化见[CHANGELOG](../CHANGELOG.md)。
正式包保留功能测试，排除仓库路径探针与实验 Bundle。

## 许可证

[MIT License](../../../../LICENSE)。
