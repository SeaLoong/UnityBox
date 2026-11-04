# 功能、Player、图形与性能测试

本指南使用 `master` 的同一个 checkout，执行八个插件的核心、平台、图形、Player 与性能测试。

## 环境和结果索引

配置另一台云机器时，直接使用[可复制的安装与测试提示词](UnityCloudSetupPrompt.md)。

固定 Editor **2022.3.22f1**、UTF **1.1.33**、NUnit **1.0.6**；完整依赖组合为真实
SDK Base/Avatars **3.8.2**、MA **1.18.0**、NDMF **1.11.0**。当前云工具链使用
共享 XDG `/workspace/unitybox-setup/unity/interactive`；机器授权与 Hub 复用见[环境流程](UnityEnvironment.md)。

[验证结果](VerificationResults.json)记录功能、程序集、Player、图形和退出码；
[原生结果](UnityNativeResults.json)保留 NUnit 运行与源内容指纹；
[性能报告](Performance.md)和[原始测量](PerformanceResults.json)记录规模与串行样本。
每个依赖范围是一次独立执行，同一核心用例在不同范围重复运行；按各次运行记录计数。

## 执行结果与归档

[验证结果](VerificationResults.json)记录当前源码对应的实际通过数、测试程序集、运行标识、
命令、退出码和源文件指纹。关键 NUnit XML 使用标准 gzip 归档，Player 的构建、执行与
流水线 JSON 一起保存在 `Docs/Verification/`，索引记录归档路径和 SHA256。

功能、图形、Player 和串行基准分别执行。纯 Unity、SDK 和完整 MA/NDMF 范围会重复执行
核心用例，各范围分别计数。历史数据通过原提交和运行标识定位；重新准备工程后按新快照
执行并生成新结果。

## 当前 master 功能结果

2026-11-05 的统一源码验证按三个依赖范围执行，均退出码 0、零失败、零跳过。
运行标识采用 UTC；测试程序集数表示实际出现在 NUnit XML 中的本仓库测试程序集。

| 依赖范围 | 通过 / 总数 | 测试程序集 | 运行标识（UTC） |
| --- | --- | --- | --- |
| 纯 Unity 核心 | 310/310 | 8 | `20261104T193647-8b4d6260` |
| SDK 回退 | 385/385 | 13 | `20261104T193355-7c7b9cce` |
| SDK + MA + NDMF | 482/482 | 19 | `20261104T193355-72562100` |

各范围重复执行核心用例，分别计数。源码、资源和 GUID 与当前 checkout 的对照记录保存在
[验证结果](VerificationResults.json)，图形、Player 和性能使用各自独立运行记录。

## 当前独立运行验收

| 范围 | 实际结果 | 运行标识（UTC） |
| --- | --- | --- |
| 图标渲染 | 1/1，核对两张 256×256 图标的红绿像素 | `20261104T193915-e87b4e70` |
| Linux Player | 13 个场景、1557 帧、4807 项输出检查，5 个功能提供者 | `20261104T194015-4b80a5f1` |
| 串行性能 | 36/36，57 条测量，覆盖八个插件 | `20261104T194142-4dce9b18` |

Linux 构建退出码 0，构建错误 0，产物 58,454,186 bytes；
构建后重载源场景，三个编辑配置组件保持完整，Player 中的编辑配置已移除。
实际[构建轨迹](Verification/linux-pipeline-trace.json)从序号 0–6 的
`BuildPlayerProcessor.PrepareForBuild` 连续记录到序号 7–13 的
`IPreprocessBuildWithReport.OnPreprocessBuild`，准备阶段记录与后续构建阶段属于同一条完成轨迹。

图形运行使用 llvmpipe 软件光栅设备。完整性能表同时记录收益与成本增加，
区分 Editor 生成、参数切换更新和 Player 固定末态更新，见[性能报告](Performance.md)。
原始 [Player 输出](Verification/linux-player.json)、[构建结果](Verification/linux-build.json)
和 NUnit 压缩 XML 与当前结果索引一起保存在仓库中。

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
组合 Blendshape、ASS 手势与重锁、口型/Voice、Prefab 重载与 Variant、碰撞体历史/几何、
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

构建回调从构建场景副本移除 AO、ASS 和口型的编辑配置组件，保留生成的 Animator、
ASS 运行时输入与状态动作。构建后重载保存的源场景，核对场景字节和配置组件保留；Player 再检查配置已移除。
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
