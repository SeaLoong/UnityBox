# ACC 0.5.5 — 实时材质规则预览

## 用户操作

在材质变体 Inspector 点一次 **预览变体**，然后连续把候选材质拖入 **替换为**。更换/清空 Replacement、增删规则、修改精准 Renderer/槽位会立即刷新场景，不必反复点按钮。规则全部清空时显示本体原材质，但预览会话仍然开启。Undo/Redo 按已撤销/重做的规则重采样。

同一 OutfitBase 的其它变体和本体标记继续共享当前预览名称；编辑其它变体或多选不会抢走当前预览，切换仍通过显式按钮。停止、保存、切换 Play Mode、重载、分析/扫描/生成、更换本体时还原。预览不改变部件激活状态；手工编辑规则是有意配置编辑，正常记录 Undo 和 Prefab overrides。

## 实现边界

- 实时更新由变体 Inspector 的规则提交驱动，无逐帧层级/材质扫描，不在普通打开/导入时分析或改写配置。
- 提交前先停止临时材质预览，记录真正的 marker Undo/Prefab overrides，再采样同一个 activeVariant，避免把临时材质序列化为本体。
- 每次重新应用都从恢复后的原始材质开始，不叠加上一帧效果。槽位下拉和 Source 自动填充使用原材质数组的独立拷贝。
- `Refresh()` 是显式规则变更后的重采样 API；直接通过脚本修改字段时需主动调用，不宣称外部任意赋值会自动检测。
- 保留 0.5.4 的独立 AnimationModeDriver、dirty 状态保全、保存/重载还原、同名组隔离与源配置字节保护。

## 验证环境与命令

Windows 11、Unity 2022.3.22f1、Python 3.13；UTF 1.1.33 / NUnit 1.0.6，真实 SDK 3.10.2 / MA 1.18.1 / NDMF 1.14.4。使用上一轮的真实本机依赖缓存，不声称为历史 Linux 锁版本。用户 Editor 保持运行于另一个工程，验证 Editor 串行执行，合计不超过两个。

```powershell
py -3.13 -X utf8 -B scripts/unity/test_project.py prepare --repo D:\VRChatProjects\UnityBox --project D:\alc1 --dependency-cache D:\unitybox-verify\cache --package AdvancedCostumeController --profile core
py -3.13 -X utf8 -B scripts/unity/test_project.py prepare --repo D:\VRChatProjects\UnityBox --project D:\alf1 --dependency-cache D:\unitybox-verify\acc-preview-cache-20261120 --package AdvancedCostumeController --profile vrc --offline --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar
py -3.13 -X utf8 -B scripts/unity/test_project.py run --project D:\alc1 --unity D:\Unity\2022.3.22f1\Editor\Unity.exe --exclude-category 'UnityBoxPerformance;UnityBoxGraphics' --timeout 1200
py -3.13 -X utf8 -B scripts/unity/test_project.py run --project D:\alf1 --unity D:\Unity\2022.3.22f1\Editor\Unity.exe --exclude-category 'UnityBoxPerformance;UnityBoxGraphics' --timeout 1200
py -3.13 -X utf8 -B scripts/unity/test_project.py run --project D:\alf1 --unity D:\Unity\2022.3.22f1\Editor\Unity.exe --graphics --test-category UnityBoxGraphics --timeout 1200
py -3.13 -X utf8 -B -m unittest discover -s .github/scripts -p 'test_*.py' -v
```

原生完整范围分别执行；core 用例在集成范围重复，不相加为独立总数。包括连续全局/精准规则、空槽、原材质拷贝、真实 Undo/Redo，实际 Inspector 的相同提交函数、非当前变体/多选提交，以及多 Inspector 重绘下实时效果保持。继续复跑保存重载、Prefab Stage、dirty/原字节和图标实际红/绿像素回归。

## 最终结果

| 范围 | 实际结果 |
| --- | --- |
| 完整 ACC core 功能 | 74/74，1 个程序集 |
| 完整 ACC SDK / MA / NDMF 功能 | 104/104，2 个程序集 |
| 图形 / 实际 Inspector / Prefab Stage | 7/7，2 个程序集 |
| Windows 打包工具 unittest | 19/23；4 个既有 CRLF / POSIX 权限预期差异 |
| 本地 0.5.5 ZIP | CRC、版本、完整 payload 与 metadata 字节对照通过 |

三个原生范围退出码均为 0，无失败、跳过或未确定项。新增 7 个 core 用例、2 个实际 Inspector 提交用例和 1 个实时 Inspector 重绘图形用例；原有范围完整重跑而非只过滤新增测试。

## 证据说明

真实结果、命令、XML 与日志哈希、源码/程序集指纹及 ZIP CRC/payload 对照见 [results.json](results.json)。使用仓库 `capture_evidence.py` 的原生复核和源码匹配函数；明列排除 3 个原先未跟踪的旧 Editor 孤立 .meta（AnimationBuilder、Models、Scanner），因为 Unity 在隔离导入副本中丢弃无对应文件的 sidecar。原 checkout 字节保留，源哈希记录于结果，其余实际源码/资源和 metadata 不忽略差异。

Windows 打包 unittest 的既有 CRLF / POSIX 权限差异独立报告；本地 ZIP 按完整文件系统 payload 校验，包含原先未跟踪的 sidecar，不冒充 Git 跟踪文件组成的远端 ZIP。未新增专用 Player 或串行性能运行；图形用例输出的图标耗时仅保留为诊断。其它插件的并行未提交改动不参与本次源码或提交范围。
