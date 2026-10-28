# Unity 核心与可选平台适配

七个插件共用 Unity 2022.3 环境，普通生成、运行、比较和采样使用 Unity 核心。
SDK、Modular Avatar（MA）、NDMF 适配通过各自程序集及真实版本条件启用。
完整组合固定 SDK Base/Avatars **3.8.2**、MA **1.18.0**、NDMF **1.11.0**。

## 工作流与输入

| 插件 | 普通 Unity | 可选平台服务 |
| --- | --- | --- |
| ACC | Scanner、服装/Parts/Mixer/材质动画、Unity Assets 窗口 | SDK Driver/Descriptor；MA 菜单、参数、FX、图标与 Setup Outfit |
| Blendshape | Float/Bool、组合 Controller、Animator 分配 | MA 菜单/参数/FX 安装、移除与整 Avatar 预算 |
| ASS | 完整手势/计时/锁定运行时和 StandaloneSecurityInput | SDK 行为适配、Playable 层；NDMF 构建与完成状态交接 |
| 口型 | 15 口型、Voice、映射、AOC/Controller 与 Clip 生成 | Descriptor 默认值、SDK 构建、NDMF 虚拟路径与图提交 |
| Collider | 任意根对象 3D/2D 快照、历史、几何和场景显示 | SDK PhysBone/Descriptor provider；NDMF 阶段采样 |
| Logger | 导入、Unity 构建、Linker、Shader 与 JSON 轨迹 | SDK 与 NDMF 阶段日志 |
| Prefab | 比较、选择、Variant/嵌套保留和保存 | 普通 Unity 入口 |

普通游戏通过脚本写 Animator 参数：口型使用 Viseme/Voice，ASS 使用 SetGesture/Restart 或
相应手势参数。Avatar 工作流由平台输入服务提供这些值；选择入口决定参数语义。

## 程序集引用

| 包 | 核心程序集 | 平台程序集 |
| --- | --- | --- |
| ACC | `.Runtime`、`UnityBox.AdvancedCostumeController.Core` | `UnityBox.AdvancedCostumeController`、`.VRChat` |
| Blendshape | `UnityBox.BlendshapeControllerGenerator` | `.ModularAvatar` |
| ASS | `UnityBox.AvatarSecuritySystem.Runtime`、`UnityBox.AvatarSecuritySystem` | `.VRChat`、`.NDMF` |
| 口型 | `UnityBox.VisemeBlendshapeOverride.Runtime`、`UnityBox.VisemeBlendshapeOverride` | `.VRChat`、`.NDMF` |
| Collider | `UnityBox.AvatarColliderMonitor` | `.VRC`、`.NDMF` |
| Logger | `UnityBox.BuildPipelineLogger` | `.VRC`、`.NDMF` |
| Prefab | `UnityBox.PrefabDiffPatcher` | 共用核心 |

外部 asmdef 按使用类型增加上述引用。ACC Models/Scanner/AnimationBuilder 位于 Core；
原构造器保留，输出目录可通过重载指定。ASS 使用 `IAnimatorBehaviourAdapter` 接入行为：
普通 Unity 由 `UnityAnimatorBehaviourAdapter` 实现，SDK 使用真实平台 Behaviour。
Blendshape 的 MA 汇总查询显式引用 NDMF 的参数服务与 VRChat 平台程序集。

MA 所需的双参数 `VRCConstraintSource` 从 SDK **3.7.3** 提供；ACC 完整 MA 入口使用
MA ≥1.18，Blendshape MA 入口按包内条件启用。普通 SDK 适配和 NDMF 条件分别记录于各自
asmdef。最低版本表达式使用 Unity 支持的普通版本字符串，例如 `3.7.0`、`1.7.0`。

## 序列化与构建

Runtime 配置类型、字段和已知 GUID 保留；实际 SDK Base 可用时继续实现 `IEditorOnly`。
ASS 原组件 GUID 从已提交场景恢复，三处引用对应同一 `ASSComponent`。

构建配置先捕获，再按本 Avatar 的完成状态交接。SDK 回退检查生成结果，NDMF 通过实际
服务激活/提交处理虚拟图。物理生成入口在服务提交边界操作 Controller；后续插件可以
重新激活动画服务继续处理。源 Controller 的可变状态图通过独立副本传递，需要改写的动画先复制再编辑；
只读持久化 Clip/Mask 可以保留资产引用。

生成器每次使用独立 Build 目录；安装引用形成 Undo 事务，重新生成、撤销、重做和移除各有
明确动作。普通核心由项目脚本分配输入，Avatar 适配负责平台菜单和同步参数。

## 验证组合

`core` 准备 UTF/NUnit 与 Unity 内建模块；`vrc` 加入真实 SDK，再通过 `--dependency` 选择
MA、NDMF。缓存中的包通过显式选择进入隔离工程。八插件组合使用：

```sh
python3 scripts/unity/test_project.py prepare \
  --repo /workspace/unitybox-refactor-review \
  --source-package /workspace/UnityBox/Assets/UnityBox/AnimatorOptimizer \
  --project /workspace/unitybox-setup/unity/projects/composed8 \
  --profile vrc --offline \
  --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar
```

源码和包内容指纹记录于项目标记。实际命令、程序集、逐帧输出、Player 构建与测量见
[测试指南](Testing.md)、[验证结果](VerificationResults.json)和[性能报告](Performance.md)。

## Animator Optimizer

优化器由独立 `feat/animator-optimizer` 提供 Runtime、Unity 核心、VRC 和 NDMF 程序集。
NDMF 入口要求 ≥1.11，使用该版本的原对象信息保持层身份和 Clip 元数据。
Bool/Int 升级、VRChat 参数语义、AAP 后端和目标类型通过明确配置选择。
对应手册位于[优化器分支](https://github.com/SeaLoong/UnityBox/tree/feat/animator-optimizer)。
