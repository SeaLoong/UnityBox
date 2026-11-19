# ASS 0.9.12：音频 1% Quality 与 Logo 无损压缩

## 实际变更与测量

- 四个音频 importer 的 `defaultSettings.quality` 从 1 改为 **0.01（1%）**，仍为 Vorbis。
  每个 `.meta` 与 0.9.11 对照仅这一行变化：GUID、采样率、Decompress On Load、
  Load In Background、preload、平台覆盖等保留；四个 MP3 源文件逐字节不变。
- Logo PNG 只重新编码 IDAT 压缩数据，保留原 **1638×1638、RGBA**；解码后全部像素字节
  完全相同，非 IDAT chunk 逐字节一致（IHDR、DPI/pHYs、XMP/iTXt、IEND）。没有量化、
  缩放、删除透明度或移除作者元数据。TextureImporter `.meta` 与 0.9.11 逐字节不变，
  包括 GUID、现有 maxTextureSize=512、Crunch、Mip Maps 与 Streaming Mip Maps。

| Logo 源文件 | 字节 |
| --- | ---: |
| 0.9.11 | 687,112 |
| 0.9.12 | 662,801 |
| 减少 | 24,311（3.5381%） |

RGBA 像素 SHA256：`a4c6ad6ba72e64571f3135adae14c038de3e2c987650550c694e7934cc18d1cc`。
原/新 PNG 与四个音频指纹见 [results.json](results.json)。

**限制：** 这是源 PNG 体积变化，不代表 Unity 构建贴图、头像下载体积或显存同比缩小；
现有导入最大尺寸未进一步降低。音频 1% 是有损编码质量，不是文件变成原来的 1%；
Decompress On Load 后的 PCM 内存仍取决于声道、采样率和时长。本次没有测量上传大小、
显存收益、Player 性能或进行主观试听，不对这些项目作优化结果声明。

## 真实验证

Windows 11、Python 3.14.2、Unity 2022.3.22f1、UTF 1.1.33 / NUnit 1.0.6；
SDK fixture 使用真实 VRChat Base/Avatars 3.8.2 与 NDMF 1.11.0，无 MA。

| 范围 | 结果 |
| --- | --- |
| core，Packages 布局 | 116/116 通过，无跳过 |
| VRChat，Assets 布局 | 48/48 通过 |
| NDMF，Assets 布局 | 5/5 通过 |
| Windows packaging unittest | 19/23；4 个既有 CRLF/POSIX 断言失败，工具没有修改 |

现有四组 `BundledAudioImportsWithBackgroundLoadingAndPlayableSamples` 增加对真实导入的
Vorbis / Quality=0.01 的断言；保留后台加载、加载类型、样本数和时长检查。
SDK/NDMF 执行生成反馈音频的真实 SDK 上传扫描和正反对照，并重跑 0.9.11 新增的
Overlay 贴图流送、未激活 Renderer、源资源保全回归。

首次启动全新 SDK fixture 时，Unity 以 `Please fix compile errors before creating new state machine behaviours`
拒绝创建状态行为，32/53 通过、21 失败，但日志没有 `error CS`。未改源码，重启同一隔离 Editor
后完整 53/53 通过。首轮 XML/日志与最终结果均保留，不隐藏首轮失败。

## 复现范围

从 Git index 导出明确跟踪的候选包，不包含本地无关未跟踪 metadata。core：

```powershell
git checkout-index --prefix=D:/uv/src12a/ -a
python scripts/unity/test_project.py prepare --repo D:\uv\src12a --project D:\uv\ac12a `
  --profile core --package AvatarSecuritySystem --dependency-cache D:\unitybox-verify\cache
python scripts/unity/test_project.py run --project D:\uv\ac12a `
  --unity D:\Unity\2022.3.22f1\Editor\Unity.exe `
  --exclude-category 'UnityBoxPerformance;UnityBoxGraphics' --timeout 1200
```

SDK fixture 用相同 prepare 准备 `D:\uv\as12`，将 ASS 放入 `Assets/UnityBox/AvatarSecuritySystem`，
复制先前已验证的隔离 SDK/NDMF 包与 manifest。保留图形设备，使用有等待/退出码检查的
`Start-Process -Wait -PassThru` 调用：

```powershell
D:\Unity\2022.3.22f1\Editor\Unity.exe -batchmode -projectPath D:\uv\as12 `
  -runTests -testPlatform EditMode -testResults D:\uv\as12\sdk-final.xml `
  -logFile D:\uv\as12\sdk-final.log `
  -assemblyNames 'UnityBox.AvatarSecuritySystem.VRChat.Tests;UnityBox.AvatarSecuritySystem.NDMF.Tests' `
  -testCategory '!UnityBoxPerformance;!UnityBoxGraphics'
```

PNG 使用 Pillow `optimize=True, compress_level=9` 重编码后，仅取其 IDAT 数据拼回原始 PNG 的
chunk 序列；重新计算该 IDAT CRC，原非 IDAT chunk 不动。保存前后核对尺寸、模式、全部像素
以及非 IDAT chunk。baseline 是提交 `2dd34a3` 的发布源码副本。

134 个随包非 Markdown 文件在干净候选源码、core 导入副本、SDK 导入副本与当前源码之间
逐字节一致；测试后只更新 Markdown 文档。ZIP 从最终 Git index 干净导出，验证完整 payload、
CRC、版本、四个 Quality、背景加载和 Logo 的字节/metadata。命令、时间、来源标记、
原始/压缩日志与 XML 的 SHA256 见 [results.json](results.json)。
