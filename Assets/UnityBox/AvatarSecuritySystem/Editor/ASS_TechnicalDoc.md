# Avatar Security System 0.9.3 技术参考

ASS 将编辑配置、密码图、原生 Unity/SDK 行为和构建资产生命周期分开组织。
[用户指南](README.md)说明自动构建和运行时输入；本页记录生成与源资源保全契约。

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
仅在生成所用配置副本中提供时间规则：hold 在 0.05–1s；tolerance 在 0 至
hold−0.01s；maximum 位于 max(1s, hold+tolerance+0.05s) 至 10s。
实际生成图的保持时间严格大于容错窗口；组件加载、打开 Inspector 和资源导入保留已存字段。
`OnValidate` 不再自动纠正源配置；默认值由新组件和用户显式 Reset 提供。

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

## 导入与源配置边界

0.9.3 保持移除旧 ASS `LegacyComponentMigration`、初始化扫描、AssetPostprocessor 迁移、场景重载、
旧修复菜单与写入 API。不存在从启动、资源导入或移动回调进入的 GUID 修复路径；
本版也不读取旧身份表来重连后续导入的资源。

固定 GUID `1c51e6a139920bb4ca12700dc82627ff`、Runtime 程序集、字段名及类型继续保持。
0.7.24 未携带 metadata 形成的随机身份在正常打开/导入时原样保留；显式引用迁移由
本包 Unity-only Editor 程序集内的恢复窗口处理，与 Runtime 和构建回调分开。

源 `ASSComponent` 的已存参数直接保留。`ASSConfigData.FromComponent` 与 `Clone` 生成
独立配置副本，再为生成图规范化时间。普通 Player/Avatar 构建仍通过原入口处理构建副本，
生成输出和临时配置的变化不回写源 Scene/Prefab。

0.9.0 的 `UserSettings/UnityBox/ASSMigration/<id>/Assets/...` 只保存对应轮次的原始
Scene/Prefab 字节，不包含旧包 metadata 或后续修改；`LegacyGuids.json` 不是恢复日志。
ASS 本身不创建旧迁移目录、不读取这些记录改写文件，也不执行自动备份恢复。
如需调查旧写入，先保存当前项目副本，再逐文件对照备份；恢复目标和内容由用户明确选择。

历史自动迁移的复现数据保留在[0.9.0 归档](../../../../Docs/Verification/ASSUpgrade/README.md)，
用于追溯该版本的脚本替换、导入和重载行为。当前版本的检查以源资源保全为目标。

## 包内手动引用恢复

Project 资源菜单 `Assets/UnityBox/Avatar Security System/Repair Component References`
打开 ASS 自带的恢复窗口，并带入当前选择。窗口与引擎位于本包现有 Unity-only Editor
程序集中，只处理 ASSComponent。正常插件更新即交付该功能，原 Tools 插件窗口入口保留。

用户选定资源后，一次 **Migrate Selected Assets** 处理整批可确定引用；同旧 GUID 的歧义
只选择一次目标类型，无逐项确认。写入前检查源 SHA、目标脚本并备份本批全部原字节，
只替换脚本 GUID，保留 fileID、字段、BOM/换行与项目 metadata。
已打开 Scene、PrefabStage 及它们的依赖（包括未保存的新引用）报告跳过，不保存、关闭或重载。
备份位于 `UserSettings/UnityBox/AvatarSecuritySystem/ComponentReferenceMigration/<id>/Assets/...`。
完整操作与其它插件入口见[组件引用说明](../../../../Docs/ComponentReferences.md)。

## 验证与界面

原生测试覆盖连续正确保持、快速姿势遍历、中断与重新计时、重复位释放、时间边界、
Restart、WD Off、用户对象缩放保持，以及真实构建场景的自动生成和原图副本。
导入保全用例检查旧身份的 Scene/Prefab 在导入和打开后字节、GUID、源配置及编辑状态
保持原状；生成用例分别检查源字段和有效构建配置。当前结果见
[组件引用验证记录](../../../../Docs/Verification/ComponentReferences/README.md)。
SDK/NDMF 用例检查实际 Behaviour、源资产隔离与最终提交；混淆检查覆盖诱饵状态单独
启用、无 Bool 池及条件参数声明。

Inspector 及其校验说明支持英语、简体、繁体和日语；缺省语言按 Unity 语言选择。
运行数量、源指纹、Player 构建和性能测量集中于仓库[测试指南](../../../../Docs/Testing.md)
与[性能报告](../../../../Docs/Performance.md)。0.9.0 的基线测量覆盖手势图生成和 Animator 更新，
0.9.3 的组件引用和源配置范围单列于当前组件引用记录。历史身体锁生成样本通过旧提交追溯。
