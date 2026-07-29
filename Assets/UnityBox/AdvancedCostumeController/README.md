# Advanced Costume Controller（ACC）

ACC 用于从指定的**服装根节点（Costumes Root）**扫描服装，并生成 Modular Avatar 菜单和 FX Animator Controller。支持服装变体、部件独立控制、持久部件分组、材质变体和混搭模式（Custom Mixer）。

## 快速开始

1. 打开 **Tools > UnityBox > Advanced Costume Controller**。
2. 选择服装根节点，点击**刷新预览**。
3. 勾选要生成的服装、本体/变体和部件。
4. 按需启用**部件控制**、**混搭模式**和**参数压缩**。
5. 查看预览区域的参数占用与压缩层数，点击**生成**并确认摘要。

ACC 会在服装根节点下复用或创建唯一的 `ACC_Menu`。除根菜单外，生成项默认保持空 Label，由 MA 直接显示其 GameObject 名称。服装与部件沿用实际对象名；当服装选择需要进入子菜单时，服装启用项会使用当前语言的“启用 + 对象名”（中文如“启用Orignial”，英文如“Enable Orignial”）。ACC 自己生成的默认节点使用当前语言的对象名，例如“部件 / 混搭”和“Parts / Custom Mix”；Mixer 启用项对应显示为“启用混搭 / Enable Custom Mix”，填写 **Custom Mixer Name** 后则使用“启用 + 自定义名称”。重新生成时，ACC 会重建菜单控制逻辑，但会保留已有 ACC 子菜单项的图标和自定义名称；若用户把 Label 改成不同于节点名的自定义文本，则会跨重新生成保留，即使编辑器语言切换导致默认节点名变化。不会复用旧 `Parameter`、`Value`、子参数或菜单来源。ACC 创建的 `ModularAvatarMenuInstaller`、`ModularAvatarMenuItem`、`ModularAvatarMergeAnimator` 和 `ModularAvatarParameters` 都位于 `ACC_Menu`；参数只会按精确名称更新或移除 ACC 自己的声明，不会修改无关的手工 MA 参数。生成的 Controller、AnimationClip 会按根菜单名称和参数前缀隔离到输出目录的专属子目录中，重新生成当前命名空间时会清理旧生成资产。

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

### 混搭模式

Custom Mixer 要求启用部件控制。启用后，ACC 会准备所有服装的部件槽位候选菜单，使不同服装的部件可以自由组合；实际未使用的服装组不会保持激活。

进入 Mixer 时，ACC 会先保留**默认服装**：默认服装组会显示，其每个 Mixer 槽位会选择默认服装对应候选，且 On/Off 状态严格沿用普通 `Parts` 菜单的初始 `activeSelf` 状态。普通 Parts 默认关闭的部件在 Mixer 中也保持未选择；其他服装组的槽位初始均为未选择。没有可混搭槽位的默认服装也会保持显示，不会因进入 Mixer 而消失。

主服装参数取值连续排列：

- 服装/变体使用 `0` 到 `服装对象数量 - 1`；
- Mixer 使用 `服装对象数量`。

例如有 3 个服装对象时，普通选择值为 `0`、`1`、`2`，Mixer 值为 `3`。每个 Mixer 槽位保留 `0` 表示未选择，候选从 `1` 开始。

进入 Mixer 时不会默认激活所有服装组。ACC 使用一个合并的 `Direct BlendTree` 激活层管理所有服装组：只有某组至少一个部件槽位选择了候选部件时，该组服装根对象及其候选变体才会激活；该组所有槽位恢复为未选择，或退出 Mixer 后，该组会关闭。这样未参与混搭的服装及其骨架不会持续处于激活状态。被选中的服装组仍会保留其所需骨架层级，避免 `SkinnedMeshRenderer` 绑定失效。

## 参数压缩

**启用参数压缩**默认关闭。关闭时，主选择和每个 Mixer 槽位使用同步 `Int`，每个 `Int` 占用 8 bit。

启用后，每个 ACC 选择域会生成：

- 一个仅本地使用的 `Int`，供菜单输入与同步编码逻辑使用；
- 表示该选择域的最少数量同步 `Bool` 位；
- 由所有有效选择域共用的编码/解码 Animator Layer。

表达式菜单仍写入离散 `Int` 值；生成的 Animator Controller 会以同名 `Float`
参数读取这些整数值。这样 `Simple1D` 和 `Direct BlendTree` 的输入始终符合 Unity/VRCFury
对 Float 权重参数的要求，不会被后处理器替换为其他内部 Float 参数。

选择域有 $N$ 个可用值时，同步位数为：

$$
\lceil \log_2(N) \rceil \text{ bit}
$$

Mixer 有 $N$ 个候选时，还包含一个“未选择”值，因此按 $N+1$ 个值计算。

共享压缩 Layer 内，每个选择域仍有自己的状态、Driver 与精确 AnyState 条件；本地编码状态和远端解码状态分开，且默认 `Idle` 状态不写入任何参数：

- 本地客户端根据本地 `Int` 编码同步 Bool 位；
- 其他客户端根据同步 Bool 位解码本地 `Int`；
- `IsLocal` Animator 参数用于区分本地菜单输入和远端位同步；`localOnly=false` 的 Driver 是“本地和远端都可执行”，因此解码状态只有在 `IsLocal=false` 的转移条件满足时才会进入。

压缩会新增同步 Bool 位参数、本地 Int 参数，以及一个由所有有效选择域共用的编码/解码 Animator Layer。预览与生成确认会显示实际 bit 占用、有效选择域数量、压缩 Layer 数量和 Controller 总 Layer 数；参数类型已知，因此只显示最终 bit 总数，不显示冗余的乘法表达式。

## 参数占用示例

| 选择域 | 未压缩 | 压缩后 |
|---|---:|---:|
| 8 个主服装选择 | 8 bit | 3 bit |
| 16 个主服装选择 | 8 bit | 4 bit |
| 4 个 Mixer 候选（含未选择共 5 值） | 8 bit | 3 bit |

普通部件 Toggle 本身已经是 Bool，不会再被重复压缩。

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

### `ACCVariantMaterialOverride`

将组件添加到材质变体对象，指定其对应的服装本体 `OutfitBase`，然后为本体材质填写替换材质。替换项为空时保持原材质。生成时材质曲线会合并到服装/变体切换动画中，不会额外生成材质 Layer。

## 扫描与菜单规则

- ACC 会跳过已被识别为嵌套服装的层级，避免重复控制。
- 同级网格对象可被识别为服装变体；`ACCVariantMaterialOverride` 可显式声明材质变体归属。
- 主服装切换和 Mixer 槽位候选使用 `Simple1D BlendTree` 根据连续的离散选择值选择动画，避免为每个候选创建独立状态转移；所有树显式保留手工阈值，不使用 Unity 自动阈值。Animator 内部以 Float 参数读取这些值，菜单侧仍使用 Int。
- 启用部件控制或存在服装变体时，服装选择会进入一层子菜单；其中的服装控件名称使用本地化的“启用 + 对象名”，普通一级服装菜单仍直接使用对象名。
- 主服装与 Mixer Enable 使用 Toggle 持久写入离散 Int 值；Button 是松开后恢复的瞬时控件，不适合服装状态。Mixer 槽位候选同样使用 Toggle，使再次点击当前候选可回到合法的 Off 值 0；普通 Parts 保持 Bool Toggle。
- 普通部件和所有 Mixer 槽位共用一个 `Parts Control` Layer；内部使用 `DirectBlendTree` 和嵌套 `Simple1D BlendTree` 处理 Off/On 与候选选择。
- Mixer 服装组激活 Clip 只负责根对象、变体和非槽位部件；槽位候选部件只由对应槽位子树负责，避免合并后同一属性被重复加权。
- 所有参数压缩选择域共用一个事件分发状态机 Layer；每个域仍有自己的状态、Driver 与 AnyState 条件，因此域间不会共享参数写入。
- 可视控制状态使用 Write Defaults On；纯参数压缩 Layer 使用 Write Defaults Off，避免 Driver 状态重置动画绑定。普通 AnyState Transition 禁止过渡到当前状态自身；压缩同步 Transition 允许自身重入，以便修正丢失或延迟到达的 Bool 位。
- 菜单节点按服装相对路径创建，名称中的 `/` 会按普通对象名称处理，不会被误解释为路径。
- 重新生成会复用 `ACC_Menu` 根节点的展示属性，例如图标；因此可在首次生成后直接为 `ACC_Menu` 设置图标，后续生成不会丢失。旧子菜单控件会被清理，ACC 控制节点会直接在 `ACC_Menu` 下重建，参数声明位于同一 `ACC_Menu` 的 `ModularAvatarParameters` 中；旧菜单项的 `Parameter`、`Value`、子参数和菜单来源绝不会复用。


## 常见问题

### 为什么压缩后仍有本地 Int？

本地 Int 用于保持菜单和现有 Animator 的离散选择语义，不占同步 bit；真正同步的是 `Bits/*` Bool 参数。

### 为什么压缩会增加 Animator Layer？

所有选择域在一个事件分发状态机中完成本地编码和远端解码。每个 AnyState 转移均带有域专属参数和 bit 条件，状态只承担一次该域的 Driver 写入，不需要表示全部域的组合；预览和生成确认会显示压缩 Layer 数量及 Controller 总 Layer 数。

Mixer 的服装组激活、普通部件和所有 Mixer 槽位共用一个 `Parts Control` Layer；其中使用 `DirectBlendTree` 叠加服装组激活 Clip 和槽位子树，槽位候选使用嵌套的 `Simple1D BlendTree`，不再为每个槽位创建独立 Layer。

### 什么时候不要启用压缩？

如果优先保证结构简单、便于调试，或项目已有足够同步参数预算，可以保持关闭。压缩主要用于同步参数较多，尤其是 Mixer 槽位较多的 Avatar。

## 安全与生成限制

- 参数前缀必须包含字母或数字，并在同一 Avatar 上保持唯一；
- 输出目录必须是 `Assets` 下的安全相对路径；
- Custom Mixer 必须同时启用部件控制；
- 服装对象数量必须在 VRChat `Int` 可表达范围内；
- 生成前的确认窗口会显示即将覆盖的 Controller、参数预算和压缩结构。
