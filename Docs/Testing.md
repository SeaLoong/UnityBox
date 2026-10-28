# 测试与验证

本指南属于 `refactor` 的七个插件。`feat/animator-optimizer` 有独立的源码、用例和报告，
尚未合并；不能把任一分支的验证范围套用到另一分支。

## 当前执行状态

真实 Unity **2022.3.22f1** 已安装并能查询版本。设置可写 XDG 路径后，实际测试启动仍
exit 1：`No valid Unity Editor license found`，未产生 NUnit XML。用户目前没有授权，
因此 **Unity 导入、IL 后处理、EditMode、真实构建/上传尚未得到通过结果**。
不会反复启动许可检查，也不会由编译代码或生成 ZIP 推断行为测试通过。

2026-10-28 最终源码已完成三种真实类型检查，未修改第三方源码：

| 范围 | 程序集数量（包含必要上游） | 报告 |
| --- | --- | --- |
| 七插件无 SDK 核心和测试 | 19 | `/workspace/unitybox-setup/unity/verified-20261028-refactor-core/compile-report.json` |
| 仅 SDK 3.8.2，无 MA/NDMF | 40 | `/workspace/unitybox-setup/unity/verified-20261028-refactor-sdk/compile-report.json` |
| SDK 3.8.2 + MA 1.18 + NDMF 1.11 的所需完整 C# 闭包 | 55 | `/workspace/unitybox-setup/unity/verified-20261028-refactor-vrc/compile-report.json` |

它们使用 Editor 自带 Roslyn、真实 Unity/SDK DLL 和上游源码；只证明所列引用组合下的
C# 类型检查成功，不完整复现 Unity 导入、asmdef 解析、平台符号或 IL 后处理。
报告保留源码 SHA256、失败与省略的上游可选引用。仅SDK配置使用不含MA/NDMF的缓存，
单独检查未启用这些程序集时的SDK回退条件编译。

最终隔离工程已准备：`verified-refactor-core`含9个包、7个预期测试程序集，
`verified-refactor-vrc`含24个包、14个预期测试程序集。目录均在
`/workspace/unitybox-setup/unity/projects/`；准备成功不是Unity执行成功。

2026-10-26 的历史证据包括：无SDK19、完整平台54个程序集类型检查，以及ACC/BGC的
SDK3.7.3 + MA1.18 + NDMF1.11最低SDK组合30个类型检查，后者报告在
`/workspace/unitybox-setup/unity/compile-acc-ma118-sdk373/compile-report.json`。
新增加的口型NDMF测试程序集使当前完整配置由54变为55；旧结果不追溯证明新测试已编译。
恢复时34个仓库程序集源码与旧最终编译哈希一致，此后构建交接修复已由上表重新验证。

更早的API签名检查位于`/workspace/unitybox-setup/unity/signature-refactor-vrc/`，
只覆盖明确列出的平台API，不是全部依赖图。历史归档/语法/metadata检查同样只描述当时
快照，不能代替最终类型检查或Unity运行。

## Python 工具和打包测试

共享环境工具要求 Python **3.12**，安装/下载工具使用安全 tar 解包能力：

```sh
python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v
python3 .github/scripts/test_package_vpm.py -v
```

共享工具 23 项用例覆盖版本/依赖锁、缓存损坏拒绝、HTTP 明确拒绝不重试、源项目不变、
重复准备清理残留，以及 exit/XML/许可/编译/超时/零测试/跳过/过期结果的失败传播。
打包另有 5 项，检查 GUID 保留、实验与生成目录排除、重建 ZIP 移除旧文件及保留其它包
测试资产。独立打包脚本只需 Python 3.9+；不要将该版本要求套给环境工具。
这些测试不会发布 Pages，也不会执行插件的 Unity 行为。

## 隔离工程

从仓库根运行，输出在 checkout 外，源场景和原资产不改动：

```sh
python3 scripts/unity/test_project.py prepare \
  --repo /workspace/unitybox-refactor-review \
  --project /workspace/unitybox-setup/unity/projects/refactor-core \
  --profile core

python3 scripts/unity/test_project.py prepare \
  --repo /workspace/unitybox-refactor-review \
  --project /workspace/unitybox-setup/unity/projects/refactor-vrc \
  --profile vrc --offline \
  --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar
```

`core` 只需真实 UTF 1.1.33/NUnit 1.0.6，跳过 SDK/MA/NDMF 适配并明确记录范围。
`vrc` 使用缓存中的真实 SDK；只有显式加入依赖，才覆盖 MA/NDMF 的可选功能。固定环境
为 SDK Base/Avatars 3.8.2、MA 1.18、NDMF 1.11，锁记录 17 个真实包及 12 个传递 Editor
模块；这不是生成工程中完整 module 条目数量。缓存存在某包不表示某项目已启用它。

prepare 使用仓库打包筛选规则，保留 Editor 测试和现有 metadata，排除实验探针/场景与
生成物。只更新工具自己创建的项目，拒绝覆盖没有所有权标记的非空目录。项目中的复制
版本不能作为继续开发的源仓库，重新 prepare 会替换工具拥有的 Packages。

有合法授权后执行；当前无需重试已知许可阻断：

```sh
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/refactor-core \
  --unity /workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity \
  --timeout 1200
python3 scripts/unity/test_project.py report \
  --project /workspace/unitybox-setup/unity/projects/refactor-core
```

runner 使用 `-batchmode -nographics -runTests -testPlatform EditMode`，不加 `-quit`。
它设置可写 XDG 而不改 HOME；已有合法许可路径可用 `--xdg-root`。每次运行保存独立命令、
日志、退出码及真实 XML；exit 0、实际执行的非零测试和预期程序集缺一不可。
完整设置见 [Unity 环境](UnityEnvironment.md) 和
[复用 skill](../.agents/skills/unitybox-testing/SKILL.md)。

## 当前声明用例

以下按 `[Test]` 与展开的 `[TestCase]` 统计，**不是已通过的 Test Runner 数量**。

| 插件 | Unity 核心 | 平台集成 | 范围 |
| --- | --- | --- | --- |
| ACC | 32，`.Core.Tests` | 19，`.Editor.Tests`，真实 SDK/MA | 选择、扫描、完整资产生成、Parts/Mixer、材质与菜单 |
| Blendshape | 14，`.Core.Tests` | 5，`.Tests`，真实 SDK/MA | Float/Bool 曲线、反向、路径、重复生成和 MA 安装 |
| ASS | 15，`.Editor.Tests` | 27，`.VRChat.Tests` | 配置/图/时间、真实 Behaviour、完成状态、源资产与复制身份/失败隔离 |
| 口型 | 9，`.Editor.Tests` | SDK 25，`.VRChat.Tests`；NDMF 5，`.NDMF.Tests` | 映射、曲线、FX 保存、回退/去重、无效层、子资产身份与服务生命周期 |
| Prefab | 9，`.Editor.Tests` | 无 | 选择、局部变换、嵌套引用、组件身份、输出 GUID |
| Collider | 9，`.Tests` | 4，`.VRC.Tests` | Unity 3D/2D 快照、历史隔离与 Descriptor provider |
| Logger | 3，`.Tests` | 1，`.VRC.Tests` | 连续构建日志状态和过滤不修改输入 |

装配名称前缀为各包的 `UnityBox.<目录名>`。缺少真实平台依赖时对应集成程序集不参与；
不能把未编译、未发现或跳过的用例计为成功。当前总计 **177** 个声明用例，其中核心91、
平台86；本轮最终构建交接补充了ASS13项与口型11项。全部仍未实际执行。

2026-10-28 最终口型类型检查23程序集通过：
`/workspace/unitybox-setup/unity/compile-viseme-identity-final-20261028/compile-report.json`。
ASS类型检查22程序集通过：
`/workspace/unitybox-setup/unity/compile-ass-source-isolation-final-20261028/compile-report.json`。
前者新增NDMF条件测试程序集，并覆盖关闭自定义层与同文件Controller选择；后者覆盖实际
完成状态、NDMF独立源资产保护、无效层、Controller身份和Motion复制失败中止。通过编译
不表示这些用例已经在Unity运行。

## 已有项目手工验证

1. 使用 Unity 2022.3 和真实 Test Framework；只测核心无需 SDK。恢复已有原始 `.meta`，
   不给旧组件随机更换 GUID。平台用例按 [依赖架构](DependencyArchitecture.md) 装入依赖。
2. 按 UPM/VPM 安装时，将包名加入 `Packages/manifest.json` 的 `testables`；直接放在
   Assets 下由 Editor 导入测试程序集。
3. 在 **Window > General > Test Runner > EditMode** 运行实际启用的装配；确认退出码、
   本次 XML、非零执行数、失败及跳过。按程序集过滤的结果只证明所选范围。
4. 用自有测试 Avatar 分别验证 SDK 回退、启用/关闭 NDMF、MA 完整构建以及连续两次构建。

仍需实测：口型 Viseme 0–14/Voice 阈值和绑定，ACC 两种 Mixer/压缩/材质/图标/Undo，
Blendshape 参数预算与重复生成，Prefab 真实资产/RectTransform/嵌套及第三方序列化，
Collider 多根对象历史和 Logger 重复构建。ASS 保持主动负载关闭，检查连续手势、超时竞争、
锁定/反馈、原 Controller/参数恢复和 NDMF 提交时序。旧探针 bundle 需重新生成，历史日志
不能证明本次通过。完整逐项影响见 [变更清单](RefactorChanges.md)。
