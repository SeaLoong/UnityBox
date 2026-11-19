# ASS 0.9.11：Overlay Logo 贴图流送

## 修复边界

Overlay 的 `Feedback.CreateOverlayMesh` 将 Resources Logo 绑定到新材质 `_G4D9`。
SDK 3.8.2 的 `VerifyAvatarMipMapStreaming` 会扫描未激活 Renderer 的材质贴图，
拒绝 `importer.mipmapEnabled && !importer.streamingMipmaps`。
因此源头像没有违规贴图，不代表构建阶段新增 Overlay 后仍然合规。

仓库此前跟踪 PNG，但没有跟踪它的 `.meta`。本次将现有 metadata 纳入发布，保留本地已有
GUID `363d399485b2d4c42a83d697ebb7a273` 和其余导入参数，明确启用 `enableMipMap: 1` 与
`streamingMipmaps: 1`。开始本次工作时，本地未跟踪配置已为 streaming=1、mipmap=0；
用户安装包中的配置则为 mipmap=1、streaming=0，且由其工程生成了不同 GUID。
新版本随包提供统一 metadata，构建仍按 Resources 名称引用 Logo，不扫描或修改用户导入器，
不关闭 mipmap、不修改 SDK。没有修改用户头像工程或 PNG 图片内容。

## 测试与结果

Windows 11，Unity 2022.3.22f1，Python 3.14.2，UTF 1.1.33 / NUnit 1.0.6；
真实 VRChat Base/Avatars 3.8.2 与 NDMF 1.11.0，无 MA。没有执行真实上传或新的 Player/性能测量。

| 范围 | 结果 |
| --- | --- |
| core（Packages 布局） | 116/116 通过，无失败或跳过 |
| SDK（Assets 布局） | 48/48 通过，含新增 5 个贴图用例 |
| NDMF（Assets 布局） | 5/5 通过 |
| Windows 打包 unittest | 19/23 通过；3 个既有 CRLF 断言、1 个 POSIX 可执行权限断言失败，工具未修改 |

新增用例 `GeneratedOverlayTexturesMeetUploadStreamingRequirementsWithoutChangingSource` 的
4 组参数覆盖 Processor 的 SDK/NDMF 模式与 Overlay 激活/未激活组合；遍历生成头像所有
Renderer 的共享材质和贴图，确认引用真实 Logo、导入器满足流送要求，生成前后图片、GUID、
metadata 不变。这里的 NDMF 模式指 `Processor.ProcessAvatar(..., true)`，不冒充完整 NDMF 构建；
已有 NDMF pass resolver/final commit 测试另外完整执行了 5 例。

`TextureScanDetectsNonStreamingMipmapsOnInactiveOverlayAndAcceptsStreamingControl` 创建独立
PNG 控制资源，保持 mipmap 开启、关闭 streaming，绑定到未激活的生成 Overlay，要求恰好检出
该贴图；仅在此独立副本开启 streaming 并重导入后，扫描必须为空。源 Logo metadata 不变。
贴图扫描断言使用与 SDK 相同的条件，不直接调用带 GUI 副作用的 SDK 私有校验方法。

## 复现与工程布局

从 Git index 导出本次跟踪文件及明确新增的 Logo metadata，避免把其它未跟踪 `.meta` 混入候选包：

```powershell
git checkout-index --prefix=D:/uv/src11/ -a
python scripts/unity/test_project.py prepare --repo D:\uv\src11 --project D:\uv\ac11 `
  --profile core --package AvatarSecuritySystem --dependency-cache D:\unitybox-verify\cache
python scripts/unity/test_project.py run --project D:\uv\ac11 `
  --unity D:\Unity\2022.3.22f1\Editor\Unity.exe `
  --exclude-category 'UnityBoxPerformance;UnityBoxGraphics' --timeout 1200
```

SDK fixture 先用同一 prepare 准备 `D:\uv\as11`，复制已有隔离工程的 SDK Base/Avatars、NDMF
和模块 manifest，将 ASS 包放入 `Assets/UnityBox/AvatarSecuritySystem`。以有图形设备的 batchmode 执行：

```powershell
D:\Unity\2022.3.22f1\Editor\Unity.exe -batchmode -projectPath D:\uv\as11 `
  -runTests -testPlatform EditMode -testResults D:\uv\as11\sdk.xml `
  -logFile D:\uv\as11\sdk.log `
  -assemblyNames 'UnityBox.AvatarSecuritySystem.VRChat.Tests;UnityBox.AvatarSecuritySystem.NDMF.Tests' `
  -testCategory '!UnityBoxPerformance;!UnityBoxGraphics'
```

初次在 Packages 布局合并执行 169 例时，迁移 fixture 的目录假设导致编译状态异常并连带使
StateMachineBehaviour 创建失败，96 通过、73 失败。移到 Assets 后 165/169 通过，只剩 4 个
既有音频测试断言路径必须在 Packages。最终以两个独立、正确布局的范围复验，全部通过。
诊断 XML 一并保留，不将它们误报为成功或隐藏。core 工程使用短路径，因此未撞上此前长路径
工程的 MAX_PATH 备份限制；没有修改该功能。

## 证据与发布

[results.json](results.json) 包含真实命令、时间、范围、准备标记、源文件和归档指纹、发布 ZIP 检查。
`core-results.xml.gz` / `sdk-results.xml.gz` 与对应压缩日志是最终结果，另留初始诊断 XML
及 Windows 打包测试日志。134 个随包非 Markdown 文件与导入副本、当前源码逐字节一致；
测试之后只调整 Markdown 文档。性能历史不改写，也不以本次导入配置修复声称新的性能数据。

发布检查从最终 Git index 导出干净目录并实际运行 `package_vpm.py`，核对 ZIP CRC、完整 payload、
Logo `.meta` 的字节/GUID/开关和 PNG 指纹；不使用本地未跟踪文件补足发布资源。
