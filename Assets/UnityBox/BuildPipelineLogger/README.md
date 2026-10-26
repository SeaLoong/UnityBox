# Build Pipeline Logger

通过 Unity Console 观察 Unity 和 VRChat 构建回调，帮助检查某个回调是否到达。
需要 Unity 2022.3 和 VRChat Avatars SDK >= 3.7.0；NDMF 是可选的独立 Editor 装配。
VPM 包名：`top.sealoong.unitybox.build-pipeline-logger`。

## 使用

在 `Tools > UnityBox > Build Pipeline Logger` 勾选需要的日志类别：

| 菜单 | 观察范围 |
| --- | --- |
| Asset Pipeline | 纹理/模型/音频导入、批量导入等 AssetPostprocessor 回调 |
| Build Pipeline | BuildPlayerProcessor、pre/post build、程序集过滤、脚本 DLL、Scene、shader/compute shader 预处理 |
| VRCSDK | build-request、Avatar preprocess/postprocess 的多个 callbackOrder 观察点 |
| NDMF | 安装 NDMF 后，在 Resolving / Generating / Transforming / Optimizing 阶段采样 |

四类默认关闭，设置通过 EditorPrefs 保存；**Enable All / Disable All** 可批量切换。
启用后重新导入相关资产或进行构建，在 Console 搜索 `[BPL]`。工具只记录日志，
过滤回调保留传入的程序集列表和 shader 数据，不会修改 Avatar 或阻止构建。
日志留存遵循 Unity Console 设置，工具没有独立的日志文件导出功能。

## 日志解释

Shader/compute shader 对每个回调类型在一次构建中只记录一次。新的 Unity build 或
VRChat Avatar preprocess 会重置去重状态，因此连续构建不需要重载脚本。
这不是每个 shader、变体或编译调用的完整追踪；单纯的编辑器 shader 重编译也不等于
一个新的已识别构建会话。

带 VRCFury/NDMF 名字的 SDK 日志表示按历史 callbackOrder 设置的观察位置，不证明
该第三方插件已经安装、运行或成功。NDMF 日志是各阶段中的观察点，不是其它所有
插件的全局开始/结束。同 order 回调的相对顺序不保证，插件升级后 order 也可能改变。

当前 Linker 回调没有启用。日志不统计完整构建耗时、资产依赖或第三方插件内部步骤，
也不能从缺少一条日志直接推断某个插件失效；应结合具体 Unity/SDK 构建错误检查。

## 单元测试与验证状态

安装 Unity Test Framework，在 Test Runner 的 **EditMode** 运行
`UnityBox.BuildPipelineLogger.Tests.RepeatedBuildLoggingTests`。
VPM 安装时，将本包名追加到项目 `Packages/manifest.json` 的 `testables`。

`Tests/Editor/RepeatedBuildLoggingTests.cs` 分别模拟 Unity 与 VRChat 的构建开始回调，
检查第二次构建会恢复 shader 日志，不依赖脚本重载。当前云环境只完成源代码/配置
检查，尚未执行 Unity 编译和这些测试；应在真实项目中连续构建两次，并分别启用各类
日志确认实际回调覆盖范围。
