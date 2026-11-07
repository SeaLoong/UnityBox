# 组件引用、源配置与手动迁移验证

本轮审查八插件的全部生产 Avatar 组件：ACC 三种标记，以及 ASS、Viseme、Animator
Optimizer 各一种配置。其余四个插件没有自有 Avatar 组件，不进入脚本身份迁移目录。
具体类名、程序集和 GUID 见[组件引用说明](../../ComponentReferences.md)。

## 实际功能结果

| 范围 | 用例 | 测试程序集 |
| --- | ---: | ---: |
| 纯 Unity 核心 | 361 / 361 | 9 |
| SDK | 442 / 442 | 14 |
| 完整 SDK + MA + NDMF | 541 / 541 | 20 |

全部功能用例无失败或跳过，各范围重复执行核心用例，分别计数。独立迁移工具使用
单独的 Editor 程序集，作为额外源码一同编译，不新增第九个 VPM 包。
完整命令、执行标识、XML 与当前源码/metadata/DLL 指纹见[验证索引](../../VerificationResults.json)。

## 真实旧包升级

[六组件三阶段归档](LegacyUpgrade/README.md)使用公开 ACC 0.4.13、ASS 0.7.24、
Viseme 0.1.14 ZIP 和明确的未发布 AO 0.2 开发源，先创建真实旧 GUID，再完整换入新包。

- 更新后复现 42 处 Missing Script，打开和导入没有自动改写原资源，VRC Descriptor 保持有效。
- 显式一次批量操作修复 12 个引用，仅写入两个需要修改的选定文件；Variant、嵌套和实例
  通过原 Prefab 来源恢复，未选择的重复组件场景保持原字节。
- 每轮逐项比较可解析组件的持久化属性和父对象引用 GlobalObjectID，另核对保留重复组件的
  原文件字节；保留资产 GUID、组件 fileID、密码/标记/口型/优化配置、材质、音频、Renderer
  及跨组件引用。捕获与比较范围的实际计数见六组件归档。
- 创建 24 个 Inspector 后再次核对，源字段保持；备份原件和 GUID 字节跨度逐项检查。

开发快照的范围、旧开发 Tests 排除清单及各阶段实际执行源码都在该归档中单独记录。

## 手动工具与安装包

下载 [UnityBox-ComponentReferences.unitypackage](https://sealoong.github.io/UnityBox/UnityBox-ComponentReferences.unitypackage)，
导入后从 `Tools → UnityBox → Component References` 打开。先退出引用目标资源的编辑场景或
Prefab Mode，在 Project 中选择资源，点击 **Use Project Selection**，再执行
**Migrate Selected Assets**。普通条目不逐项确认；歧义旧 GUID 只需选择一次目标类型。

工具没有启动或导入迁移回调。分析只读；执行只改选定资源的失效脚本 GUID，先备份并
重新核对源 SHA。重复、未识别或正在编辑的资源列入结果，保留组件和配置，不删除或合并。
通用用例另覆盖 UTF-8 BOM、中文/emoji、CRLF、扫描后修改、有效引用保留和编辑依赖。
实际图形环境的三项用例均通过；其中新增 Prefab Stage 用例在关闭 autoSave 后，只在内存中
加入嵌套 Prefab 和 Mesh 资源引用，验证分析与执行均跳过这些编辑依赖，并保留 dirty 状态、
对象身份、未保存的 Transform 与资源引用，以及六份资源/metadata 的原始字节。

安装包由真实 Unity ExportPackage 导出，仅含独立工具 Editor 目录；不包含 Tests 或插件
依赖。[安装包记录](Installer.json)保存实际导出 SHA256 与逐文件身份；发布工具逐文件核对导出源码及 metadata，拒绝过期安装包或夹带其它资源。

## 构建、图形与性能

图形、Linux Player 和八插件加独立工具的基准分别串行执行。当前结果与采样协议见
[测试指南](../../Testing.md)、[性能报告](../../Performance.md)及[原始测量](../../PerformanceResults.json)。
组件引用扫描使用 128、1,024、4,096 个组件，预热一次后采样五次，同时检查条目数和
原文件字节。Editor 生成、手动扫描和 Player Animator 更新按各自工作负载记录。

四个没有生产组件的包保持原发布内容；四个有组件的包保留原脚本 GUID，并修复加载、
Inspector 或构建读取时对源配置的隐式改写。[变更与影响](../../RefactorChanges.md)列出各包具体行为。

本机串行扫描中位数：128 个组件 92.6815 ms，1,024 个组件 862.4242 ms，4,096 个组件 3,308.0420 ms；每个规模 5 次采样，完整分位数见性能报告。
