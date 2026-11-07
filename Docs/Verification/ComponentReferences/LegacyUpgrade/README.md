# 插件包内手动恢复的真实升级记录

五个独立合成消费者工程覆盖 ACC、ASS、Viseme、AO 单独安装，以及四插件共存。每个当前阶段只更新对应插件 ZIP；手动恢复 API 随插件安装。

本轮当前包：ACC 0.5.2、ASS 0.9.3、Viseme 0.2.2、AO 0.3.2。

| 消费者 | 类型数 | 复现 Missing | 显式批量调用 | 修复引用 | 每次属性/PPtr 核对 |
| --- | ---: | ---: | ---: | ---: | ---: |
| [acc](Scopes/acc/three-stage-summary.json) | 3 | 21 | 1 | 6 | 472 / 63 |
| [ass](Scopes/ass/three-stage-summary.json) | 1 | 7 | 1 | 2 | 203 / 21 |
| [viseme](Scopes/viseme/three-stage-summary.json) | 1 | 7 | 1 | 2 | 966 / 14 |
| [ao](Scopes/ao/three-stage-summary.json) | 1 | 7 | 1 | 2 | 477 / 21 |
| [all](Scopes/all/three-stage-summary.json) | 6 | 42 | 4 | 12 | 2097 / 119 |

第二阶段 `passed: true` 表示成功复现预期 Missing Script。第三阶段每个已安装插件显式执行一次批量 Scan→Apply；四插件共存时分别调用四个所属 API。各批仅处理自身类型，并核对其它插件的旧 GUID 字节保持。

资源覆盖 Base Prefab、Variant、嵌套 Prefab、四组场景对象、实例覆盖、非默认配置和 Material/Renderer/Animator/AudioClip 引用。每个插件另有旧无效＋新有效同类组件案例，只读扫描后保持完整字节和两份组件。

每批备份与该插件执行前的实际文件一致；后一插件的备份包含前面已完成的 GUID 修复。最终文件只改变确认的 `m_Script.guid` 跨度，资产 GUID、组件 GlobalObjectID、序列化 fileID/PPtr、字段、换行与其它字节保持。

重复案例的旧 MonoBehaviour YAML 文档还逐字节对照最初创建、显式添加新组件后及最终状态，原配置文档完整保留。

原字段在全部批次后和 Inspector OnEnable/延迟回调后各核对一次。ObjectReference 只记录父 GlobalObjectID；原 YAML 的 PPtr/fileID另做字节核对。实际数量由各 scope 的 `migrated.json.comparisonPasses` 给出。

公开旧版为 ACC 0.4.13、ASS 0.7.24、Viseme 0.1.14 完整 ZIP，导入前核对 CRC/SHA256。AO 使用未发布开发提交 `782af0bf8569fdde6782d420761f38c8f8d6824d` 的精确 Runtime/Editor 源，仅排除旧开发 Tests；来源和排除列表完整记录。

第一阶段即保存原状态、Unity 实际生成的脚本/helper metadata、旧 DLL 指纹和原资源字节。当前阶段仅重建专用工程 Library，并逐项核对原资产/meta 保持；冷导入检验实际磁盘 GUID。

每个 `Scopes/<scope>/` 保存原生三阶段结果、命令、来源、原始创建状态、每阶段实际 fixture 源、生成 metadata、gzip 原文件/最终文件/逐插件备份及源码指纹。源码指纹同时匹配实际编译包、当前 ZIP 和 checkout。Editor/launcher 日志与原生 DLL 保留在专用测试工作区。

## 复现

先用仓库 `scripts/unity/test_project.py prepare --profile vrc` 准备专用消费者工程，`--package` 只选择该 scope 的插件；添加真实 SDK、MA、NDMF 依赖。然后执行本目录运行器：

```bash
python3 "$TASK_REPO/scripts/unity/test_project.py" prepare \
  --repo "$TASK_REPO" --project "$TASK_PROJECT" --profile vrc --offline \
  --dependency-cache "$TASK_DEPENDENCY_CACHE" --package AdvancedCostumeController \
  --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar

python3 "$TASK_ARCHIVE/run_upgrade.py" --scope acc \
  --project "$TASK_PROJECT" --unity "$TASK_UNITY" --xdg-root "$TASK_XDG" \
  --legacy-package "top.sealoong.unitybox.advanced-costume-controller=$TASK_ACC_OLD_ZIP" \
  --expected-legacy-sha "top.sealoong.unitybox.advanced-costume-controller=$TASK_ACC_OLD_SHA" \
  --current-package "top.sealoong.unitybox.advanced-costume-controller=$TASK_ACC_CURRENT_ZIP" \
  --cold-upgrade-import
```

其它单插件使用 `--scope ass`、`viseme`、`ao` 并替换对应输入；共存使用 `--scope all` 并提供全部四包。AO 添加 `--legacy-ao-source 782af0bf8569fdde6782d420761f38c8f8d6824d --legacy-ao-production-only`。旧 ZIP URL/SHA、当前 ZIP 身份及 AO 源证明见 [input-provenance.json](input-provenance.json)。

可用 `--stop-after legacy` 暂停，再以同一 scope、当前 ZIP 输入和 `--resume-from broken --cold-upgrade-import` 继续。每阶段断言完成后自行退出。

[summary.json](summary.json)汇总完成结果；[sha256.json](sha256.json)索引全部归档文件。
