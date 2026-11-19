# ASS 0.9.10：时间行数值框修正与禁用防御默认值

手势时间三行是滑块与延迟数值框的混合控件：滑块范围由当前 `hold`／`tolerance` 推导
（hold 为 `max(0.05s, tolerance)–1s`，tolerance 为 `0–hold`，maximum 为
`max(1s, hold+tolerance+0.05s)–10s`），拖动和数值框提交都调用同一个提交函数，
继续保留旧值只读绘制、显式“修正时间”和 Undo/Redo。

0.9.9 首发时滑块用 `EditorGUI.Slider`，它自带一个数值框，与右侧延迟数值框叠加成每行两格
（由用户截图发现）。0.9.10 改用无内建数值框的 `GUI.HorizontalSlider`，每行只保留一个
延迟数值框；范围、提交路径与只读绘制语义不变。

「禁用防御」恢复默认关闭：新建组件与显式 Reset 在「默认启用防御」关闭时生成受密码保护的
防御层；只有手动勾选才跳过防御生成。旧组件已保存的值不变，Standalone 构建仍在配置副本中
强制跳过防御。

## 验证范围

Unity 2022.3.22f1、UTF 1.1.33、NUnit 1.0.6，Windows 11（`Windows-11-10.0.26300-SP0`，16 线程）。
隔离工程由 `scripts/unity/test_project.py prepare --profile core --package AvatarSecuritySystem`
生成，只复制当前 ASS 0.9.9 源码与测试。

- core：排除 `UnityBoxPerformance;UnityBoxGraphics`，执行
  `UnityBox.AvatarSecuritySystem.Editor.Tests` 全部功能用例。
- graphics：真实 Editor 窗口（`--graphics`，不传 `-nographics`）绘制含旧非法时间的
  Inspector，核对不写回组件、Prefab 字节与 metadata 不变。
- SDK：同源 ASS 包复制到独立工程，嵌入真实 `com.vrchat.base`／`com.vrchat.avatars` 3.8.2 与
  `nadena.dev.ndmf` 1.11，执行 `UnityBox.AvatarSecuritySystem.VRChat.Tests`（43）与
  `UnityBox.AvatarSecuritySystem.NDMF.Tests`（5）。该范围不装 MA，避免与本包无关的编辑器包干扰。
- 基线对照：同一机器用 `a1930d6`（0.9.8）源码准备独立工程，执行同一 core 范围，确认失败项
  与本次改动无关。

SDK 工程不能用 `-nographics`：该批真实 SDK 包在无图形设备时启动即退出；因此 SDK 与图形
范围使用有图形设备的 batchmode，core 使用 `-nographics`。

## 实际结果

| 范围 | 实际结果 |
| --- | --- |
| 0.9.10 core（功能） | 116 用例：114 通过，2 失败 |
| 0.9.10 graphics | 1/1 通过（真实窗口绘制，旧值不写回） |
| 0.9.10 SDK | 48/48 通过（VRChat 43，NDMF 5） |
| 0.9.10 打包 | `package_vpm.py` 生成 149 条目 ZIP，manifest 版本 0.9.10，含 `GUI.HorizontalSlider` 修正 |
| 0.9.8 基线 core | 108 用例：106 通过，2 失败（同一批迁移用例） |
| 工具与打包 unittest | `scripts/unity` 44/44 通过；`.github/scripts` 23 条中 18 通过，5 条为 Windows 文本/权限/默认编码差异（见下） |

新增与关键用例：

- `GestureTimingTests.SliderRangeOffersOnlyValuesTheTimingContractAccepts`（8 组输入）：
  保持、容错、最大等待的滑块端点和中点都落在共享提交契约内，提交后不会被夹回；
  旧非法值（0.15s/0.3s）的区间同样成立。
- `GestureTimingTests.InspectorTimingEditsApplyDependentLimitsAsOneUndoableEdit`、
  `InspectorExplicitTimingCorrectionRepairsLegacyValuesAndCanUndoToTheOriginalData`：
  提交路径与 Undo/Redo 保持原行为。
- `AnimationCoreTests.ConfigurationSnapshotOwnsPasswordListAndPreservesSafeDefaults`：
  新组件 `disableDefense=false`、`defaultEnableDefense=false`。
- `BuildAssetIsolationTests.NewComponentGeneratesUnlockedDefenseWithoutActiveLoadDefense`：
  真实 SDK 组件默认值，SDK 范围通过。
- `GestureTimingInspectorGuiTests.OpeningAndDrawingLegacyTimingValuesPreservesTheComponentAndPrefabBytes`：
  绘制 `0.15s/0.3s/0.1s` 的旧组件不写回字段、不弄脏场景、Prefab 与 metadata 字节不变。

## 数值框重复的修正依据

Unity 2022.3 的 `EditorGUI.Slider` 在内部 `DoSlider` 中固定绘制滑块加一个 `DoFloatField`
数值框（宽度为 `EditorGUIUtility.fieldWidth`），所以 0.9.9 的滑块区间右侧又出现一格，
与延迟数值框并排。`GUI.HorizontalSlider` 只绘制 `skin.horizontalSlider` 与
`horizontalSliderThumb`，没有数值框；0.9.10 改用它，因此每行恰好一个可输入数值框。
修正后重新执行图形绘制用例（真实窗口）与 core／SDK 范围，结果见上表。

## 两个 core 失败的结论

失败用例是 `ComponentMigrationTests` 的
`ExplicitBatchPreservesEveryByteExceptSelectedScriptGuidAndBacksUpOriginal` 与
`WholeProjectUpgradeRestoresCleanSavedSceneAndKeepsUnrelatedDirtySceneInstances`：
`DirectoryNotFoundException`，备份目标路径长度为 260 字符，正好撞上 Windows `MAX_PATH`。
未改动的 `a1930d6` 基线在同一机器上失败同样两条；历史 Linux 云工具链路径较短时通过。
该限制来自 `MigrationEngine` 的备份根拼写，与本次时间控件／默认值改动无关，本次不修改。

## 打包 unittest 的 5 条 Windows 差异

`.github/scripts` 套件按 Linux 环境编写：用例 fixture 写入 `\n`、检查 ZIP 可执行位（0o755）
并以 UTF-8 解码历史 listing；Windows 上分别得到 `\r\n`、420/493 权限和 GBK 默认解码，
因此 4 条失败、1 条 error。它们与本次 ASS 改动无关；实际 `package_vpm.py` 打包成功
（149 条目、`0.9.9`），`scripts/unity` 套件 44/44 通过。完整 Linux 打包检查仍由 CI 执行。

## 复现命令

```powershell
python scripts/unity/test_project.py prepare --repo <checkout> --project <core-project> `
  --profile core --package AvatarSecuritySystem --dependency-cache <cache>
python scripts/unity/test_project.py run --project <core-project> --unity <Unity.exe> `
  --exclude-category "UnityBoxPerformance;UnityBoxGraphics" --timeout 1500
python scripts/unity/test_project.py run --project <core-project> --unity <Unity.exe> `
  --graphics --test-category UnityBoxGraphics `
  --test-filter OpeningAndDrawingLegacyTimingValuesPreservesTheComponentAndPrefabBytes
```

SDK 范围（同源包 + 真实 SDK/NDMF，不传 `-nographics`）：

```powershell
<Unity.exe> -batchmode -projectPath <sdk-project> -runTests -testPlatform EditMode `
  -testResults <sdk-project>\results.xml -logFile <sdk-project>\tests.log `
  -assemblyNames "UnityBox.AvatarSecuritySystem.VRChat.Tests;UnityBox.AvatarSecuritySystem.NDMF.Tests" `
  -testCategory "!UnityBoxGraphics;!UnityBoxPerformance"
```

## 归档

- `core-results.xml.gz`、`graphics-results.xml.gz`、`sdk-results.xml.gz`：本次实际 NUnit 结果。
- `baseline-results.xml.gz`：`a1930d6` 未改源码的 core 对照结果。
- [`sha256.json`](sha256.json)：源码包文件数与 SHA256、基线包 SHA256、各归档 SHA256、运行环境。

源码状态为当前分支 ASS 0.9.10；完整提交标识见对应提交说明。

本次在 Windows 本地执行；`Docs/VerificationResults.json`、`UnityNativeResults.json` 仍为
上一次 Linux 完整工具链的记录，待下一次完整工具链运行（`capture_evidence.py`）时按当时源码
重新生成，本页归档即为本次实际运行证据。
