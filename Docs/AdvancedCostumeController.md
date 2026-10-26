# Advanced Costume Controller 使用手册

Advanced Costume Controller（以下简称 **ACC**）是一个基于 [Modular Avatar](https://modular-avatar.nadena.dev/) 的 VRChat Avatar 编辑器工具。它会扫描指定层级中的服装、部件和变体，并自动生成：

- VRC 表情菜单（通过 `ModularAvatarMenuInstaller` 安装）；
- 参数声明（`ModularAvatarParameters`）；
- FX Animator Controller（通过 `ModularAvatarMergeAnimator` 合并）；
- 服装切换、部件开关、混搭和材质替换所需的 AnimationClip。

ACC 不仅可用于完整服装，也可用于头发、眼睛、饰品等 Avatar 区域；识别优先级为 ACC 自己的 `ACCOutfitMarker`，其次是 `MA Outfit Root`/`armatureRoot` 和独立骨架分支中的 `MA Merge Armature`，最后兼容原有独立骨架识别。也可用 `ACCOutfitMarker` 显式标记任意服装根容器。

## 前置条件

- Unity `2022.3`；其他版本尚未验证；
- VRChat SDK Avatars `3.7.0` 或更高版本；
- Modular Avatar `1.18.0` 或更高版本；
- 已有 `ACCOutfitMarker`、`MA Outfit Root` 或 `MA Merge Armature` 的服装会被优先识别，其中 ACC 自己的标记优先级最高；
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
| **Outfit Base** | 被识别为一套服装本体的对象；它拥有 `MA Outfit Root`、`MA Merge Armature` 骨架分支、可推导的独立骨架，或带有 `ACCOutfitMarker` 显式标记。 |
| **Outfit Object** | 服装组的控制与菜单根；独占的变体/共享骨架容器可作为组根。容器还包含其他独立服装时回退到各自 Base；不会把 Costumes Root 当成服装组根。 |
| **Part / 部件** | 自动部件是本体直接子层级中的网格容器；显式 `ACCPartGroupMarker` 还可声明更深层对象或无网格容器。 |
| **Variant / 变体** | 与 Outfit Base 同级的替换对象，或材质变体标记对象。 |
| **Parameter Prefix** | ACC 的唯一命名空间，同时也是主服装选择参数；两值选择域可使用 Bool，多值域使用 Int。 |
| **Root Menu Name** | VRChat 菜单中根节点的显示名称；留空时与 Parameter Prefix 一致。 |

## 层级与扫描规则

### Outfit Base 的识别条件

扫描优先处理 `ACCOutfitMarker` 及材质变体明确指向的本体，其次是 `MA Outfit Root`。包含这些显式本体的祖先只作为组织容器，扫描器会继续向下查找本体。

没有显式服装声明时，扫描器会尝试从所属的 `MA Merge Armature` 或 `SkinnedMeshRenderer.rootBone` 推导骨架。自动命中要求当前节点拥有直接骨架分支并且后代有网格；最低共同祖先关系用于避免把包含多套服装的上层目录当成一套服装。骨架分支内包含 Mesh 并非一律被排除，识别以实际归属关系为准。

识别不依赖 `Armature`、`Bone` 等对象名称，也没有 Ignore Names 配置。复杂层级无法自动判断时，优先用显式标记表达归属。

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

Marker 是完全显式的声明：即使对象本身及后代没有网格或骨架，ACC 也会将它识别为服装。带 `ACCOutfitMarker` 或 `MA Outfit Root` 的同级对象会保留为独立服装，不会被吞为变体；无网格材质变体使用 `ACCVariantMaterialOverride` 并指向本体。标记命中后，ACC 和自动识别服装一样停止扫描其后代，因此其中嵌套网格会继续按部件规则处理，不会成为另一套服装。

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

没有显式部件标记时，ACC 自动收集 Outfit Base 的**直接子对象**。对象需要：

- 自身或后代包含 `SkinnedMeshRenderer`，或 `MeshRenderer + MeshFilter`；
- 不在任意服装骨架的 `rootBone` 分支内。

菜单、PhysBone 配置、Constraint 容器等无网格对象不会成为自动部件。`ACCPartGroupMarker` 可显式控制无网格或更深层对象；标记子对象会抑制覆盖它的自动父部件，避免重叠控制。

## 窗口配置

| 选项 | 说明 |
|---|---|
| **Language / 语言** | `Auto（自动）`、`English`、`中文`。影响 ACC 编辑器窗口、生成确认对话框和组件 Inspector。 |
| **Costumes Root / 服装根节点** | 必填。选择本次 ACC 的扫描和动画根。 |
| **Root Menu Name / 根菜单名称** | VRChat 菜单中根节点的显示名；为空时默认与参数前缀一致。 |
| **Parameter Prefix / 参数前缀** | 必填且同一 Avatar 内必须唯一。它同时是主服装选择参数、Layer 前缀、Controller 名和输出目录命名空间；两值主选择可使用 Bool。为空时自动回退到根对象名称。 |
| **Default Outfit / 默认服装** | 可选。可指定服装本体、对象变体或材质变体作为初始选择；对象移出 `Costumes Root` 会自动清空；未指定或未启用时使用第一个启用对象所属服装组和对象/变体。 |
| **Enable Parts Control / 启用部件控制** | 启用普通模式的部件开关。 |
| **Enable Custom Mixer / 启用混搭** | 仅在已启用 Parts Control 时可选；启用 Mixer 服装入口。默认复用普通部件参数，不新增 Mixer 槽位参数。 |
| **Use Independent Part Parameters / 使用独立部件参数** | 仅在已启用 Mixer 时显示；开启后为每个 Mixer 槽位生成 `0..N` 候选参数，可选择本体/变体；默认关闭以减少同步参数。 |
| **Custom Mixer Name / 混搭菜单名称** | 菜单中混搭入口的显示名；留空时默认显示「混搭」/「Custom Mix」（按语言），参数路径固定使用 `Mixer` 前缀。 |
| **Auto Generate Menu Icons / 自动生成菜单图标** | 在隐藏 Preview Scene 中为 ACC 菜单项拍摄透明 256×256 PNG；会覆盖本次生成菜单项的图标。 |
| **Output Folder / 输出目录** | 自动生成资产的基础目录；必须是安全的 `Assets/...` 相对路径。实际路径会追加场景、Avatar 和 ACC 命名空间。 |

### Parameter Prefix 的规则

假设 `Parameter Prefix = Hair`：

```text
主服装选择参数：Hair（两值域可为 Bool，多值域为 Int）
普通部件参数：Hair/{OutfitPath}/Parts/{PartPath}
混搭部件槽位参数：Hair/Mixer/{OutfitGroupPath}/{PartSlotPath}（仅在启用独立部件参数时生成）
共享 Mixer 变体参数：Hair/Mixer/{OutfitGroupPath}/Variant（仅在该服装组有多个已选对象时生成）
```

上述路径是逻辑格式。相对路径中的空格、标点等被替换时会追加原路径的稳定 SHA256 后缀，以区分 `A-B`、`A B`、`A_B`；以窗口显示的实际参数为准。主参数前缀按输入保留。混搭参数路径统一使用固定前缀 `Mixer`，不受用户自定义的混搭菜单名称影响。

同一 Avatar 上每套 ACC 必须使用不同前缀。例如：

| 控制目标 | 推荐 Parameter Prefix |
|---|---|
| 发型 | `Hair` |
| 眼睛 | `Eyes` |
| 衣服 | `Clothes` |
| 饰品 | `Accessories` |

Parameter Prefix 必须至少包含一个字母或数字；只包含空格、符号或分隔符的前缀无法产生稳定的 Controller 文件名，ACC 会拒绝生成。

如果最终只有一个实际可选服装对象、没有变体且未启用混搭，主选择值是固定常量，不包含需要网络同步的信息。ACC 仍保留 local-only 参数供菜单与 Animator 内部使用，但无论是否启用参数压缩都占用 `0 bit`。普通部件 Bool 参数仍按实际控制项数量计算；一旦存在变体或混搭，主选择重新成为多值选择域并需要同步。

## 普通服装与部件控制

### 仅服装切换

关闭 Parts Control 时，ACC 只生成服装/变体选择菜单和主选择参数：

```text
Clothes Root Menu
├── Casual Outfit       → Clothes = 0
├── Formal Outfit       → Clothes = 1
└── Sport Outfit        → Clothes = 2
```

### 启用 Parts Control

启用后，ACC 额外生成：

- 每个部件或分组一个同步 Bool 参数；
- `Parts Control` Layer 的单一 `Parts` State，根据普通部件参数播放 OFF/ON Clip；启用独立混搭参数时，State 内部再由主服装参数外层 `Simple1D` 选择 Normal 或混搭部件树；
- 每套服装下的 `Parts` 子菜单。

```text
Clothes Root Menu
└── Casual Outfit
    ├── Parts
    │   ├── Hat           → 对应部件 Bool
    │   └── Glasses       → 对应部件 Bool
    └── Casual Outfit     → Clothes = 0
```

部件菜单的默认值取自当前 `activeSelf`，`Parts Control` 会直接使用这些 Bool 参数在 OFF/ON Clip 间选择，不再生成独立的 `Parts Init` Layer。共享普通部件参数模式的混搭复用同一部件树和这些 Bool 参数。

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

这种分组保存于 Avatar 层级中，是推荐的长期工作流。

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

预览标题会统计可控制与已排除的部件数量。每个可控制卡片仍可勾选是否参与本次生成；参数路径显示在卡片最后一行，便于长路径换行阅读。分组标识带有颜色：相同分组名使用同色，自动部件按对象路径分别分配颜色。

启用部件控制时，Exclude 部件的勾选框可用于恢复为 Group，并显示可编辑的分组名；关闭部件控制时，所有部件勾选框均为禁用状态。只读状态下的占位框不会参与选择或生成。

预览中的服装对象和部件按它们在 Avatar Hierarchy 中的实际顺序显示，不会因为分组顺序重新排列。点击服装对象或部件名称会选中对象、Ping 并按对象及其子 Renderer 的实际世界 Bounds 聚焦 Scene 视图，方便从预览反查层级对象。

### 临时方式：预览中的“分组 / Group”文本框

在预览中为多个已识别部件填写相同分组名，会让它们共用：

- 一个菜单项；
- 一个参数；
- 一个 BlendTree 子项；
- 一张同时设置多个 `m_IsActive` 的动画 Clip。

此分组名只保存于当前 ACC 窗口会话。关闭窗口、切换 Root 或刷新预览后会清空，不适合作为长期配置。

如果确认预览分组正确，可点击当前服装预览末尾的 **保存预览分组到服装 / Save Preview Groups to Outfit**。保存会按当前部件勾选状态持久化：勾选部件写入 `Group` Marker，未勾选部件写入 `Exclude` Marker。ACC 会先弹出变更预览，列出将新增、更新、移除或设置为不控制的对象；确认后通过 Unity Undo 批量修改 `ACCPartGroupMarker` 并刷新预览。预览与持久 Marker 完全一致时按钮不可点击。

保存为 `Exclude` 后，该部件会进入已排除列表。启用部件控制时，已排除部件仍可重新勾选，预览会显示分组输入框并默认使用对象名；保存后会将原 `Exclude` Marker 更新为 `Group`。关闭部件控制时，已排除部件的勾选框保持禁用。

刷新预览、切换 **Costumes Root** 或保存分组后都会重建预览并清空临时编辑。若存在未保存的服装/变体/部件勾选或临时分组，ACC 会先弹出确认；选择取消不会丢失当前编辑，选择丢弃并继续才会刷新。保存某一套服装时，如果其他服装仍有未保存编辑，也会单独提示确认。窗口初始化时的首次扫描不提示，因为此时尚不存在可丢弃的预览草稿。

### 菜单名称格式化

在该 Outfit Base 的 **ACC Outfit Marker** Inspector 中，可以为该套服装的自动部件菜单标签配置：

- **Remove Prefix / 移除前缀**：精确移除统一前缀；
- **Remove Suffix / 移除后缀**：精确移除统一后缀；
- **Regex Pattern / Regex Replacement**：在前后缀处理后执行正则替换。

Inspector 会列出该 Outfit Marker 下所有扫描到的可控制与已排除部件，并显示 `Auto`、`Marker Group`、`Excluded Marker` 决策标签和格式化后的菜单名称。例如，前缀设置为 `Clothes_` 时，`Clothes_Hat` 在预览和菜单中显示为 `Hat`。没有 `ACC Outfit Marker` 的自动识别服装不会应用名称格式化。

不同部件格式化成同一个名称时，菜单节点会追加序号，保留各自控制项；只有显式使用同一分组才共享参数。格式化只影响自动部件的**菜单显示名称**。参数名、Animator 控制条件和动画绑定路径仍使用原始层级，确保重命名显示文本不会破坏已有参数契约。`ACC Part Group Marker` 的显式 Group Name 不会被格式化。

### 组件 Inspector

`ACC Outfit Marker`、`ACC Part Group Marker` 和 `ACC Variant Material Override` 的 Inspector 会显示组件用途说明和必要的配置字段。

- **语言跟随 ACC 窗口设置**：三个组件的文字会与 ACC 主窗口的语言选择同步（Auto / English / 中文），不再只依赖系统语言。
- **多选同时编辑**：选中多个同类组件时，Inspector 会提示修改将应用到所有选中对象。
- **原生帮助按钮**：每个组件标题栏右上角的 Unity 原生 `?` 按钮会打开本手册对应的章节。
- **简洁的头部**：组件顶部有用途图标和简短描述（如「显式服装标记」/「Explicit Outfit Marker」），与 Unity 组件名区隔。

### 组件操作与 Undo/Redo

ACC 组件编辑使用显式的 Unity Undo 分组，确保“字段编辑”和“组件创建”不会被错误合并：

- `ACCOutfitMarker` 的部件名称格式化字段支持 Undo/Redo；
- `ACCPartGroupMarker` 的 `Mode`、`Group Name` 支持 Undo/Redo。切换到 `Exclude` 会同时清空 `Group Name`，这两个字段会作为同一次编辑恢复；第一次 Undo 会恢复字段状态，不会直接删除组件；
- `ACCVariantMaterialOverride` 的 `Outfit Base`、全局材质规则、精准 Renderer 槽位规则支持 Undo/Redo；
- 材质变体的刷新全局材质、自动分析、完整预制件转换，以及预览中的持久分组保存，均作为一个完整操作撤销，避免逐条规则或逐个对象产生难以使用的 Undo 历史；
- 多选编辑会将同一按钮或字段操作应用到所有可编辑目标，并合并为一次 Undo；
- 在 Prefab 实例中修改 ACC 组件、菜单项或参数组件时，ACC 会登记 Prefab 覆盖，保存场景后修改不会丢失；
- 通过 Unity 添加/移除组件仍由 Unity 管理。刚添加 ACC 组件后立即修改字段时，Undo 顺序是先恢复字段，再撤销组件添加；
- 材质变体首次打开 Inspector 的自动初始化是独立的 Undo 操作；生成资产目录的清理属于 AssetDatabase 文件操作，不受场景 Undo 保护。

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
- 未明确声明为本体材质变体的完整服装兄弟会被排除；
- 挂有 `ACCOutfitMarker` 或 `MA Outfit Root` 的同级对象保留为独立服装；
- 包裹另一套显式/自动识别服装的同级组织目录也被排除；
- 如果同级对象带有 `ACCVariantMaterialOverride`，仅当其所指服装为本体时才作为变体；
- 如果同级对象只有网格但无明确归属，且尚未被其他 Outfit 认领，则作为当前服装的变体。

> 当同层存在多套服装时，建议为变体添加 `ACCVariantMaterialOverride` 组件并正确设置 Outfit Base，以避免归属歧义。

有可控制部件时，ACC 使用扫描得到的服装组根作为菜单节点，并在其中生成各变体和 Parts 菜单；没有可控制部件且只有一个已选对象时，才会直接放到当前父菜单，不再额外生成“变体/部件”层。如果存在多个已选 Base/Variant，仍保留服装组菜单层并在其中生成对象选择项，但不创建 Parts 菜单。`MA Outfit Root`、`ACCOutfitMarker` 和已配置的 `MA Merge Armature` 会阻止 ACC 继续向下把嵌套服装误识别为当前服装的变体。

若 `P` 下同时有 `A + A_variant` 和独立服装 `B`，P 只是组织目录，A、B 各自以 Base 为组根；菜单保留 `P/A`、`P/B` 的完整路径，选中服装时会启用所需父目录。`P/B` 和 `Q/B` 不会合并为同一个 Mixer 菜单。

> 选择任意对象变体时，ACC 会保持 Outfit Base 活动，以承载可能位于本体下的共享部件。若所有同级变体都包含完整 Mesh，请先在 Avatar 中验证 Base 与变体同时 active 时不会产生叠穿。

### 默认服装选择

选择规则如下：

1. `Default Outfit` 显式指定的对象；如果指定的是某个变体，该变体会作为普通服装参数的初始值，并作为混搭默认服装组的初始候选；
2. 未指定或指定对象未启用时，使用预览中第一个启用对象所属服装组和对象/变体。

`Default Outfit` 支持直接拖入 Outfit Base、对象变体或 `ACCVariantMaterialOverride` 材质变体。对象必须位于当前 `Costumes Root` 下；移出根节点后输入会自动清空。指定对象未被选中时，ACC 使用第一个启用对象所属服装组和对象/变体作为默认值。

<a id="acc-variant-material-override"></a>

## 材质变体

材质变体适用于“不复制运行时 Mesh，只更换同一 Outfit Base 的材质”的场景。变体对象既可以是空对象，也可以是服装作者提供的完整对照预制件；完整对照预制件运行时不会与本体网格同时激活。

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

### 两种替换方式

Inspector 的两个列表默认展开：

1. **全局材质替换 / Global Material Replacements**
    - 一条 `Source → Replacement` 会替换 Outfit Base 中所有使用该 Source 的 Renderer 槽位；
    - 适合统一换色；Replacement 留空表示不替换。
2. **精准 Renderer 槽位覆盖 / Precise Renderer Slot Overrides**
    - 只覆盖指定 Renderer 的指定槽位；
    - 优先于全局替换；
    - 适合同一 Source 只在部分 Mesh 上需要替换的情况。

点击 **自动分析最优替换 / Analyze Optimal Replacements** 会按本体和当前变体中 Renderer 的相对路径、类型和同节点组件序号进行匹配。对每个 Source 材质，出现次数最多的目标材质生成全局规则，其他少数映射生成精准例外；如果“保持原材质”占多数，则不生成全局规则，只记录少数变化。重复分析会重建相同的确定性配置，不会累积重复项。

首次添加 `ACCVariantMaterialOverride` 且已经有可用的 `OutfitBase` 时，Inspector 会自动执行一次上述材质对照并刷新列表；已有非空规则会被视为用户配置，不会因重新打开 Inspector 被覆盖。

### 配置步骤

#### 空对象材质变体

1. 在 Outfit Base 同级新建空对象并添加 `ACC Variant Material Override`。
2. 设置 **Outfit Base**。
3. 使用**刷新全局材质**配置统一替换，或手工添加精准覆盖。
4. 回到 ACC 窗口刷新预览并生成。

#### 从完整服装预制件转换

1. 将本体与另一套完整服装预制件放在同一个父对象下。
2. 在场景中右键要转换的变体对象。
3. 使用 **GameObject > AdvancedCostumeController > 转换成服装变体 (Convert to Outfit Variant)**。
4. ACC 会按 Outfit Marker、MA Merge Armature/独立骨架和 Renderer 匹配度自动识别同级本体；无法唯一判断时会停止，不会猜测修改。
5. 在确认窗口核对自动识别的本体和变体来源。
6. ACC 先只读检查是否有匹配材质槽位；没有匹配时停止并保留来源对象。成功转换会添加/更新材质标记，并清除来源的组件和子对象，留下材质规则；需要保留完整来源时先复制一份。可在 Inspector 中继续调整规则。

转换、自动分析、刷新和列表编辑支持场景 Undo/Redo。已转换对象不再保有完整来源层级，再次分析请使用 Inspector 的外部材质来源；重复点击转换不能恢复已删除的来源。

材质曲线会写入普通 `Outfit Switching` Clip，不会生成独立材质 Layer。每次切换都会先恢复原材质，再应用当前材质变体，避免前一个变体残留。

## Custom Mixer 混搭

Custom Mixer（混搭）是主服装选择中的一个特殊服装状态。它**必须启用 Parts Control**，并提供两种部件参数策略。

默认关闭 **Use Independent Part Parameters / 使用独立部件参数** 时，混搭复用普通部件菜单已经生成的 Bool 参数：进入混搭后激活所有已选基础服装组，由各服装组的普通部件参数控制部件；这种模式不新增混搭槽位参数，适合优先减少同步参数的 Avatar。共享部件菜单带有 Parts 默认图标；服装组存在多个已选对象时，会在服装组菜单内与该菜单同级新增一个整体变体选择域用于选择 Base/Variant，两值时直接为同步 Bool，多值关闭压缩时为同步 Int，开启压缩时编码为同步 Bool 位；只有一个对象时不生成该参数。它不再为每个部件槽位分别生成变体参数。

开启 **Use Independent Part Parameters / 使用独立部件参数** 时，每种部件/分组使用一个选择参数：`0` 表示关闭，`1..N` 表示某个本体或变体提供的部件。不同服装组之间可以自由组合；材质变体作为候选时，只对当前部件槽位的 Renderer 应用材质替换，不会因为一个部件开关重染整套服装。

独立参数模式的混搭菜单按“服装组 → 版本 → 部件/分组候选”组织。普通模式的部件控制顺序和当前预览分组会被复用；本体候选以本次勾选为准，取消的本体/变体不再回到候选中，没有可选候选的槽位不生成：

- 同一服装组、同一部件/分组槽位只能选择一个候选；
- 同一服装组、不同部件/分组槽位可以同时选择；
- 不同服装组之间：可以自由组合；
- 没有变体的槽位只有 `0/1` 两个值，直接使用 Bool；出现多个版本候选时使用 `0..N` 的 Int。

例如：

```text
Outfit A：Base + Variant A1，部件 A / B / C
Outfit B：Base + Variant B1，部件 D / E / F
```

允许：`选择 Outfit A 的 Variant A1 的 A 和 B，再选择 Outfit B 的 Base 的 D`。

不允许：同时打开 `Outfit A` 的 A 槽位 Base 候选和 Variant A1 候选，因为它们属于同一部件槽位。

共享普通部件参数模式的混搭菜单按“服装组 → 变体项目 + 共享部件 / Shared Parts 菜单”组织；变体项目与 Shared Parts 同级，Shared Parts 菜单复用对应服装组普通 Parts 菜单的 Bool 参数，整体变体项目共用该服装组的一个选择域。

### 启用步骤

1. 启用 **Enable Parts Control**。
2. 启用 **Enable Custom Mixer / 启用混搭**。
3. 如需本体/变体候选槽位选择，再启用 **Use Independent Part Parameters**；默认关闭以节省同步参数。
4. 按需修改 `Custom Mixer Name / 混搭菜单名称`（留空时默认显示「混搭」/「Custom Mix」）。
5. 刷新预览，确认需要参加混搭的 Base/变体均已勾选。
6. 生成。

### 混搭运行逻辑

点击混搭菜单中的 **启用混搭 / Enable Custom Mix** 后（自定义混搭名称时为“启用 + 自定义名称”），主 `{ParameterPrefix}` 被设为普通服装索引后的特殊值：

1. 主服装参数进入普通服装索引后的混搭特殊值；
2. 共享参数模式激活所有基础服装组，各组整体变体参数先选择 Base/Variant，普通部件 Bool 参数再控制各部件；只有一个已选对象的服装组不生成整体变体参数；
3. 独立参数模式保留默认服装组及默认选择对象，默认服装槽位沿用普通 `Parts` 的初始 `activeSelf` 状态，其他服装组槽位默认为 `0`；
4. 独立槽位动画会先关闭该槽位的所有候选，再只打开当前值对应的候选；材质变体候选会先恢复当前部件的本体材质，再应用当前替换；
5. 独立模式下所有槽位为 `0` 时服装组关闭，至少一个槽位非零时激活该组；
6. 两种模式的普通部件和混搭部件树共用一个 `Parts` State；独立模式由主服装参数外层 `Simple1D` 选择 Normal/Mixer，共享模式直接复用普通部件树。

主服装选择与混搭启用项使用 **Toggle**，以便松开菜单后仍保持选择；VRChat 的 **Button** 是瞬时控件，不适合服装状态。独立参数模式的混搭候选也使用 **Toggle**，写入对应槽位值；共享模式的混搭部件项目复用普通 Parts 的 Bool Toggle。

启用部件控制时，服装选择项位于服装子菜单内，并直接使用服装对象名；关闭部件控制且不需要变体子菜单时，服装项同样直接使用对象名。

启用参数压缩时，多值主选择、共享整体变体和独立混搭槽位保持各自独立的本地 Int、同步 Bool 位、状态和 AnyState 条件；共享普通部件参数模式不生成混搭槽位压缩域。两值域不会再额外创建压缩 Int。所有压缩状态共享一个事件分发 Animator Layer；默认 `Idle` 状态不写入参数，避免全局 Driver 在本地初始化时覆盖保存值。

混搭参数路径格式为：

```text
{ParameterPrefix}/Mixer/{OutfitGroupPath}/{PartSlotPath}
```

上述 Mixer 槽位路径仅在启用独立部件参数时生成；共享模式直接使用普通部件路径：

```text
{ParameterPrefix}/{OutfitPath}/Parts/{PartPath}
{ParameterPrefix}/{OutfitPath}/Groups/{GroupName}
```

独立部件参数模式下，每个部件/分组槽位使用一个参数；两值槽位为 Bool，多值槽位未压缩时为同步 Int（8 bits），启用压缩后为对应数量的同步 Bool 位。共享模式下，每个有多个已选对象的服装组使用一个整体变体选择域，两值时为同步 Bool，多值未压缩时为同步 Int，启用压缩后按选择域编码，部件仍复用普通 Bool 参数。Mixer 主选择值紧随最后一个普通服装对象。启用 Mixer 时最多 255 个普通选择对象；未启用 Mixer 时最多 256 个（索引 0–255）。

### 混搭菜单示例

下面假设普通选择共有 `JacketBase`、`JacketRed`、`PantsBase` 三个对象，Mixer 入口值为 **3**，不是固定的 255。名称使用安全字符，示例只展示一个部件槽位。

独立部件参数模式：

```text
Clothes Root Menu
└── Custom Mix
    ├── Enable Custom Mix          → Clothes = 3
    ├── Jacket
    │   ├── JacketBase
    │   │   └── Collar             → Clothes/Mixer/Jacket/Parts/Collar = 1
    │   └── JacketRed
    │       └── Collar             → Clothes/Mixer/Jacket/Parts/Collar = 2
    └── PantsBase
        └── PantsBase
            └── Belt               → 对应槽位值 = 1（0 为 Off）
```

共享普通部件参数模式：

```text
Clothes Root Menu
└── Custom Mix
    ├── Enable Custom Mix          → Clothes = 3
    ├── Jacket
    │   ├── JacketBase             → 对应整体变体值 = 0
    │   ├── JacketRed              → 对应整体变体值 = 1
    │   └── Shared Parts
    │       └── Collar             → 普通 Collar Bool
    └── PantsBase
        └── Shared Parts
            └── Belt               → 普通 Belt Bool
```

整体变体域恰好两个值时使用同步 Bool；多于两个值时才使用 Int 或压缩位。

## 生成结果与多实例隔离

### 自动菜单图标

启用 **自动生成菜单图标** 后，ACC 在菜单和 Controller 生成完成后执行离线拍摄：

1. 将完整 Avatar 克隆到隐藏 Preview Scene，以取得 SkinnedMeshRenderer 的正确骨骼姿势；
2. 保留当前菜单目标的原始 `SkinnedMeshRenderer`、材质和骨骼引用，不对蒙皮网格调用 `BakeMesh`；
3. 为目标 Renderer 临时设置专用 Layer，并让相机只通过 `cullingMask` 渲染该 Layer：服装/变体只显示自身，部件/分组只显示其控制对象，其他 Avatar 与场景内容不参与渲染；
4. 服装和变体先合并当前 ACC 实际拍摄目标的 Renderer Bounds，使用同一个联合中心、比例和相机位置，保证不同服装在图标中的比例一致；因此 ACC 用于发型、眼睛等局部区域时，会以该区域的整体范围取景，而不是按整个人物中心缩小；Mixer 入口使用所有服装组启用时的组合目标；Parts 文件夹使用 ACC 预置空白图标，部件控制项按自身控制对象独立拍摄；关闭自动图标时不会给部件子项补预置图标；
5. 根据 Avatar 实际朝向使用正交相机从正面取景，并使用方向光补光；
6. 通过 RenderTexture 和 `ReadPixels` 导出透明 256×256 PNG；
7. 将 PNG 导入为无压缩、无 Mipmap 的 Texture2D，并写入 MA Menu Item 的 Icon；
8. 即使 RenderTexture 中没有可见像素，也会保存透明 PNG 并赋给当前拍摄项；未单独拍摄且没有已有图标的父级子菜单才会继承第一个有图标的后代图标，部件子项不会继承 Parts 文件夹图标。

输出路径：

```text
{ResolvedGeneratedFolder}/MenuIcons/*.png
```

图标拍摄不会进入 Play Mode，不修改原网格/骨骼姿态，也不依赖 Av3Emulator；完整 Generate 仍会修改场景中的菜单、参数和 MA 组件。材质变体会在克隆 Avatar 的原始 Renderer 上应用全局替换与精准 Renderer 槽位覆盖。拍摄器只更新实际请求的菜单项；但开启自动图标时，完整 Generate 会重建整个专属输出目录，其中的旧图标也会删除。位于该目录外的手工图标可通过展示属性恢复；不要把需长期保留的图标放在生成目录。根菜单缺省使用 `Resources/OutlineClothing2.png`，Parts 文件夹使用 `Resources/OutlineBlank2.png`；开启自动图标时部件子项拍摄自身网格，关闭时不会给部件子项补这张预置图。

设计参考：[Narazaka/ParameterIconGenerator](https://github.com/Narazaka/ParameterIconGenerator)（Zlib License）。参考项目在 Play Mode 中通过 Av3Emulator 切换参数；ACC 仅复用通用的 Camera、RenderTexture 与 PNG 输出思路，并根据自身生成数据采用独立的 Preview Scene 实现。

### Avatar 层级

每次生成会复用或创建菜单根：

```text
{CostumesRoot}/ACC_Menu
```

菜单 GameObject 名称固定为 `ACC_Menu`，其在 VRChat 中的显示标签由 **Root Menu Name** 字段控制。重新生成时会完整重置 `ModularAvatarMenuItem` 的 `Parameter`、`Value`、子参数和菜单源，并直接在 `ACC_Menu` 下重建全部 ACC 子菜单。这样不会复用参数相关控制字段，也不会保留旧控件让 MA 的全层级参数扫描再次收集。

ACC 会保存并恢复旧 ACC 子菜单项的展示属性：除根菜单外，默认 Label 保持为空并由 MA 显示节点 GameObject 名称。服装和部件直接沿用实际对象名；ACC 的默认节点使用当前语言的对象名（中文为“部件 / 混搭 / 启用”，英文为“Parts / Custom Mix / Enable”），而填写 Custom Mixer Name 后会直接使用用户文本。若用户将 Label 改为不同的自定义文本则保留该文本，图标同样保留；语言切换造成默认节点名变化时，ACC 会通过生成语义和控制参数恢复展示属性。该恢复只处理展示属性，不会恢复任何旧控制参数。

普通服装的嵌套父级菜单、Mixer 路径和 Parts 菜单都使用稳定语义键；跨语言或重新生成时，ACC 会优先恢复对应节点的图标和自定义 Label。

ACC 创建的 `ModularAvatarParameters` 直接挂在 `ACC_Menu` 上（MA 不允许在同一对象上挂多个该组件）。ACC 只按精确名称更新或删除自身当前/旧 Controller 声明过的参数，前缀声明和无关手工参数均保持不变。请将需要手工维护的菜单节点放在 `ACC_Menu` 之外。

菜单根包含：

- `ModularAvatarMenuInstaller`（若父级已有则跳过）；
- `ModularAvatarMenuItem`（Children 子菜单，标签为 Root Menu Name）；
- `ModularAvatarMergeAnimator`（FX，Relative Path Root 为 Costumes Root）。
- `ModularAvatarParameters`（ACC 自己的精确参数声明）。

### 输出资产

实际输出路径：

```text
{OutputFolder}/{StableSceneName}/{StableAvatarName}/{StableParameterPrefix}/
├── {StableParameterPrefix}.controller
├── Animations/
    ├── Outfit_000_*.anim
    ├── Outfit_XXX_CustomMixer.anim
    ├── Parts/*.anim
    └── Mixer/*.anim
└── MenuIcons/*.png
```

Scene、Avatar、Parameter Prefix 各段经稳定文件名转换：由字母、数字、下划线组成的短名称保持原样，其余名称清理并追加稳定哈希，过长可读前缀截断。Root Menu Name 不参与目录。隔离依据是这些名称，并非对象实例或场景完整路径：同名场景、同名 Avatar 加同一 Prefix 仍可能共用输出，需使用不同前缀或 Output Folder。

Layer 名也带 Parameter Prefix：

```text
Hair/Outfit Switching
Hair/Parts Control（单一 Parts State；独立 Mixer 参数时包含 Normal/Mixer 外层 Simple1D）
Hair/Parameter Compression（启用压缩时）
```

头发、眼睛、衣服等多个 ACC 可以在同一 Avatar 上共存，前提是它们的 Parameter Prefix 不同。ACC 主面板预览区底部第一行显示 `动画层(N)。主选择(Int/Bool) + 部件(Int/Bool) + 混搭槽位(Int/Bool) + 混搭变体(Int/Bool) = 总 bit`，共享普通部件参数模式不会显示 Mixer 槽位占用；占用为 0 的分类会省略。全 0 时显示“无同步参数占用”。启用参数压缩后，第二行显示各分类的 `本地 Int → 同步 Bool` 压缩方式，并在存在 local-only 参数时追加 `本地参数(Int + Bool + Float)`；生成确认窗口会继续显示完整参数明细。

## 重新生成与清理规则

若当前 ACC Controller 已存在，生成摘要会提示继续生成将覆盖。确认后，会清理并重建当前 Scene / Avatar / ACC 专属目录：

```text
{OutputFolder}/{StableSceneName}/{StableAvatarName}/{StableParameterPrefix}/
```

因此：

- 重新生成会替换当前命名空间的输出，保留前一版本的需求应先备份；
- 同名旧 Clip 不会残留；
- 不会影响其他 Parameter Prefix 对应的 ACC 输出；
- 开启自动图标时会重建 `MenuIcons`；关闭自动图标时会保留已有 `MenuIcons`，因此仅切换生成开关不会丢失菜单图标。
- **不要**将需要保留的手工资产放进 ACC 专属输出目录。
- Output Folder 必须位于 `Assets` 下，且不能包含 `.` 或 `..` 路径段；不满足时 ACC 会拒绝生成。
- 菜单按精确对象名称处理 `/`，但 Unity 动画绑定仍以 `/` 分隔层级。请避免需动画控制的对象名包含 `/`，也避免同父下重复名称；菜单能区分不等于动画能正确绑定。

## 故障排查

### 刷新预览后没有服装

检查：

1. Costumes Root 是否选对；
2. 服装 Armature 是否已有 `MA Merge Armature`；
3. 服装是否有 `SkinnedMeshRenderer`；
4. `SkinnedMeshRenderer.rootBone` 是否已赋值；
5. rootBone 是否位于预期 Outfit Base 的后代；
6. 骨架和网格的归属是否让当前节点被识别为上层组织容器。

如果服装没有独立骨架、只有网格，或希望手动指定某个容器为服装根，请在其 Outfit Base 上添加 `ACCOutfitMarker` 后重新刷新预览。

### 部件没有出现在 Parts 菜单

检查对象是否：

1. 自动部件是否为 Outfit Base 的直接子对象并有有效网格；
2. 深层或无网格对象是否添加 `ACCPartGroupMarker`；
3. 是否位于骨架分支、被 Exclude 或更深层 Marker 覆盖；
4. 是否启用 Parts Control，并仍在预览中被勾选。

### Custom Mixer 开关不可选

必须先启用 **Enable Parts Control**。这是 Mixer 的前置条件。

### 多个 ACC 互相影响

确认每个 ACC 使用不同的 `Parameter Prefix`。特别注意手动改名后不要留空或重复。

### 材质变体不生效

检查：

1. `ACCVariantMaterialOverride` 是否挂在 Outfit Base 的同级对象；
2. 组件的 Outfit Base 引用是否正确；
3. 是否已设置 Outfit Base；新建组件会自动初始化材质列表，已有组件也可以手动点击“刷新全局材质”或“自动分析最优替换”；
4. 是否填写了 Replace With；
5. 修改标记后是否重新 Refresh Preview 并 Generate。

### 生成后手工修改的菜单或参数消失

这是预期行为。ACC 会复用并更新 `ACC_Menu`，同时清理并重建当前 Parameter Prefix 的输出目录。ACC 会保留已有菜单组件的图标等展示属性，但会重建 `ACC_Menu` 下的所有控制项；自定义菜单节点和手工资产应放在 ACC 生成结构之外。

### VRChat 参数占用超过限制

ACC 主面板的预览区底部和生成前摘要会显示估算的参数位占用及计算式。Int 参数占用 8 bits，Bool 参数占用 1 bit，VRChat 总上限为 256 bits。如超出限制，可减少部件或分组数量，或关闭 **Use Independent Part Parameters** 改用共享普通部件参数模式来降低占用。

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
│   ├── ACCMarkerEditors.cs               ACC 标记 Inspector 与统一 Undo 边界
│   ├── ACCVariantMaterialOverrideEditor.cs
│   └── MenuIconGenerator.cs              菜单图标 Preview Scene 渲染
└── Runtime/
    ├── ACCOutfitMarker.cs                 无骨架服装的显式识别标记
    ├── ACCPartGroupMarker.cs              持久化部件分组标记
    └── ACCVariantMaterialOverride.cs      材质变体标记组件
```

## 注意事项

- 生成操作支持 Unity Undo；输出目录中资产的删除属于 AssetDatabase 操作，生成前请确认摘要。
- ACC 使用 Write Defaults 策略；与其他 FX 工具混用时，上传前应使用 Modular Avatar 的构建预览检查最终 Animator。
- 部件分组优先使用层级父对象；预览中的文本分组默认是临时会话配置，也可以通过“保存预览分组到服装”持久化为 Marker。
- Parameter Prefix 同时是参数和隔离键。修改它等同于创建新的 ACC 命名空间；旧 Prefix 的输出目录不会被新 Prefix 自动清理。


## 审查修正与重新生成

- 最低 Modular Avatar 版本为 **1.18.0**，这是 `ModularAvatarOutfitRoot` 的首个稳定版本；菜单使用的 `PortableControl` 也不支持原先声明的 1.10.0。
- 独立混搭的本体候选以当前预览中的部件勾选与临时分组为准，避免重新使用扫描时的旧分组或恢复已取消勾选的组成员。
- 转换材质变体前先检查匹配槽位；没有匹配时保留来源组件和子对象，避免生成空规则后仍删除来源内容。
- 不同部件经显示名格式化后同名时，菜单节点追加序号，仍保留各自独立参数。
- 含空格、标点、路径分隔符的生成资产名称使用稳定 SHA256 后缀区分，避免 `A-B`、`A B` 与 `A_B` 覆盖同一资产。参数路径仅在清理改变原文时加后缀；原本安全的路径继续沿用旧名称。
- 更新后请重新 Generate。含被清理字符的旧参数名或生成目录可能改变，VRChat 已保存的相关菜单选择可能重置；旧输出目录不会自动删除，请核对引用后自行清理。

## 回归验证

包内 `Tests/Editor` 包含 `OutfitContainerRegressionTests` 与 `PartSelectionRegressionTests`，共 19 个 EditMode 用例，检查共享组根、保留真实菜单路径、祖先激活、分组/候选选择、名称碰撞和失败转换保留来源。

在安装依赖和 Unity Test Framework 的 Unity 2022.3 项目中运行 Test Runner → EditMode。若通过 VPM 安装，将 `top.sealoong.unitybox.advanced-costume-controller` 加入项目 manifest 的 `testables`。这些测试不覆盖全部渲染、Undo、VRChat 网络同步或第三方构建组合；仍需用测试 Avatar 验证两种 Mixer、压缩开关、材质变体和重复生成。

当前云端只完成源码/语法与打包检查，尚未执行 Unity 编译、这些 EditMode 用例、图标渲染或游戏内同步测试。
