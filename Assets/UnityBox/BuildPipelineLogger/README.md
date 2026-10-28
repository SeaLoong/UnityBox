# Build Pipeline Logger

通过 Unity Console 观察资产导入和构建回调，帮助检查某个回调是否到达。
需要 Unity 2022.3；独立 Unity 项目不需要 VRChat SDK、Modular Avatar 或 NDMF。
VPM 包名：`top.sealoong.unitybox.build-pipeline-logger`。

## 使用

在 `Tools > UnityBox > Build Pipeline Logger` 勾选需要的日志类别：

| 菜单 | 观察范围 | 依赖 |
| --- | --- | --- |
| Asset Pipeline | 纹理/模型/音频导入、批量导入等 AssetPostprocessor 回调 | Unity |
| Build Pipeline | BuildPlayerProcessor、pre/post build、程序集过滤、脚本 DLL、Scene、shader/compute shader 预处理 | Unity |
| VRCSDK | build-request、Avatar preprocess/postprocess 的多个 callbackOrder 观察点 | 可选 VRChat Avatars SDK（使用 >= 3.7.0） |
| NDMF | Resolving / Generating / Transforming / Optimizing 各阶段采样 | 可选 NDMF |

四类默认关闭，设置通过原有 EditorPrefs 键保存；**Enable All / Disable All** 批量
切换保存的偏好。未安装对应包时，VRCSDK / NDMF 菜单不可用，Unity 日志仍可正常使用。
启用后重新导入资产或进行构建，在 Console 搜索 `[BPL]`。

工具只记录日志，过滤回调保留传入的程序集列表和 shader 数据，不会修改 Avatar
或阻止构建。日志留存遵循 Unity Console 设置，没有独立的日志文件导出功能。

## 依赖边界

- `UnityBox.BuildPipelineLogger` 只引用 Unity，包含资产导入和 Unity 构建回调。
- `UnityBox.BuildPipelineLogger.VRC` 在存在 `com.vrchat.avatars` 时启用，包含 SDK
  回调，并引用 `VRC.SDKBase` / `VRC.SDKBase.Editor`。
- `UnityBox.BuildPipelineLogger.NDMF` 在存在 `nadena.dev.ndmf` 时启用，不引用 VRC。

包不再强制安装 VRChat SDK；各集成使用真实已安装包的 API，没有替代 SDK 的实现。
这次拆分保留菜单路径、设置键、日志类别和回调类名，无需迁移场景组件。

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

## 单元测试

安装 Unity Test Framework，在 Test Runner 的 **EditMode** 运行：

- `UnityBox.BuildPipelineLogger.Tests`：仅需 Unity，检查连续构建恢复 shader 日志、
  同一构建内去重、关闭日志及程序集过滤不修改输入。
- `UnityBox.BuildPipelineLogger.VRC.Tests`：额外需要真实 VRChat Avatars SDK，检查
  连续 Avatar preprocess 恢复 shader 日志；缺少 SDK 时该程序集不会编译。

通过 VPM/UPM 安装时，将本包名追加到 `Packages/manifest.json` 的 `testables`。
源码位于 `Tests/Editor/RepeatedBuildLoggingTests.cs` 和 `Tests/Editor/VRC/`。
这些测试调用真实回调实现，但不能代替完整的导入/构建验证；Unity 执行结果以实际
Test Runner 报告为准，源码和配置检查不代表测试通过。
