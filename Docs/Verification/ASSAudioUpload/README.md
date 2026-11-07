# ASS 0.9.5 音频上传校验

ASS 自带音频此前没有 AudioImporter metadata。Unity 默认导入为
`Decompress On Load` 且 `Load In Background=false`；VRC SDK 将这类状态行为音频注册为
上传错误。0.9.5 为四个自带 MP3 固定导入 metadata 并启用后台加载，原 MP3 内容保持。

## 开关与触发路径

“关闭警告音效”只移除倒计时警告；“关闭覆盖界面”只控制 Overlay。密码成功音继续生成。
因此两者同时关闭时，成功状态中的 `PasswordSuccess` 仍会进入 SDK 校验。

`Processor.LoadAudioResources` 加载默认声音，`GesturePassword` 和 `Countdown` 通过
`VrcAnimatorBehaviourAdapter.AddPlayAudioBehaviour` 写入真实 `VRCAnimatorPlayAudio.Clips`。
SDK3.8.2 的 `VRC.SDK3A.Editor.VRCSdkControlPanelAvatarBuilder.ScanAvatarForAnimatorPlayAudio`
按以下条件返回错误音频，并通过 `OnGUIError` 阻止上传：

```csharp
audio.loadType == AudioClipLoadType.DecompressOnLoad && !audio.loadInBackground
```

修复保持两个开关的独立含义。更新包后重新发起正常构建，生成副本使用新的音频导入设置。
自选音频的导入设置仍由资源所有者控制；使用 SDK 的 Fix 或音频 Inspector 开启后台加载。

## 原生验证

全部测试使用 Unity2022.3.22f1、UTF1.1.33、SDK3.8.2；完整范围增加 NDMF1.11、MA1.18。
每个工程只安装当前 ASS 包与所选依赖，执行其完整功能程序集，排除独立性能和图形分类。

- 检查四个 MP3 的实际 AudioClip/AudioImporter 后台加载设置及有效音频长度、样本数。
- 生成警告音与 Overlay 的四种开关组合；核对成功/警告音、AudioSource 路径与对象集合。
- 直接反射调用真实 SDK 私有校验方法，检查生成结果返回空错误列表。
- 用临时 Assets 中的 MP3 重现前台解压错误，仅开启其后台加载后 SDK 错误消失。
  四个包音频及其 metadata 在整个对照中逐字节保持。
- 通过 NDMF 的实际调度、图提交及 Finish，检查最终 FX 的音频引用和 SDK 校验，
  同时保留上游/下游虚拟图及原 Controller。
- 构建运行普通 Unity Player，检查实际密码输出与构建前源配置保留。

[执行与源码指纹](evidence.json)保存原生命令、NUnit/XML/程序集哈希、实际包版本和比较结果。
本页的各依赖范围包含重复用例，分别计数。

## 执行结果

| ASS 专项范围 | 实际结果 |
| --- | --- |
| ass-audio-core | 80/80 通过，1 个测试程序集 |
| ass-audio-sdk | 121/121 通过，2 个测试程序集 |
| ass-audio-full | 123/123 通过，3 个测试程序集 |
| Linux Player | 2 个场景、252 帧、13 次输出检查通过；源配置 1 个保留 |
| 打包工具 | 23/23 通过；8 个当前 ZIP 与源码及 metadata 一致 |

## 全库基线

八插件功能、图形和串行性能的上一轮完整快照为 `7cd4b40`，其中 ASS 为0.9.4。
该快照保留在[全库索引](../../VerificationResults.json)、[性能报告](../../Performance.md)和
[整项目引用恢复归档](../WholeAssetsUpgrade/README.md)；本次 ASS 专项结果单列，不改写旧执行版本。
