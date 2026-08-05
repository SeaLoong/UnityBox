# Advanced Costume Controller（ACC）

ACC 用于从指定的**服装根节点（Costumes Root）**扫描服装，并生成 Modular Avatar 菜单和 FX Animator Controller。支持服装变体、部件独立控制、持久部件分组、材质变体和混搭模式（Custom Mixer）。

## 快速开始

1. 打开 **Tools > UnityBox > Advanced Costume Controller**。
2. 选择服装根节点，点击**刷新预览**。
3. 勾选要生成的服装、本体/变体和部件。
4. 按需启用**部件控制**、**混搭**（可选使用独立部件参数）和**参数压缩**。
5. 可选启用**自动生成菜单图标**。
6. 查看预览区域的参数占用与压缩层数，点击**生成**并确认摘要。

**默认服装或变体**可以直接拖入某套服装本体、对象变体或材质变体。生成时会优先使用指定对象作为普通服装选择和 Mixer 的初始版本；留空时仍按名称关键词和扫描顺序自动选择。

ACC 会在服装根节点下复用或创建唯一的 `ACC_Menu`。除根菜单外，生成项默认保持空 Label，由 MA 直接显示其 GameObject 名称。服装与部件沿用实际对象名；ACC 自己生成的默认节点使用当前语言的对象名，例如“部件 / 混搭”和“Parts / Custom Mix”；Mixer 启用项对应显示为“启用混搭 / Enable Custom Mix”，填写 **Custom Mixer Name** 后则使用“启用 + 自定义名称”。重新生成时，ACC 会重建菜单控制逻辑，但会保留已有 ACC 子菜单项的图标和自定义名称；若用户把 Label 改成不同于节点名的自定义文本，则会跨重新生成保留，即使编辑器语言切换导致默认节点名变化。不会复用旧 `Parameter`、`Value`、子参数或菜单来源。ACC 创建的 `ModularAvatarMenuInstaller`、`ModularAvatarMenuItem`、`ModularAvatarMergeAnimator` 和 `ModularAvatarParameters` 都位于 `ACC_Menu`；参数只会按精确名称更新或移除 ACC 自己的声明，不会修改无关的手工 MA 参数。生成的 Controller、AnimationClip 会按场景、Avatar 和参数前缀隔离到输出目录的专属子目录中，重新生成当前命名空间时会清理旧生成资产；关闭**自动生成菜单图标**时会保留该目录下已有的 `MenuIcons` 资产。

## 配置项

### 服装根节点

ACC 优先将服装骨架分支中的 `MA Merge Armature` 视为明确的服装识别信号，并兼容原有的骨架加网格识别。对于原有识别到的独立骨架，如果尚未配置 MA Merge Armature，ACC 会在确认 Generate 后**直接调用 Modular Avatar 的 Setup Outfit**；Armature 目标、锁定模式、骨骼重命名和未来 MA 的逻辑更新均由 MA 自己处理。如果 MA 无法处理当前 Outfit 或未实际创建组件，ACC 才会以当前 MA 默认配置（Avatar Armature、`BaseToMerge`、默认名称处理）作为兜底，并输出警告。若对象没有独立骨架，或需要把一个容器明确声明为服装根，可添加 `ACCOutfitMarker`。

### 参数前缀

参数前缀同时用于：

- 主服装选择参数；
- Mixer 参数路径；
- Animator Layer 命名空间；
- 生成资产和 Controller 文件名。

同一个 Avatar 上的不同 ACC 实例必须使用不同前缀。前缀至少需要包含一个字母或数字。

### 部件控制

启用后，ACC 会为扫描到的部件生成独立 Toggle。部件可以通过 `ACCPartGroupMarker` 按持久分组组合，也可以使用窗口预览中的临时分组。`Exclude` 标记会排除当前对象或其所在自动部件，不生成部件控制。

在预览中填写临时分组、取消部件勾选或选择/取消选择服装对象后，可以点击服装预览末尾的**保存预览分组到服装**。保存会按当前勾选状态持久化：勾选部件写入 `Group` Marker，未勾选部件写入 `Exclude` Marker。确认窗口会列出将新增、更新、移除和设置为不控制的对象；确认后 ACC 使用 Undo 批量修改场景中的 `ACCPartGroupMarker`。预览与现有 Marker 完全一致时按钮保持禁用。

保存为 `Exclude` 后，该部件会进入已排除列表。启用部件控制时，已排除部件仍可重新勾选，预览会显示分组输入框并默认使用对象名；保存后会将原 `Exclude` Marker 更新为 `Group`。关闭部件控制时，已排除部件的勾选框保持禁用。

刷新预览、切换 **Costumes Root** 或保存分组后都会重建预览并清空临时编辑。若存在未保存的服装/变体/部件勾选或临时分组，ACC 会先弹出确认；选择取消不会丢失当前编辑，选择丢弃并继续才会刷新。保存某一套服装时，如果其他服装仍有未保存编辑，也会单独提示确认。

部件预览会为每个分组显示颜色标记：相同分组名使用同色，未分组的自动部件按对象路径分别分配颜色，便于快速检查分组是否一致。
预览中的只读部件会显示禁用的勾选框占位；启用部件控制时，Exclude 部件的勾选框可用于恢复为 Group，关闭部件控制时才会显示禁用占位。
预览中的服装对象和部件按它们在 Avatar Hierarchy 中的实际顺序显示，不会因为分组顺序重新排列。点击服装对象或部件名称会选中对象、Ping 并按对象及其子 Renderer 的实际世界 Bounds 聚焦 Scene 视图，方便从预览反查层级对象。

### 混搭模式

Custom Mixer 要求启用部件控制。启用后可以选择两种部件参数策略；面板中的**使用独立部件参数**默认关闭。

关闭**使用独立部件参数**时，Mixer 会复用普通部件菜单已经生成的 Bool 参数：

- Mixer 仍然是主服装参数的一个特殊值；
- 进入 Mixer 后打开所有已选中的基础服装组；
- 各服装组的部件由原有普通部件 Bool 参数控制，不新增 `Mixer/...` 槽位参数；
- Mixer 菜单中的“共享部件 / Shared Parts”项目与普通部件菜单使用同一参数，并使用 Parts 默认图标；
- 服装组有多个已选对象时，在服装组菜单内与 Shared Parts 同级额外生成一个整体变体选择域，统一选择该组的 Base/Variant；关闭压缩时为同步 Int，开启压缩时按选择域规则编码为同步 Bool 位；只有一个已选对象时不生成变体参数；
- 该模式主要用于减少同步参数，不再为每个部件槽位分别生成变体 Int；部件开关仍使用普通 Bool 参数。

启用**使用独立部件参数**时，使用完整的槽位候选模式：ACC 会为每种部件/分组生成一个选择参数：`0` 表示关闭，`1..N` 表示某个本体或变体提供的部件；这样不同服装组的部件可以自由组合，材质变体候选只会对当前部件应用材质替换。此时不再使用共享模式的服装组整体变体参数。

进入独立参数 Mixer 时，ACC 会先保留**默认服装**：默认服装组的每个槽位会选择默认服装对应的候选，并严格沿用普通 `Parts` 菜单的初始 `activeSelf` 状态；其它服装组的槽位为 Off。切换到其它服装候选后，动画只打开该候选的部件，不会把同一槽位的其它版本部件一起打开。

主服装参数取值连续排列：

- 服装/变体使用 `0` 到 `服装对象数量 - 1`；
- Mixer 使用 `服装对象数量`。

例如有 3 个服装对象时，普通选择值为 `0`、`1`、`2`，Mixer 值为 `3`。没有变体的部件槽位只有 `0/1` 两个值，会直接使用 Bool；有多个版本候选时使用 `0..N` 的 Int，启用参数压缩后再编码为 Bool 位。

共享模式进入 Mixer 后激活所有基础服装组，各组的整体变体 `Simple1D` 先选择 Base/Variant，再由普通部件 Bool 控制部件。独立参数 Mixer 不会默认激活所有服装组。ACC 使用一个合并的 `Direct BlendTree` 管理每个槽位：某组任一槽位为非零时激活该组，槽位的 `Simple1D` 子树只打开当前值对应的候选部件；全部槽位回到 `0`，或退出 Mixer 后，该组会关闭。两种模式的普通部件和 Mixer 部件树现在共用一个 `Parts` State；独立参数模式由主服装参数外层 `Simple1D` 选择 Normal 或 Mixer 子树，共享模式则在 Mixer 分支中叠加各服装组的整体变体树。

### 自动生成菜单图标

启用后，Generate 会为 ACC 生成的服装、变体、部件和 Mixer 入口/服装项生成图标；部件菜单文件夹使用 ACC 预置的空白图标，部件控制项优先拍摄其自身网格：

- 在隐藏的 Unity Preview Scene 中克隆完整 Avatar，并保留原始 `SkinnedMeshRenderer`、材质和骨骼引用；
- 为当前目标 Renderer 临时设置专用渲染 Layer，Camera 只通过 `cullingMask` 渲染该 Layer：服装图标只展示该服装，部件/分组图标只展示该控制项包含的对象，Avatar 身体、其他服装和场景内容不会进入画面；
- 所有服装/变体先合并当前 ACC 实际拍摄目标的 Renderer Bounds，使用同一个联合中心、比例和相机位置；因此 ACC 用于发型、眼睛等局部区域时，会以该区域的整体范围取景，而不是按整个人物中心缩小；部件则按自身 Bounds 独立特写；
- 根据 Avatar 实际朝向自动从正面使用正交相机取景；
- 使用透明背景导出 256×256 PNG；
- 即使拍摄结果完全透明或空白，也会保存 PNG 并作为该菜单项的图标，不会跳过或继承无关图标；
- Mixer 入口会在所有服装组启用的状态下拍摄组合图标；
- 部件菜单文件夹使用 `Resources/OutlineBlank2.png`；开启自动图标后部件控制项拍摄自身控制对象，关闭自动图标时不向部件子项补预置图标；根菜单默认使用 `Resources/OutlineClothing2.png`；
- 材质变体图标会应用全局规则和精准槽位覆盖；
- 父级子菜单没有独立目标时，会继承第一个有图标的后代图标。

图标保存到当前 ACC 输出目录下的 `MenuIcons/`，重新生成时会更新实际拍摄请求对应的 PNG。开启此选项不会清理根菜单、Parts 菜单或其它未参与拍摄节点的已有图标；实际拍摄请求会更新对应菜单项图标。关闭时不会运行图标生成器，Parts 文件夹仍可使用 `OutlineBlank2`，但不会给部件子项补这张预置图标。

ACC 输出目录固定按 `{SceneName}/{AvatarName}/{ParameterPrefix}` 隔离，不再使用 Root Menu Name 作为目录。不同场景的同名 Avatar、同一场景中不同名称的 Avatar 均不会互相覆盖，也不会再出现默认的 `Costumes/Costumes`。

实现参考了 [ParameterIconGenerator](https://github.com/Narazaka/ParameterIconGenerator) 的“原始 Renderer + 专用 Layer + Camera.cullingMask + Camera.Render”方案，以及 Unity `PreviewRenderUtility` 的预览场景做法。参考项目通过 Play Mode 与 Av3Emulator 驱动参数，而 ACC 因为已知每个生成菜单项的目标对象，采用无外部依赖的 Preview Scene 离线渲染；不再调用 `SkinnedMeshRenderer.BakeMesh`，避免导入服装的补偿性 Transform 导致网格坐标被重复解释。

## 参数压缩

**启用参数压缩**默认关闭。两值选择域（包括两套服装的主切换和无变体部件槽位）直接使用同步 Bool；多值域关闭压缩时使用同步 `Int`，每个 `Int` 占用 8 bit，开启压缩后编码为若干同步 Bool 位。

启用后，每个多值 ACC 选择域会生成：

- 一个仅本地使用的 `Int`，供多值菜单输入与同步编码逻辑使用；
- 表示该选择域的最少数量同步 `Bool` 位；
- 由所有有效选择域共用的编码/解码 Animator Layer。

多值表达式菜单仍写入离散 `Int` 值；生成的 Animator Controller 会以同名 `Float`
参数读取这些整数值。这样 `Simple1D` 和 `Direct BlendTree` 的输入始终符合 Unity/VRCFury
对 Float 权重参数的要求，不会被后处理器替换为其他内部 Float 参数。

恰好只有两个取值的选择域直接使用表达式 `Bool`；Animator 内部仍使用数值 `Float` 读取其 `0/1` 值，不额外创建本地 Int 或压缩位。

选择域有 $N$ 个可用值时，同步位数为：

$$
\lceil \log_2(N) \rceil \text{ bit}
$$

启用独立部件参数时，Mixer 有 $N$ 个候选还包含一个“未选择”值，因此按 $N+1$ 个值计算；共享普通部件参数模式不生成 Mixer 槽位选择域，但每个有多个对象的服装组会使用一个整体变体选择域。

共享压缩 Layer 内，每个选择域仍有自己的状态、Driver 与精确 AnyState 条件；本地编码状态和远端解码状态分开，且默认 `Idle` 状态不写入任何参数：

- 本地客户端根据本地 `Int` 编码同步 Bool 位；
- 其他客户端根据同步 Bool 位解码本地 `Int`；
- `IsLocal` Animator 参数用于区分本地菜单输入和远端位同步；`localOnly=false` 的 Driver 是“本地和远端都可执行”，因此解码状态只有在 `IsLocal=false` 的转移条件满足时才会进入。

压缩会新增同步 Bool 位参数、本地 Int 参数，以及一个由所有有效选择域共用的编码/解码 Animator Layer。只有启用独立部件参数时，Mixer 槽位才会作为压缩选择域计入；共享模式的整体变体也会按选择域规则参与压缩。预览区第一行显示 `动画层(N)。主选择(Int/Bool) + 部件(Int/Bool) + 混搭槽位(Int/Bool) + 混搭变体(Int/Bool) = 总 bit`，占用为 0 的分类会省略；全 0 时显示“无同步参数占用”。启用参数压缩后，第二行显示各分类的 `本地 Int → 同步 Bool` 压缩方式，并在存在 local-only 参数时追加 `本地参数(Int + Bool + Float)`；生成确认仍会显示完整的计算明细。

## 参数占用示例

| 选择域 | 未压缩 | 压缩后 |
|---|---:|---:|
| 1 个固定主服装选择（无 Mixer） | 0 bit | 0 bit |
| 8 个主服装选择 | 8 bit | 3 bit |
| 16 个主服装选择 | 8 bit | 4 bit |
| 4 个 Mixer 候选（含未选择共 5 值） | 8 bit | 3 bit |

普通部件 Toggle 本身已经是 Bool，不会再被重复压缩。

只有一个实际可选服装对象且未启用 Mixer 时，主选择没有需要同步的信息。ACC 会保留一个 local-only 参数供菜单和 Animator 内部使用，但无论是否启用参数压缩都不会占用 VRChat 同步 bit。若存在变体或启用 Mixer，主选择域有多个有效值，仍需要同步。

## ACC 组件帮助

### `ACCOutfitMarker`

将当前对象明确声明为服装根，适用于没有独立骨架的服装或需要指定容器根的场景。支持自动部件菜单名称格式化：

- 移除统一前缀；
- 移除统一后缀；
- 使用正则表达式和替换文本修改显示名称。

### `ACCPartGroupMarker`

- `Group`：相同分组名称的对象共用一个部件开关；
- `Exclude`：排除当前对象及其所在自动部件；
- 分组名称为空时使用当前对象名称。

### 组件操作与 Undo/Redo

ACC 的三个组件都使用 Unity Undo：

- `ACCOutfitMarker` 的前缀、后缀和正则格式化字段支持逐次撤销和重做；
- `ACCPartGroupMarker` 的 `Mode` 和 `Group Name` 修改会作为一次独立编辑记录。切换到 `Exclude` 时自动清空的分组名也包含在同一次记录中；Undo 会先恢复字段，组件不会被立即删除；
- `ACCVariantMaterialOverride` 的 `Outfit Base`、全局规则、精准槽位覆盖、刷新和自动分析支持撤销和重做；
- “保存预览分组到服装”、完整预制件转换和生成操作会分别作为一个可撤销的操作，批量编辑多个对象也会合并为一次记录；
- 在 Prefab 实例中修改 ACC 组件、菜单项或参数组件时，ACC 会登记 Prefab 覆盖，保存场景后修改不会丢失；
- 通过 Unity 添加或移除 ACC 组件仍遵循 Unity 自身的组件 Undo 记录。刚添加组件后修改字段时，第一次 Undo 会先撤销字段修改，下一次才撤销组件添加；
- 材质变体首次打开 Inspector 时的自动初始化也会产生独立记录，必要时可以先撤销自动生成的规则，再撤销组件添加；
- 生成目录中 Controller、AnimationClip、PNG 的删除属于 AssetDatabase 文件操作，不纳入场景 Undo，生成前请确认摘要并备份需要保留的手工资产。

### `ACCVariantMaterialOverride`

将组件添加到材质变体对象并指定 `OutfitBase`。首次创建组件且本体引用有效时，Inspector 会自动执行一次材质对照并刷新列表；已有规则不会因为重新打开 Inspector 被覆盖。Inspector 提供两种互补方式：


- **全局替换**：`Source → Replacement` 应用于所有匹配槽位；
- **精准覆盖**：指定 Renderer 槽位，优先于全局规则。

点击**分析当前对象**会按 Renderer 相对路径比较材质：多数映射生成全局规则，少数映射生成精准覆盖。

Inspector 中的**刷新本体材质**只读取 `OutfitBase` 的 Renderer 材质并刷新全局材质列表，即使当前变体已经转换并清空了层级也可以使用；**分析当前对象**才会读取当前变体层级并生成替换规则。

手动分析、拖入来源和菜单转换共用同一套分析逻辑；自动分析也会保留本体的全部 `Source` 条目，未替换项的 `Replacement` 为空。

如果当前变体已被转换并清空层级，可在 Inspector 的**外部材质来源**中拖入另一份完整变体重新分析。结果写入当前组件，来源只读；没有匹配槽位时不会清空已有规则。

如果服装作者提供了两套完整预制件，只需选中要转换的变体并使用 **GameObject > AdvancedCostumeController > 转换成服装变体**。ACC 会从同级对象中自动识别唯一/最佳匹配的 Outfit Base，确认后添加或更新组件并生成最优规则。重复触发会重算同一套配置，不会重复添加组件。转换后的完整变体预制件只作为材质对照来源，运行时不会与本体网格同时激活。

生成时材质曲线会合并到服装/变体切换动画中，不会额外生成材质 Layer。

## 扫描与菜单规则

- ACC 会跳过已被识别为嵌套服装的层级，避免重复控制。
- 同级网格对象可被识别为服装变体；`ACCVariantMaterialOverride` 可显式声明材质变体归属。
- 主服装切换和 Mixer 槽位候选使用 `Simple1D BlendTree` 根据离散值选择动画，避免为每个候选创建独立状态转移；两值域使用 Bool，多值域使用 Float Animator 参数，所有树显式保留手工阈值，不使用 Unity 自动阈值。
- 启用部件控制或存在服装变体时，服装选择会进入一层子菜单；其中的服装控件名称使用本地化的“启用 + 对象名”，普通一级服装菜单仍直接使用对象名。
- 主服装与 Mixer Enable 使用 Toggle 持久写入离散 Int 值；Button 是松开后恢复的瞬时控件，不适合服装状态。独立部件参数模式的 Mixer 槽位候选同样使用 Toggle，使再次点击当前候选可回到合法的 Off 值 0；共享模式的 Mixer 部件项目复用普通 Parts 的 Bool Toggle。
- 普通部件和所有 Mixer 槽位共用一个 `Parts Control` Layer 与 `Parts` State；独立参数模式由外层 `Simple1D` 选择 Normal/Mixer，再用 `DirectBlendTree` 和嵌套 `Simple1D BlendTree` 处理 Off/On 与候选选择，共享模式直接复用普通部件树。
- Mixer 服装组激活 Clip 只负责根对象、变体和非槽位部件；槽位候选部件只由对应槽位子树负责，避免合并后同一属性被重复加权。
- 所有参数压缩选择域共用一个事件分发状态机 Layer；每个域仍有自己的状态、Driver 与 AnyState 条件，因此域间不会共享参数写入。
- 可视控制状态使用 Write Defaults On；纯参数压缩 Layer 使用 Write Defaults Off，避免 Driver 状态重置动画绑定。所有 AnyState Transition 都禁止过渡到当前状态自身；压缩 Encode/Decode 状态执行一次后回到无 Driver 的 Idle，若位仍未同步则从 Idle 重新检查，避免在当前状态内反复执行 Driver。Idle 不需要空白 AnimationClip。
- 菜单节点按服装相对路径创建，名称中的 `/` 会按普通对象名称处理，不会被误解释为路径。
- 重新生成会复用 `ACC_Menu` 根节点的展示属性，例如图标；因此可在首次生成后直接为 `ACC_Menu` 设置图标，后续生成不会丢失。旧子菜单控件会被清理，ACC 控制节点会直接在 `ACC_Menu` 下重建，参数声明位于同一 `ACC_Menu` 的 `ModularAvatarParameters` 中；旧菜单项的 `Parameter`、`Value`、子参数和菜单来源绝不会复用。


## 常见问题

### 为什么压缩后仍有本地 Int？

本地 Int 用于保持菜单和现有 Animator 的离散选择语义，不占同步 bit；真正同步的是 `Bits/*` Bool 参数。

### 为什么压缩会增加 Animator Layer？

所有选择域在一个事件分发状态机中完成本地编码和远端解码。每个 AnyState 转移均带有域专属参数和 bit 条件，状态只承担一次该域的 Driver 写入，不需要表示全部域的组合；预览和生成确认会显示压缩 Layer 数量及 Controller 总 Layer 数。

独立参数 Mixer 的服装组激活、普通部件和所有 Mixer 槽位共用一个 `Parts Control` Layer；其中使用 `DirectBlendTree` 叠加服装组激活 Clip 和槽位子树，槽位候选使用嵌套的 `Simple1D BlendTree`，不再为每个槽位创建独立 Layer。共享普通部件参数模式则在 Mixer 入口激活基础服装组，直接复用普通部件树，不创建槽位参数。

### 什么时候不要启用压缩？

如果优先保证结构简单、便于调试，或项目已有足够同步参数预算，可以保持关闭。压缩主要用于同步参数较多，尤其是 Mixer 槽位较多的 Avatar。

## 安全与生成限制

- 参数前缀必须包含字母或数字，并在同一 Avatar 上保持唯一；
- 输出目录必须是 `Assets` 下的安全相对路径；
- Custom Mixer 必须同时启用部件控制；
- 服装对象数量必须在 VRChat `Int` 可表达范围内；
- 生成前的确认窗口会显示即将覆盖的 Controller、参数预算和压缩结构。
