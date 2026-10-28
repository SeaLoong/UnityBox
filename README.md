# UnityBox · Animator Optimizer

`feat/animator-optimizer` 维护 UnityBox 的 Animator Optimizer，基于 `master` 开发。
它提供普通 Unity Animator 分析、转换、AAP State Register、离线资源处理和可选 SDK/NDMF
构建集成；参数平台语义、目标和转换方式通过明确配置选择。

## 源码与手册

| 内容 | 开发分支 | 入口 |
| --- | --- | --- |
| Animator Optimizer | `feat/animator-optimizer` | [包手册](Assets/UnityBox/AnimatorOptimizer/README.md)、[实现与迁移](Docs/AnimatorOptimizerAudit.md) |
| ACC、Blendshape、ASS、口型、Prefab、Collider、Logger | `refactor` | [七插件总览](https://github.com/SeaLoong/UnityBox/blob/refactor/README.md)、[变更清单](https://github.com/SeaLoong/UnityBox/blob/refactor/Docs/RefactorChanges.md) |

本分支保留其它七包的基线源码，持续维护的功能和文档位于 `refactor`。八插件验证工程通过
`--source-package` 直接组合两个 checkout 的源包，源码分支各自提交和推送。

## 本轮 Animator 工作

- AAP 的寄存器更新与可见输出使用同一次输入决策，处理首帧 Entry 和 Bool/Int 历史状态。
- NDMF 支持实际虚拟图的 AAP 转换、手动选层和最终提交，保留对应层身份及绑定范围。
- 转换、复制、保存重载与 Undo/Redo 包含状态机、State、Clip 和 BlendTree 子资产。
- 真实 Animator 覆盖参数、默认值、Mask、层覆盖、动态时间、Root Exit 和空 Motion 场景。
- 独立 Linux Player 运行成对的源/转换图，性能阶段测量不同层数的更新成本。

具体入口和参数契约见[包手册](Assets/UnityBox/AnimatorOptimizer/README.md)。
普通 Unity 直接使用核心；可选 Avatar 测试组合固定 SDK **3.8.2**、MA **1.18**、
NDMF **1.11**，优化器的 NDMF 入口要求 ≥1.11。

## 测试与性能

八插件组合功能测试已通过：纯 Unity **294/294**、SDK **369/369**、完整 MA/NDMF
**456/456**，分别执行8、13、19个测试程序集，均退出码0、零失败、零跳过。
各范围重复核心用例，分别计数；运行标识、原始结果和源指纹见[测试指南](Docs/Testing.md)。

[测试指南](Docs/Testing.md) 提供核心、完整依赖、组合工程、Player、图形模式及串行基准命令。
[验证结果](Docs/VerificationResults.json) 保存运行和内容指纹；
[性能报告](Docs/Performance.md)与[测量数据](Docs/PerformanceResults.json)记录输入规模、
样本、中位数、p95 和环境。共享工具及归档检查分别使用：

```sh
python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v
python3 .github/scripts/test_package_vpm.py -v
```

环境安装、Personal 激活及复用见 [Unity 环境](Docs/UnityEnvironment.md) 和
[测试 skill](.agents/skills/unitybox-testing/SKILL.md)。配置其它云机器可直接使用
[安装与测试提示词](Docs/UnityCloudSetupPrompt.md)。原场景和已知 GUID 随源码保留，
生成器使用独立输出，测试使用 checkout 外的项目。

## 安装与分发

普通 Unity 可通过源码或本地 UPM 安装优化器。Avatar 项目按所选入口安装真实 SDK/NDMF。
VPM 源与发布入口：

```text
https://sealoong.github.io/UnityBox/vpm.json
```

源码分支对应开发提交，线上 listing 对应发布版本。两个工作分支分别保留开发历史；
原优化器 rebase 前提交位于远端备份分支。
项目采用 [MIT License](LICENSE)。
