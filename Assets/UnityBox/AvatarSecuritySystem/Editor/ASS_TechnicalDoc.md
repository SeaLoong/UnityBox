# Avatar Security System 0.9.0 技术参考

ASS 将编辑配置、密码图、原生 Unity/SDK 行为和构建资产生命周期分开组织。
[用户指南](README.md)说明自动构建和运行时输入；本页记录生成与升级契约。

## 模块和装配

| 模块 | 职责 |
| --- | --- |
| Runtime/Component.cs | 编辑配置、共享时间规范化；SDK Base 可用时实现 IEditorOnly |
| Runtime/AnimatorStateEntryActions.cs | 原生状态进入时执行参数、层权重与声音动作 |
| Runtime/StandaloneSecurityInput.cs | SetGesture、IsPasswordCorrect、HasTimedOut、Restart；IsUnlocked 为兼容别名 |
| ASSConfigData.cs | 配置捕获、独立列表副本及统一时间规范化 |
| StandaloneConfigurationStripper.cs | 实际 Player 构建场景副本中自动生成，再移除 ASSComponent |
| StandaloneSecurityBuilder.cs | 复制现有 Controller/AOC、清理旧 ASS 层、追加手势与反馈图 |
| GesturePassword/Countdown/Feedback | 连续手势、期限、Overlay 和声音 |
| AnimatorLifecycle.cs | 密码结果与下一轮输入、超时重试和功能层重新启用 |
| UnityAnimatorBehaviourAdapter.cs | 生成原生 StateMachineBehaviour |
| VRChat/VrcAnimatorBehaviourAdapter.cs | 生成真实 SDK Behaviour |
| ControllerGraphCopy.cs | 可编辑 Controller 副本、Override 展开和引用重映射 |
| LegacyComponentMigration.cs | 旧脚本身份发现、Scene/Prefab 重连、备份与后续导入 |
| Obfuscator.cs / VRChat/Processor.cs | 名称映射、每 Avatar 输出与 SDK 原引用还原 |
| NDMF/NDMFPlugin.cs | Resolving 捕获与 PlatformFinish 生成 |

核心 Editor/Runtime 只引用 Unity。SDK Base/Avatars ≥3.7.0、NDMF ≥1.8 通过可选装配接入。
`IAnimatorBehaviourAdapter` 与 `IAnimatorLayerWeightAdapter` 分别表达参数/音频和层权重动作。
纯声音与层权重动作直接执行；参数赋值或 localOnly 判断需要时才建立参数类型缓存。
空音频相对路径选择 Animator 根节点 AudioSource，非空路径选择子节点。

## 普通 Unity 自动构建

`StandaloneConfigurationStripper.OnProcessScene` 只在真实 BuildReport 的场景处理中生成。
它收集场景副本中的 ASSComponent，为每个目标创建独立 StandaloneBuild 输出，调用后端、
分配 Animator Controller 与 StandaloneSecurityInput，最后移除副本中的编辑配置。
源场景和原配置留作编辑；普通用户按正常 Build Player 流程使用，Inspector 无手动生成按钮。

`StandaloneSecurityBuilder.Build(root, config, controllerPath)` 保留供 Editor 扩展和测试调用：

```csharp
var config = ASSConfigData.FromComponent(component);
var result = StandaloneSecurityBuilder.Build(root, config, "Assets/Generated/ASS.controller");
root.GetComponent<Animator>().runtimeAnimatorController = result;
```

后端复制配置列表，并强制普通 Unity 的遗留主动负载关闭。已有 Controller/AOC 使用
`ControllerGraphCopy` 建立独立可编辑副本，保留用户状态、Clip 身份语义及原 WD 设置，
随后清除已识别的 ASS 层（包含遗留 ASS_Lock）并生成密码、计时、声音和生命周期层。
缺少 Animator 或输入组件时由构建场景处理器创建；普通图的 IsLocal 默认 true。

0.9.0 的新增 ASS 状态全部 WD Off，层同步索引为 -1。新图只驱动 ASS 自己的反馈对象，
不生成 Avatar 身体锁定、不写用户对象缩放，也不改写用户层的 WD。
`disableRootChildren` 与 `writeDefaultsMode` 隐藏保留历史值，生成阶段不再用它们启用锁定。

## 手势时间契约

默认 hold=0.5s、tolerance=0.3s、maximum=3s。`ASSComponent.NormalizeGestureTimes`
为 Inspector、配置副本与生成器提供同一规则：hold 在 0.05–1s；tolerance 在 0 至
hold−0.01s；maximum 位于 max(1s, hold+tolerance+0.05s) 至 10s。
因此实际保持时间始终严格大于容错窗口，旧的不合理值在加载/构建时规范化。

每一步的 Holding 只在正确手势连续成立时累计时间。错误姿势先进入 Interrupted，
短暂恢复也会重新进入 Holding 并从零计时；超过容错窗口返回序列入口。
前一位 Confirmed 后才允许下一位开始，重复相邻位需要先切换到不同姿势完成释放。
最大等待、倒计时截止与 Restart 转移排在成功接受之前，避免采样边界接纳过期输入。

| 层 | 主路径与状态 |
| --- | --- |
| Password | Wait → InputWaiting → Holding/Interrupted → Confirmed/下一位 → Success → Completed → Release |
| Countdown | Remote/Countdown → TimeUp 或密码已通过 → Release |
| Audio | 等待 → 警告声音 → Stop/Release |
| Lifecycle | Armed ↔ 密码已通过；Restart 开始下一轮 |

生命周期层无用户属性曲线，进入 Armed 时清 TimeUp/Restart 并重新启用 ASS 功能层。
密码结果变回 false 或 Restart 可重新接受输入，超时终态也支持新一轮。
`IsPasswordCorrect` 读取布尔结果；旧 `IsUnlocked` 只映射同一结果，含义不涉及 Avatar 对象锁。

## 资产身份与 Controller 副本

`Utils.SaveControllerGraph` 持久化尚无资源路径的状态机、状态、转场、Behaviour 与 Motion；
已有路径的对象保留原关系。`ControllerGraphCopy` 通过原 Clip 身份展开 Override 链，
用具体图类型构造器和 CopySerialized 建立副本，再重映射共享引用。Clip 覆盖只作用于
Motion 引用，Behaviour 的独立音频设置继续表达原语义；瞬态 Clip/Mask 一并保存。

SDK 路径为每个 Avatar 分配 Generated/Build_<id>，记录原 Playable Controller、参数和
自定义开关，后处理还原原引用并按该输出归属清理。NDMF 使用当前 pass 的配置、名称
映射和结果归属，上下游服务在实际提交边界交接，最终输出随构建结果保留。

VRChat 混淆在注入诱饵状态前声明实际使用的条件参数，独立于诱饵层开关。
`PrepareDecoyParameters` 在参数池没有 Bool 时补齐本地 false 布尔守卫；参数注册按
实际 Float/Int/Bool 类型处理，避免仅启用诱饵状态时出现未声明或类型不符的条件。

## 旧脚本 GUID 恢复

固定 GUID `1c51e6a139920bb4ca12700dc82627ff` 最初来自仓库样例场景。
0.7.24 的真实 ZIP 未携带 Runtime/Component.cs.meta，旧安装各自生成 GUID；0.8.0 加入
固定身份时遗漏了这些升级引用。0.9.0 用 LegacyComponentMigration 恢复同名 Runtime
程序集中的 ASSComponent，继续保留 0.8.0 的固定身份。

完整旧配置以 gesturePassword、useRightHand、countdownDuration、gestureHoldTime、
gestureErrorTolerance、gestureMaxHoldTime、disableRootChildren、writeDefaultsMode 识别；
脚本 GUID 必须失效，非空 EditorClassIdentifier 必须对应 ASSComponent。
处理先发现完整配置的旧身份，再修同身份的兼容引用，包含只序列化脚本的 stripped
Prefab 实例。Scene/Prefab 依赖一并读取，确认身份保存于
UserSettings/UnityBox/ASSMigration/LegacyGuids.json，每次调用重新读取，支持后续导入。
其它类型的 Missing Script 与已有有效脚本引用保持原关联。

迁移仅改 m_Script.guid，保留组件 fileID、其它字段文本、资源引用、BOM/换行及 Prefab
对应关系；每次写入前把原字节备份到 UserSettings/UnityBox/ASSMigration/<id>/<assetPath>。
旧计时若不满足 0.9.0 的关系，在组件加载和配置捕获时按上面的共同规则规范化，原始
备份仍保存调整前的文件。

未保存的 Scene/Prefab 延后恢复，保存后运行
`Tools/UnityBox/Avatar Security System/Repair Legacy Components`。干净的已加载 Scene
完成写入后重载。`RepairAssets(IEnumerable<string>, bool reloadCleanScenes=true)` 返回恢复数、
变更资源、延后资源和备份目录；`TryMigrateYaml` 提供不写文件的文本迁移入口。

## 验证与界面

原生测试覆盖连续正确保持、快速姿势遍历、中断与重新计时、重复位释放、时间边界、
Restart、WD Off、用户对象缩放保持，以及真实构建场景的自动生成和原图副本。
升级用例覆盖多个旧 GUID、Scene/Prefab、stripped 引用、后续导入、配置与引用、原字节
备份及幂等。真实 0.7.24 → 0.8.0 → 当前包的启动升级另存
[三阶段记录与复用脚本](../../../../Docs/Verification/ASSUpgrade/README.md)。
SDK/NDMF 用例检查实际 Behaviour、源资产隔离与最终提交；混淆检查覆盖诱饵状态单独
启用、无 Bool 池及条件参数声明。

Inspector 及其校验说明支持英语、简体、繁体和日语；缺省语言按 Unity 语言选择。
运行数量、源指纹、Player 构建和性能测量集中于仓库[测试指南](../../../../Docs/Testing.md)
与[性能报告](../../../../Docs/Performance.md)。当前基准测量手势图生成和 Animator 更新，
历史身体锁生成样本通过旧提交追溯。
