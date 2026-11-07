# ASS 0.9.0 自动迁移历史归档

这里保存的是 **0.9.0 已撤回实现**在合成工程中的三阶段复现记录。0.9.1 已完整移除该
迁移器、启动/导入触发、旧 ASS 修复菜单/API 和加载时参数纠正；
[0.9.1 导入安全记录](../ASSImportSafety/README.md)保留对应检查。当前六类组件使用独立
手动工具，操作见[组件引用手册](../../ComponentReferences.md)，结果见
[本轮记录](../ComponentReferences/README.md)。本归档只用于旧版本追溯。

## 历史行为与边界

| 阶段 | 当时的输入与判定 | 原始记录 |
| --- | --- | --- |
| 旧版正常 | 原样导入线上 0.7.24 ZIP，由 Unity 为缺少 metadata 的脚本生成随机 GUID，再保存 Scene、Prefab 和实例覆盖 | [旧版创建](legacy-0.7.24-create.json) |
| 升级损坏 | 换入线上 0.8.0 ZIP，合成资源出现 Missing Script，序列化文件仍在 | [故障复现](published-0.8.0-missing.json) |
| 旧自动迁移 | 换入当时的 0.9.0，启动/导入回调直接改写 GUID 并重载场景 | [0.9.0 执行结果](recovered-0.9.0.json) |

第二阶段 `passed: true` 表示复现预期故障。第三阶段记录该合成 fixture 在 0.9.0
自动迁移实现上的执行结果。
0.9.0 的脏状态保护没有覆盖所有被编辑场景引用的依赖 Prefab；强制导入和场景重载还会
触发其它插件回调。组件 OnValidate 曾把已存 `.22/.44` 改为 `.22/.21`，所以原文件 GUID
替换之外也存在内存配置副作用。这些行为正是 0.9.1 撤回的范围。

## 原始证据

[summary.json](summary.json)与[source-fingerprint.json](source-fingerprint.json)记录当时
0.9.0 的 ZIP、脚本和 107 个非 Markdown 源文件/资源身份，属于该旧快照。
[published-inputs.json](published-inputs.json)记录两个旧发布 ZIP 来源与 SHA256。
[legacy-generated-component.meta](legacy-generated-component.meta)是 Unity 在该合成项目
生成的旧脚本身份：`ca3ed01327e4ffa4e9e907c90419f826`。
0.9.0 将它改为已发布固定身份 `1c51e6a139920bb4ca12700dc82627ff`。

[原 Scene](legacy-scene.unity.gz)与[原 Prefab](legacy-prefab.prefab.gz)是合成资源的迁移前
字节，标准 gzip、mtime=0。[sha256.json](sha256.json)索引归档文件；原始 JSON 保留
当时命令、路径和结果，完整 Editor 登录/启动日志没有入库。

实际旧 fixture 保存在 [ASSRealZipUpgrade.cs](ASSRealZipUpgrade.cs)，旧启动器保存在
[run_upgrade.py](run_upgrade.py)。它们只用于在独立的、可丢弃且由测试工具拥有的工程
复盘 **0.9.0 固定版本**，不针对用户工程，也不适用于移除自动迁移后的当前包。
若需追溯原始说明，可查看 `036062d` 提交中的同名归档。

## 已有备份的含义

用户项目的 `UserSettings/UnityBox/ASSMigration/<id>/Assets/...` 保存当轮写入前的
Scene/Prefab 原字节，仅覆盖那一轮改写的文件。它不包含脚本 `.meta`、包版本、后续
编辑和其它插件回调的副作用。`LegacyGuids.json` 保存身份映射，实际写入文件按各轮
`Assets/...` 备份核对。

先保留当前工程副本和所有备份，再逐文件比较。恢复由用户明确选定资源和内容后执行，
并分别核对包版本、脚本 metadata 和后续编辑。
