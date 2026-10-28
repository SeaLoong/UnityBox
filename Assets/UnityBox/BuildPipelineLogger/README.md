# Build Pipeline Logger

观察 Unity、VRChat SDK 和 NDMF 构建回调，并导出有序、带时间戳的 JSON 构建记录。
需要 Unity 2022.3，核心只使用 Unity API。VPM 包名：
`top.sealoong.unitybox.build-pipeline-logger`。

## 使用

在 `Tools > UnityBox > Build Pipeline Logger` 开启所需类别，重新导入资产或执行构建。
Console 搜索 `[BPL]`；点击 **Export Current Trace...** 保存当前 JSON 记录。

| 菜单 | 观察内容 | 依赖 |
| --- | --- | --- |
| Asset Pipeline | 纹理、模型、音频、批量导入等 AssetPostprocessor 回调 | Unity |
| Build Pipeline | BuildPlayerProcessor、pre/post build、程序集、脚本 DLL、Scene、shader/compute shader、UnityLinker | Unity |
| VRCSDK | build request、多个 order 的 Avatar preprocess/postprocess | 可选 VRChat Avatars SDK ≥3.7 |
| NDMF | Resolving、Generating、Transforming、Optimizing 阶段 | 可选 NDMF |

四类默认关闭；设置保存在原 EditorPrefs 键。菜单和公开设置属性同步更新运行缓存，切换后
立即生效。关闭的 shader 回调直接读取缓存开关，保留所有变体数据。
**Enable All / Disable All** 批量切换类别；各可选程序集随对应包启用。

## 有序构建记录

Unity preprocess 或 Avatar preprocess 建立新会话，重置 shader 去重及记录列表。
每条记录包含 sequence、相对开始时间 elapsed_ms、pipeline、order、marker 和 message。
Unity/Avatar 的最后一个 postprocess 标记会话完成，导出同时包含开始 UTC 和总耗时。
连续两次构建各自生成新会话。构建结束后的资产导入启动新的记录；**Export Last Completed Build...**
保留并导出上一份已完成构建的数据，后续导入记录与它分开。

Shader/compute shader 对每个回调类型在一个会话中记录首个调用。数字 order 是本工具
回调在管线中的位置；带框架名字的标签作为对应 order 的定位提示。实际 NDMF 阶段记录
由 NDMF 插件回调写入。UnityLinker 通过 Editor 自带 `IUnityLinkerProcessor` 记录链接点，
返回空附加链接文件，保持原有链接输入。

代码可调用：

```csharp
BuildPipelineLoggerTrace.ExportJson();
BuildPipelineLoggerTrace.Save(outputPath);
BuildPipelineLoggerTrace.SaveLastCompleted(completedBuildPath);
```

`Entries` 提供有序只读列表，`Completed` 表示当前会话是否收到结束回调。
过滤接口返回原程序集数组，并原样保留 shader/compute shader 变体列表；日志记录与构建
数据处理分别执行。

## 程序集与测试

`UnityBox.BuildPipelineLogger` 包含独立 Unity 设置、记录、导出和回调。
`.VRC`、`.NDMF` 分别包含对应平台回调，使用真实依赖包。

核心测试 `UnityBox.BuildPipelineLogger.Tests` 覆盖重复构建、shader 去重、关闭路径、
程序集原引用、JSON 时间/顺序/完成状态，以及真实 UnityLinker 回调。`.VRC.Tests` 覆盖
Avatar 连续 preprocess。UPM/VPM 安装时将本包名加入 manifest 的 `testables`。

`UnityBoxPerformance` 测量 1000/10000 次关闭的 shader 回调与原 EditorPrefs 读取对照，
并测量 128/1024 条构建记录的写入与 JSON 导出；每个场景预热后采样 9 次。
实际分配量、耗时和复现命令见[性能报告](../../../Docs/Performance.md)，构建回归见
[测试指南](../../../Docs/Testing.md)。

本包在 Unity 2022.3.22f1 的执行记录与程序集结果统一保存在
[验证结果](../../../Docs/VerificationResults.json)，性能场景由独立运行采样。
