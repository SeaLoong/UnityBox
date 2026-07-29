# Advanced Costume Controller 使用手册

Advanced Costume Controller（以下简称 **ACC**）是一个基于 [Modular Avatar](https://modular-avatar.nadena.dev/) 的 VRChat Avatar 编辑器工具。它会扫描指定层级中的服装、部件和变体，并自动生成：

- VRC 表情菜单（通过 `ModularAvatarMenuInstaller` 安装）；
- 参数声明（`ModularAvatarParameters`）；
- FX Animator Controller（通过 `ModularAvatarMergeAnimator` 合并）；
- 服装切换、部件开关、混搭和材质替换所需的 AnimationClip。

ACC 不仅可用于完整服装，也可用于头发、眼睛、饰品等 Avatar 区域；优先通过独立骨架分支中的 `MA Merge Armature` 识别，也兼容原有独立骨架识别，并可用 `ACCOutfitMarker` 显式标记任意服装根容器。

## 前置条件

- Unity `2022.3` 或兼容版本；
- VRChat SDK Avatars；
- Modular Avatar `1.10.0` 或更高版本；
- 已有 `MA Merge Armature` 的服装会被优先识别；
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
2. 在顶部选择界面语言；默认 `Auto（自动）` 跟随 Unity 系统语言。
3. 将 `Clothes Root` 拖入 **Costumes Root / 服装根节点**。
4. 参数前缀和根菜单名称自动填充为 Root 名称，可手动修改。
5. 如需自定义根菜单在 VRChat 中的显示名，修改 **Root Menu Name**。
6. 点击 **Refresh Preview / 刷新预览**，确认服装和部件识别正确。
7. 如需开关帽子、眼镜等，开启 **Enable Parts Control / 启用部件控制**。
8. 点击 **Generate / 生成**，阅读摘要并确认。

生成后，`Clothes Root` 下会出现 `ACC_Menu` 对象。该对象已添加 MA Menu Installer、MA Parameters 和 MA Merge Animator，上传时由 Modular Avatar 自动处理。

## 核心概念

| 名称 | 含义 |
|---|---|
| **Costumes Root** | 本次 ACC 扫描与动画绑定的根节点；生成动画路径都相对它计算。 |
| **Outfit Base** | 被识别为一套服装本体的对象；它拥有 `MA Merge Armature` 骨架分支、可推导的独立骨架，或带有 `ACCOutfitMarker` 显式标记。 |
| **Outfit Object** | 菜单中代表一套服装的对象；有变体时是共同父对象，无变体时等于 Outfit Base。 |
| **Part / 部件** | Outfit Base 的直接子对象，含网格且不属于骨架分支。 |
| **Variant / 变体** | 与 Outfit Base 同级的替换对象，或材质变体标记对象。 |
| **Parameter Prefix** | ACC 的唯一命名空间，同时也是主服装 Int 参数。 |
| **Root Menu Name** | VRChat 菜单中根节点的显示名称；留空时与 Parameter Prefix 一致。 |

## 层级与扫描规则

### Outfit Base 的识别条件

ACC 不会再以“找到 Mesh 后取其父对象”的方式猜测服装根。若对象的直接无 Mesh 骨架分支中已有 `MA Merge Armature`，ACC 会优先将其作为明确的服装骨架信号；否则兼容原有规则，一个对象成为 Outfit Base 需要同时满足：

1. 后代包含可渲染网格；
2. 后代包含 `SkinnedMeshRenderer`；
3. 至少一个 `SkinnedMeshRenderer.rootBone` 位于该对象的后代；
4. 从 root bone 向上到 Outfit Base 的顶层骨架分支本身不含 Mesh。

因此，识别由真实骨架结构决定，**不依赖对象名称**。`Armature`、`Bone`、`Skeleton` 等名称无需配置忽略列表；ACC 已没有 Ignore Names 选项。

对于由旧规则识别出的独立骨架，若尚未存在 `MA Merge Armature`，ACC 会在用户确认 **Generate** 后直接调用 Modular Avatar 的 **Setup Outfit**。因此 Armature 目标、锁定模式、骨骼命名修正、Mesh Settings 与未来 MA 版本的修复逻辑均由 MA 自己处理；刷新预览不会改动 Avatar。若 MA 的调用失败或没有实际添加组件，ACC 才会以当前 MA 默认语义（Avatar Armature、`BaseToMerge`、默认名称处理）创建最小兜底，并写入 Warning。

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
| **Language / 语言** | `Auto（自动）`、`English`、`中文`。影响 ACC 编辑器窗口、生成确认对话框和组件 Inspector。 |
| **Costumes Root / 服装根节点** | 必填。选择本次 ACC 的扫描和动画根。 |
| **Root Menu Name / 根菜单名称** | VRChat 菜单中根节点的显示名；为空时默认与参数前缀一致。 |
| **Parameter Prefix / 参数前缀** | 必填且同一 Avatar 内必须唯一。它同时是主 Int 参数、参数前缀、Layer 前缀、Controller 名和输出目录命名空间。为空时自动回退到根对象名称。 |
| **Default Outfit / 默认服装** | 可选。指定初始服装；未指定时按名称关键词匹配，最后回退到第一个服装。 |
| **Enable Parts Control / 启用部件控制** | 启用普通模式的部件开关。 |
| **Enable Custom Mixer / 启用混搭模式** | 仅在已启用 Parts Control 时可选；启用独立的混搭参数和动画层。 |
| **Custom Mixer Name / 混搭菜单名称** | 菜单中混搭入口的显示名；留空时默认显示「混搭」/「Custom Mix」（按语言），参数路径固定使用 `Mixer` 前缀。 |
| **Output Folder / 输出目录** | 自动生成资产的基础目录；必须是安全的 `Assets/...` 相对路径，实际路径追加 `{RootMenuName}/{ParameterPrefix}`。 |

### Parameter Prefix 的规则

假设 `Parameter Prefix = Hair`：

```text
主服装 Int 参数：Hair
普通部件参数：Hair/{OutfitPath}/Parts/{PartPath}
混搭部件槽位参数：Hair/Mixer/{OutfitGroupPath}/{PartSlotPath}
```

混搭参数路径统一使用固定前缀 `Mixer`，不受用户自定义的混搭菜单名称影响。

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
- `Parts Control` Direct BlendTree，根据普通部件参数播放 OFF/ON Clip；
- 每套服装下的 `Parts` 子菜单。

```text
Clothes Root Menu
└── Casual Outfit
    ├── Parts
    │   ├── Hat           → Clothes/Casual_Outfit/Hat
    │   └── Glasses       → Clothes/Casual_Outfit/Glasses
    └── Casual Outfit     → Clothes = 0
```

部件菜单的默认值取自当前 `activeSelf`，`Parts Control` 会直接使用这些 Bool 参数在 OFF/ON Clip 间选择，不再生成独立的 `Parts Init` Layer。

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

### 组件 Inspector

`ACC Outfit Marker`、`ACC Part Group Marker` 和 `ACC Variant Material Override` 的 Inspector 会显示组件用途说明和必要的配置字段。

- **语言跟随 ACC 窗口设置**：三个组件的文字会与 ACC 主窗口的语言选择同步（Auto / English / 中文），不再只依赖系统语言。
- **多选同时编辑**：选中多个同类组件时，Inspector 会提示修改将应用到所有选中对象。
- **原生帮助按钮**：每个组件标题栏右上角的 Unity 原生 `?` 按钮会打开本手册对应的章节。
- **简洁的头部**：组件顶部有用途图标和简短描述（如「显式服装标记」/「Explicit Outfit Marker」），与 Unity 组件名区隔。

## 变体

### 对象变体

对象变体位于 Outfit Base 的同级（可在任意层级），可以是容器内或直接位于 Costumes Root 下：

```text
Costumes Root
├── Jacket Variants
│   ├── Jacket Red           ← Outfit Base；拥有骨架
│   │   ├── Armature
│   │   └── Jacket Mesh
│   └── Jacket Blue          ← 同级对象变体
│       └── Jacket Mesh
├── Hair Base                ← 也可直接位于根下
└── Hair Variant             ← 同级变体
```

识别规则：
- 自动排除自身就是完整服装（拥有骨架）的同级对象；
- 自动排除挂有 `ACCOutfitMarker` 的同级对象；
- 如果同级对象带有 `ACCVariantMaterialOverride`，仅当其所指服装为本体时才作为变体；
- 如果同级对象只有网格但无明确归属，且尚未被其他 Outfit 认领，则作为当前服装的变体。

> 当同层存在多套服装时，建议为变体添加 `ACCVariantMaterialOverride` 组件并正确设置 Outfit Base，以避免归属歧义。

ACC 将变体组的共同父对象作为菜单节点，并生成各变体的选择项。

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

Custom Mixer 是一个特殊的服装状态，用于从多套服装以及它们的变体中组合部件。它**必须启用 Parts Control**。

混搭不会重新扫描并创建一套独立的部件控制。它直接复用普通模式已经生成的部件分组；各变体只为这些普通部件分组提供对应候选对象。

它不是“选择一套变体后控制这套变体的部件”，而是把每个服装组的部件拆成多个**槽位**。混搭菜单不会额外显示普通模式中的 `Parts` / `Groups` 分类层，部件项直接放在变体菜单下；普通模式的分组只用于决定槽位和参数复用。槽位顺序严格沿用普通模式的部件控制顺序，`Parts` 与 `Groups` 不会改变先后关系：

- 同一服装组、同一槽位：只能选择一个变体提供的部件；
- 同一服装组、不同槽位：可以同时选择；
- 不同服装组之间：可以自由组合；
- 变体本身没有额外的启用开关，选择候选部件即代表选择该变体的该槽位。

例如：

```text
Outfit A：A / B / C
Variant A1：A / B / C
Outfit B：D / E / F
Variant B1：D / E / F
```

允许：`Outfit A 的 A + Variant A1 的 B + Outfit B 的 D + Variant B1 的 E`。

不允许：`Outfit A 的 A + Variant A1 的 A`，因为它们属于同一服装组的同一槽位。

### 启用步骤

1. 启用 **Enable Parts Control**。
2. 启用 **Enable Custom Mixer**。
3. 按需修改 `Custom Mixer Name`（留空时默认显示「混搭」/「Custom Mix」）。
4. 刷新预览，确认需要参加混搭的 Base/变体均已勾选。
5. 生成。

### Mixer 的运行逻辑

点击混搭菜单中的 **Enable / 启用** 后，主 `{ParameterPrefix}` 被设为普通服装索引后的特殊值：

1. `Outfit Switching` 进入特殊混搭状态；
2. 默认服装组与默认选择对象保持激活；没有可混搭槽位的默认服装也会保持显示；
3. 每个部件槽位使用一个独立 Int 参数；
4. 默认服装组的槽位会预选默认对象对应候选，并且 On/Off 严格沿用普通 `Parts` 菜单的默认 `activeSelf` 状态；其他服装组的槽位默认是 0；
5. 槽位 Int 的值对应某个变体提供的候选部件，值为 0 表示该槽位关闭；
6. 槽位动画只关闭并打开该槽位的候选部件，不影响同服装组的其他槽位；
7. 退出混搭后，混搭层进入无曲线状态，不覆盖普通服装和普通部件控制。

主服装选择与 Mixer 的 Enable 使用 **Button**：它们表示必须始终存在的互斥主选择，点击已选项不会把主参数回写为 `0`。Mixer 槽位候选使用 **Toggle**：槽位 `0` 是合法的 Off 状态，因此再次点击当前候选会将该槽位关闭；切换到另一个候选则直接写入新的候选值。

启用参数压缩时，主选择和所有 Mixer 槽位仍保持各自独立的本地 Int、同步 Bool 位、状态和 AnyState 条件；这些状态共享一个事件分发 Animator Layer。状态只在进入时对自己所属的选择域执行 Driver，不需要为所有域创建组合状态。

混搭参数路径格式为：

```text
{ParameterPrefix}/Mixer/{OutfitGroupPath}/{PartSlotPath}
```

每个槽位使用一个 Int（8 bits），不再为每个变体部件额外创建 Bool，也不再为每个变体创建独立的整体选择层。Mixer 主选择值紧随最后一个普通服装对象；VRChat Int 可表达范围限制普通服装对象数量最多为 255。

### Mixer 菜单示例

```text
Clothes Root Menu
└── Custom Mix
    ├── Enable                     → Clothes = 255
    ├── Jacket Variants
    │   ├── Jacket Base
    │   │   ├── Collar              → Clothes/Mixer/Jacket_Variants/Parts/Collar = 1
    │   │   └── Upper               → Clothes/Mixer/Jacket_Variants/Groups/Upper = 1
    │   └── Jacket Red
    │       ├── Collar              → Clothes/Mixer/Jacket_Variants/Parts/Collar = 2
    │       └── Upper               → Clothes/Mixer/Jacket_Variants/Groups/Upper = 2
    └── Pants Variants
        ├── Pants Base
        │   └── Belt                → .../Pants_Variants/Parts/Belt = 1
        └── Pants Blue
            └── Belt                → .../Pants_Variants/Parts/Belt = 2
```

## 生成结果与多实例隔离

### Avatar 层级

每次生成会复用或创建菜单根：

```text
{CostumesRoot}/ACC_Menu
```

菜单 GameObject 名称固定为 `ACC_Menu`，其在 VRChat 中的显示标签由 **Root Menu Name** 字段控制。重新生成时会完整重置 `ModularAvatarMenuItem` 的 `Parameter`、`Value`、子参数和菜单源，并直接在 `ACC_Menu` 下重建全部 ACC 子菜单。这样不会复用参数相关控制字段，也不会保留旧控件让 MA 的全层级参数扫描再次收集。

ACC 会保存并恢复旧 ACC 子菜单项的展示属性：除根菜单外，默认 Label 保持为空并由 MA 显示节点 GameObject 名称。服装和部件直接沿用实际对象名；ACC 的默认节点使用当前语言的对象名（中文为“部件 / 混搭 / 启用”，英文为“Parts / Custom Mix / Enable”），而填写 Custom Mixer Name 后会直接使用用户文本。若用户将 Label 改为不同的自定义文本则保留该文本，图标同样保留；语言切换造成默认节点名变化时，ACC 会通过生成语义和控制参数恢复展示属性。该恢复只处理展示属性，不会恢复任何旧控制参数。

ACC 创建的 `ModularAvatarParameters` 直接挂在 `ACC_Menu` 上（MA 不允许在同一对象上挂多个该组件）。ACC 只按精确名称更新或删除自身当前/旧 Controller 声明过的参数，前缀声明和无关手工参数均保持不变。请将需要手工维护的菜单节点放在 `ACC_Menu` 之外。

菜单根包含：

- `ModularAvatarMenuInstaller`（若父级已有则跳过）；
- `ModularAvatarMenuItem`（Children 子菜单，标签为 Root Menu Name）；
- `ModularAvatarMergeAnimator`（FX，Relative Path Root 为 Costumes Root）。
- `ModularAvatarParameters`（ACC 自己的精确参数声明）。

### 输出资产

实际输出路径：

```text
{OutputFolder}/{SanitizedRootMenuName}/{SanitizedParameterPrefix}/
├── {SanitizedParameterPrefix}.controller
└── Animations/
    ├── Outfit_000_*.anim
    ├── Outfit_XXX_Mixer.anim
    ├── PartsInit_OFF.anim
    ├── Parts/*.anim
    └── Mixer/*.anim
```

菜单对象名固定 `ACC_Menu`，输出目录取决于 **Root Menu Name**（而非 Avatar 名称），不同 ACC 实例使用不同名称即可隔离。

Layer 名也带 Parameter Prefix：

```text
Hair/Outfit Switching
Hair/Parts Init
Hair/Parts Control
Hair/Mixer_{OutfitGroup}_{PartSlot}
```

头发、眼睛、衣服等多个 ACC 可以在同一 Avatar 上共存，前提是它们的 Parameter Prefix 不同。ACC 主面板预览区底部会显示实时的 VRChat 参数位占用估算（Int 8bit、Bool 1bit）。

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
2. 服装 Armature 是否已有 `MA Merge Armature`；
3. 服装是否有 `SkinnedMeshRenderer`；
4. `SkinnedMeshRenderer.rootBone` 是否已赋值；
5. rootBone 是否位于预期 Outfit Base 的后代；
6. 顶层骨架分支是否意外包含 Mesh。

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

这是预期行为。ACC 会复用并更新 `ACC_Menu`，同时清理并重建当前 Parameter Prefix 的输出目录。ACC 会保留已有菜单组件的图标等展示属性，但会重建 `ACC_Menu` 下的所有控制项；自定义菜单节点和手工资产应放在 ACC 生成结构之外。

### VRChat 参数占用超过限制

ACC 主面板的预览区底部和生成前摘要会显示估算的参数位占用。Int 参数占用 8 bits，Bool 参数占用 1 bit，VRChat 总上限为 256 bits。如超出限制，可减少部件或分组数量、关闭混搭模式等方式降低占用。

## 开发结构

```text
Assets/UnityBox/AdvancedCostumeController/
├── Editor/
│   ├── Window.cs                         编辑器窗口与预览选择
│   ├── Scanner.cs                        骨架/网格扫描
│   ├── Generator.cs                      菜单、参数、Controller 生成协调
│   ├── AnimationBuilder.cs               Animator Layer 与 Clip 创建
│   ├── Mixer.cs                          Custom Mixer 菜单创建
│   ├── Models.cs                         数据模型与配置
│   ├── Utils.cs                          工具类
│   ├── Localization.cs                   编辑器中英文本地化
│   ├── ACCMarkerEditors.cs               ACC Outfit Marker / Part Group Marker Inspector
│   ├── ACCVariantMaterialOverrideEditor.cs
│   └── ACCInspectorUI                    (在 ACCMarkerEditors.cs 中，组件头部绘制)
└── Runtime/
    ├── ACCOutfitMarker.cs                 无骨架服装的显式识别标记
    ├── ACCPartGroupMarker.cs              持久化部件分组标记
    └── ACCVariantMaterialOverride.cs      材质变体标记组件
```

## 注意事项

- 生成操作支持 Unity Undo；输出目录中资产的删除属于 AssetDatabase 操作，生成前请确认摘要。
- ACC 使用 Write Defaults 策略；与其他 FX 工具混用时，上传前应使用 Modular Avatar 的构建预览检查最终 Animator。
- 部件分组优先使用层级父对象；预览中的文本分组仅为临时会话配置，不会持久化。
- Parameter Prefix 同时是参数和隔离键。修改它等同于创建新的 ACC 命名空间；旧 Prefix 的输出目录不会被新 Prefix 自动清理。
