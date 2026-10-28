# UnityBox

> 当前为 `feat/animator-optimizer`：已 rebase 到 `master`，重点是 Animator Optimizer。其余 7 个插件的完整修复与最新手册在独立的 `refactor` 分支；本分支未合并那些生产逻辑变更。

此仓库用于存放开发中或可直接复用的 Unity 工具脚本，便于在多个项目间共享。

## 通过 VCC / ALCOM 安装

1. 将本仓库添加为 VPM 源：
   - **一键添加（推荐）**：点击链接 → [添加到 VCC](https://sealoong.github.io/UnityBox/)
   - **手动添加**：在 VCC / ALCOM 的设置中添加以下 VPM 源地址：

     ```
     https://sealoong.github.io/UnityBox/vpm.json
     ```

2. 在 VCC / ALCOM 中找到以下包并按需安装：

| 包名                            | 说明                                                        |
| ------------------------------- | ----------------------------------------------------------- |
| Advanced Costume Controller     | VRChat 换装控制器，通过 Modular Avatar 菜单快速配置衣装切换 |
| Blendshape Controller Generator | Blendshape 动画控制器生成器                                 |
| Avatar Collider Monitor         | VRChat 模型碰撞体监视器                                     |
| Build Pipeline Logger           | 构建管线日志记录器                                          |
| Animator Optimizer             | 参数驱动 Animator 图分析与受限 BlendTree 转换 |
| Prefab Diff Patcher             | Prefab 差异比较与选择合并 |
| Avatar Security System          | 本分支保留的 VRChat 手势密码工具；修复与当前边界见 refactor |
| Viseme Blendshape Override      | 用内建 `Viseme` / `Voice` 参数驱动可限幅、可独立响应区间的口型替代层 |

## Animator Optimizer

- [功能、限制与使用方法](Assets/UnityBox/AnimatorOptimizer/README.md)
- [审查与修复记录](Docs/AnimatorOptimizerAudit.md)
- [测试指南](Docs/Testing.md)
- [可复用 Unity 环境与测试项目](Docs/UnityEnvironment.md)
- 核心仅要求 Unity 2022.3。普通 Unity 中可通过离线窗口分析、清理、转换并保存 Controller，
  不安装 SDK/NDMF 也可使用；包清单不再强制安装平台依赖。
- Avatar 构建适配可选：VRChat Base/Avatars ≥3.7；NDMF 虚拟图适配另需 NDMF ≥1.11。
  缺依赖时相应装配和集成测试不参与。普通 Unity Build 只剥离配置组件，不自动优化 Controller。
- AAP 状态寄存器仍是默认关闭的实验功能。Bool/Int 升为 Float 后，普通 Unity 参数写入方
  需要相应使用 SetFloat 或关闭升级；不能套用 VRChat 参数映射假设。
- 空白离线窗口默认使用普通 Unity 参数语义，关闭 Bool/Int 升级。Avatar Playable Layer
  自动采用 VRChat 内建参数规则，其余 Animator/Explicit Controller 需显式启用，进入
  SDK/NDMF 构建也不自动切换。组件配置保留已有升级选项；新增平台语义开关默认关闭。

2026-10-29（北京时间）云端 Unity Hub 已通过现有 Personal 账号完成激活。Unity 2022.3.22f1
真实 EditMode 执行：核心 **127/127**、仅 SDK **139/139**、SDK + NDMF **172/172**、
SDK + NDMF + MA **174/174**，均零失败、零跳过，Unity exit 0。报告和范围见
[测试指南](Docs/Testing.md)。完整范围包含 2 个使用真实 MA 的兼容用例。此前 5/19/23 程序集的 Roslyn 检查保留
为历史证据；本次实际执行不等于 Avatar 上传、完整逐帧等价性或性能已经验证。

本次真实 Editor 验证还修复了 Animator 图复制的原生断言，以及 Float 条件存在覆盖空洞时
近似模式错误丢失状态历史的问题。后者现在保留原图并报告原因，可能减少以前看似成功的
转换；显式数值边界近似仍可用于满足完整覆盖约束的图。
NDMF 虚拟适配最低版本已提高到 1.11：源对象身份映射依赖该版本的元数据；旧 1.9/1.10
不会启用这项适配。核心和仅 SDK 路径不受最低版本调整影响，升级前后的构建入口需重新核对。
NDMF 服务现按实际有效配置激活；原关闭或默认的 Playable 来源不会因服务改写 Descriptor
而被重新启用。无配置时不激活服务，子 Animator 入口恢复原关闭的 Playable 状态，避免
复活残留控制器。该保守策略也跳过其它插件稍后为原无效来源新建的层，详见审查记录。

## 目录结构

每个工具都是独立的 VPM 包，位于 `Assets/UnityBox/` 下：

```
Assets/UnityBox/
├── AnimatorOptimizer/              # top.sealoong.unitybox.animator-optimizer
├── AdvancedCostumeController/        # top.sealoong.unitybox.advanced-costume-controller
├── BlendshapeControllerGenerator/    # top.sealoong.unitybox.blendshape-controller-generator
├── AvatarColliderMonitor/            # top.sealoong.unitybox.avatar-collider-monitor
├── BuildPipelineLogger/              # top.sealoong.unitybox.build-pipeline-logger
├── PrefabDiffPatcher/                # top.sealoong.unitybox.prefab-diff-patcher
├── AvatarSecuritySystem/             # top.sealoong.unitybox.avatar-security-system
└── VisemeBlendshapeOverride/         # top.sealoong.unitybox.viseme-blendshape-override
```

## 工具文档

- Advanced Costume Controller docs: `Docs/AdvancedCostumeController.md`
- Blendshape Controller Generator docs: `Docs/BlendshapeControllerGenerator.md`
- Avatar Security System docs: `Assets/UnityBox/AvatarSecuritySystem/Editor/README.md`
- Viseme Blendshape Override docs: `Docs/VisemeBlendshapeOverride.md`

上列七包仍是本分支保留的代码与资料；其最新修复、独立 Unity 拆分、
`Docs/RefactorChanges.md` 与 `Docs/DependencyArchitecture.md` 在 `refactor`。
切换前保存工作，不要把 refactor 的依赖表或测试结论直接用于本分支七包。
线上 VPM listing 的已发布版本也可能不同于当前功能分支源码。

## VPM 仓库发布说明

- GitHub Actions 会自动通过 `git ls-files 'Assets/UnityBox/*/package.json'` 发现主仓库中已跟踪的包，因此新增包时通常**不再需要手动修改 workflow 里的路径列表**。
- 因为发现范围只包含**主仓库已跟踪文件**，所以像 Git 子模块内容不会被自动发布到当前仓库的 VPM listing 中。

## 许可证

本项目采用 MIT 许可证（MIT）。详见仓库根目录的 `LICENSE` 文件。简要说明：你可以自由地复制、修改、合并、发布、分发、再许可和/或出售本软件的副本，但必须在所有副本或重要部分中包含原始版权声明和本许可声明。软件按"原样"提供，不附带任何明示或暗示的担保。
