# 测试与验证

本指南属于 `refactor` 的七个插件。`feat/animator-optimizer` 有独立的源码、用例和报告，
尚未合并；不能把任一分支的验证范围套用到另一分支。

## 当前执行状态

现有 Personal 账号登录 Unity Hub 后已激活机器，无需另购 Pro。2026-10-29（Asia/Shanghai；原始运行标识采用 UTC），在真实
**Unity 2022.3.22f1 / Unity Test Framework 1.1.33 / NUnit 1.0.6** 执行以下 EditMode
范围。每行是独立依赖工程；同一核心用例会在多个范围重复运行，不应相加为不同测试。

| 范围 | 已执行 / 通过 | 预期测试程序集 | 本次运行标识（UTC） |
| --- | --- | --- | --- |
| 无 SDK、MA、NDMF 核心 | 93 / 93 | 7 / 7 | `20261028T172507-b0e56a65` |
| 仅 SDK 3.8.2，无 MA/NDMF | 150 / 150 | 11 / 11 | `20261028T172551-8128a2f9` |
| SDK 3.8.2 + MA 1.18 + NDMF 1.11 | 179 / 179 | 14 / 14 | `20261028T172818-8625ecdf` |

三个范围均 **零失败、零跳过、零 inconclusive、exit 0**，取得本次真实 XML，
无 runner 捕获的编译/许可错误。历史的许可失败和中断诊断保留，不计为通过。成功项目
保留于 `/workspace/unitybox-setup/unity/projects/`：核心 `refactor-exposed-final-core`、
完整 `refactor-exposed-final-vrc`；仅 SDK 工程为 `refactor-exposed-final-sdk`。
每次执行的 `.unitybox-test-results/<run_id>/` 保留命令、日志、XML 和报告。

外层最终报告位于 `/workspace/unitybox-setup/unity/`：

- `refactor-exposed-final-core.json`
- `refactor-exposed-final-sdk.json`
- `refactor-exposed-final-vrc.json`

仓库内的 [持久化结果摘要](UnityNativeResults.json) 记录各范围计数、运行标识、测试快照哈希
和XML哈希。测试后的包内修改仅更新 Prefab README，行为源码、asmdef、meta及包清单与
三个测试快照一致；后续文档编辑不声称保留整个包的相同SHA256。

这些结果实际覆盖 Unity 导入、编译、程序集发现及所列 EditMode 行为，不证明所有
第三方组合、Avatar 最终构建/上传或游戏内行为。Unity 2022 的旧 ShortcutProfileStore
尝试写只读 HOME 中偏好目录的启动异常仍存在，未阻止执行；详情见环境文档。

原生执行暴露并修复了 Prefab 内置组件拒绝覆写、未选子树 `(Clone)` 名称、归一化引用
误比较 PPtr，以及 Unity versionDefine 未启用口型 SDK/NDMF 程序集等问题。测试夹具改用
自有 Preview Scene，保留输入资产和用户场景；不是通过跳过或删除失败断言得到全绿。

此前 2026-10-28 的真实 C# 类型检查（核心19、仅SDK40、完整55个程序集）仍是历史补充
证据，其 `verified-20261028-refactor-{core,sdk,vrc}/compile-report.json` 保留源码哈希。
它们只证明所列引用下的类型正确，不能替代原生 asmdef 解析、IL 后处理与行为执行。
历史 SDK3.7.3 + MA1.18 + NDMF1.11 最低组合30个程序集也只完成当时类型检查，未把其
结果外推为该最低组合已通过原生测试。

## Python 工具和打包测试

共享环境工具要求 Python **3.12**，安装/下载工具使用安全 tar 解包能力：

```sh
python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v
python3 .github/scripts/test_package_vpm.py -v
```

共享工具 25 项用例覆盖版本/依赖锁、缓存损坏拒绝、HTTP 明确拒绝不重试、源项目不变、
重复准备清理残留、默认/过滤时显式限定本仓库测试范围，以及 exit/XML/许可/编译/超时/
零测试/跳过/过期结果的失败传播；空测试范围在启动前拒绝。
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

使用本机已激活的共享 XDG 路径执行：

```sh
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/refactor-core \
  --unity /workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity \
  --xdg-root /workspace/unitybox-setup/unity/interactive \
  --timeout 1200
python3 scripts/unity/test_project.py report \
  --project /workspace/unitybox-setup/unity/projects/refactor-core
```

runner 使用 `-batchmode -nographics -runTests -testPlatform EditMode`，不加 `-quit`。
默认以 `-assemblyNames` 明确限定当前工程预期的 UnityBox 测试，避免误跑 SDK 上游
VTPTests。`--assembly-names`/`--test-filter` 缩小范围的结果仅证明该范围。
它设置可写 XDG 而不改 HOME；已有合法许可路径可用 `--xdg-root`。每次运行保存独立命令、
日志、退出码及真实 XML；exit 0、实际执行的非零测试和预期程序集缺一不可。
完整设置见 [Unity 环境](UnityEnvironment.md) 和
[复用 skill](../.agents/skills/unitybox-testing/SKILL.md)。

## 当前声明用例

以下按测试程序集统计；与完整配置本次 Test Runner XML 的 **179/179** 一致。
只有核心范围运行时执行93项；仅SDK不包含MA/NDMF用例。

| 插件 | Unity 核心 | 平台集成 | 范围 |
| --- | --- | --- | --- |
| ACC | 32，`.Core.Tests` | 19，`.Editor.Tests`，真实 SDK/MA | 选择、扫描、完整资产生成、Parts/Mixer、材质与菜单 |
| Blendshape | 14，`.Core.Tests` | 5，`.Tests`，真实 SDK/MA | Float/Bool 曲线、反向、路径、重复生成和 MA 安装 |
| ASS | 15，`.Editor.Tests` | 27，`.VRChat.Tests` | 配置/图/时间、真实 Behaviour、完成状态、源资产与复制身份/失败隔离 |
| 口型 | 9，`.Editor.Tests` | SDK 25，`.VRChat.Tests`；NDMF 5，`.NDMF.Tests` | 映射、曲线、FX 保存、回退/去重、无效层、子资产身份与服务生命周期 |
| Prefab | 11，`.Editor.Tests` | 无 | 选择、局部变换、嵌套引用、组件身份、输出 GUID |
| Collider | 9，`.Tests` | 4，`.VRC.Tests` | Unity 3D/2D 快照、历史隔离与 Descriptor provider |
| Logger | 3，`.Tests` | 1，`.VRC.Tests` | 连续构建日志状态和过滤不修改输入 |

装配名称前缀为各包的 `UnityBox.<目录名>`。缺少真实平台依赖时对应集成程序集不参与；
不能把未编译、未发现或跳过的用例计为成功。当前总计 **179** 项，其中核心93、
平台86；本轮最终构建交接补充了ASS13项与口型11项，原生审查再补充Prefab的2项
ExposedReference回归，均已在完整范围真实执行通过。

完整范围中 ASS42项、口型39项均已执行通过，包含实际完成状态、独立源资产保护、
无效层、Controller/localFileID身份、Motion复制失败中止、动画层禁用与服务生命周期。
这补充了此前类型检查，不等同于完整 Avatar 上传或游戏内输入已验证。

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
