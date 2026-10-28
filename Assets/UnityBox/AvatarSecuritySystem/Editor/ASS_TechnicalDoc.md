# Avatar Security System 技术参考

ASS 将配置、图生成、原生 Unity 运行、VRChat 行为和构建资产生命周期分别组织为独立模块。
[用户指南](README.md)包含 Inspector 与输入 API 的使用流程。

## 结构与装配

| 模块 | 职责 |
| --- | --- |
| Runtime/Component.cs | 角色配置；SDK Base 可用时实现真实 IEditorOnly |
| Runtime/AnimatorStateEntryActions.cs | 原生 Animator 状态进入时设置参数、层权重并播放声音 |
| Runtime/StandaloneSecurityInput.cs | SetGesture、IsUnlocked、HasTimedOut、Restart 的运行时接口 |
| ASSConfigData.cs | 配置捕获和独立列表副本 |
| StandaloneConfigurationStripper.cs | Unity 构建场景副本中移除 ASSComponent，保留运行驱动与源配置 |
| StandaloneSecurityBuilder.cs | 生成普通 Unity 可持久化 Controller 和输入组件 |
| UnityAnimatorBehaviourAdapter.cs | 将生成请求转为原生 StateMachineBehaviour |
| VRChat/VrcAnimatorBehaviourAdapter.cs | 将同一请求转为 SDK Behaviour |
| Lock/GesturePassword/Countdown/Feedback | 身体显示、手势、期限、警告与视听资源 |
| AnimatorLifecycle.cs | 解锁后重启及超时重试；恢复已释放功能层 |
| Utils.cs | 参数/转场、独立层创建、图持久化与资源工具 |
| ControllerGraphCopy.cs | 内存图复制与 OverrideController 展开 |
| Obfuscator.cs | 名称映射、Motion 副本与每次构建的上下文捕获/恢复 |
| VRChat/Processor.cs | Descriptor、参数、功能生成、独立目录及 SDK 还原 |
| NDMF/NDMFPlugin.cs | Resolving 捕获与 PlatformFinish 生成 |

核心 Editor/Runtime 装配引用 Unity；VRChat 与 NDMF 使用可选适配装配。SDK 门槛为
Base/Avatars 3.7，NDMF 适配为 1.8。普通 Unity 使用 UnityAnimatorBehaviourAdapter，
SDK 路径使用 VrcAnimatorBehaviourAdapter；第三方平台可实现 IAnimatorBehaviourAdapter
及 IAnimatorLayerWeightAdapter 接入状态进入操作与层权重操作。

## 生成图和运行周期

所有生成层显式设置 `syncedLayerIndex=-1`，分别运行自己的状态机。中性 Base Layer 在
最前，功能层依次追加；生命周期层不绑定用户属性，使用 WD Off。功能层使用 WD On，
完成恢复/反馈后通过层权重归零释放输出。

| 层 | 主路径 | 进入操作 |
| --- | --- | --- |
| Lock | Remote → Locked → UnlockRestore → Unlocked | 完成后释放自身 |
| Password | Wait → InputWaiting → Holding → Confirmed/下一位 → Success → Completed → Release | Success 写 PasswordCorrect |
| Countdown | Remote → Countdown → TimeUp/Unlocked → Release | TimeUp 本地写超时状态 |
| Audio | Remote → Waiting → WarningBeep → Stop → Release | 进入警告状态播放声音 |
| Lifecycle | Armed ↔ Unlocked；Restart 可回到 Armed | 重置 TimeUp/Restart，启用功能层 |

Holding Clip 长度等于 maxHold；确认转场的 normalized exit time 为 hold/maxHold，且继续
检查当前手势。错误输入使用两条转场表达小于或大于，连续保持从头重新计时。
Confirmed 允许保留上一位或转向下一位，容错状态按配置期限等待下一位；末位直接成功。

解锁后生命周期层保持工作。PasswordCorrect 变回 false 时进入 Armed，恢复所有功能层
权重；它们从 Release/Unlocked 回到等待入口。Restart 本地 Bool 可从 Armed 自身重进，
重置超时状态并恢复层权重，使超时终态同样接受新输入。参数写入在真实原生
StateMachineBehaviour 或 SDK ParameterDriver 的状态进入回调中执行。

普通 Unity 后端缓存当前 Controller 的参数类型，按 Bool/Int/Float/Trigger 类型执行赋值。
localOnly 按 IsLocal 的实际 Bool/Int/Float 类型判断。StandaloneSecurityInput 持有生成后的
参数名称，因此名称混淆与运行时输入 API 可同时使用。

## 身体显示与动画控制权

生成前只扫描一次 Avatar 组件，索引 Contact 自身所在的根分支及 rootTransform 指向的
根分支。Lock 与 UnlockRestore 复用这份 HashSet。普通分支生成三条缩放曲线；Contact
分支按原 Renderer 启用状态隐藏/恢复，Transform 保持工作。

UnlockRestore 恢复生成时的缩放/可见性，然后 Lock 层权重归零。生命周期层没有身体
属性曲线，用户自身动画层继续驱动其缩放和可见性。真实 Animator 用例包含基础层持续
输出另一缩放值的场景，检查解锁后该值重新成为输出。

## 资源身份与复制

`Utils.SaveControllerGraph` 遍历状态机、状态、转场、Behaviour 和 Motion，把尚无资源
路径的生成对象加入 Controller。已有路径的对象保持原资源关系，避免重复 AddObjectToAsset。
生成后的 Clip、状态与原生 Behaviour 可直接进入 Player 构建。

`ControllerGraphCopy` 展开 OverrideController 链，按原始 Clip 对象身份解析有效覆盖。
图对象通过明确的 AnimatorStateMachine/State/Transition/BlendTree 构造器创建，随后
CopySerialized 并重映射图内共享引用。Override 仅替换状态和 BlendTree 的 Motion 引用，
Behaviour 中的独立音频/Clip 配置保持其语义。瞬态 Clip/Mask 一并持久化。

持久化 Controller 副本保留 CopyAsset 的同文件 local file ID 选择；外部 Motion 按资源
身份复制。内存 Controller 也生成独立可持久化图；SDK 后处理还原原 Descriptor 引用。

原配置 MonoScript GUID 已由 Assets/UnityBox.unity 三处具有 gesturePassword 等 ASS 字段
的序列化块恢复，值为 `1c51e6a139920bb4ca12700dc82627ff`。类型解析测试直接检查该 GUID
对应 MonoScript.GetClass() 为 ASSComponent，原场景文件保持原内容。
此前缺失的 Editor 脚本、asmdef 和包目录 metadata 已补齐确定的稳定 GUID；已有 metadata
保持原值，仓库路径探针继续由正式打包筛选排除。

## 多 Avatar 构建生命周期

每次 Avatar 构建分配 `Generated/Build_<id>`，Controller、参数和共享空 Clip 都进入该目录。
同一 Avatar 的生成重试更新自己的目录；另一个 Avatar 的目录及引用继续保留。
Obfuscator 的种子、目录、生成资源集合和跳过二次处理的层集合随该 Avatar 的构建快照保存，
晚期处理恢复对应快照，交错处理不同 Avatar 时仍使用各自的名称和输出路径。

SDK 路径记录原 base/special Controller、参数及自定义开关；后处理先还原，再仅回收此次
SDK 快照所属的生成目录。NDMF 路径按 pass 成功完成标记 SDK 去重，输出目录随结果保留。
NDMF pass 的扩展上下文兼容性由调度器管理，上游虚拟动画提交后再进入物理生成阶段。

生命周期状态中的 LayerControl 指向兄弟层，并以 `ASS_TargetLayer/<层名>` 记录目标。
晚期索引刷新按最终 Controller 层名重建这些索引；其余 ASS 自释放控制继续指向所属层。

## 开发与测量

- Editor 核心测试运行原生 Animator：连续保持、错误输入、完整序列、释放与重锁、超时
  Restart、基础动画层控制权、图持久化、Override 交换、内存复制和原 GUID。
- NDMF 用例使用真实插件调度计划，按上游虚拟编辑→ASS 物理生成→下游重新激活虚拟编辑
  执行 BuildContext.RunPass 与 Finish；最终 Controller 保留三方层，且原 Controller 内容保持原值。
- SDK 测试覆盖真实 Behaviour、参数副本、上传配置清理、完成状态、多 Avatar 输出、
  SDK 目录回收和最终层索引。
- 性能测试固定 16/64/256 个根子对象，预热后采集 7 个 Stopwatch 样本，输出结构化
  UNITYBOX_BENCHMARK 行；图生成功能与实际 Animator 求值分别记录。
- 构建、Player 执行、用例及测量结果统一记录于仓库 [Testing](../../../../Docs/Testing.md)
  和 [Performance](../../../../Docs/Performance.md)。

遗留主动负载默认关闭，普通 Unity Builder 始终生成手势工作流；SDK 仍保留已有配置
字段供既有工程读取。正式包保留既有 metadata，排除动画路径实验探针和平台 Bundle。
