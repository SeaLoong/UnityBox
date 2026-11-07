# 六种配置组件的真实旧包升级记录

本记录来自独立合成工程，不载入用户工程。完整结果见 [summary.json](summary.json)、
[三阶段记录](three-stage-summary.json)和[最终源码与状态指纹](final-source-and-state.json)。

| 阶段 | 实际输入与操作 | 结果 |
| --- | --- | --- |
| 旧版创建 | 完整公开 ACC 0.4.13、ASS 0.7.24、Viseme 0.1.14 ZIP，以及明确的未发布 AO 0.2 开发源 | 六种配置的真实 MonoScript GUID、原字节和对象身份已记录 |
| 故障复现 | 完整换入 ACC 0.5.1、ASS 0.9.2、Viseme 0.2.1、AO 0.3.1 候选 ZIP；只打开和导入 | 复现 42 处 Missing Script，原文件保持；SDK Descriptor 仍有效 |
| 手动迁移 | 明确选择 Base/Variant/Nested/MainScene 四个资源，执行一次批量 Apply | 修复 12 个引用，仅写入 Base.prefab 和 Avatar.unity；其它选中资源通过原 Prefab 来源恢复 |

第二阶段的 `passed: true` 表示成功复现预期故障。第三阶段是显式 API 操作，
没有启动或导入自动写入，也没有逐组件确认。单独的 Duplicate.unity 只读扫描并报告
旧无效＋新有效的同类组件，不选入批量写入，文件和两份组件都保留。

## 保存与核对内容

测试创建六种配置：ACC Outfit、Part Group、Variant Material Override、ASS、Viseme、AO。
它们含非默认字段、Material/Renderer/Animator/AudioClip 引用及一个引用全部配置的
fixture-owned MonoBehaviour 数组。资源覆盖 Base、Variant、嵌套 Prefab、四组场景对象、
实例属性覆盖和独立的重复组件案例。

- 保存 50 条组件记录、24 个原资源/metadata 文件；所有资产 GUID 和组件 GlobalObjectID 保留。
- 每次读取核对 **2,080 个持久化属性**，其中 **119 个父 ObjectReference GlobalObjectID**。
- 创建 **24 个当前 Inspector**，等待延迟回调后再次核对，原字段与引用保持。
- 写入前备份与原字节一致；更新文件只改变已确认的 `m_Script.guid` 字节跨度，
  所有序列化 fileID、PPtr、配置、换行和其它内容保留。
- 原 Variant、嵌套关系、实例覆盖及 SDK Descriptor 的类型与配置保持。

旧 fixture 的属性迭代器曾进入 ObjectReference 的 native 实现，记录了 238 个
`*.m_FileID` / `*.m_PathID` 子值。这些是跨 Editor 进程会变化的内部实例标识，
不是 YAML 中的持久化 fileID。最终比较器仅排除这些子项，保留父引用 GlobalObjectID，
并对原始 YAML 的每个 PPtr/fileID 做字节级检查。首次记录没有被删改；各阶段实际执行的
fixture 源码分别保存在 [source-snapshots](source-snapshots/) 中。

## 输入与身份

[input-provenance.json](input-provenance.json)列出三个公开旧 ZIP 的 URL、CRC/SHA 核对来源、
当前候选归档 SHA，以及 AO 的原树和实际测试输入树身份。
AO 使用 [782af0bf8569fdde6782d420761f38c8f8d6824d](https://github.com/SeaLoong/UnityBox/tree/782af0bf8569fdde6782d420761f38c8f8d6824d/Assets/UnityBox/AnimatorOptimizer)
的精确 Git 源；[开发源证明](ao-development-source.json)记录其原文件。
该版本没有公开发行 ZIP。本轮保留全部原 Runtime/Editor 代码和 metadata，
仅排除旧开发 Tests：其中重复的 TestRunner 引用会触发当前 Unity 的 asmdef 校验，
不属于生产组件导入或本轮迁移范围。排除清单保存在 [legacy-run.json](legacy-run.json)。

[generated-script-metadata](generated-script-metadata/)保存 Unity 实际生成的六个旧脚本身份；
[legacy-state.json](legacy-state.json)记录其配置与对象身份。这些旧 GUID 仅属于本合成工程，
不是适用于所有项目的固定旧值。Part Group 的显式类型映射来自本阶段确认的真实脚本，
没有仅凭 `Mode/GroupName` 字段猜测。

## 归档文件

- [legacy.json](legacy.json)、[broken.json](broken.json)、[migrated.json](migrated.json)保存原生断言结果；
  对应 `*-run.json` 保留实际命令、退出码、输入和各阶段源码 SHA。
- `original/`、`post/`、`backup-original/` 使用标准 gzip（mtime=0）保存合成原文件、
  最终文件和实际备份；解压后可直接比较 GUID 跨度。
- [duplicate-before-apply.unity.gz](duplicate-before-apply.unity.gz)是显式添加新组件后的重复案例基线。
- [ComponentUpgradeFixture.cs](ComponentUpgradeFixture.cs)、
  [ComponentUpgradeReferenceHolder.cs](ComponentUpgradeReferenceHolder.cs)与
  [run_upgrade.py](run_upgrade.py)是最终可复用工具；[sha256.json](sha256.json)索引本归档文件。

原始 Editor/launcher 日志和失败诊断留在测试工作区。本归档没有登录信息、许可证内容或
完整启动日志。JSON 内工作区绝对路径用于说明当时来源；本目录中的归档采用相对路径。

## 复现命令

在已配置真实 Unity 2022.3.22f1、SDK 3.8.2、MA 1.18、NDMF 1.11 的机器执行，
将下列 `TASK_*` 变量设置为本机实际路径。工程必须放在源码 checkout 之外的新目录。

```bash
python3 "$TASK_REPO/scripts/unity/test_project.py" prepare \
  --repo "$TASK_REPO" --project "$TASK_PROJECT" --profile vrc --offline \
  --dependency-cache "$TASK_DEPENDENCY_CACHE" \
  --package AdvancedCostumeController --package AvatarSecuritySystem \
  --package VisemeBlendshapeOverride --package AnimatorOptimizer \
  --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar

python3 "$TASK_ARCHIVE/run_upgrade.py" \
  --project "$TASK_PROJECT" --unity "$TASK_UNITY" --xdg-root "$TASK_XDG" \
  --legacy-package "top.sealoong.unitybox.advanced-costume-controller=$TASK_ACC_OLD_ZIP" \
  --legacy-package "top.sealoong.unitybox.avatar-security-system=$TASK_ASS_OLD_ZIP" \
  --legacy-package "top.sealoong.unitybox.viseme-blendshape-override=$TASK_VISEME_OLD_ZIP" \
  --legacy-package "top.sealoong.unitybox.animator-optimizer=$TASK_AO_OLD_SOURCE" \
  --expected-legacy-sha "top.sealoong.unitybox.advanced-costume-controller=$TASK_ACC_OLD_SHA" \
  --expected-legacy-sha "top.sealoong.unitybox.avatar-security-system=$TASK_ASS_OLD_SHA" \
  --expected-legacy-sha "top.sealoong.unitybox.viseme-blendshape-override=$TASK_VISEME_OLD_SHA" \
  --legacy-ao-source 782af0bf8569fdde6782d420761f38c8f8d6824d \
  --legacy-ao-production-only \
  --current-package "top.sealoong.unitybox.advanced-costume-controller=$TASK_ACC_CURRENT_ZIP" \
  --current-package "top.sealoong.unitybox.avatar-security-system=$TASK_ASS_CURRENT_ZIP" \
  --current-package "top.sealoong.unitybox.viseme-blendshape-override=$TASK_VISEME_CURRENT_ZIP" \
  --current-package "top.sealoong.unitybox.animator-optimizer=$TASK_AO_CURRENT_ZIP" \
  --migration-tool "$TASK_MIGRATION_TOOL"
```

旧 ZIP 来源及 SHA 见 `input-provenance.json`；当前 ZIP 用仓库发布打包器生成。
`TASK_AO_OLD_SOURCE` 指精确开发提交中的 AnimatorOptimizer 包目录，保留原 metadata。
`TASK_ARCHIVE` 指本归档目录；`TASK_MIGRATION_TOOL` 指独立工具根目录或其 Editor 目录。
运行器只关闭专用集成工程的 package testables，不通过旧开发 Test Runner 来执行此 fixture。

可加 `--stop-after legacy` 或 `--stop-after broken` 暂停；使用同一工程及
`--resume-from broken` / `--resume-from migrated` 继续。每阶段独立启动 Editor，
断言完成后自行退出，不传提前截断延迟回调的 `-quit`。
