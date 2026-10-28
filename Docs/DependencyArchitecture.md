# Unity 核心与可选平台适配

`refactor` 的七个包都以 Unity 2022.3 为核心环境。VRChat、Modular Avatar（MA）和 NDMF
只负责各自平台集成，包清单不再强制安装整套依赖。组件原有序列化身份和使用入口尽量保留；
缺少平台包时，对应功能不可用，不会生成假的 SDK 类型或模拟网络/手势输入。

## 功能边界

| 插件 | 无 SDK 可用内容 | 可选集成及条件 |
| --- | --- | --- |
| ACC | Runtime 标记、完整 Scanner/模型/AnimationBuilder、选择/路径算法、普通 Unity 生成窗口 | Driver/Descriptor：SDK Avatars ≥3.7；完整 MA 窗口/菜单/Setup Outfit：Base/Avatars ≥3.7.3、MA ≥1.18 |
| Blendshape | 完整扫描窗口、Float/Bool Controller/Clip 生成 | MA 菜单/参数/FX：Base/Avatars ≥3.7.3、MA ≥1.13 |
| ASS | 配置、Controller/Motion/参数工具、手势/计时/锁定图构造 API | 完整 Avatar 流程：SDK Avatars ≥3.7；NDMF：同时 ≥1.8 |
| 口型覆盖 | 配置、15 口型枚举、Renderer/映射验证、Inspector资产生成和Controller API | Descriptor/SDK 构建：Avatars ≥3.7；虚拟路径：同时 NDMF ≥1.7 |
| 碰撞体监控 | 任意 GameObject 的 Collider/Collider2D 快照与 Unity 构建采样 | SDK Descriptor/provider；NDMF 阶段采样，二者分别按包存在启用 |
| 构建日志 | Unity 资产导入、场景/脚本/shader 构建日志 | SDK 回调和 NDMF 日志分别按包存在启用 |
| Prefab 差异合并 | 完整比较、选择和输出 | 无额外平台依赖 |

“核心可用”不表示所有工具都有新的一键普通 Unity 工作流。ACC/Blendshape 有直接资产
生成窗口；ASS 核心图需要调用者显式提供 `IAnimatorBehaviourAdapter`，没有通用密码运行时；
口型Inspector可生成新Controller资产，使用者仍需提供参数输入、映射及输出绑定。普通 Animator 不会自动得到 VRChat 的
Viseme/Voice、手势或 IsLocal 服务。

完整集成环境固定 SDK **3.8.2**、MA **1.18.0**、NDMF **1.11.0**。MA 调用的双参数
`VRCConstraintSource` 构造器从 SDK 3.7.3 才存在；不能由 MA 自身较宽的依赖声明推断
SDK 3.7.0～3.7.2 可编译。已验证版本组合不等于所有更高版本组合均兼容。

## 程序集与 API 迁移

| 包 | 核心程序集 | 可选程序集 |
| --- | --- | --- |
| ACC | `.Runtime`、`UnityBox.AdvancedCostumeController.Core` | 原 `UnityBox.AdvancedCostumeController`（完整 MA 编辑器）、`.VRChat` |
| Blendshape | `UnityBox.BlendshapeControllerGenerator` | `.ModularAvatar` |
| ASS | `UnityBox.AvatarSecuritySystem.Runtime`、`UnityBox.AvatarSecuritySystem` | `.VRChat`、`.NDMF` |
| 口型 | `UnityBox.VisemeBlendshapeOverride.Runtime`、`UnityBox.VisemeBlendshapeOverride` | `.VRChat`、`.NDMF` |
| Collider | `UnityBox.AvatarColliderMonitor` | `.VRC`、`.NDMF` |
| Logger | `UnityBox.BuildPipelineLogger` | `.VRC`、`.NDMF` |
| Prefab | `UnityBox.PrefabDiffPatcher` | 无 |

ACC 的 Models、Scanner、AnimationBuilder、Localization、ChoiceParameterLayout 等公开类型
搬入 Core，原 Avatar 生成器使用同一份实现。旧 `AnimationBuilder` 单参数构造器保留，并有
指定输出目录的重载。外部 asmdef 直接使用这些类型时增加 Core 引用。

ASS 的 Processor、SDK 配置捕获、Playable 处理和 Defense 位于 VRChat；通用 ASSConfigData
与动画行为接口在核心。手势/锁定/倒计时构造器要求适配器，缺少时明确拒绝；SDK 注入
`VrcAnimatorBehaviourAdapter.Instance`。旧 Editor 扩展若直接引用平台类型需新增 VRChat 引用。

口型核心用自己的稳定整数枚举表达 15 个口型，SDK 枚举转换只在适配中。Collider 的核心
`RecordSnapshot` 继续接受 Component，也新增 GameObject；额外配置通过 provider 事件接入。
Logger 的菜单键/日志类别保留，但回调类已经位于对应可选程序集。不要只凭命名空间相同就
假设外部程序集引用不需要调整。

## Runtime 与构建生命周期

ACC、ASS、口型原 Runtime 组件始终可编译。SDK Base ≥3.7 时条件实现真实 `IEditorOnly`，
保留 SDK 在 preprocess 前的合法组件检查；缺 SDK 时只依赖 Unity。原字段名、类名及已知
GUID 保留。搬迁的7个原本未提交metadata的Editor文件新增记录身份，不声称恢复未知旧值；
Runtime组件仍缺失的原始`.meta`须从原项目恢复，不能通过随机值修复既有引用。

NDMF 的 RemoveEditorOnly 删除带 EditorOnly Tag 的对象，不代表自动删除所有标记接口
组件。ASS/口型适配显式捕获配置或处理构建副本，再由自己的清理步骤删除配置组件。
是否已安装 NDMF、是否本次实际执行，以及是否生成成功是三个不同状态；SDK 回退不能只凭
包存在就跳过。本次交接修复及验证记录见 [变更清单](RefactorChanges.md)。

普通 Unity 核心不会因为有 SDK/MA 包就自动获得平台输入服务。ACC 普通生成入口不自动
分配 Animator，Blendshape 可显式关闭 Install MA Controls。生成资产的删除、重新生成和
场景 Undo 边界见各自手册。

## 测试矩阵

核心测试只引用 Unity/真实 Test Framework；平台测试位于单独程序集，要求实际 SDK/MA/NDMF
及 `UNITY_INCLUDE_TESTS`。包安装时需在项目 manifest 的 `testables` 中声明待测包。

`test_project.py prepare --profile core` 只准备核心依赖并记录被忽略的 VPM 工作流依赖。
`--profile vrc` 增加真实 SDK；若要覆盖 NDMF 和 MA，显式传 `--dependency nadena.dev.ndmf`
和 `--dependency nadena.dev.modular-avatar`。缓存里有包不等于测试项目已经装入它。
独立目录与当前证据见 [测试指南](Testing.md)、[Unity 环境](UnityEnvironment.md)。

真实 Unity 2022.3.22f1 的最终运行通过核心 **93/93**、仅SDK **150/150** 与完整平台
**179/179**，零跳过，预期程序集均已发现，具体运行标识和实际报告见
[测试指南](Testing.md)。Prefab包含两项 `ExposedReference` 回归；其原生序列化 fixture
使用仅 Editor 且受 Test Framework 约束的独立程序集，不进入 Player。

原生导入发现，部分 `versionDefines.expression` 的开区间写法（如 `[3.7.0,)`）
未启用预期符号；已改用 Unity 支持的最低版本写法 `3.7.0`，NDMF 同理使用 `1.7.0`。
仅靠此前 Roslyn 范围解析无法发现这个问题。当前 runner 显式选择本仓库测试程序集，
同时检查预期程序集均被发现；缺包、未编译或未发现不能算通过。

完整平台测试使用真实 SDK 3.8.2、MA 1.18、NDMF 1.11，具体范围和报告见
[测试指南](Testing.md)。这些 EditMode 结果不代表实际 Avatar 上传或 VRChat 逐帧行为。

## Animator Optimizer 分支

`feat/animator-optimizer` 已 rebase 到 master，但尚未与本分支合并。优化器独立实现 Runtime、
普通 Animator 分析/转换核心、VRC 平台接口及 NDMF 虚拟适配。它的参数平台语义由明确选项
控制，不因 SDK 已安装自动启用；普通 Unity SetBool/SetInteger 与升级后的 Float 参数存在
输入契约变化。请读该分支的 README、AnimatorOptimizerAudit 和 Testing，而不要将本分支
七包的测试数量或改动直接套用到优化器。
