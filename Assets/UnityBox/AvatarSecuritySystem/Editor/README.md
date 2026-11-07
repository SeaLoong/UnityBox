# Avatar Security System

ASS **0.9.0** 提供手势密码、输入倒计时、Overlay 和声音反馈。普通 Unity 在 Player 构建时
自动生成运行图；VRChat 沿用 SDK/NDMF 自动构建入口。密码结果由参数和运行时 API 提供，
角色身体、Renderer 和原有动画继续由项目自己的逻辑控制。

## 更新后旧组件显示 Missing Script

更新 ASS 到 **0.9.0**，等待 Unity 完成脚本编译。首次加载该版本时会自动恢复 `Assets`
内 Scene 和 Prefab 的旧 ASS 脚本关联；之后导入的旧资源也使用同一恢复流程。
保留原 Missing Script 组件及其中的配置，恢复会重连脚本，继续读取原密码、手侧、
音频引用和 Prefab 覆盖。旧计时配置按下方的时间约束规范化。

场景或 Prefab 有未保存修改时，先保存，再运行：

`Tools → UnityBox → Avatar Security System → Repair Legacy Components`

每次修改前，原文件按 Assets 相对路径备份到
`UserSettings/UnityBox/ASSMigration/<id>/`。已确认的旧 GUID 保存为
`UserSettings/UnityBox/ASSMigration/LegacyGuids.json`，供后续导入的 Prefab 实例继续恢复。
已有正确脚本引用和其它类型的 Missing Script 保持原关联。

原因是 0.7.24 发布 ZIP 没有携带脚本 `.meta`，Unity 在不同旧项目生成了不同 GUID。
0.8.0 加入固定 GUID 时遗漏了随机 GUID 的迁移。0.9.0 补齐这条升级路径，继续沿用
`1c51e6a139920bb4ca12700dc82627ff`，Runtime 程序集和 `ASSComponent` 类型名称保持不变。

Unity 第三方包也需要保存导入资源的 `.meta`。官方[包布局说明](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-layout.html)
明确要求 `Editor`、`Runtime`、`Tests` 及其内容包含 `.meta`；`Documentation~` 等忽略目录除外。
脚本通过其中的 GUID 被 Scene/Prefab 引用，首次安装时由 Unity 在各项目分别生成的身份不能替代
发布包内的稳定身份。本仓库发布检查现会拒绝缺少 `.meta` 的脚本或程序集。

真实发布包的三阶段升级流程、复用脚本及原始记录见
[旧包升级回归](../../../../Docs/Verification/ASSUpgrade/README.md)。

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

Inspector 使用普通构建入口，无手动 Generate 按钮。自动生成只作用于构建场景副本：
分配独立 Controller、配置运行时输入与状态行为，然后移除编辑配置。源场景继续保留
原 Controller 与 `ASSComponent`，供后续编辑。新生成状态均为 WD Off；旧 ASS 生成层
（包括遗留锁层）在副本内清理，用户层及其 WD 设置保留。

## 连续保持与防快速遍历

默认 `gestureHoldTime=0.5s`、`gestureErrorTolerance=0.3s`。保持时间必须严格大于容错时间；
Inspector、配置副本和构建器共用同一规范化规则。旧值不满足该关系时，容错会下调至保持
时间以下，例如旧的 0.15s / 0.3s 会规范为 0.15s / 0.14s。

每一位都必须连续保持正确姿势达到保持时长。切到错误姿势会中断当前计时，恢复正确
姿势后从零重新累计；容错窗口到期会清除当前序列进度。连续快速轮换 0–7 不会累计为
有效密码。重复的相邻密码位必须先切换到不同姿势，再重新保持该姿势。

超过单步最大保持/等待窗口会返回输入入口；倒计时到期和 `Restart` 优先于接受本帧手势。
密码成功后产生结果与声音/Overlay 反馈，完成状态释放 ASS 自身功能层。结果变回 false
或调用 `Restart` 后接受下一轮输入。应用依据 `IsPasswordCorrect` 处理自己的交互状态。

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

## 配置、显示与兼容

- 混淆只启用诱饵状态、关闭诱饵层时，仍声明条件使用的参数；参数池没有 Bool 时
  添加本地布尔守卫，参数类型与转移条件保持一致。
- 0.9.0 移除 Avatar 对象锁定功能和对应选项，生成器仅管理 ASS 自己的反馈对象。
- 旧 `disableRootChildren`、`writeDefaultsMode` 隐藏保留，用于读取历史序列化数据；
  新图不依据它们生成身体锁定或强制修改用户层的 WD。
- Overlay 和警告声音独立开关；显式音频引用优先，成功反馈继续按密码流程执行。
- 空密码在 Avatar 构建中表示跳过 ASS。普通构建同样保留原 Animator，仅移除构建副本的
  空配置；Editor 后端 API 要求至少一位有效密码。
- Inspector 的英语、简体中文、繁体中文和日语文本随界面语言选择显示，包含保持/容错
  的时间说明、构建校验及迁移提示。

## 测试与性能

原生 Animator 回归覆盖连续保持、快速遍历、中断后的重新计时、重复位释放、容错与
超时边界、Restart 和 WD Off。构建回归检查自动生成、既有 Controller/AOC 的用户层、
源场景配置保留以及 Avatar 对象缩放不受 ASS 影响。

`LegacyComponentMigrationTests` 用不同旧 GUID 检查 Scene、Prefab、stripped 引用和后续
导入，验证配置、组件 fileID、资源 GUID、引用、备份原字节、CRLF/BOM 及幂等。
SDK 与 NDMF 测试覆盖实际 Behaviour、参数副本、多 Avatar 生命周期和最终提交。

本版实际执行数量、运行标识和源码指纹见[测试指南](../../../../Docs/Testing.md)及
[验证结果](../../../../Docs/VerificationResults.json)。测量使用当前手势图生成和 Animator
更新场景，数据及环境见[性能报告](../../../../Docs/Performance.md)。
实现与扩展 API 见[技术参考](ASS_TechnicalDoc.md)，历史变化见[CHANGELOG](../CHANGELOG.md)。
正式包保留功能测试，排除仓库路径探针与实验 Bundle。

## 许可证

[MIT License](../../../../LICENSE)。
