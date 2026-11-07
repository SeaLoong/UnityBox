# 功能、Player、图形与性能测试

本指南使用 `master` 的同一个 checkout，执行八个插件的核心、平台、图形、Player 与性能测试。

## 环境和结果索引

配置另一台云机器时，直接使用[可复制的安装与测试提示词](UnityCloudSetupPrompt.md)。

固定 Editor **2022.3.22f1**、UTF **1.1.33**、NUnit **1.0.6**；完整依赖组合为真实
SDK Base/Avatars **3.8.2**、MA **1.18.0**、NDMF **1.11.0**。当前云工具链使用
共享 XDG `/workspace/unitybox-setup/unity/interactive`；机器授权与 Hub 复用见[环境流程](UnityEnvironment.md)。

当前六类组件的真实升级、手动迁移和源配置保全检查见
[Component References 记录](Verification/ComponentReferences/README.md)；
[ASS 0.9.1 导入安全记录](Verification/ASSImportSafety/README.md)保留上一版范围。
[验证结果](VerificationResults.json)记录当前八插件及独立工具的功能、程序集、Player、图形和退出码；
[原生结果](UnityNativeResults.json)保留 NUnit 运行与源内容指纹；
[性能报告](Performance.md)和[原始测量](PerformanceResults.json)记录规模与串行样本。
每个依赖范围是一次独立执行，同一核心用例在不同范围重复运行；按各次运行记录计数。

## 执行结果与归档

[验证结果](VerificationResults.json)记录本轮最终源码的实际通过数、测试程序集、运行标识、
命令、退出码和源文件指纹。关键 NUnit XML 使用标准 gzip 归档，Player 的构建、执行与
流水线 JSON 一起保存在 `Docs/Verification/`，索引记录归档路径和 SHA256。

功能、图形、Player 和串行基准分别执行。纯 Unity、SDK 和完整 MA/NDMF 范围会重复执行
核心用例，各范围分别计数。历史数据通过原提交和运行标识定位；重新准备工程后按新快照
执行并生成新结果。

## 当前组件引用与源配置检查

2026-11-07 最终源码的各范围结果如下，均无失败或跳过：

| 范围 | 实际结果 |
| --- | --- |
| 纯 Unity 核心 | 361 项，9 个测试程序集 |
| SDK | 442 项，14 个测试程序集 |
| 完整 SDK/MA/NDMF | 541 项，20 个测试程序集 |
| 图形 | 2 项，包括真实 Inspector 重绘及图标像素 |
| 串行基准 | 39 项，八插件加独立扫描工具共 60 条测量 |
| Linux Player | 13 场景、1,557 帧、4,807 次检查；源场景 3 个编辑配置保留 |
| Python / 发布打包 | 45 / 18 项 |

[组件引用矩阵](ComponentReferences.md)覆盖八个包中的六种自有生产组件，区分真实旧
ACC/ASS/Viseme 发布 ZIP 与 AO 0.2 开发快照。测试在独立工程保存旧包实际生成的 GUID、
配置、fileID 和引用，再更新对应完整包；新版本启动/导入仍保持原字节，手动迁移后
检查脚本连接与全部其它序列化内容。

工具回归检查只读 Analyze、选定范围、依赖追溯不扩写、歧义旧 GUID 一次类型选择、
一键批量无逐项确认、已打开 Scene/PrefabStage 及依赖跳过、未知/重复保留、源 SHA
变化检查、全部目标先备份以及 metadata/BOM/CRLF/fileID 保全。
ACC Inspector 的显式提交与 Undo、AO 配置/Options 副本、Viseme 深拷贝 Voice 解析和
ASS 时间副本分别检查源字段及原文件。执行命令、数量、退出码和指纹由
[本轮记录](Verification/ComponentReferences/README.md)逐范围列出。

## ASS 0.9.1 导入与配置保全（历史发布）

该版移除旧 ASS 自动迁移器、启动/导入扫描、GUID 改写、场景重载及修复菜单/API，并停止
组件加载时的 OnValidate 参数纠正。测试在隔离工程比较导入、打开前后的 Scene/Prefab
字节、脚本 GUID、既有字段及编辑状态；生成图的规范化只在配置副本中发生。
该版执行命令、退出码、用例数和源指纹单列于[ASS 导入安全记录](Verification/ASSImportSafety/README.md)。

Missing Script 的排查记录应包括组件实际 `m_Script` GUID、脚本路径、所属包和
Console 首条编译错误。导入保全测试只对隔离 fixture 操作，原项目资源留在原处。

## 0.9.0 全库基线（历史快照）

以下为 2026-11-07、`036062d` 发布快照的执行结果；三个功能范围分别计数，
这些数字不作为 0.9.1 改动后的执行数量：

| 范围 | 实际结果 |
| --- | --- |
| 纯 Unity 核心 | 352 / 352，通过 8 个测试程序集 |
| SDK | 431 / 431，通过 13 个测试程序集 |
| 完整 MA/NDMF | 528 / 528，通过 19 个测试程序集 |
| 图形 | 1 / 1，实际渲染像素与图标输出通过 |
| 独立串行性能 | 36 / 36，八插件共 57 条测量记录 |
| Linux Player | 构建和运行成功；13 个场景、1,557 帧、4,807 次输出检查通过；源场景 3 个编辑配置保留 |

全部用例均无失败或跳过。手势输入的 Player 稳态更新中位数为 0.031083 ms；
该历史快照的生成成本及其它插件对照见[当时的性能报告](https://github.com/SeaLoong/UnityBox/blob/036062df594fe3c1d575d600aec68bc5f79f85be/Docs/Performance.md)。

该快照的实际测试程序集、UTC 运行标识和源码指纹集中于[历史验证结果](https://github.com/SeaLoong/UnityBox/blob/036062df594fe3c1d575d600aec68bc5f79f85be/Docs/VerificationResults.json)。
纯 Unity、SDK、完整 MA/NDMF 各范围重复核心用例，分别计数；图形、Player 和串行性能
使用独立记录。2026-11-05 的主分支基线结果保存在 `827b865` 提交，0.9.0 结果按其源码
快照记录，原始 NUnit gzip、Player/构建 JSON 与工具输出保存在该提交的 `Docs/Verification/`；
当前目录保存本轮记录。

0.9.0 当时的 ASS 检查包括（自动迁移实现现已撤回）：

- 真实旧随机 GUID 的 Scene/Prefab 升级、stripped 引用及后续导入，检查配置、组件 fileID、
  资产 GUID、音频、Prefab 覆盖和修复前字节备份；未保存修改延后、重复修复幂等。
- Player 构建场景自动生成 Animator 和输入组件；源场景配置和源 Controller/AOC 的用户图
  与 WD 保留，旧 ASS 层在副本中清理，新增 ASS 状态 WD Off，用户对象缩放保持。
- 连续正确 Holding、快速遍历拒绝累积、中断重计、重复位释放、容错与超时边界、Restart；
  默认保持 0.5s、容错 0.3s；0.9.1 将时间规范化限定于生成配置副本，源字段不在加载时改写。
- 仅启用诱饵状态、关闭诱饵层及无 Bool 参数池的混淆组合，检查声明的参数与转移条件类型。
- 英语、简体、繁体和日语界面/校验说明，SDK/NDMF 真实构建与最终提交。

Player 继续检查编辑配置从产物移除、运行时输入/状态动作保留。Build Pipeline Logger 的
完成轨迹覆盖 PrepareForBuild、preprocess、场景与 Linker，实际顺序见归档 JSON。
图形运行设备、逐场景性能收益与开销、计数 API 的具体字段定义见[性能报告](Performance.md)。

### 历史自动迁移归档

[0.9.0 三阶段记录](Verification/ASSUpgrade/README.md)保存旧包、故障复现及已撤回自动
迁移器的执行证据，仅用于追溯旧实现。当前包的验收要求源资源保全，不继续运行旧自动
修复断言，也不在用户工程中重现该旧迁移流程。

## 准备独立工程

测试工程位于 checkout 外，复制当前源包及真实依赖，保留 metadata 和测试。
准备标记记录来源、提交、实际复制的内容 SHA256、依赖和预期测试程序集。

```sh
python3 scripts/unity/test_project.py prepare \
  --repo /workspace/UnityBox \
  --project /workspace/unitybox-setup/unity/projects/master-core \
  --profile core --offline
```

完整依赖使用另一个目录，选择 `--profile vrc --offline`，再加
`--dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar`。
普通核心由 Unity 和 UTF/NUnit 组成。只检查 SDK 回退时使用 `--profile vrc --offline`；
完整集成再显式加入上述 NDMF/MA 依赖。平台入口随实际安装的包启用。

完整八插件平台范围示例：

```sh
python3 scripts/unity/test_project.py prepare \
  --repo /workspace/UnityBox \
  --project /workspace/unitybox-setup/unity/projects/master-full \
  --profile vrc --offline \
  --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar
```

省略 `--package` 时准备 `master` 的全部八包；按需重复传 `--package` 选择具体插件。
每次源码快照使用独立输出目录。缺少缓存依赖时先运行 `fetch_dependencies.py`；
准备工具仅更新自己拥有的输出目录。

## 功能测试

```sh
python3 scripts/unity/test_project.py run \
  --project TEST_PROJECT \
  --xdg-root /workspace/unitybox-setup/unity/interactive \
  --exclude-category 'UnityBoxPerformance;UnityBoxGraphics' \
  --timeout 1200
python3 scripts/unity/test_project.py report --project TEST_PROJECT
```

将 `TEST_PROJECT` 替换为准备的目录。运行器显式传递本仓库测试程序集，实际执行
`-batchmode -nographics -runTests -testPlatform EditMode`；每次结果存入独立
`.unitybox-test-results/<run_id>/`。报告读取真实退出码、XML、程序集及输出断言。

功能范围包括 Animator 优化的参数/WD/Mask/状态历史与最终图、服装/Parts/Mixer、
组合 Blendshape、ASS 连续手势与自动构建、口型/Voice、Prefab 重载与 Variant、碰撞体历史/几何、
日志和 Linker。平台范围追加真实 SDK、MA 与 NDMF 的安装、预算、回调和最终 Controller 交接。

具体生成、迁移和恢复操作见 [变更清单](RefactorChanges.md)。

## 独立 Linux Player

```sh
python3 scripts/unity/verify_player.py \
  --project TEST_PROJECT \
  --xdg-root /workspace/unitybox-setup/unity/interactive \
  --timeout 1200
```

工具在隔离工程注入验证脚本，创建真实场景，以 Linux Mono Release 构建，并运行产出的
Player。场景提供者生成自己的持久化 Controller、Clip、Mesh 和对象；测试按步骤写输入、
手动推进 Animator、检查 Transform/可见性/Blendshape/参数输出。每个选中的运行时插件
提供对应 hook，组合工程核对提供者和场景覆盖。报告包含构建结果、实际输出检查、逐
Animator 稳态耗时、运行包大小和构建轨迹，存入 `.unitybox-player-results/<run_id>/`。

ASS 构建回调先在场景副本自动复制原 Controller 并生成手势图，再移除编辑配置；AO 与
口型配置同样从构建副本移除。产物保留 Animator、ASS 运行时输入和状态动作。构建后重载保存的源场景，核对场景字节和配置组件保留；Player 再检查配置已移除。
输出断言完成后，每个 Animator 先预热50次，再采集11组、每组200次更新；这项计时描述
最后一组输入下的稳态 `Animator.Update` 成本。托管堆是整个 Player 的保留堆大小；
采集器不提供有效分配或 Controller 内存计数时，报告用 `-1` 标记该计数字段。

## 图形输出

图标用例属于 `UnityBoxGraphics`，使用已有私有桌面的 DISPLAY/Xauthority 启动：

```python
import subprocess
import sys
sys.path.insert(0, '/workspace/unitybox-setup/desktop')
from desktop_session import environment
subprocess.run([
    sys.executable, 'scripts/unity/test_project.py', 'run',
    '--project', 'TEST_PROJECT', '--graphics',
    '--test-category', 'UnityBoxGraphics',
    '--xdg-root', '/workspace/unitybox-setup/unity/interactive'
], env=environment(), check=True)
```

该范围读取自己创建的 Camera/Texture 像素并检查图标内容。平台测试工程装入所需 MA/SDK，
图形范围通过独立命令执行。

## 串行性能基准

```sh
python3 scripts/unity/test_project.py run \
  --project TEST_PROJECT --benchmark \
  --xdg-root /workspace/unitybox-setup/unity/interactive \
  --timeout 1200
```

`--benchmark` 选择 `UnityBoxPerformance`，读取真实 `UNITYBOX_BENCHMARK` JSON 行。
每个场景先预热再重复采样，记录规模、样本数、中位数、p95、最小/最大耗时和正确性检查。
生成/持久化与 Animator 更新使用独立场景；缓存路径与参考路径保留各自测量项。
基准阶段关闭其它测试 Editor，串行使用一个 Editor；Player 也独立执行。
图标的渲染与导入成本使用图形场景单独采样。字段单位、输入规模和数据表见
[性能报告](Performance.md)。

## 工具、归档与已有项目

共享脚本要求 Python 3.12，源场景与已知 metadata 在准备前后核对：

```sh
python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v
python3 -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

本地 VPM 工作流在一次性 staging 重建 ZIP，并检查内容、CRC、SHA256、metadata 与 listing。
已有 UPM/VPM 项目在 manifest 的 `testables` 加入待测包，随后从 Test Runner 选择对应
EditMode、Graphics 或 Performance 范围。依赖、激活和环境复用见
[Unity 环境](UnityEnvironment.md)及[测试 skill](../.agents/skills/unitybox-testing/SKILL.md)。

## 生成结果文档

源码冻结并完成各范围运行后，使用真实报告生成当前文档和归档。工具记录项目、包版本、
实际导入程序集、源码及 GUID 指纹，重新读取 XML、日志和 Player JSON 后再写入结果。
`--tooling` 指向 JSON 报告，其 `checks` 数组每项包含 `name`、`command` 参数数组、
`exit_code` 和 `log` 路径；测试数从成功的 unittest 摘要读取。
`--vpm` 指向本次本地发布工作流报告，工具从对应 site 重验当前版本 ZIP 的每个文件、
manifest、CRC 和 listing SHA256。将 `UNITYBOX_VPM_REPORT` 设为该报告的实际路径。

```sh
python3 scripts/unity/capture_evidence.py --repo /workspace/UnityBox \
  --run core=/workspace/unitybox-setup/unity/master-final-core-result.json \
  --run sdk=/workspace/unitybox-setup/unity/master-final-sdk-result.json \
  --run full=/workspace/unitybox-setup/unity/master-final-full-result.json \
  --graphics graphics=/workspace/unitybox-setup/unity/master-final-graphics-result.json \
  --benchmark benchmark=/workspace/unitybox-setup/unity/master-final-benchmark-result.json \
  --player linux=/workspace/unitybox-setup/unity/master-final-player-result.json \
  --tooling /workspace/unitybox-setup/unity/master-final-tooling-result.json \
  --vpm "$UNITYBOX_VPM_REPORT"
```

当前 XML、Player、工具日志与 VPM 报告按索引归档；旧运行通过 Git 历史保留。
本地工作流检查与 GitHub Actions 部署分别记录，推送后继续核对实际部署结果。
