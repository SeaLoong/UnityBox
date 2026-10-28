# Animator Optimizer 测试指南

本指南属于 `feat/animator-optimizer`。其他七包的修复与独立 Unity 拆分在 `refactor`，
两个分支尚未合并。优化器功能边界见 [包说明](../Assets/UnityBox/AnimatorOptimizer/README.md)
和 [审查记录](AnimatorOptimizerAudit.md)。

## 当前执行状态（2026-10-29，北京时间）

已有 Personal 账号通过云端 Hub 完成机器激活。真实 Unity 2022.3.22f1 已能导入独立项目并
执行 EditMode 测试；原先 `No valid Unity Editor license found` 是激活前的历史记录。
核心仅要求 Unity 2022.3，测试另需真实 UTF / NUnit。平台用例需要 VRChat Base/Avatars
≥3.7；NDMF 用例同时需要 NDMF ≥1.11。MA 的 2 项兼容测试另要求 MA ≥1.18、NDMF ≥1.11、
SDK ≥3.7.3。本插件 package 不强制安装这些平台依赖。

NDMF 最低版本从此前声明的 1.9 提高到 1.11：1.9/1.10 的虚拟节点不提供源对象
元数据，无法正确映射手动选择与原参数语义。旧版本不启用此适配；核心与仅 SDK 测试不受
影响。升级后应使用相同目标与配置重测，不能把旧的 API 存在检查视为完整版本支持。

本次固定 UTF 1.1.33、NUnit 1.0.6、SDK Base/Avatars 3.8.2、NDMF 1.11.0、MA 1.18.0。
下表只统计本包的测试套件，不把 SDK 上游测试混入。不同范围包含重复的核心测试，不相加。

| 依赖范围 | 实际结果 | run ID / 报告文件名 |
| --- | --- | --- |
| 无 SDK 核心 | **127/127**，0 失败，0 跳过，exit 0 | `20261028T171358-c5cbfa10` / `native-ao-final-core-v2-result.json` |
| 仅 SDK，无 NDMF / MA | **139/139**，0 失败，0 跳过，exit 0 | `20261028T171546-8d963795` / `native-optimizer-sdk-final-result.json` |
| SDK + NDMF，无 MA | **172/172**，0 失败，0 跳过，exit 0 | `20261028T173227-54647502` / `native-optimizer-ndmf-metadata-final-v2-result.json` |
| SDK + NDMF + MA | **174/174**，0 失败，0 跳过，exit 0 | `20261028T173628-b739272a` / `native-optimizer-full-metadata-final-result.json` |

可随 Git 审查的汇总见 [UnityNativeResults.json](UnityNativeResults.json)，记录真实范围、
源码快照与结果哈希。原始运行 JSON 位于 `/workspace/unitybox-setup/unity/`；其中 `results_xml` 指向对应独立项目
`.unitybox-test-results/<run ID>/results.xml`，另有命令、日志、程序集计数及 XML SHA256。
run ID 使用 UTC，文档日期使用北京时间。当前项目源码快照及依赖版本由准备记录保存，
不能用旧 XML 或另一分支的报告作为本次结果。原生测试修复详情见
[审查记录](AnimatorOptimizerAudit.md)。

在激活之前，2026-10-28 完成的 Roslyn 类型检查为核心 5、仅 SDK 19、SDK+NDMF 23 个
程序集，报告分别位于 `/workspace/unitybox-setup/unity/optimizer-subasset-{core,sdk,full}-20261028/compile-report.json`。
它们使用真实依赖、未改第三方源码，保留作该阶段的编译证据。较早 SDK 3.7 签名检查位于
`signature-optimizer-platform-target/`，明确省略部分上游脚本装配；不能把最终 SDK 3.8.2
结果追溯给所有最低版本。早期语法、JSON、归档检查也不等于 Editor 执行。
本次实际导入和 EditMode 通过仍不证明完整 Animator 逐帧等价性、Avatar 上传或性能。
环境安装、缓存、XDG 和合法激活流程见 [Unity 环境](UnityEnvironment.md)。

## 共享脚本流程

使用 [scripts/unity/test_project.py](../scripts/unity/test_project.py) 将需要的包复制到 checkout
以外的独立项目，统一准备 manifest/testables、运行 Editor 并读取本次报告。脚本不会激活
许可证，也不会替代用户项目的原始 metadata。云端核心验证：

```sh
python3 scripts/unity/test_project.py prepare \
  --repo /workspace/UnityBox \
  --project /workspace/unitybox-setup/unity/projects/optimizer-core \
  --profile core --package AnimatorOptimizer \
  --dependency-cache /workspace/unitybox-setup/unity/dependencies
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/optimizer-core \
  --unity /workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity \
  --xdg-root /workspace/unitybox-setup/unity/interactive
python3 scripts/unity/test_project.py report \
  --project /workspace/unitybox-setup/unity/projects/optimizer-core
```

`--package AnimatorOptimizer` 很重要：本分支其余七包尚未合入 refactor 的可选依赖改动，
不应在优化器无 SDK 验证时无意导入它们。平台测试用独立输出目录：

```sh
python3 scripts/unity/test_project.py prepare \
  --repo /workspace/UnityBox \
  --project /workspace/unitybox-setup/unity/projects/optimizer-vrc \
  --profile vrc --package AnimatorOptimizer \
  --dependency-cache /workspace/unitybox-setup/unity/dependencies \
  --dependency nadena.dev.ndmf
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/optimizer-vrc \
  --unity /workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity \
  --xdg-root /workspace/unitybox-setup/unity/interactive
python3 scripts/unity/test_project.py report \
  --project /workspace/unitybox-setup/unity/projects/optimizer-vrc
```

共享锁当前固定 SDK 3.8.2、NDMF 1.11；只测 SDK 回退时省略 NDMF 依赖，用另一个项目保存结果。
完整范围再显式加入 `--dependency nadena.dev.modular-avatar`，使真实 MA 的 2 个兼容用例参与。
`--offline` 要求所有必要真实依赖已在缓存中，缺失时补齐缓存，不造 SDK 占位包或强行定义
版本符号。本地将 checkout/project/cache/Editor 路径替换为实际位置。
`run` 默认总会传入所选本仓库测试装配的 `-assemblyNames`，避免 SDK 上游套件混入；
没有可运行测试装配时在启动 Editor 前失败。可用 `--assembly-names 'A;B'`、`--test-filter`
限定诊断范围，并用 `--timeout`、`--xdg-root` 配置进程与合法授权位置。过滤结果需明确标注，
不能替代修复后的完整范围。

当前基础锁已备齐真实依赖：core 为 2 包，vrc 为 17 包，含真实 UGUI 源码。锁中的
2/12 个模块仅指包清单的传递依赖计数；实际测试项目还显式声明 Animation、Physics、
Physics2D、Audio 等 Editor 内建模块，由固定 Editor 安装层提供，不是工程仅启用 2/12 个模块。
NDMF 仍由 `--dependency` 显式加入；若测试与 MA 的合并组合，再加
`--dependency nadena.dev.modular-avatar`（当前锁为 1.18.0）。依赖准备本身不等于编译通过，
历史 23 程序集报告记录激活之前的 C# 类型检查，上表记录本次真实 Editor 执行。
VRC 项目还显式加入真实 Editor 内建 Android JNI 模块，以满足 SDK Oculus 在 Linux Editor 中
的 Android.Permission 引用；core 不引入它。

## 当前用例清单

| 装配 | 依赖条件 | 声明用例数与范围 |
| --- | --- | --- |
| `UnityBox.AnimatorOptimizer.Tests` | Unity/UTF | 127：分析/转换、参数语义与 Undo、AAP API、普通 Processor、独立构建剥离和装配边界 |
| `UnityBox.AnimatorOptimizer.VRC.Tests` | SDK Base/Avatars ≥3.7 | 12：真实 Descriptor、ParameterDriver、LayerControl、目标/语义传递及恢复 |
| `UnityBox.AnimatorOptimizer.NDMF.Tests` | 上述 SDK + NDMF ≥1.11 | 33：虚拟图、普通/Playable 语义、配置/选层交接及按需服务生命周期 |
| `UnityBox.AnimatorOptimizer.ModularAvatar.Tests` | SDK ≥3.7.3、NDMF ≥1.11、MA ≥1.18 | 2：真实 MMD LayerControl 在 AAP 转换与响应层合并中的保留 |

共 174 个声明用例，按 `[Test]` 与展开的 `[TestCase]` 计数；实际执行数单独记录在上表。
未安装平台依赖时对应装配不参与，不能用核心结果推断被排除的集成套件也已通过。
另有 `UnityBox.AnimatorOptimizer.TestFixtures` 原生 Behaviour 注册装配：仅在 Editor+UTF
存在时启用，不标为 TestAssemblies、不参与 Player，不应作为零用例测试套件计入覆盖。

本轮增加参数平台语义与构建传递用例，另增加9个NDMF原有效来源/服务生命周期回归，
以及2个同文件多Controller的离线/构建复制身份回归。
无配置时不激活AnimatorServices；原关闭customizeAnimationLayers、默认层或无自定义
Controller的Playable目标保持跳过，避免NDMF先开启开关后复活残留图。子Animator入口
按需激活服务后恢复原关闭Descriptor状态；仅释放本插件本次激活的服务。

本轮参数语义用例中，普通 Unity 的 `UseVrChatParameterSemantics=false`
要求参数实际声明，同名 IsLocal/Viseme 作为本地参数处理；显式 VRChat 模式才推断内建
输入并保护这些名称。AvatarPlayableLayer 目标自动启用，其余目标需显式开启组件字段
`useVrChatParameterSemantics`（默认 false），SDK/NDMF 构建也不自动切换；普通子 Animator
不会自动获得平台内建输入。空白离线窗口默认关闭 Bool/Int 升级，传入配置保留原选项；公共 Options
升级默认仍为 true 以兼容已有 API，直接调用者需明确检查 SetFloat 输入契约。

## 已有项目的手工运行

1. 在 Unity 2022.3 项目安装兼容 Test Framework；只测核心无需 SDK/NDMF。
2. 恢复原项目 `.meta`，特别是组件 GUID，不能用随机新 GUID 修复旧场景引用。
3. VPM/UPM 安装时在 manifest 的 `testables` 加入
   `top.sealoong.unitybox.animator-optimizer`；直接放在 Assets 下无需该条目。
4. 在 **Window > General > Test Runner > EditMode** 运行已启用的测试装配。
   SDK 用例在 `Tests/Editor/VRC`，NDMF 用例在 `Tests/Editor/NDMF`，真实 MA 兼容用例在
   `Tests/Editor/ModularAvatar`。

也可直接批处理，不追加 `-quit`：

```sh
"$UNITY_EDITOR" -batchmode -nographics -projectPath "/path/to/test-project" \
  -runTests -testPlatform EditMode \
  -assemblyNames "UnityBox.AnimatorOptimizer.Tests" \
  -testResults "/path/to/results.xml" -logFile "/path/to/editor.log"
```

上例仅运行核心套件；集成范围优先使用共享脚本，根据已安装依赖选择正确程序集。
核对退出码、本次 XML、实际执行数以及失败/跳过情况，零测试不算通过。测试使用临时对象/
资产，优先在独立项目运行。Python 发布测试仍可从仓库根执行：

```sh
python3 .github/scripts/test_package_vpm.py -v
```

共享测试工具本身另有 25 项 Python 用例通过，可重复运行：

```sh
python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v
```

它们验证依赖锁、工程复制、进程/XML/许可证错误与范围报告，包含默认/过滤执行显式套件
以及空套件不启动的回归。打包脚本另有 5 项 Python 用例；它们均不是插件的 Unity 行为测试。

## 仍需验证的行为

构造图正确不证明 Unity 逐帧输出等价。需用真实 Animator 对照转换前后输出、默认值、
参数切换、Mask/层顺序与时间边界；普通 Unity 脚本遇到 Bool/Int 升为 Float 时须同步改用
SetFloat，或关闭升级。外部输入不能由静态分析自动证明兼容。

AAP StateRegister 初始化/反馈时序和实际成本、真实多插件构建顺序、同名/多 Avatar 连续
构建、完整 Undo、实际上传与性能仍需实测。NDMF→SDK fallback、手动选层、原有效
Playable 来源、配置剥离/还原等已有 EditMode fixture，尚不能替代真实 Avatar 的端到端构建。
普通 Unity Build 只剥离配置，不自动优化；离线保存副本和 Avatar 构建自动处理是不同入口。
