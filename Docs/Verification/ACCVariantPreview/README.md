# ACC 0.5.4 — 同组材质变体预览

## 行为与安全边界

- 变体 Inspector 的预览入口以 OutfitBase **实例引用**共享状态；其它变体显示当前名称并能切换，本体 ACCOutfitMarker 提供停止入口。
- 单个全局临时会话，切换前恢复；不依赖选择或 Inspector 生命周期。不改变服装/部件激活状态。
- 使用 ACC 独立 AnimationModeDriver 登记原始材质属性；不抢占其它工具的 AnimationMode。生成动画与预览共享材质规则解析。
- 停止、保存、Undo/Redo、Play Mode、重载和退出时恢复。分析 preflight、刷新、扫描、生成与配置 Undo 边界先停止。
- 无资产/配置迁移，保留 Runtime 字段及已有 GUID。源场景和用户工程不参与测试。

## 当前机器

Windows 11，Unity 2022.3.22f1，Python 3.13。已有用户 Editor 在另一个工程运行，未关闭或修改该工程；验证 Editor 始终串行，合计最多两个 Editor。真实本机集成依赖为 SDK 3.10.2、MA 1.18.1、NDMF 1.14.4，UTF 1.1.33 / NUnit 1.0.6；不是历史 Linux 锁版本组合。

隔离缓存由原本机安装包复制至 `D:\unitybox-verify\acc-preview-cache-20261120`；Collections 2.1.4 来自 Unity 官方包缓存，以兼容 UTF 1.1.33。所有测试工程均使用仓库 test_project.py --repo 创建，位于 checkout 外。

## 最终结果

| 范围 | 结果 |
| --- | --- |
| ACC core 完整功能范围 | 67/67，通过 1 个测试程序集 |
| ACC SDK + MA + NDMF 完整功能范围 | 95/95，通过 2 个测试程序集 |
| 图形 / Inspector / Prefab Stage | 6/6，通过 2 个测试程序集 |
| 打包工具 Windows UTF-8 执行 | 19/23；4 个既有 CRLF / POSIX 文件权限预期差异 |
| 本地 ACC 0.5.4 ZIP | CRC、版本、完整 payload 与 metadata 字节对照通过 |

三个 Unity 范围退出码均为 0，无失败、跳过或未确定项。功能范围存在重复核心用例，不相加为独立用例数。图形用例同时验证真实红/绿图标像素；其输出的 icon-render 时间仅保留为诊断，不视为其它 Editor 停止后的串行性能测量。

初期诊断包含过长 Windows 路径、Unity 内置 NUnit 缺少 NonParallelizable、测试 Scene 创建上下文，以及新发现的 Renderer dirty 标记残留；分别通过短工程路径、兼容测试 API、独立已保存锚点 fixture 和只清除本次临时赋值/还原标记修正。最终完整范围均重跑，所有失败原始报告单列保留。

## 可重复命令

```powershell
py -3.13 -X utf8 -B scripts/unity/test_project.py prepare --repo D:\VRChatProjects\UnityBox --project <new-core-project> --dependency-cache D:\unitybox-verify\cache --package AdvancedCostumeController --profile core
py -3.13 -X utf8 -B scripts/unity/test_project.py prepare --repo D:\VRChatProjects\UnityBox --project <new-full-project> --dependency-cache D:\unitybox-verify\acc-preview-cache-20261120 --package AdvancedCostumeController --profile vrc --offline --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar
py -3.13 -X utf8 -B scripts/unity/test_project.py run --project <project> --unity D:\Unity\2022.3.22f1\Editor\Unity.exe --exclude-category 'UnityBoxPerformance;UnityBoxGraphics' --timeout 1200
py -3.13 -X utf8 -B scripts/unity/test_project.py run --project <full-project> --unity D:\Unity\2022.3.22f1\Editor\Unity.exe --graphics --test-category UnityBoxGraphics --timeout 1200
py -3.13 -X utf8 -B -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

证据使用 `capture_evidence.py` 的原生 XML/日志复核与源码指纹实现。Windows 本地 checkout 原先有 3 个未跟踪的孤立 sidecar（旧 Editor/AnimationBuilder、Models、Scanner 对应 .meta，但源文件已在 Core）；Unity 在隔离导入副本中丢弃它们。证据适配器只将这 3 个无对应资源的 sidecar 明列为指纹排除项，记录原字节 SHA256，用户 checkout 原件不删除、不提交。其余实际源码、资源及 metadata 按工具规则比较，不忽略代码差异；最终本地 ZIP 另按完整文件系统 payload 校验，不冒充仅 Git 跟踪文件组成的远端 ZIP。

真实执行结果、源码与原始记录哈希见 [results.json](results.json)。历史探测/失败运行单列，不冒充最终通过结果。本次为 Editor 预览交互，不新增 Player 或性能测量，历史记录保持原身份。
