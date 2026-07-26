# Advanced Costume Controller 使用手册

Advanced Costume Controller（以下简称 **ACC**）是一个基于 [Modular Avatar](https://modular-avatar.nadena.dev/) 的 VRChat Avatar 编辑器工具。它会扫描指定层级中的服装、部件和变体，并自动生成：

- VRC 表情菜单（通过 `ModularAvatarMenuInstaller` 安装）；
- 参数声明（`ModularAvatarParameters`）；
- FX Animator Controller（通过 `ModularAvatarMergeAnimator` 合并）；
- 服装切换、部件开关、混搭和材质替换所需的 AnimationClip。

ACC 不仅可用于完整服装，也可用于头发、眼睛、饰品等 Avatar 区域；默认通过独立骨架识别，也可用 `ACCOutfitMarker` 显式标记任意服装根容器。

## 前置条件

- Unity `2022.3` 或兼容版本；
- VRChat SDK Avatars；
- Modular Avatar `1.10.0` 或更高版本；
- 自动识别时：目标区域中至少有一个 `SkinnedMeshRenderer`，且正确指定了 `rootBone`；
- 无独立骨架或网格时：将 `ACCOutfitMarker` 挂到服装根对象；
- 服装或可控区域已放在 Avatar 的层级中。

打开工具：

```text
Tools > UnityBox > Advanced Costume Controller
```

## 快速开始

以下是一套普通服装的最小示例：

```text
Avatar
└── Clothes Root                 ← 选择为 Costumes Root
    └── Casual Outfit             ← 一套服装
        ├── Armature              ← 无 Mesh 的骨架分支
        ├── Body Mesh             ← SkinnedMeshRenderer
        ├── Hat                   ← 可控部件
        └── Glasses               ← 可控部件
```

1. 打开 ACC 窗口。
2. 在顶部选择界面语言；默认 `Auto / 自动` 跟随 Unity 系统语言。
3. 将 `Clothes Root` 拖入 **Costumes Root / 服装根节点**。
4. 确认 **Parameter Prefix / 参数前缀**。默认自动使用 Root 名称。
5. 点击 **Refresh Preview / 刷新预览**，确认服装和部件识别正确。
6. 如需开关帽子、眼镜等，开启 **Enable Parts Control / 启用部件控制**。
7. 点击 **Generate / 生成**，阅读摘要并确认。

生成后，`Clothes Root` 下会出现 `Clothes Root Menu`。该对象已添加 MA Menu Installer、MA Parameters 和 MA Merge Animator，上传时由 Modular Avatar 自动处理。

## 核心概念

| 名称 | 含义 |
|---|---|
| **Costumes Root** | 本次 ACC 扫描与动画绑定的根节点；生成动画路径都相对它计算。 |
| **Outfit Base** | 被识别为一套服装本体的对象；它拥有服装骨架分支，或带有 `ACCOutfitMarker` 显式标记。 |
| **Outfit Object** | 菜单中代表一套服装的对象；有变体时是共同父对象，无变体时等于 Outfit Base。 |
| **Part / 部件** | Outfit Base 的直接子对象，含网格且不属于骨架分支。 |
| **Variant / 变体** | 与 Outfit Base 同级的替换对象，或材质变体标记对象。 |
| **Parameter Prefix** | ACC 的唯一命名空间，同时也是主服装 Int 参数。 |

## 层级与扫描规则

### Outfit Base 的识别条件

ACC 不会再以“找到 Mesh 后取其父对象”的方式猜测服装根。一个对象成为 Outfit Base，需要同时满足：

1. 后代包含可渲染网格；
2. 后代包含 `SkinnedMeshRenderer`；
3. 至少一个 `SkinnedMeshRenderer.rootBone` 位于该对象的后代；
4. 从 root bone 向上到 Outfit Base 的顶层骨架分支本身不含 Mesh。

因此，识别由真实骨架结构决定，**不依赖对象名称**。`Armature`、`Bone`、`Skeleton` 等名称无需配置忽略列表；ACC 已没有 Ignore Names 选项。

<a id="acc-outfit-marker"></a>

### 无独立骨架的原版服装

部分原版服装、Mesh 拆分件或复用 Avatar 骨架的服装层级只有网格，没有自身的 `Armature` 分支。这类对象无法通过自动骨架扫描识别，可显式添加组件：

```text
UnityBox > ACC Outfit Marker
```

将 `ACCOutfitMarker` 挂到希望作为 Outfit Base 的对象上：

```text
Costumes Root
└── Original Outfit Meshes       ← 添加 ACC Outfit Marker
    ├── Top Mesh
    ├── Bottom Mesh
    └── Shoes Mesh
```

Marker 是完全显式的声明：即使对象本身及后代没有网格或骨架，ACC 也会将它识别为服装。带 Marker 的同级对象也可作为无网格变体加入同一变体组。标记命中后，ACC 和自动识别服装一样停止扫描其后代，因此其中嵌套网格会继续按部件规则处理，不会成为另一套服装。

`ACC Outfit Marker` 的 Inspector 同时提供该套服装的部件菜单名称格式化设置；规则会随 Marker 持久保存，不受 ACC 窗口关闭或刷新影响。

### 为什么嵌套网格不会被重复识别

扫描到 Outfit Base 后，ACC 不会再将其后代当成候选服装扫描：

```text
Outfit
├── Armature
├── Main Mesh
└── Accessory Container
    └── Accessory Mesh
```

这里仅识别 `Outfit` 一套服装；`Accessory Container` 是其部件候选，不会变成第二套服装。

### 部件的识别条件

ACC 仅收集 Outfit Base 的**直接子对象**。对象需要：

- 自身或后代包含 `SkinnedMeshRenderer`，或 `MeshRenderer + MeshFilter`；
- 不在任意服装骨架的 `rootBone` 分支内。

菜单、PhysBone 配置、Constraint 容器等不含网格的功能对象不会成为部件，也不会被部件动画直接开关。

## 窗口配置

| 选项 | 说明 |
|---|---|
| **Language / 语言** | `Auto / 自动`、`English`、`中文`。影响 ACC 编辑器窗口和生成确认对话框。 |
| **Costumes Root / 服装根节点** | 必填。选择本次 ACC 的扫描和动画根。 |
| **Parameter Prefix / 参数前缀** | 必填且同一 Avatar 内必须唯一。它同时是主 Int 参数、参数前缀、Layer 前缀、Controller 名和输出目录命名空间。 |
| **Default Outfit / 默认服装** | 可选。指定初始服装；未指定时按名称关键词匹配，最后回退到第一个服装。 |
| **Enable Parts Control / 启用部件控制** | 启用普通模式的部件开关。 |
| **Enable Custom Mixer / 启用混搭模式** | 仅在已启用 Parts Control 时可选；启用独立的混搭参数和动画层。 |
| **Custom Mixer Name / 混搭菜单名称** | 菜单中的混搭入口名，默认 `CustomMix`。 |
| **Output Folder / 输出目录** | 自动生成资产的基础目录；必须是安全的 `Assets/...` 相对路径，实际路径会追加 Avatar 名和 Parameter Prefix。 |

### Parameter Prefix 的规则

假设 `Parameter Prefix = Hair`：

```text
主服装 Int 参数：Hair
普通部件参数：Hair/{OutfitPath}/{PartPath}
混搭部件参数：Hair/{CustomMixerName}/{OutfitPath}/{PartPath}
混搭变体参数：Hair/{CustomMixerName}/{OutfitGroupPath}
```

同一 Avatar 上每套 ACC 必须使用不同前缀。例如：

| 控制目标 | 推荐 Parameter Prefix |
|---|---|
| 发型 | `Hair` |
| 眼睛 | `Eyes` |
| 衣服 | `Clothes` |
| 饰品 | `Accessories` |

Parameter Prefix 必须至少包含一个字母或数字；只包含空格、符号或分隔符的前缀无法产生稳定的 Controller 文件名，ACC 会拒绝生成。

## 普通服装与部件控制

### 仅服装切换

关闭 Parts Control 时，ACC 只生成服装/变体选择菜单和主 Int 参数：

```text
Clothes Root Menu
├── Casual Outfit       → Clothes = 0
├── Formal Outfit       → Clothes = 1
└── Sport Outfit        → Clothes = 2
```

### 启用 Parts Control

启用后，ACC 额外生成：

- 每个部件或分组一个同步 Bool 参数；
- `Parts Init` Layer，在启动时将全部可控部件明确置为 OFF；
- `Parts Control` Direct BlendTree，根据普通部件参数播放 ON Clip；
- 每套服装下的 `Parts` 子菜单。

```text
Clothes Root Menu
└── Casual Outfit
    ├── Parts
    │   ├── Hat           → Clothes/Casual_Outfit/Hat
    │   └── Glasses       → Clothes/Casual_Outfit/Glasses
    └── Casual Outfit     → Clothes = 0
```

部件菜单的默认值取自当前 `activeSelf`。运行时仍由 `Parts Init` 初始化所有可控部件，因此请以生成菜单和 Animator 行为为准。

## 部件分组

### 推荐方式：使用层级父对象

将需要一起开关的网格放入同一个直接子对象：

```text
Outfit Base
├── Armature
├── Body Mesh
├── Bag Set                  ← 一个部件、一个开关
│   ├── Bag Mesh
│   ├── Strap Mesh
│   └── Buckle Mesh
└── Hat                      ← 另一个部件、另一个开关
```

`Bag Set` 是 Outfit Base 的直接子对象且后代含 Mesh，因此会作为一个整体部件。ACC 只动画其 `m_IsActive`，关闭它会连带关闭所有子网格。

这种分组保存于 Avatar 层级或 Prefab 中，是推荐的长期工作流。

<a id="acc-part-group-marker"></a>

### 持久方式：ACC Part Group Marker

当部件无法或不适合调整父子层级时，可在任意部件或容器对象上添加：

```text
UnityBox > ACC Part Group Marker
```

设置组件的 **Group Name**：同一 Outfit Base 下所有 Group Name 相同的 Marker 会共用一个菜单开关、参数和动画 Clip。

```text
Outfit Base
├── Armature
├── Hat Mesh                 ← ACC Part Group Marker：Head Set
├── Glasses Mesh             ← ACC Part Group Marker：Head Set
└── Bag Mesh                 ← ACC Part Group Marker：Bag
```

上述例子生成 `Head Set` 和 `Bag` 两个控制项；`Head Set` 会同时开关帽子和眼镜。Marker 也可挂在没有 Mesh 的容器对象上，控制该容器的 `m_IsActive`。Group Name 留空时默认使用对象名称。

带 Marker 的对象会优先于普通顶层部件自动识别：若 Marker 位于某个顶层部件的后代，ACC 只生成 Marker 对应的控制项，避免父对象和标记子对象同时写入重叠动画。

#### 不控制部件

`ACC Part Group Marker` 的 **Mode** 设为 `Exclude` 时，ACC 不会为该对象生成部件菜单、参数或动画控制。它也会抑制包含该对象的自动顶层部件控制，适用于不希望被 ACC 管理的内置网格、功能节点或已有其他动画控制的部件。

`Exclude` 模式不使用 Group Name。

### 部件扫描预览

ACC 窗口会始终显示该服装所有扫描到的部件决策，即使尚未启用 Parts Control：

- **`[A]`**：自动识别的普通部件；
- **`[MG: 名称]`**：由 `ACC Part Group Marker` 持久分组；
- **`[SG: 名称]`**：当前 ACC 窗口中临时填写的分组；
- **`[X]`**：由 `Exclude` Marker 排除，不会生成菜单、参数或动画。

预览标题会统计可控制与已排除的部件数量。每个可控制卡片仍可勾选是否参与本次生成；参数路径显示在卡片最后一行，便于长路径换行阅读。

### 临时方式：预览中的“分组 / Group”文本框

在预览中为多个已识别部件填写相同分组名，会让它们共用：

- 一个菜单项；
- 一个参数；
- 一个 BlendTree 子项；
- 一张同时设置多个 `m_IsActive` 的动画 Clip。

此分组名只保存于当前 ACC 窗口会话。关闭窗口、切换 Root 或刷新预览后会清空，不适合作为长期配置。

### 菜单名称格式化

在该 Outfit Base 的 **ACC Outfit Marker** Inspector 中，可以为该套服装的自动部件菜单标签配置：

- **Remove Prefix / 移除前缀**：精确移除统一前缀；
- **Remove Suffix / 移除后缀**：精确移除统一后缀；
- **Regex Pattern / Regex Replacement**：在前后缀处理后执行正则替换。

Inspector 会列出该 Outfit Marker 下所有扫描到的可控制与已排除部件，并显示 `Auto`、`Marker Group`、`Excluded Marker` 决策标签和格式化后的菜单名称。例如，前缀设置为 `Clothes_` 时，`Clothes_Hat` 在预览和菜单中显示为 `Hat`。没有 `ACC Outfit Marker` 的自动识别服装不会应用名称格式化。

格式化只影响自动部件的**菜单显示名称**。参数名、Animator 控制条件和动画绑定路径仍使用原始层级，确保重命名显示文本不会破坏已有参数契约。`ACC Part Group Marker` 的显式 Group Name 不会被格式化。

### 组件 Inspector 语言

`ACC Outfit Marker`、`ACC Part Group Marker` 和 `ACC Variant Material Override` 都使用与 ACC 主窗口一致的系统 Auto 中英显示规则：中文系统显示中文，其他系统显示英文。它们的 Inspector 会显示组件用途和必要的配置提示。

每个组件标题右侧的 `?` 按钮会打开本手册中对应的仓库文档章节。

## 变体

### 对象变体

对象变体位于 Outfit Base 的同级，共同父对象不能是 Costumes Root：

```text
Costumes Root
└── Jacket Variants
    ├── Jacket Red           ← Outfit Base；拥有骨架
    │   ├── Armature
    │   └── Jacket Mesh
    └── Jacket Blue          ← 同级对象变体
        └── Jacket Mesh
```

ACC 将 `Jacket Variants` 作为菜单节点，并生成 `Jacket Red`、`Jacket Blue` 选择项。

> 选择任意对象变体时，ACC 会保持 Outfit Base 活动，以承载可能位于本体下的共享部件。若所有同级变体都包含完整 Mesh，请先在 Avatar 中验证 Base 与变体同时 active 时不会产生叠穿。

### 默认服装选择

优先级如下：

1. `Default Outfit` 显式指定的对象；
2. 名称或路径含 `origin`、`original`、`default`、`base`、`vanilla`、`standard`、`normal` 的服装；
3. 扫描顺序中的第一个服装。

<a id="acc-variant-material-override"></a>

## 材质变体

材质变体适用于“不复制 Mesh，只更换同一 Outfit Base 的材质”的场景。

### 层级示例

```text
Costumes Root
└── Jacket Variants
    ├── Jacket Base                  ← Outfit Base
    │   ├── Armature
    │   └── Jacket Mesh
    ├── Red Material                 ← ACCVariantMaterialOverride
    └── Blue Material                ← ACCVariantMaterialOverride
```

材质标记对象必须与 Outfit Base 同级，并将 `Outfit Base` 字段指向 `Jacket Base`。

### 配置步骤

1. 新建空对象作为材质变体，放在 Outfit Base 同级。
2. 添加组件：`UnityBox > ACC Variant Material Override`。
3. 在 Inspector 设置 **Outfit Base**。
   - 新增组件时会尝试选择第一个同级对象，必须人工确认引用正确。
4. 点击 **Refresh Materials**。
5. 为每个 Source Material 设置 **Replace With**；留空表示保留 Source Material。
6. 回到 ACC 窗口，点击 Refresh Preview 并 Generate。

材质曲线会写入普通 `Outfit Switching` Clip 或 Mixer 变体 Clip，不会生成独立材质 Layer。每次切换都会先恢复原材质，再应用当前材质变体，避免前一个变体残留。

## Custom Mixer 混搭

Custom Mixer 用于从多套服装中自由组合部件。它**必须启用 Parts Control**。

### 启用步骤

1. 启用 **Enable Parts Control**。
2. 启用 **Enable Custom Mixer**。
3. 按需修改 `Custom Mixer Name`（不能为空）。
4. 刷新预览，确认需要参加混搭的部件均已勾选。
5. 生成。

### Mixer 的运行逻辑

点击 `CustomMix/Enable` 后，主 `{ParameterPrefix}` 被设为普通服装索引后的特殊值：

1. `Outfit Switching` 进入 `Custom Mixer` 状态；
2. 所有选中服装的 Outfit Base 被激活；
3. `Parts Init` 保持所有可控部件 OFF；
4. 普通 `Parts Control` 进入无曲线 Off 状态，不再干扰混搭；
5. `Mixer Parts` 使用独立参数开启部件或分组；
6. 每个有变体的服装组由 `Mixer_{Group}` Layer 选择变体；
7. Mixer 中选择材质变体时会同步应用材质曲线。

普通模式与混搭模式的部件参数互不共用；模式切换不会直接改写另一模式已保存的参数值。

### Mixer 菜单示例

```text
Clothes Root Menu
└── CustomMix
    ├── Enable                  → Clothes = N
    ├── Jacket Variants
    │   ├── Jacket Red          → Clothes/CustomMix/Jacket_Variants = 0
    │   ├── Jacket Blue         → Clothes/CustomMix/Jacket_Variants = 1
    │   └── Parts
    │       ├── Collar          → Clothes/CustomMix/Jacket_Variants/Collar
    │       └── Sleeves         → Clothes/CustomMix/Jacket_Variants/Sleeves
    └── Casual Outfit
        ├── Hat                 → Clothes/CustomMix/Casual_Outfit/Hat
        └── Glasses             → Clothes/CustomMix/Casual_Outfit/Glasses
```

## 生成结果与多实例隔离

### Avatar 层级

每次生成会删除并重建：

```text
{CostumesRoot}/{CostumesRoot.name} Menu
```

菜单根包含：

- `ModularAvatarMenuInstaller`；
- `ModularAvatarMenuItem`（Children 子菜单）；
- `ModularAvatarParameters`；
- `ModularAvatarMergeAnimator`（FX，Relative Path Root 为 Costumes Root）。

### 输出资产

实际输出路径：

```text
{OutputFolder}/{AvatarName}/{SanitizedParameterPrefix}/
├── {SanitizedParameterPrefix}.controller
└── Animations/
    ├── Outfit_000_*.anim
    ├── Outfit_XXX_CustomMixer.anim
    ├── PartsInit_OFF.anim
    ├── Parts/*.anim
    └── Mixer/MixerVariant_*.anim
```

Layer 名也带 Parameter Prefix：

```text
Hair/Outfit Switching
Hair/Parts Init
Hair/Parts Control
Hair/Mixer Parts
Hair/Mixer_{OutfitGroup}
```

头发、眼睛、衣服等多个 ACC 可以在同一 Avatar 上共存，前提是它们的 Parameter Prefix 不同。

## 重新生成与清理规则

若当前 ACC Controller 已存在，ACC 会询问是否覆盖。确认后，会删除并重建：

```text
{OutputFolder}/{AvatarName}/{ParameterPrefix}/
```

因此：

- 可以安全反复 Generate；
- 同名旧 Clip 不会残留；
- 不会影响其他 Parameter Prefix 对应的 ACC 输出；
- **不要**将需要保留的手工资产放进 ACC 专属输出目录。
- Output Folder 必须位于 `Assets` 下，且不能包含 `.` 或 `..` 路径段；不满足时 ACC 会拒绝生成。
- 对象名称可以包含 `/`；ACC 在重生成菜单时会按精确对象名称处理，不会将它误解析为层级路径。

## 故障排查

### 刷新预览后没有服装

检查：

1. Costumes Root 是否选对；
2. 服装是否有 `SkinnedMeshRenderer`；
3. `SkinnedMeshRenderer.rootBone` 是否已赋值；
4. rootBone 是否位于预期 Outfit Base 的后代；
5. 顶层骨架分支是否意外包含 Mesh。

如果服装没有独立骨架、只有网格，或希望手动指定某个容器为服装根，请在其 Outfit Base 上添加 `ACCOutfitMarker` 后重新刷新预览。

### 部件没有出现在 Parts 菜单

检查对象是否：

1. 是 Outfit Base 的直接子对象；
2. 自身或后代包含有效网格；
3. 不在服装骨架的 rootBone 分支内；
4. 仍在预览中被勾选。

### Custom Mixer 开关不可选

必须先启用 **Enable Parts Control**。这是 Mixer 的前置条件。

### 多个 ACC 互相影响

确认每个 ACC 使用不同的 `Parameter Prefix`。特别注意手动改名后不要留空或重复。

### 材质变体不生效

检查：

1. `ACCVariantMaterialOverride` 是否挂在 Outfit Base 的同级对象；
2. 组件的 Outfit Base 引用是否正确；
3. 是否在设置 Outfit Base 后点击过 Refresh Materials；
4. 是否填写了 Replace With；
5. 修改标记后是否重新 Refresh Preview 并 Generate。

### 生成后手工修改的菜单或参数消失

这是预期行为。ACC 会重建 `{CostumesRoot.name} Menu` 与当前 Parameter Prefix 的输出目录。自定义菜单或手工资产应放在 ACC 生成范围之外。

## 开发结构

```text
Assets/UnityBox/AdvancedCostumeController/
├── Editor/
│   ├── Window.cs                         编辑器窗口与预览选择
│   ├── Scanner.cs                        骨架/网格扫描
│   ├── Generator.cs                      菜单、参数、Controller 生成协调
│   ├── AnimationBuilder.cs               Animator Layer 与 Clip 创建
│   ├── Mixer.cs                          Custom Mixer 菜单创建
│   ├── ACCVariantMaterialOverrideEditor.cs
│   └── Localization.cs                   编辑器中英文本地化
└── Runtime/
    ├── ACCOutfitMarker.cs                 无骨架服装的显式识别标记
    ├── ACCPartGroupMarker.cs               持久化部件分组标记
    └── ACCVariantMaterialOverride.cs      材质变体标记组件
```

## 注意事项

- 生成操作支持 Unity Undo；输出目录中资产的删除属于 AssetDatabase 操作，生成前请确认摘要。
- ACC 使用 Write Defaults 策略；与其他 FX 工具混用时，上传前应使用 Modular Avatar 的构建预览检查最终 Animator。
- 部件分组优先使用层级父对象；预览中的文本分组仅为临时会话配置，不会持久化。
- Parameter Prefix 同时是参数和隔离键。修改它等同于创建新的 ACC 命名空间；旧 Prefix 的输出目录不会被新 Prefix 自动清理。
