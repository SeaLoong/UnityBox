# 2026-10-24 路径绑定实验归档

本文保留当时 Windows 项目 `D:\VRChatProjects\UnityBox` 的研究记录和用户提供的运行
结果。现行实现入口是 [ASS 用户手册](Assets/UnityBox/AvatarSecuritySystem/Editor/README.md)、
[技术参考](Assets/UnityBox/AvatarSecuritySystem/Editor/ASS_TechnicalDoc.md)及
[插件总览](Docs/PluginAudit.md)。当前源码继续开发，历史“暂停”指令由后续迭代任务接续。

## 实验资源

| 内容 | 路径/值 |
| --- | --- |
| 构建器 | `Assets/UnityBox/AvatarSecuritySystem/Editor/AssetBundlePathBindingProbeBuilder.cs` |
| 包运行器 | `Assets/UnityBox/AvatarSecuritySystem/Runtime/BundleProbeSceneRunner.cs` |
| 运行时探针 | `Assets/UnityBox/AvatarSecuritySystem/Runtime/AnimationPathBindingProbe.cs` |
| 内存版本 | `Assets/UnityBox/AvatarSecuritySystem/Editor/AnimationPathBindingProbe.cs` |
| 原包 | `Assets/UnityBox/AvatarSecuritySystem/Tests/Bundles/ass_path_probe` |
| 修改包 | `Assets/UnityBox/AvatarSecuritySystem/Tests/Bundles/ass_path_probe_modified` |
| Unity | `2022.3.22f1` |

构造两个 Transform 动画目标：名字包含 `ASS_Marker_OLD` 的目标和 `PlainMarker` 对照目标。
运行器从磁盘加载包，在 Play Mode 比较两个目标的缩放输出。

## 字节和运行记录

- 两个包大小均为 **41,600 bytes**。
- 原包中 `ASS_Marker_OLD` 出现一次，偏移 **0x92E0**；`PlainMarker` 出现一次，偏移 **0x9348**。
- 修改包仅把 **14 字节**的 `ASS_Marker_OLD` 替换为 `ASS_Marker_NEW`，其它字节相同。
- 序列化数据含 `AnimationClipBindingConstant.m_ClipBindingConstant.genericBindings`，路径绑定
  使用构建时计算的整数 hash；检查时没有 `m_LocalScale.x` 等可读路径文本。
- 用户提供的修改包运行日志：标记目标初始 scale 保持 **1**，对照目标 scale 到 **2**。

该构造展示了对象路径与预计算动画绑定的对应关系：等长改名改变运行时路径 hash，Clip
继续引用构建时的 hash；对照路径保持原值并继续输出动画。重新构建资源时，Unity 按当前
路径重新生成绑定。

菜单保留 Original Bundle、Modified Bundle 和内存改名三个入口。构建使用显式
`AssetBundleBuild[]`，切换场景和清理实验资源时提供保存/取消。

## 当时调研的工具路径

当时分析的 VRC-Hotswap 类实现对解压数据中的 Avatar ID、Prefab 名、CAB 标识和 Unity
版本字符串进行替换，再重新压缩并加载 AssetBundle。上述实验对应其中的对象名替换步骤。

[AvatarObfuscator](https://github.com/cocokoishi/AvatarObfuscator) 当时 README 为 v0.4.9。
已阅读的公开源码包括 `Runtime/ObfuscationOptions.cs`、
`Editor/Inspector/AvatarObfuscatorEditor.cs`、`Editor/Internal/NameGenerator.cs` 和
`Editor/Passes/ObfuscateHierarchyPass.cs`。它在 NDMF 构建中改写对象、blendshape 和参数名，
同时更新动画绑定。保留集合包含 Avatar 根、Armature、人形骨骼及祖先和相关眼/下颌对象。
`NameGenerator.Next()` 在生成名前加入 Prefix，Inspector 的 Get Blueprint ID 将
`PipelineManager.blueprintId` 加入前缀。

SDK 代码中，`BuildAndUpload` 经过 Blueprint ID reserve、构建和上传，Avatar ID 与
`PipelineManager.blueprintId` 关联，服务端处理所有权和授权。参考代码位置：

- `Packages/com.vrchat.avatars/Editor/VRCSDK/SDK3A/VRCSdkControlPanelAvatarBuilder.cs`
- `Packages/com.vrchat.base/Editor/VRCSDK/Dependencies/VRChat/API/VRCApi.cs`

## 归档与现行实现

研究中的 Gate、身份字段耦合和内容水印是当时列出的方案。现行 ASS 工作流由手势输入、
连续保持、超时、锁定、反馈、解锁后重锁和 Restart 组成；生成与运行结果以当前源码和
[验证记录](Docs/VerificationResults.json)为索引。
历史探针和二进制保留在源码，正式 VPM 归档按打包规则排除这些实验资源。
