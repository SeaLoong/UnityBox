# 会话交接记录（截至 2026-10-24）

> 本文整理当前会话中关于 Unity/VRChat Avatar Security System（ASS）与防热上传的讨论、已验证实验、代码状态及未完成事项，供后续继续工作时恢复上下文。**当前用户指令是停止所有其他工作，只完成此文档；Gate 原型和其他实现工作均暂停。**
>
> 防护讨论仅作为资产保护与鲁棒性设计记录。对平台行为、攻击工具兼容性等未实测事项均标记为假设，不应当作事实承诺。

## 1. 当前工作区与项目

- OS：Windows。
- Unity 项目：`D:\VRChatProjects\UnityBox`。
- ASS 主要目录：`Assets/UnityBox/AvatarSecuritySystem/`。
- ASS 的 Editor / Runtime asmdef 分开；可挂载并序列化到 Avatar 的 MonoBehaviour 必须在 Runtime assembly 中。Editor-only assembly 的类型不能直接作为场景组件使用。
- Unity MCP 工具在本会话早期无法启动（进程以代码 1 退出），Unity 菜单测试主要由用户在 Editor 中执行。
- 仓库记忆：`/memories/repo/avatar-security-system.md` 记录了 ASS 的生成约束及本会话补充的防热上传知识。

## 2. 目标与问题演进

讨论目标逐步收敛为：

1. 不是承诺让 VRChat 官方云端安全检查拒绝盗传包；此前没有充分依据证明能控制或触发该结果。
2. 研究一种特定热上传工具（VRC-Hotswap 一类）对 AssetBundle 做字符串替换时，是否会破坏动画绑定。
3. 用最小 Unity AssetBundle 测试验证“对象路径被改、clip 中预计算绑定未改”的效果。
4. 评估 AvatarObfuscator 对这种方案的影响。
5. 推演对手若升级到解包后精准修改身份字段时，ASS 还可以依赖什么。
6. 用户最后明确将威胁模型限定为“自动化解包、精准替换一些内容，但不完全重建”；随后要求**停止所有工作，把上下文整理成文档**。本文件就是该请求的交付物。

## 3. 已验证的技术事实

### 3.1 Hotswap 字符串替换实验

先前分析的 VRC-Hotswap 源码显示，该工具的工作方式是对解压后的目标 AssetBundle 数据做字节/字符串级全局替换，涉及旧 Avatar ID（`avtr_<uuid>`）、Prefab ID 类名称、CAB 标识和 Unity 版本字符串，然后重新压缩并用 AssetBundle 加载 API 做可加载性检查。它不会在 Unity Editor 中重新构建目标 Avatar。

重要边界：这是对该工具实现路径的分析，不能外推为所有热上传工具的行为；“能加载 AssetBundle”也不等于身份元数据、VRChat 上传或游戏内语义均正确。

### 3.2 最小 AssetBundle 绑定实验（已实测）

实验项目文件：

- `Assets/UnityBox/AvatarSecuritySystem/Editor/AssetBundlePathBindingProbeBuilder.cs`
- `Assets/UnityBox/AvatarSecuritySystem/Runtime/BundleProbeSceneRunner.cs`
- `Assets/UnityBox/AvatarSecuritySystem/Runtime/AnimationPathBindingProbe.cs`
- 另有早期内存版：`Assets/UnityBox/AvatarSecuritySystem/Editor/AnimationPathBindingProbe.cs`
- 生成的测试包：`Assets/UnityBox/AvatarSecuritySystem/Tests/Bundles/ass_path_probe` 和 `ass_path_probe_modified`

测试构造两个动画目标：一个名字含可被替换标记 `ASS_Marker_OLD`，另一个 `PlainMarker` 作为对照。对 AssetBundle 做等长字节替换，加载修改后的包，并比较动画是否驱动两个对象。

字节取证结果：

- 原包大小 41,600 bytes。
- 原包只找到一处 `ASS_Marker_OLD`（偏移 `0x92E0`），一处 `PlainMarker`（偏移 `0x9348`）。
- 未发现 `m_LocalScale.x` 等路径文本；类型树/序列化数据可见 `AnimationClipBindingConstant.m_ClipBindingConstant.genericBindings` 绑定数据。
- 修改包大小相同；只有该 14 字节标记被替换为 `ASS_Marker_NEW`，其余数据相同。
- 用户提供的运行日志：修改包中 ID marker 初始 scale 保持 `1`（未被动画驱动），PlainMarker scale 到 `2`（动画正常驱动）。

**实验结论（限于 Unity 版本、构建格式和该测试包）：** 打包后的 AnimationClip 绑定不依赖可供这类文本替换器改写的路径字符串；Transform 路径绑定以预计算的整数哈希形式保存。对象名改变后，其运行时路径 hash 与 clip 中旧 hash 不匹配，动画断绑。对照目标正常，说明不是整个 harness 失效。

### 3.3 重要语义区分

- Clip 里“有没有原路径的可读文本”与绑定是否依赖原路径不是一回事。
- Player/AssetBundle 中可以没有可见的原路径字符串，但 clip 仍保存由构建时路径计算出的绑定值/hash。
- 对象路径的值仍关键；如果对象名被替换、clip 的 hash 未重算，绑定可能失效。
- 要重新计算绑定，通常需要完整的 Unity 资源重建/重序列化过程；不能把此结论扩大成任何可能的重建器都无法做到。

## 4. AvatarObfuscator 调研结果

用户提供的仓库链接：<https://github.com/cocokoishi/AvatarObfuscator>

调研时 README 显示 v0.4.9，工具是 NDMF 构建期名称混淆器。已查阅 README、CHANGELOG、`Runtime/ObfuscationOptions.cs`、`Editor/Inspector/AvatarObfuscatorEditor.cs`、`Editor/Internal/NameGenerator.cs`、`Editor/Passes/ObfuscateHierarchyPass.cs` 等公开源码页面。

已确认：

- 它的本职是反扒模可读性混淆：GameObject 名、blendshape、Animator/Expression 参数、部分资产名等；会重写动画绑定以维持正常 Avatar 功能。
- 层级混淆会改名非保留对象；保留集合包含 Avatar 根、Armature、人形骨骼和祖先，以及部分 VRChat eye/jaw 和 MMD Body 场景对象。
- `NameGenerator.Next()` 将 `Prefix` 拼到生成名之前；插件把 `obfuscationNamePrefix` 接入该字段。
- Inspector 有“Get Blueprint ID”按钮，将 `PipelineManager.blueprintId` 追加到前缀。
- v0.4.6 changelog 对“防 Hotswap”有作者评论；v0.4.8 changelog 也讨论了打包后动画绑定 hash。作者意见是背景材料，不等于经过验证的安全结论。

与 ASS 的潜在交互：如果 ASS 未来把 Blueprint ID 放进某个 gate 对象名，而 AvatarObfuscator 后续又改写该对象，ID 子串可能消失，基于名字替换的绊线就不触发。相反，如果 obfuscation prefix 包含 Blueprint ID，生成名可能保留该 ID 子串。**实际构建顺序、前缀拼接后的字段编码和最终包结果仍需在具体项目中实测；不要把这条交互当作已完成的兼容保证。**

## 5. VRChat 上传链背景（前期分析摘要）

此前检查过 SDK 代码路径，摘要如下：

- Avatar 上传大致经过 `BuildAndUpload`、Blueprint ID reserve、构建、上传。
- 上传关联 `PipelineManager.blueprintId` 与 Avatar ID；所有权和授权由服务端验证。
- 上传过程会传输 AssetBundle 及版本/平台等元数据。
- 这不证明服务器会扫描或验证包内所有同名/重复身份字段，也不证明“只改某字段”必然可上传。不同 SDK/服务端版本、字段来源及上传路径可能改变行为。
- **没有在本会话中完成“只修改 PipelineManager 身份字段能否上传”的对照实验。**这是后续若恢复工作时首先应验证的假设，且只应使用自有 Avatar、账号和测试素材。

## 6. 威胁模型与边界（记录为假设，不是保证）

### A. 盲目全局字节替换

如果工具替换对象名中的 ID，而不重算动画 path hash，相关动画绑定会断。该行为有最小包实验支持。它可作为针对特定简单替换器的兼容性绊线，不应描述为通用防热传。

### B. 结构化精准编辑（用户当前关心的对手上限）

假设对手工具会解析包、识别并只修改身份字段，同时避开 Avatar 内容字段，不重建整个 Avatar。此时，任何依赖“名字里出现 ID 后被替换”的机制都可能不触发。

可作为防御设计方向、但尚未验证的想法：

- 保留 gate/路径 hash 绊线作为低成本防线，但不依赖它覆盖精准工具。
- 让关键锁定/解锁行为有清晰 fail-closed 语义，并审计剪掉动画或控制器引用后的默认状态。
- 考虑跨多个运行时引用系统的耦合，防止无意的局部改动，但必须优先确保正常 Avatar 兼容性；不能为了造成对手工具失败而破坏用户自己的构建。
- 用稳定的内容水印/取证标记帮助事后识别来源；这属于追溯，不等于阻止上传。
- 任何“伪造类型树、诱饵身份字段、MonoScript 名混淆可误导工具”等此前提及的思路都是未经实验的高风险猜测，**不应直接实现**，也不应将其作为当前方案承诺。

### C. 完整重建/重序列化

如果对手完整重建 Avatar 并重新生成绑定，path-hash 绊线可能消失。用户明确接受此为防护边界：不要求抵御完整重建。仍不应因此宣称语义锁或水印绝对不可绕过。

## 7. ASS 相关现状与注意点

以下为既有仓库记忆中记录的 ASS 约束，后续开发需先查当前代码确认仍然成立：

- ASS 在 NDMF/VRCFury/SDK 多条构建链中有处理时序问题；`IEditorOnly` 组件可能在部分回调前被移除，需通过早期快照传递配置。
- ASS 生成控制器/临时资产时要避免二次构建引用已删除的 `Generated` 文件；清理前后需要恢复 Descriptor 引用。
- 反馈、锁定、Overlay、音频和 Defense 有职责拆分；ASS 动画通常 WD Off，避免覆盖用户对象状态。
- 当前 `ASS_Lock` 相关设计偏向根子对象缩放隐藏/恢复，带 Contact Sender/Receiver 或 PhysBone root 的对象不能不加区分地缩放隐藏。
- `Obfuscator.cs` 曾有静态字段跨构建残留问题，混淆关闭时必须重置为默认名称。
- 之前查出的 ASS 密码动作序列为左手 `Fist → ThumbsUp → HandOpen → Victory`（数值 `1 → 7 → 2 → 4`），可从解包状态机推断；这是安全审计结论，说明静态隐藏不足以当作安全边界。
- 仓库记忆还记录了多种 decoy 参数名，但 decoy 本身不等于安全验证机制。

## 8. 本会话代码/测试状态

本会话早先创建并调整了最小 Bundle probe 的 Editor/Runtime 脚本；历史结果显示三个关键脚本已编译通过，且运行实验通过。探针包括：

- Editor 菜单 `Tools/UnityBox/ASS/Animation Path Binding Probe/Build & Run: Modified Bundle (Hotswap Sim)`。
- 对照菜单 `Build & Run: Original Bundle (Control)`。
- Bundle 运行器在 Play Mode 加载磁盘中的 bundle，避免改过的 bundle 被 Unity Editor 重新导入/重序列化。
- 曾处理 Editor/Runtime assembly 边界、`Object` 命名冲突和跨 assembly 字段可见性问题。

注意：本轮文档任务没有重新运行 Unity 编译或测试；以上状态来自之前会话摘要/用户运行日志，不代表 2026-10-24 当前工作区一定无变化。

## 9. 已暂停的工作与当前状态

用户指令：**停止所有的工作，把当前会话内容、所有上下文信息编写成一份文档放到里面。**

因此：

- Gate 原型没有实现。
- “身份耦合网”、参数链、Blueprint ID 名称校验均只是讨论中的设计方向。
- 没有新增 ASS 生产代码或修改现有锁逻辑。
- 没有执行剩余的 VRChat 平台校验实验。
- 当前只完成本交接文档。

先前待办里曾出现以下拟议后续工作（均暂停）：检查 ASS 现有安全机制、分析 VRChat 上传校验边界、设计兼容的防热上传方案、实现代码和技术文档、运行静态检查并复核。当前用户要求优先级覆盖这些待办。

## 10. 恢复工作时的建议顺序（不代表现在继续执行）

1. 先核对工作区状态和这份文档引用的 probe 文件是否仍存在、是否有本地未提交修改。
2. 用自有 Avatar 和测试账号验证“仅改变必要身份元数据”是否足以完成测试上传；明确所有权/API/缓存约束，不从推测开始实现。
3. 先做兼容性测试矩阵：ASS 开关、NDMF 有无、VRCFury/Avatar Optimizer/AvatarObfuscator 组合、Blueprint ID 缺失/存在、动画 gate 的构建末期对象名和绑定。
4. 若实现 gate，优先让不匹配导致防御维持安全态，并提供明确构建日志；遇到 Blueprint ID 缺失或构建链改名时不要静默跳过。
5. 所有绊线只作为有限工具覆盖的附加防线；文档口径明确不防完整重建，不承诺官方云端拒绝盗传。
6. 不实施未经验证的伪类型树/诱饵字段等破坏性对抗；先证明平台与 Unity 加载器的实际行为，并确保不会误伤正常用户构建。

## 11. 参考链接

- AvatarObfuscator 仓库：<https://github.com/cocokoishi/AvatarObfuscator>
- AvatarObfuscator README / 源码 / CHANGELOG：仓库 `main` 分支（会变化；后续引用请固定 commit/tag）。
- Unity/VRChat SDK 相关代码路径（此前分析）：
  - `Packages/com.vrchat.avatars/Editor/VRCSDK/SDK3A/VRCSdkControlPanelAvatarBuilder.cs`
  - `Packages/com.vrchat.base/Editor/VRCSDK/Dependencies/VRChat/API/VRCApi.cs`
- ASS probe 资产：`Assets/UnityBox/AvatarSecuritySystem/Tests/Bundles/`。

---

**一句话交接：** 已实验证明，特定全局字符串替换会把对象名改掉、却留着 AssetBundle 动画路径哈希，从而断开相应动画；这只是针对简单热传工具的绊线。对精准编辑，只能把它作为附加项，主要评估安全态默认值、锁的鲁棒性和取证水印；上传字段边界尚未实测。当前没有进行 Gate 原型开发，用户要求暂停全部其他工作。
