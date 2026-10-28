# Animator Optimizer 测试指南

本指南属于 `feat/animator-optimizer`。其他七包的修复与独立 Unity 拆分在 `refactor`，
两个分支尚未合并。优化器功能边界见 [包说明](../Assets/UnityBox/AnimatorOptimizer/README.md)
和 [审查记录](AnimatorOptimizerAudit.md)。

## 当前执行状态（2026-10-28）

核心仅要求 Unity 2022.3，测试另需真实 Unity Test Framework。平台用例需要
VRChat Base/Avatars ≥3.7；NDMF 用例同时需要 NDMF ≥1.9。package 不强制安装它们。

Unity 2022.3.22f1 已安装，`-version` 成功。配置可写 XDG 目录解决只读 home 问题后，
Editor 启动仍 exit 1：`No valid Unity Editor license found`。因此没有 Editor 导入、
IL 后处理或 EditMode 执行通过结果。

使用 Editor 自带 Roslyn 和真实 Unity/Test Framework 引用，核心 Runtime、Editor、
Tests 及两个 UTF 程序集共 5 个类型检查全部通过，没有 SDK 替代类型。证据在本次工作区
`/workspace/unitybox-setup/unity/optimizer-subasset-core-20261028/compile-report.json`。
core profile 按条件排除了 VRC/NDMF 及其测试，不能单用这一结果推断平台代码通过。

最终共享验证环境固定 SDK Base/Avatars **3.8.2**、MA **1.18.0**、NDMF **1.11.0**。
优化器实际启用的真实依赖闭包共 **23 个程序集 C# 编译通过**，`failed=[]`，包括 SDK/
NDMF 源码、优化器 Runtime/核心/平台适配和测试；没有修改第三方源码。证据在
`/workspace/unitybox-setup/unity/optimizer-subasset-full-20261028/compile-report.json`。优化器不要求 MA，
其 23 个程序集不包含 MA，也不包含本分支其余七包；第三方 Collections Playmode 测试按
profile 条件排除。完整 C# 依赖闭包通过仍不包含 Unity import、IL 后处理、build 或测试执行。

最终源码另以仅安装 SDK、不安装 MA/NDMF 的真实闭包共 **19 个程序集**通过，证据为
`/workspace/unitybox-setup/unity/optimizer-subasset-sdk-20261028/compile-report.json`。
这单独检查了 SDK 回退和未启用 NDMF 时的条件编译，包含最后两项复制身份回归；
仍不是 Editor 执行。

早期另以官方 SDK 3.7 DLL 对 Runtime、核心、VRC 适配和 VRC.Tests 共四个程序集做签名类型
检查，全部通过；NDMF 适配与 NDMF.Tests 两个程序集也通过，引用由未修改上游 NDMF 1.11
Runtime/Editor 源码构建。报告为本次工作区
`/workspace/unitybox-setup/unity/signature-optimizer-platform-target/signature-vrc-report.json`
和同目录 `signature-ndmf-report.json`。该检查明确省略未构建且本次源码无需调用的 SDK
脚本装配，不是完整 asmdef 图、Editor 导入或运行测试。保留这些历史证据，不把后来 SDK
3.8.2 的完整闭包结果追溯给早期源码或所有最低支持版本。

早期审查的 57 个 C# 语法解析、25 个 JSON、5 项 Python 测试和八包 ZIP/CRC/SHA256/metadata
校验属于当时快照。它们不代表后续新增程序集已经在 Unity 导入，也不证明运行时语义等价。
环境安装、缓存、XDG 和许可证排查见 [Unity 环境](UnityEnvironment.md)。

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
  --unity /workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity
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
  --unity /workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity
python3 scripts/unity/test_project.py report \
  --project /workspace/unitybox-setup/unity/projects/optimizer-vrc
```

共享锁当前固定 SDK 3.8.2、NDMF 1.11；只测 SDK 回退时省略 NDMF 依赖，用另一个项目保存结果。
`--offline` 要求所有必要真实依赖已在缓存中，缺失时补齐缓存，不造 SDK 占位包或强行定义
版本符号。本地将 checkout/project/cache/Editor 路径替换为实际位置。
`run` 可用 `--assembly-names 'A;B'`、`--test-filter`、`--timeout`、`--xdg-root` 限定运行。

当前基础锁已备齐真实依赖：core 为 2 包，vrc 为 17 包，含真实 UGUI 源码。锁中的
2/12 个模块仅指包清单的传递依赖计数；实际测试项目还显式声明 Animation、Physics、
Physics2D、Audio 等 Editor 内建模块，由固定 Editor 安装层提供，不是工程仅启用 2/12 个模块。
NDMF 仍由 `--dependency` 显式加入；若测试与 MA 的合并组合，再加
`--dependency nadena.dev.modular-avatar`（当前锁为 1.18.0）。依赖准备本身不等于编译通过，
上文最终 23 程序集报告才记录本次实际 C# 编译结果。
下载重试已成功，当前 Editor 执行的阻碍是缺少有效授权。

## 当前用例清单

| 装配 | 依赖条件 | 声明用例数与范围 |
| --- | --- | --- |
| `UnityBox.AnimatorOptimizer.Tests` | Unity/UTF | 127：分析/转换、参数语义与 Undo、AAP API、普通 Processor、独立构建剥离和装配边界 |
| `UnityBox.AnimatorOptimizer.VRC.Tests` | SDK Base/Avatars ≥3.7 | 12：真实 Descriptor、ParameterDriver、LayerControl、目标/语义传递及恢复 |
| `UnityBox.AnimatorOptimizer.NDMF.Tests` | 上述 SDK + NDMF ≥1.9 | 29：虚拟图、普通/Playable 语义、配置/选层交接及按需服务生命周期 |

共 168 个声明用例，按 `[Test]` 与展开的 `[TestCase]` 计数，不是 Test Runner 成功数。
未安装平台依赖时对应装配不参与，这不等于其中的测试已执行或通过。

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
   SDK 用例在 `Tests/Editor/VRC`，NDMF 用例在 `Tests/Editor/NDMF`。

也可直接批处理，不追加 `-quit`：

```sh
"$UNITY_EDITOR" -batchmode -nographics -projectPath "/path/to/test-project" \
  -runTests -testPlatform EditMode \
  -testResults "/path/to/results.xml" -logFile "/path/to/editor.log"
```

核对退出码、本次 XML、实际执行数以及失败/跳过情况，零测试不算通过。测试使用临时对象/
资产，优先在独立项目运行。Python 发布测试仍可从仓库根执行：

```sh
python3 .github/scripts/test_package_vpm.py -v
```

共享测试工具本身另有 23 项 Python 用例通过，可重复运行：

```sh
python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v
```

它们验证依赖锁、工程复制、进程/XML/许可证错误与范围报告，不是插件的 Unity 行为测试。

## 仍需验证的行为

构造图正确不证明 Unity 逐帧输出等价。需用真实 Animator 对照转换前后输出、默认值、
参数切换、Mask/层顺序与时间边界；普通 Unity 脚本遇到 Bool/Int 升为 Float 时须同步改用
SetFloat，或关闭升级。外部输入不能由静态分析自动证明兼容。

AAP StateRegister 初始化/反馈时序和实际成本、NDMF→SDK fallback、合并后手动选层、
禁用自定义 Playable 层、配置剥离和构建后还原、同名/多 Avatar 连续构建、完整 Undo、
第三方插件顺序、实际上传与性能均待实测。普通 Unity Build 只剥离配置，不自动优化；
离线保存副本和 Avatar 构建自动处理是不同入口。
