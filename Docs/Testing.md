# 测试与验证

## 当前执行状态

`refactor` 包含 7 个插件的 Editor 回归测试和 Python 打包测试。
本轮云端运行了 Python 测试、实际 VPM 打包/校验以及 C# 语法和装配配置检查。
云端没有 Unity Editor、许可证及完整 SDK 项目，**Unity 编译和 EditMode 用例均未运行**。
游戏内、真实上传链、图标渲染及第三方组合不能从语法或 ZIP 校验推断通过。

`feat/animator-optimizer` 是独立分支，包含自己的优化器用例和审查记录。
切换分支前先保存工作；不要把某分支的源码、测试结果或功能说明套用到另一分支。

## Python 发布测试

从仓库根目录运行，使用 Python 3.9 或更高版本，无额外包依赖：

```sh
python3 .github/scripts/test_package_vpm.py -v
```

测试覆盖 `.meta`/GUID 保留、排除生成目录及 ASS 实验、重复打包清除旧文件、
保留其他包的测试资产。GitHub workflow 在打包前执行同一组测试。
这些测试不会发布 GitHub Pages，也不会执行 Unity 逻辑。

## Unity EditMode

1. 使用 Unity 2022.3 项目，按各包 manifest 安装 VRChat/MA/NDMF 依赖。恢复原项目
   缺失的 `.meta`，不要给已有组件重新分配 GUID 来修复引用。
2. 从 Unity Package Manager 安装兼容的 Unity Test Framework。
3. 代码位于 `Assets/UnityBox` 时由 Editor 导入测试装配。按 VPM/UPM 包安装时，
   将待测试包完整名称加入项目 `Packages/manifest.json` 顶层 `testables` 数组，
   保留已有依赖和其他设置。
4. 在 **Window > General > Test Runner > EditMode** 运行相关装配，确认用例实际执行，
   将失败、跳过与零用例区分记录。

| 插件 | 测试装配 | 主要覆盖 |
| --- | --- | --- |
| ACC | `UnityBox.AdvancedCostumeController.Editor.Tests` | 分组、父容器、菜单、选择、命名、转换失败保护 |
| Blendshape | `UnityBox.BlendshapeControllerGenerator.Tests` | 反向、参数/路径冲突、重复生成及旧引用 |
| ASS | `UnityBox.AvatarSecuritySystem.Editor.Tests` | 资产隔离、参数/配置、内存 Controller、安全默认值 |
| 口型 | `UnityBox.VisemeBlendshapeOverride.Editor.Tests` | FX、映射、阈值、路径和回退限制 |
| Prefab | `UnityBox.PrefabDiffPatcher.Editor.Tests` | 选择、变换、引用、组件身份、输出 GUID |
| Collider | `UnityBox.AvatarColliderMonitor.Tests` | 快照、属性变化、Avatar 隔离和历史上限 |
| Logger | `UnityBox.BuildPipelineLogger.Tests` | 连续构建的日志状态 |

可用实际 Editor 路径执行批处理，不附加会让测试提前退出的 `-quit`：

```sh
"$UNITY_EDITOR" -batchmode -nographics -projectPath "/path/to/project" \
  -runTests -testPlatform EditMode \
  -testResults "/path/to/results.xml" -logFile "/path/to/editor.log"
```

检查退出状态与本次新生成的 XML，确认测试数大于零。可通过 `-assemblyNames`
限定单个测试装配。用例创建并清理自己的临时对象/资产，建议在测试项目中运行。

## 需要人工验证的行为

- SDK 回退和 NDMF 路径分别连续构建，检查源 Controller、参数资产及场景引用。
- 检查 MA 最终 FX、总参数预算和多插件同时写同一动画绑定的情况。
- 口型测试 `Viseme` 0–14 和不同 `Voice` 值；服装测试两种 Mixer、材质、Undo。
- Prefab 用副本测试保存、RectTransform、嵌套 Prefab 和第三方自定义序列化。
- ASS 关闭主动负载，测试密码/锁定/反馈；负载功能不是已验证的可靠保护措施。
- ASS 历史 bundle 需通过实验菜单重建，旧二进制或旧日志不能证明本次通过。
