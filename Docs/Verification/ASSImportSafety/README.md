# ASS 0.9.1 导入与配置保全

本次移除启动/导入迁移、全项目 GUID 改写、场景重新加载、修复菜单/API，以及组件加载时的
OnValidate 参数纠正。源组件保留已保存字段，生成器只规范化配置副本。
Inspector 移除新增的顶部自动构建提示，恢复旧版介绍、字段名与多行说明风格，保留四语言。

## 实际执行结果

| 范围 | 结果 |
| --- | --- |
| ASS 纯 Unity | 61 / 61 通过 |
| ASS SDK 3.8.2 + MA 1.18 + NDMF 1.11 | 98 / 98 通过 |
| Linux Mono Player | 构建和运行成功；2 个场景、252 帧、13 次检查通过 |
| 发布打包单元测试 | 14 / 14 通过 |
| 本地 VPM 工作流 | 八包内容、metadata、manifest、ZIP CRC 与 listing SHA256 通过 |

Editor 为真实 Unity 2022.3.22f1，全部功能用例无失败或跳过。两个功能范围会重复核心用例，
分别计数；本次只重新执行受改动影响的 ASS 范围，其它七包源码及版本与 `036062d` 相同。
八插件旧测量保存在 [0.9.0 历史性能报告](../../Performance.md)，不是本版重新执行的数量。

## 保全检查

- 打开、重新导入已有配置时，Prefab、Scene 和 `.meta` 保持原字节；旧脚本 GUID 不会被替换。
- 重新导入被脏场景引用的 Prefab 后，已记录的 Transform 覆写、对象身份与脏状态保留。
- 已有保持、容错、最大时长、警告阈值和防御开关保留；构建副本独立处理有效取值。
- 真实 VRC Avatar Descriptor 在普通 ASS 与旧 Missing Script 场景中继续保持类型、配置、
  脚本 GUID 和脚本 metadata；旧无效引用原样保留，按具体组件引用另外处理。
- 四语言字段、Tooltip 和原有介绍可解析，新增顶部自动生成 HelpBox 已移除。
- Player 实际执行连续输入、超时、Restart 和用户动画保留，并检查源配置保留、产物编辑配置移除。

## 证据与重跑

[Results.json](Results.json)记录实际命令、退出码、范围、源码/资源与程序集指纹，以及归档 SHA256。
`core-results.xml.gz`、`full-results.xml.gz` 保存移除执行身份元数据后的真实 NUnit 用例；
`linux-*.json` 保存构建和 Player 输出，`tooling-*.log` 与 `vpm-workflow.json`
保存打包检查。本目录没有用户工程、账号信息或完整 Editor 登录日志。

按[测试指南](../../Testing.md)准备独立工程，在 prepare 命令加 `--package AvatarSecuritySystem`。
核心使用 `--profile core --offline`；完整范围使用 `--profile vrc --offline`
及 `--dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar`。
执行完整功能类别后，关闭其它 Editor，再运行 `verify_player.py`；结果保存在各工程的独立运行目录。
当前资源有 Missing Script 时先读取其实际 `m_Script` GUID；安装和导入不会自动重连或恢复备份。
