# 原始验证记录

当前 ASS 0.9.1 的范围见[导入安全记录](ASSImportSafety/README.md)。下列全库 XML、
Player 与性能数据保留 0.9.0 对应快照，按各索引的版本和源指纹读取。

`*-results.xml.gz` 保存真实 Unity Test Framework / NUnit 输出，使用标准 gzip 压缩（mtime=0）。执行身份、环境变量与命令行元数据在存在时移除；每个测试用例保留原内容。[VerificationResults.json](../VerificationResults.json) 分别记录原 XML、归档 XML 和 gzip 文件 SHA256。

`*-build.json`、`*-player.json` 和 `*-pipeline-trace.json` 分别保存实际 Linux 构建结果、Player 场景断言与性能数据、构建回调顺序。归档内容来自固定测试 fixture，不包含 Hub 会话、账号登录信息或完整 Editor 启动日志。

`tooling-*.log` 保存实际 Python unittest 输出，测试数从成功摘要读取；`vpm-workflow.json` 保存本地发布工作流的报告。对应包 ZIP 在该次记录时已逐文件与相应源码比对，CRC、manifest 版本和 listing SHA256 记录在结果索引中。归档以各次索引引用为准，旧运行通过提交历史保留。

解压示例：`gzip -dc core-results.xml.gz > core-results.xml`。

[ASS 0.9.0 历史迁移归档](ASSUpgrade/README.md)保留旧三阶段行为、原文件、源码指纹与
当时的复现脚本；该自动写入实现已撤回，不用于当前包的安装或迁移流程。
