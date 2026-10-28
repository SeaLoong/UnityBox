# 功能、Player、图形与性能测试

本指南维护 `refactor` 七插件及跨分支八插件组合的执行流程。

## 环境和结果索引

配置另一台云机器时，直接使用[可复制的安装与测试提示词](UnityCloudSetupPrompt.md)。

固定 Editor **2022.3.22f1**、UTF **1.1.33**、NUnit **1.0.6**；完整依赖组合为真实
SDK Base/Avatars **3.8.2**、MA **1.18.0**、NDMF **1.11.0**。云端 Personal 已激活，测试
使用共享 XDG `/workspace/unitybox-setup/unity/interactive`。

[验证结果](VerificationResults.json)记录功能、程序集、Player、图形和退出码；
[原生结果](UnityNativeResults.json)保留 NUnit 运行与源内容指纹；
[性能报告](Performance.md)和[原始测量](PerformanceResults.json)记录规模与串行样本。
每个依赖范围是一次独立执行，同一核心用例在不同范围重复运行；按各次运行记录计数。

## 本轮组合功能结果

以下运行组合 refactor 七包与 feature 优化器，排除独立的图形与性能类别。
三次均退出码0、零失败、零跳过；各范围重复执行核心用例，分别列出。

| 依赖范围 | 通过 / 总数 | 测试程序集 | 运行标识 |
| --- | --- | --- | --- |
| 纯 Unity 核心 | 294/294 | 8 | `20261028T190954-d55f5b48` |
| SDK 回退 | 369/369 | 13 | `20261028T190613-01bcfc9f` |
| SDK + MA + NDMF | 456/456 | 19 | `20261028T190613-fb9a2df0` |

具体断言、命令和源码指纹见[验证结果](VerificationResults.json)。关键 NUnit XML 压缩归档及
Player 的构建、执行和流水线原始 JSON 随提交保存于 `Docs/Verification/`，在结果索引中
记录各文件路径与哈希。Player、图形和串行测量使用各自运行记录。

## 准备独立工程

测试工程位于 checkout 外，复制当前源包及真实依赖，保留 metadata 和测试。
准备标记记录来源、提交、实际复制的内容 SHA256、依赖和预期测试程序集。

```sh
python3 scripts/unity/test_project.py prepare \
  --repo /workspace/unitybox-refactor-review \
  --project /workspace/unitybox-setup/unity/projects/refactor-core \
  --profile core --offline
```

完整依赖使用另一个目录，选择 `--profile vrc --offline`，再加
`--dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar`。
普通核心由 Unity 和 UTF/NUnit 组成。只检查 SDK 回退时使用 `--profile vrc --offline`；
完整集成再显式加入上述 NDMF/MA 依赖。平台入口随实际安装的包启用。

八插件组合示例：

```sh
python3 scripts/unity/test_project.py prepare \
  --repo /workspace/unitybox-refactor-review \
  --source-package /workspace/UnityBox/Assets/UnityBox/AnimatorOptimizer \
  --project /workspace/unitybox-setup/unity/projects/composed8-vrc \
  --profile vrc --offline \
  --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar
```

`--source-package` 添加另一个 checkout 的包，主仓库和额外包保持各自源码与 Git 分支。
缺少缓存依赖时先运行 `fetch_dependencies.py`；准备工具仅更新自己拥有的输出目录。

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

七插件功能范围包含服装/Parts/Mixer、组合 Blendshape、ASS 手势与重锁、口型/Voice、
Prefab 重载与 Variant、碰撞体历史/几何、日志和 Linker。平台范围追加真实 SDK、MA 与
NDMF 的安装、预算、回调和最终 Controller 交接。

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
ASS 运行时输入与状态动作。构建结束后核对源场景配置仍在，Player 再检查配置已移除。
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
python3 .github/scripts/test_package_vpm.py -v
```

本地 VPM 工作流在一次性 staging 重建 ZIP，并检查内容、CRC、SHA256、metadata 与 listing。
已有 UPM/VPM 项目在 manifest 的 `testables` 加入待测包，随后从 Test Runner 选择对应
EditMode、Graphics 或 Performance 范围。依赖、激活和环境复用见
[Unity 环境](UnityEnvironment.md)及[测试 skill](../.agents/skills/unitybox-testing/SKILL.md)。
