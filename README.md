# UnityBox

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
| Prefab Diff Patcher             | 比较两个 Prefab 并按选择合并对象、组件与引用 |
| Avatar Security System          | VRChat Avatar 手势解锁、显示锁定与反馈工具                          |
| Viseme Blendshape Override      | 用内建 `Viseme` / `Voice` 参数驱动可限幅、可独立响应区间的口型替代层 |

## 目录结构

每个工具都是独立的 VPM 包，位于 `Assets/UnityBox/` 下：

```
Assets/UnityBox/
├── AdvancedCostumeController/        # top.sealoong.unitybox.advanced-costume-controller
├── BlendshapeControllerGenerator/    # top.sealoong.unitybox.blendshape-controller-generator
├── AvatarColliderMonitor/            # top.sealoong.unitybox.avatar-collider-monitor
├── BuildPipelineLogger/              # top.sealoong.unitybox.build-pipeline-logger
├── PrefabDiffPatcher/                # top.sealoong.unitybox.prefab-diff-patcher
├── AvatarSecuritySystem/             # top.sealoong.unitybox.avatar-security-system
└── VisemeBlendshapeOverride/         # top.sealoong.unitybox.viseme-blendshape-override
```

## 工具文档

所有包要求 Unity 2022.3；下表列出额外依赖的最低版本，具体以各包 `package.json` 为准。

| 工具 | 当前说明 | 依赖 |
| --- | --- | --- |
| ACC | [完整手册](Docs/AdvancedCostumeController.md)、[包内说明](Assets/UnityBox/AdvancedCostumeController/README.md) | VRChat Avatars ≥3.7、Modular Avatar ≥1.18 |
| Blendshape 生成器 | [完整手册](Docs/BlendshapeControllerGenerator.md) | VRChat Avatars ≥3.7、Modular Avatar ≥1.13 |
| 碰撞体监控 | [使用与采样边界](Assets/UnityBox/AvatarColliderMonitor/README.md) | VRChat Avatars ≥3.7；NDMF 可选 |
| 构建日志 | [使用与日志范围](Assets/UnityBox/BuildPipelineLogger/README.md) | VRChat Avatars ≥3.7；NDMF 可选 |
| Prefab 差异合并 | [使用与合并语义](Assets/UnityBox/PrefabDiffPatcher/README.md) | 不要求 VRChat SDK |
| ASS | [用户手册](Assets/UnityBox/AvatarSecuritySystem/Editor/README.md)、[技术参考](Assets/UnityBox/AvatarSecuritySystem/Editor/ASS_TechnicalDoc.md) | VRChat Avatars ≥3.7；NDMF 可选 |
| 口型覆盖 | [完整手册](Docs/VisemeBlendshapeOverride.md)、[包内说明](Assets/UnityBox/VisemeBlendshapeOverride/README.md) | VRChat Avatars ≥3.7、NDMF ≥1.7 |

测试环境、命令、测试范围与未验证项见 [测试指南](Docs/Testing.md)。
`feat/animator-optimizer` 的插件名是 **Animator Optimizer**，不等同于外部的 Avatar Optimizer；它尚未合入本分支。

## VPM 仓库发布说明

- GitHub Actions 会自动通过 `git ls-files 'Assets/UnityBox/*/package.json'` 发现主仓库中已跟踪的包，因此新增包时通常**不再需要手动修改 workflow 里的路径列表**。
- 因为发现范围只包含**主仓库已跟踪文件**，所以像 Git 子模块内容不会被自动发布到当前仓库的 VPM listing 中。

## 开发与审查

- 全仓修复和文档更新位于 `refactor`；Animator 优化器在 `feat/animator-optimizer` 独立审查，该分支已 rebase 到 `master`。
- 全仓功能审查、修复及待验证项见 [插件审查记录](Docs/PluginAudit.md)。
- 包构建测试：`python3 .github/scripts/test_package_vpm.py`。发布包保留已有 `.meta`，排除生成目录和 ASS 实验探针；Editor 回归测试源码保留。
- 已缺失的 `.meta` 需要从原开发项目恢复。不要重新生成已有脚本的 GUID 来修补场景引用。

## 许可证

本项目采用 MIT 许可证（MIT）。详见仓库根目录的 `LICENSE` 文件。简要说明：你可以自由地复制、修改、合并、发布、分发、再许可和/或出售本软件的副本，但必须在所有副本或重要部分中包含原始版权声明和本许可声明。软件按"原样"提供，不附带任何明示或暗示的担保。
