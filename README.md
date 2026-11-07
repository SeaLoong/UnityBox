# UnityBox

Unity 2022.3 的编辑、动画生成和构建工具集。八个插件在 `master` 统一维护，
普通 Unity 功能独立运行，VRChat、Modular Avatar 和 NDMF 按需接入。
同一个 checkout 提供完整源码、手册、测试和可复现的性能流程。

## 工具入口

| 插件 | 普通 Unity 工作流 | Avatar 集成 | 手册 |
| --- | --- | --- | --- |
| Animator Optimizer | Animator 分析、静态层合并、参数选择与 AAP 状态寄存器 | SDK Playable、NDMF 虚拟图与 MA 集成 | [优化器手册](Assets/UnityBox/AnimatorOptimizer/README.md) |
| Advanced Costume Controller | 服装、变体、Parts、两种 Mixer、材质动画生成 | MA 菜单、FX、图标、骨架与同步参数 | [服装手册](Docs/AdvancedCostumeController.md) |
| Blendshape Controller Generator | Float/Bool/反向曲线、组合 Controller、Animator 分配 | MA 安装/移除、总参数预算与 Undo | [Blendshape 手册](Docs/BlendshapeControllerGenerator.md) |
| Viseme Blendshape Override | 15 口型、Voice 曲线、OverrideController、序列化迁移 | Descriptor、SDK 回退、NDMF 虚拟图 | [口型手册](Docs/VisemeBlendshapeOverride.md) |
| Avatar Security System | 自动构建手势密码、倒计时、反馈与 Restart，保留原 Animator 图 | SDK 参数/LayerControl、NDMF 构建 | [用户手册](Assets/UnityBox/AvatarSecuritySystem/Editor/README.md) |
| Prefab Diff Patcher | 属性/组件/子树选择合并、Variant 与嵌套链接、引用重映射 | 共用普通 Unity 入口 | [Prefab 手册](Assets/UnityBox/PrefabDiffPatcher/README.md) |
| Avatar Collider Monitor | 3D/2D 几何快照、缓存预览、历史 JSON、世界包围盒 | PhysBoneCollider、Descriptor、构建阶段采样 | [碰撞体手册](Assets/UnityBox/AvatarColliderMonitor/README.md) |
| Build Pipeline Logger | 有序耗时轨迹、Shader 热路径、UnityLinker、JSON 导出 | SDK 与 NDMF 阶段日志 | [日志手册](Assets/UnityBox/BuildPipelineLogger/README.md) |

各包位于 `Assets/UnityBox/<目录>`，清单名称为 `top.sealoong.unitybox.*`。
普通核心直接使用 Unity；SDK、MA、NDMF 功能按实际安装的包启用。
程序集和输入服务见 [依赖架构](Docs/DependencyArchitecture.md)。

## 安装与升级

通过 [VCC / ALCOM](https://sealoong.github.io/UnityBox/) 添加 VPM 源：

```text
https://sealoong.github.io/UnityBox/vpm.json
```

源码或本地 UPM 安装直接从 `master` 的 `Assets/UnityBox` 选择所需包。
源码版本以对应提交和包内 `package.json` 为准，VPM listing 列出已部署的发布产物。
升级时保留 `.meta` 与场景，按各工具入口更新 Controller、菜单和参数安装结果；
ASS 0.9.0 随正常 Player/Avatar 构建自动生成。
生成器将每次输出保留在独立 Build 目录，场景 Undo 可以恢复之前的引用。
ASS 0.9.0 自动恢复旧发布包产生的随机脚本 GUID；未保存的 Scene/Prefab 保存后可通过
修复菜单完成重连。普通 Unity 的 ASS 在正常 Player 构建中自动生成，无手动生成步骤。
本版移除 Avatar 对象锁定，采用连续手势保持、默认保持 0.5s / 容错 0.3s、严格时间关系
及四语言界面。诱饵状态可独立启用，无 Bool 池时也会补齐条件所用的本地布尔守卫。
升级和数据备份步骤见[ASS 用户指南](Assets/UnityBox/AvatarSecuritySystem/Editor/README.md)。

## 功能、构建与性能

八个插件从同一个 `master` checkout 准备纯 Unity、SDK、完整 MA/NDMF 三种隔离工程，
分别执行功能、图形、Linux Player 和串行性能测试。当前运行标识、通过数、原始结果及
源码指纹见[测试指南](Docs/Testing.md)和[验证结果](Docs/VerificationResults.json)；
三个依赖范围重复执行核心用例，分别计数。

本轮 ASS 0.9.0 的升级、自动构建、连续保持和 WD Off 检查纳入对应范围；
实际通过数、运行标识、Player 场景和串行测量以[最新结果索引](Docs/VerificationResults.json)
及[测试指南](Docs/Testing.md)为准。历史发布结果按对应提交追溯。

[功能总览](Docs/PluginAudit.md) 列出每个插件的输入、输出和执行场景；
[变更与迁移](Docs/RefactorChanges.md) 解释修复触发条件、行为变化和生成资产的升级方式。
[测试指南](Docs/Testing.md) 包含核心、完整依赖、图标渲染、独立 Player 和串行基准命令。
[性能报告](Docs/Performance.md) 与 [原始测量](Docs/PerformanceResults.json) 记录规模、样本数、
中位数、p95 和实际运行环境；[验证结果](Docs/VerificationResults.json) 保存各次运行及内容指纹。

测试工具使用真实 Unity **2022.3.22f1**、UTF **1.1.33**、NUnit **1.0.6**。
Avatar 组合固定 SDK Base/Avatars **3.8.2**、MA **1.18.0**、NDMF **1.11.0**。
现有 Personal 已激活云端 Editor。环境复用见 [Unity 环境](Docs/UnityEnvironment.md) 与
[测试 skill](.agents/skills/unitybox-testing/SKILL.md)。配置其它云机器可直接复制
[安装与测试提示词](Docs/UnityCloudSetupPrompt.md)。

## 开发和发布

共享工具要求 Python 3.12：

```sh
python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v
python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

GitHub Actions 根据 `Assets/UnityBox/*/package.json` 构建包。筛选器保留源码、测试和既有
metadata，排除 Generated、实验探针和平台 bundle。每个发布包使用自身清单版本，ZIP 的
时间和权限元数据固定；发布保留已有 listing 条目与原 ZIP。已发布版本的内容保持不可变，
功能或资源变化通过提升包版本发布。
推送 `master` 会触发 VPM listing 构建与 GitHub Pages 部署。发布前同步包版本和手册，
并检查生成 ZIP、listing SHA256 与 VCC 入口。[发布说明](Docs/ReleaseNotes.md)记录当前升级内容。
原 `refactor`、优化器开发分支及 rebase 前备份保留历史；后续开发从 `master` 继续。

项目采用 [MIT License](LICENSE)。
