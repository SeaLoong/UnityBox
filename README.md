# UnityBox

用于普通 Unity 和 Avatar 编辑流程的一组 Unity 2022.3 工具。当前 `refactor` 分支拆分了
独立核心与可选 VRChat/Modular Avatar/NDMF 适配；完整变更、升级后果和剩余限制见
[变更清单](Docs/RefactorChanges.md)。Animator Optimizer 在独立
`feat/animator-optimizer`，已 rebase 到 master，尚未合并；它不等同于外部 Avatar Optimizer。

## 安装

通过 [VCC / ALCOM 添加入口](https://sealoong.github.io/UnityBox/) 或手工添加 VPM 源：

```text
https://sealoong.github.io/UnityBox/vpm.json
```

普通 Unity 可使用源码或本地 UPM 包。当前分支不再强制安装所有 Avatar 依赖；已有 Avatar
工程保留所需 SDK/MA/NDMF。线上 listing 版本可能尚未包含本分支改动，安装后应检查实际版本。

## 工具与文档

| 工具 | 普通 Unity 功能 | 可选平台功能与最低条件 | 文档 |
| --- | --- | --- | --- |
| Advanced Costume Controller | 扫描、服装/部件/Mixer/材质动画资产生成 | SDK ≥3.7.3、MA ≥1.18：菜单、FX、骨架、图标和网络参数 | [手册](Docs/AdvancedCostumeController.md) |
| Blendshape Controller Generator | Float/Bool blendshape控制器生成窗口 | SDK ≥3.7.3、MA ≥1.13：菜单、FX和同步参数 | [手册](Docs/BlendshapeControllerGenerator.md) |
| Avatar Collider Monitor | 任意根对象3D/2D碰撞体快照、历史和场景采样 | SDK Descriptor/provider、独立NDMF阶段采样 | [说明](Assets/UnityBox/AvatarColliderMonitor/README.md) |
| Build Pipeline Logger | Unity导入与构建日志 | 独立SDK/NDMF回调日志 | [说明](Assets/UnityBox/BuildPipelineLogger/README.md) |
| Prefab Diff Patcher | Prefab比较、选择合并、引用重映射 | 无额外依赖 | [说明](Assets/UnityBox/PrefabDiffPatcher/README.md) |
| Avatar Security System | 配置、Controller工具和显式行为适配的图构造API | SDK ≥3.7：手势/锁定/反馈；NDMF另需≥1.8 | [用户手册](Assets/UnityBox/AvatarSecuritySystem/Editor/README.md)、[技术参考](Assets/UnityBox/AvatarSecuritySystem/Editor/ASS_TechnicalDoc.md) |
| Viseme Blendshape Override | 口型Controller/Clip生成、映射和Voice曲线 | SDK ≥3.7：Avatar输入与构建；NDMF另需≥1.7 | [手册](Docs/VisemeBlendshapeOverride.md) |

SDK指真实VRChat Base/Avatars组合。MA使用的API从SDK3.7.3才存在，3.7.0～3.7.2不能
据宽泛依赖声明视为兼容。当前完整验证组合固定SDK3.8.2、MA1.18、NDMF1.11。
各包位于`Assets/UnityBox/<工具目录>`，独立清单使用`top.sealoong.unitybox.*`名称。
程序集/API迁移、组件身份与构建边界见[依赖架构](Docs/DependencyArchitecture.md)。

## 开发与验证

- [插件审查](Docs/PluginAudit.md)、[全部改动及后果](Docs/RefactorChanges.md)、[temp审查](Docs/TempBranchReview.md)
- [测试指南与实际结果](Docs/Testing.md)、[Unity安装和隔离工程](Docs/UnityEnvironment.md)
- [可复用测试skill](.agents/skills/unitybox-testing/SKILL.md)、[仓库规则](AGENTS.md)

Unity2022.3.22f1已安装。最终真实C#类型检查通过：无SDK19、仅SDK40、完整平台55个
程序集；插件共声明177个回归用例，尚未实际执行。实际Unity测试
启动因没有许可证exit1，没有NUnitXML；用户当前无授权，因此EditMode、导入、构建上传
及运行时行为尚未获通过结论。Python测试和ZIP校验不能替代它们。

共享工具测试：`python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v`，要求
Python3.12；打包测试：`python3 .github/scripts/test_package_vpm.py`。
源场景、实验资产和已知GUID保留；原本未提交的metadata需从开发项目恢复，不能生成随机
GUID修复旧组件引用。生成器升级后应重新生成并检查参数名、旧引用、Undo和已保存值。

## VPM 发布

GitHub Actions从已跟踪的`Assets/UnityBox/*/package.json`发现包，不自动包含子模块。
发布筛选器保留已有meta与Editor回归测试，排除Generated和ASS实验探针/fixture/bundle。
ZIP覆盖重建，避免旧文件残留；本轮未代为推送、发布或合并两个分支。

## 许可证

本项目采用 [MIT License](LICENSE)。软件按原样提供，使用和分发时保留版权及许可声明。
