# Unity 云环境与隔离测试

环境已安装真实 Linux Unity Editor **2022.3.22f1**：
`/workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity`。
安装工具是 [scripts/unity/install_editor.py](../scripts/unity/install_editor.py)，固定 GameCI
镜像层及 SHA256，验证后只提取 Editor；安装二进制不会激活许可证。
共享的安装、依赖和测试脚本已在 **Python 3.12** 验证；安装/依赖解包使用
`tarfile.extractall(filter="data")`，不能沿用独立 VPM 打包脚本的 Python 3.9 要求。
已安装且版本/锁校验正确时直接复用，不需每个任务重新安装或下载。

在 Linux x86-64 环境从仓库根目录执行以下命令，可以复用或准备固定工具链：

```bash
python3 scripts/unity/install_editor.py
python3 scripts/unity/fetch_dependencies.py --profile core
python3 scripts/unity/fetch_dependencies.py --profile vrc
```

下载并解包 Editor 需要至少 13 GiB 可用空间；安装器校验固定镜像层 SHA256，已安装时
核对版本。脚本只准备二进制和依赖，不激活 Unity。依赖校验不改写源工程。
云环境的 `install_script` 和 `start_skill` 已保存到配置草稿，复用脚本为
`/workspace/unitybox-setup/setup.sh`，保留工具副本位于 `toolkit/unity`。
草稿保存不等于环境发布；尚未验证从已发布配置创建全新任务。

长期 checkout 和验证证据放在 `/workspace`。环境重启会清空 `/tmp`；2026-10-28 已将
refactor 审查副本恢复到 `/workspace/unitybox-refactor-review`，恢复源码与保留的编译
哈希核对后再继续修复。临时目录只放可重新生成的数据，不存未提交的唯一工作副本。

目前真实 Editor 启动返回 **exit 1**，日志报告没有有效 Unity 许可证，未产生 NUnit XML。
因此还不能报告 Unity EditMode 测试通过。许可证启动日志位于
`/workspace/unitybox-setup/unity/startup-xdg.log`；隔离测试的完整结果保存在各工程的
`.unitybox-test-results/`。Python 工具测试、C# 语法分析和使用真实 Editor DLL 的 Roslyn
编译是不同层次的证据，均不能代替 Unity 内实际执行。
用户已确认目前没有任何 Unity 授权；在授权条件改变前不再重复启动许可检查或询问。

## 已准备的依赖

`/workspace/unitybox-setup/unity/dependencies` 保存完整真实包：

| 包 | 版本 | 用途 |
| --- | --- | --- |
| `com.unity.test-framework` | 1.1.33 | Unity EditMode 测试执行 |
| `com.unity.ext.nunit` | 1.0.6 | Unity 使用的 NUnit |
| `com.vrchat.base` | 3.8.2 | 固定 VRC 集成组合 |
| `com.vrchat.avatars` | 3.8.2 | 固定 VRC 集成组合 |
| `nadena.dev.modular-avatar` | 1.18.0 | 完整 Avatar 工作流 |
| `nadena.dev.ndmf` | 1.11.0 | 可选 NDMF 集成 |
| `com.unity.ugui` | 1.0.0 | SDK 使用的真实 Unity UI 源码包 |

核心测试工程把真实 UTF/NUnit 包嵌入 `Packages`，不需要联网下载测试依赖。
真实 [依赖锁](../scripts/unity/dependencies.lock.json) 目前完整记录 `core` 和 `vrc`：
core 为 2 个包及 2 个 Editor 模块；vrc 包括 17 个包及 12 个 Editor 模块，包含 SDK
声明的 Burst、Collections、Mathematics、Newtonsoft、Timeline、XR、Postprocessing
以及传递依赖。两种 profile 均已按锁重复校验通过，这不代表 Unity 测试已经执行。

SDK 3.7 的早期隔离工程暴露了隐含的 `InputField` / `Toggle` 依赖；当前固定 SDK
3.8.2 已显式声明 UGUI 1.0.0。隔离 VRC 工程完整复制 Editor 随附的真实 UGUI C#
源码并校验包树；它与只记录 manifest 的内置 modules 不同。core 不引入 UGUI 包。

完整集成固定为 SDK **3.8.2** + MA **1.18.0** + NDMF **1.11.0**。真实 DLL 检查发现
SDK 3.7.0～3.7.2 缺少 MA 1.13/1.18 调用的双参数 `VRCConstraintSource` 构造函数，
3.7.3 首次提供。因此 ACC/BGC 的 MA 集成最低门槛修正为 SDK **3.7.3**；独立 SDK
适配器的最低版本仍按各自 asmdef。固定 3.8.2 组合还覆盖 MA 的新版 SDK 条件分支。
未修改第三方源码，完整所需依赖闭包的 Roslyn 类型检查已通过；Unity 导入、IL 后处理、
构建和行为测试仍需有授权的 Editor 执行。

历史 SDK 3.7 的 AnimatorOptimizer + NDMF 最低版本类型检查证据保留其原范围，不能
扩大为 MA 全套兼容结论。原 Base/Avatars 3.7.0 包目录、官方 ZIP 和当时依赖锁保存在
`/workspace/unitybox-setup/unity/sdk-compatibility/3.7.0/`，原缓存中的 3.7 ZIP 也已保留。
core 的固定版本与依赖范围没有改变。

此前 CDN 返回 403，阻断了完整 VRC 下载；环境网络状态改变后，已实际下载固定版本
tarball 并验证完整图。当前缓存已完整，不应把旧的 CDN 阻断描述为现状。
新环境可用 [fetch_dependencies.py](../scripts/unity/fetch_dependencies.py) 按锁准备：

```bash
python scripts/unity/fetch_dependencies.py --profile core
python scripts/unity/fetch_dependencies.py --profile vrc
```

普通运行验证归档 SHA256、解包树 SHA256、包身份和依赖声明；Editor 模块校验其
manifest。核心包来自已校验安装层内的真实 UTF/NUnit tgz，不查询 CDN。
需要明确更新锁时使用 `--update-lock --profile core` 或 `--update-lock --profile vrc`。
前者只更新核心并保留已有 VRC 记录；后者仅在完整 VRC 图验证成功后扩展锁。
`prepared_profiles` 与 `profiles` 表明已经完成的范围，下载失败不写假校验值，也不
破坏已有锁。普通复用不会重新查询 registry。HTTP 403 等明确拒绝立即停止；只有
暂时网络错误有限重试。网络状态没有改变时不要重复同一被拒绝的下载命令。

## 准备工程

[测试工具](../scripts/unity/test_project.py) 支持 `prepare`、`run`、`report`。
`--repo` 指定被测试的 checkout，无需切分支；`--project` 必须是仓库外的独立目录。
以下命令从包含该脚本的仓库根目录执行：

```bash
python scripts/unity/test_project.py prepare \
  --repo /workspace/unitybox-refactor-review \
  --project /workspace/unitybox-setup/unity/projects/refactor-core \
  --profile core

python scripts/unity/test_project.py prepare \
  --repo /workspace/UnityBox \
  --project /workspace/unitybox-setup/unity/projects/optimizer-core \
  --profile core --package AnimatorOptimizer
```

第一条复制 `refactor` 副本中的插件；第二条只复制 feature 分支的 AnimatorOptimizer。
目录名是当前云环境的位置，不是脚本内绑定的分支。`--package` 可重复，接受
`Assets/UnityBox` 下的目录名或完整包名；省略时选择该 checkout 的所有包。

准备过程读取目标 checkout 的 `.github/scripts/package_vpm.include_file` 规则，排除
打包规则定义的探针、生成物和实验场景，并保留 Editor 测试及现有 `.meta`。不会复制
仓库根场景，也不会改写源代码或用户场景。依赖包按原内容完整复制，不伪造 SDK。
`Packages/manifest.json` 的 `testables` 包含被复制的本仓库包。

每个工程有 `.unitybox-test-project.json` 所有权标记，记录源路径、提交、复制内容的
SHA256、依赖版本和预期测试程序集。重复 prepare 会替换该工具拥有的 `Packages`
目录，移除上次残留包；非空且没有工具标记的目录会被拒绝。不要在这些一次性工程中
编辑需要保留的资产。重新 prepare 后旧运行记录不能作为新工程的通过证据。

`core` profile 不安装仓库包 `vpmDependencies` 中声明的完整工作流依赖，独立核心
测试不会强制启用 SDK/Modular Avatar 集成。当前 ACC 的包声明也已去除这些强制依赖。忽略的 VPM
声明会明确记入准备及运行报告，源 `package.json` 不改动。此模式只验证真实可用依赖
条件下启用的程序集；SDK/NDMF 集成不会被计为已经测试。真正必需的 UPM 依赖仍需缓存
齐全，否则准备失败。

## SDK 和 NDMF 集成

使用不同输出目录保留独立结果：

```bash
python scripts/unity/test_project.py prepare \
  --repo /workspace/UnityBox \
  --project /workspace/unitybox-setup/unity/projects/optimizer-vrc \
  --profile vrc --package AnimatorOptimizer \
  --dependency-cache /workspace/unitybox-setup/unity/dependencies
```

`vrc` profile 嵌入真实 VRChat Base/Avatars 包，并解析所选插件声明的 VPM/UPM 依赖。
可增加 `--offline`，要求全部依赖已缓存。缓存以 `<cache>/<包名>/package.json` 组织。
准备前会验证 SDK >= 3.7.0 和每条缓存依赖的版本范围，包括预发布版本与上界；
不合要求时不会改动已有隔离工程。已安装的包名本身不代表对应测试程序集已启用。
这个最低版本检查用于独立 SDK 用例；带 MA 1.18 的完整集成应使用上面的固定 3.8.2
依赖锁，不能仅凭通过 >= 3.7.0 检查推断第三方组合兼容。
如需测 NDMF，在缓存中加入真实 NDMF 及其依赖，再增加 `--dependency nadena.dev.ndmf`。
这不会自动下载 NDMF，也不会为缺失类型生成替身。NDMF 程序集是否启用仍由它自己的
版本条件决定。
ACC/BGC 的 Modular Avatar 集成也已改为可选；要一并验证，应再传
`--dependency nadena.dev.modular-avatar`。缓存中存在 MA 不会自动让它进入每个工程，
SDK-only 准备结果不能代表 MA/NDMF 集成已覆盖。

## 执行和读取结果

```bash
python scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/refactor-core \
  --unity /workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity \
  --timeout 1200

python scripts/unity/test_project.py report \
  --project /workspace/unitybox-setup/unity/projects/refactor-core
```

runner 使用真实 Editor 的 `-batchmode -nographics -runTests -testPlatform EditMode`，
不加会提前终止 Test Runner 的 `-quit`。它为本次进程设置可写的 `XDG_CONFIG_HOME`、
`XDG_DATA_HOME`、`XDG_CACHE_HOME`，**不改动 HOME**。默认 XDG 根目录在隔离工程中，
也可用 `--xdg-root` 指向已配置许可证的可写目录；许可证本身需通过合法授权方式提供。
工具不执行激活或上传。

每次运行都创建新的 `.unitybox-test-results/<时间>-<随机ID>/`，包含：

- `run.json`：真实命令、依赖/profile、开始结束时间、进程 exit code 和超时状态。
- `unity.log`、`launcher.log`：Editor 和启动进程日志。
- `results.xml`：只有真实 Test Runner 生成时才存在。
- `report.json`：XML 摘要、程序集、失败及跳过原因、最终判定；不会补造缺失 XML。

只有进程 exit 0、没有已识别的许可/编译错误、新 XML 有实际执行的非零测试、没有
失败/未知/不确定结果，且默认预期的测试程序集确实执行，才返回通过。超时会终止
该次启动的进程组；无 XML、零测试、全部跳过和许可失败都返回非零。跳过用例会单独
列出，不能将它们说成通过。`report` 可用 `--run-id` 查看历史运行，并重新核对证据。

仅调查特定程序集时使用 `--assembly-names 'Assembly.A.Tests;Assembly.B.Tests'`；
可再使用 `--test-filter` 过滤测试名。单独指定 test filter 时报告只代表选中的范围，
不会要求其它程序集也出现。完整验证需再执行未过滤的测试。

## 工具自身测试

```bash
python -m unittest discover -s scripts/unity -p 'test_*.py' -v
```

Python 测试覆盖打包筛选保留测试、源场景不变、重复准备清理残留、依赖隔离和
exit/XML/许可/编译/跳过等失败传播，以及锁完整性、缓存损坏拒绝、版本约束与明确
HTTP 拒绝不重试。这些是测试工具的测试，不是插件的 Unity 行为测试。

## 无授权时的补充类型检查

[compile_assemblies.py](../scripts/unity/compile_assemblies.py) 使用已安装 Editor 自带
Roslyn、真实 Unity DLL，以及真实依赖源码/预编译 DLL。它解析程序集引用和版本条件，
编译所选插件及所需上游闭包，不生成替身类型。输出目录必须放在 checkout 外：

```bash
python scripts/unity/compile_assemblies.py \
  --repo /workspace/UnityBox --package AnimatorOptimizer \
  --editor /workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity \
  --dependency-cache /workspace/unitybox-setup/unity/dependencies \
  --output /workspace/unitybox-setup/unity/compile-optimizer-core \
  --tests --profile core
```

改为 `--profile vrc` 会使用缓存中的完整平台包；与测试工程准备工具的显式
`--dependency` 选择不同。检查七个插件时改 `--repo` 为 refactor checkout 并去掉
`--package`。每个输出目录保存 `compile-report.json`、响应文件、编译日志和 DLL。
报告列出实际源码 SHA256、失败程序集和被省略的上游可选引用。此工具不完整复现
Unity 导入器、asmdef 解析、平台符号或 IL 后处理规则，因此通过仅表示此引用组合下
的 C# 类型检查成功，不能代替实际 Editor 导入与测试。

2026-10-28 最终源码的类型检查证据（最低 SDK 组合保留 2026-10-26 的独立记录）：

| 范围 | 类型检查通过的程序集数（含所需上游） | 报告目录（位于 `/workspace/unitybox-setup/unity/`） |
| --- | --- | --- |
| AnimatorOptimizer，无 SDK | 5 | `optimizer-subasset-core-20261028/` |
| refactor 七插件，无 SDK | 19 | `verified-20261028-refactor-core/` |
| AnimatorOptimizer，仅 SDK 3.8.2 | 19 | `optimizer-subasset-sdk-20261028/` |
| AnimatorOptimizer，SDK 3.8.2 + NDMF 1.11 | 23 | `optimizer-subasset-full-20261028/` |
| refactor 七插件，仅 SDK 3.8.2 | 40 | `verified-20261028-refactor-sdk/` |
| refactor 七插件，SDK 3.8.2 + MA 1.18 + NDMF 1.11 | 55 | `verified-20261028-refactor-vrc/` |
| ACC/BGC，SDK 3.7.3 + MA 1.18 + NDMF 1.11 最低 SDK 组合 | 30 | `compile-acc-ma118-sdk373/` |

SDK-only 的编译缓存视图只链接真实依赖并排除 MA/NDMF。最新隔离测试工程位于
`projects/verified-optimizer-core`、`projects/verified-optimizer-vrc`、
`projects/verified-refactor-core` 和 `projects/verified-refactor-vrc`；原始许可失败记录
仍保存在先前的 `projects/optimizer-core/.unitybox-test-results/`，没有伪装成新源码的运行结果。

两分支声明的 Unity 测试分别为优化器 168、七插件 177；**这些用例尚未实际执行**。
工具自身的 Python 测试 23 项通过，打包脚本测试另有 5 项通过。实际 Unity 测试启动
返回 exit 1、无 NUnit XML，结论仍为许可证阻断。
