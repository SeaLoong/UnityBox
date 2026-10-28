# UnityBox

Unity 2022.3 的编辑、动画生成和构建工具集。`refactor` 持续维护七个插件；
Animator Optimizer 在 `feat/animator-optimizer` 开发。八插件组合通过隔离工程装入两个
checkout 的包进行功能、Player 和性能测试，源码分支保持独立。

## 工具入口

| 插件 | 普通 Unity 工作流 | Avatar 集成 | 手册 |
| --- | --- | --- | --- |
| Advanced Costume Controller | 服装、变体、Parts、两种 Mixer、材质动画生成 | MA 菜单、FX、图标、骨架与同步参数 | [服装手册](Docs/AdvancedCostumeController.md) |
| Blendshape Controller Generator | Float/Bool/反向曲线、组合 Controller、Animator 分配 | MA 安装/移除、总参数预算与 Undo | [Blendshape 手册](Docs/BlendshapeControllerGenerator.md) |
| Viseme Blendshape Override | 15 口型、Voice 曲线、OverrideController、序列化迁移 | Descriptor、SDK 回退、NDMF 虚拟图 | [口型手册](Docs/VisemeBlendshapeOverride.md) |
| Avatar Security System | 手势保持、密码、超时、解锁、重锁和 Restart 的完整运行时 | SDK 参数/LayerControl、NDMF 构建 | [用户手册](Assets/UnityBox/AvatarSecuritySystem/Editor/README.md) |
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

源码或本地 UPM 安装可以直接选择所需包。使用本轮分支实现时，从 `refactor` 获取七个包，
从 `feat/animator-optimizer` 获取优化器；版本对应当前源码提交，发布 listing 对应发布产物。
升级时保留 `.meta` 与场景，重新生成 Controller、菜单和参数安装结果。
生成器将每次输出保留在独立 Build 目录，场景 Undo 可以恢复之前的引用。
ASS 组件原 GUID 已从仓库场景恢复，三处原场景引用继续使用同一个脚本身份。

## 功能、构建与性能

八插件组合功能测试已通过：纯 Unity **294/294**、SDK **369/369**、完整 MA/NDMF
**456/456**，分别执行8、13、19个测试程序集，均退出码0、零失败、零跳过。
各范围重复核心用例，分别计数；运行标识、原始结果和源指纹见[测试指南](Docs/Testing.md)。

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
python3 .github/scripts/test_package_vpm.py -v
```

GitHub Actions 根据 `Assets/UnityBox/*/package.json` 构建包。筛选器保留源码、测试和既有
metadata，排除 Generated、实验探针和平台 bundle；归档每次覆盖重建。
两个工作分支各自保留开发提交，原优化器 rebase 前的提交另存于远端备份分支。

项目采用 [MIT License](LICENSE)。
