# Changelog

## [0.3.25] - 2026-07-30

### Fixed

- 修复参数压缩层将本地编码和全局解码 Driver 放在同一状态的问题；现在以无 Driver 的 `Idle` 状态启动，并仅在 `IsLocal=false` 时进入解码状态，避免加载或本地初始化时错误改写选择参数。

## [0.3.24] - 2026-07-30

### Changed

- 优化嵌套服装菜单名称：启用部件控制或存在变体时，服装选择项按当前语言显示为“启用 + 对象名”或 `Enable + ObjectName`；Mixer 启用项显示为“启用混搭”或 `Enable Custom Mix`，自定义 Mixer 名称也会参与显示。

## [0.3.23] - 2026-07-30

### Fixed

- 修复主服装和 Mixer Enable 使用 VRChat `Button` 导致松开菜单后参数恢复、服装只能在按住期间生效的问题；现在改用持久的 `Toggle`。

## [0.3.22] - 2026-07-30

### Fixed

- 修复复用 `ACC_Menu` 时保留同名菜单项 `Parameter`、`Value`、子参数或外部菜单源的问题。现在只复用菜单根的展示属性；控制字段和 ACC 子菜单会直接在 `ACC_Menu` 下重建。
- ACC 创建的菜单、Animator 和参数组件统一保留在 `ACC_Menu`；仅按精确 ACC 参数名清理旧声明，保留无关手工 MA 参数。移除错误引入的 `__ACC_Generated_Menu` 与 `__ACC_Generated_Parameters` 层级节点，并在下次生成时自动迁移已有节点。

### Added

- 扫描器支持将服装骨架分支中的 `MA Merge Armature` 作为明确的服装识别信号。原有骨架识别命中但未配置该组件时，确认生成后会直接调用 MA `Setup Outfit`，避免复制或落后于 MA 的实现逻辑。
- `ACC_Menu` 会保留图标等展示属性；用户可在首次生成后自行设置图标，后续重新生成不会覆盖它们。
- 重新生成会保留 ACC 子菜单项的图标和自定义 Label；对象/部件的默认名称仍会随当前层级名称更新，旧 Parameter、Value 等控制数据不会复用。
- 除根菜单外，生成菜单项默认使用空 Label 并直接显示自身 GameObject 名称；Mixer、Parts、Enable 与槽位名称不再通过硬编码本地化 Label 覆盖对象名。
- Custom Mixer Name 现在直接作为 Mixer 节点对象名；窗口提示和生成摘要明确显示该节点名，而非误导为固定的菜单 Label。
- Parts、默认 Mixer 与 Enable 节点改为直接使用当前编辑器语言的对象名（部件/混搭/启用或 Parts/Custom Mix/Enable）；语言切换后仍能保留这些节点的自定义 Label 与图标。
- 修复 3 个及以上离散选择值时 Unity 自动重算 `Simple1D BlendTree` 阈值，造成中间服装或 Mixer 候选无法切换的问题；ACC 现在保留所有手工离散阈值。
- 主服装、Mixer Enable 和 Mixer 槽位候选改用 Button 写入共享离散参数，避免多个 Toggle 相互取消时将选择值回写为 0。
- 修复进入 Mixer 后服装全消失：默认服装组及默认对象会保持激活；默认服装的 Mixer 槽位默认值与普通 Parts 的初始 On/Off 状态一致。
- MA `Setup Outfit` 无法处理服装或未实际创建 Merge Armature 时，使用与当前 MA 默认配置一致的最小兜底，并输出 Warning。
- 主服装与 Mixer Enable 保持 Button 互斥选择；Mixer 槽位候选改为 Toggle，使点击已选候选可回到槽位 Off 值 0。
- 所有压缩选择域共用一个事件分发 Animator Layer。每域仍保留独立状态、Driver 与精确 AnyState 条件，减少 Layer 数但不减少同步域或转换条件。

## [0.3.15] - 2026-07-29

### Fixed

- 修复主服装与 Mixer 槽位的 `Int` Animator 参数被作为 BlendTree 权重读取时，VRCFury 会替换为 Controller 首个 Float 参数的问题；选择值现在在 Animator 内部使用 Float，避免被替换为 Modular Avatar 的 `__MA/Internal/MMDNotActive`。
- 不再复用 Unity 默认 `Base Layer` 作为 ACC 首层，避免首层特殊语义与 MA MMD Relay 的层序处理影响服装树。
- 修复将多个参数压缩选择域错误置于同一状态机的问题；每个选择域现在有独立的合并编码/解码 Layer，可独立同步。
- Mixer 服装组激活不再直接以槽位选择编号作为 Direct BlendTree 权重，改为 $0/1$ 门控，避免候选编号大于 1 时产生过量权重。
- 重新生成时会同步更新已存在 MA 参数的 `syncType`，避免旧生成配置保留过期参数类型。

## [0.3.14] - 2026-07-28

### Fixed

- 修复 AAO 合并 `Parts Control` 后 Custom Mixer 候选部件可能被错误加权的问题：服装组激活 Clip 不再写入 Mixer 槽位候选部件，候选部件只由对应的 Simple1D 槽位子树控制。
- 修复服装本体直接位于 CostumesRoot 下且存在同级网格时，根结点本身被识别为服装（OutfitObject 取到根结点）的问题；根结点一级不再做父级变体分组。
- 确认 `__MA/Internal/MMDNotActive` 属于 Modular Avatar 的 MMD Relay 内部参数；后续 0.3.15 修复了旧 ACC BlendTree 使用 Int 权重时被构建器误替换为该参数的问题。

## [0.3.13] - 2026-07-28

### Changed

- 将主服装切换和 Mixer 槽位候选全部改为 `Simple1D BlendTree`。
- 普通部件、Mixer 槽位和 Mixer 服装组激活统一到一个 `Parts Control` Layer。
- 所有参数压缩域共用一个根 StateMachine Layer，并增加值/位变化保护，减少重复 Transition。
- 生成状态统一使用 Write Defaults On，AnyState Transition 禁止过渡到自身；首个 ACC Layer 复用 Base Layer。
- 修复 Custom Mixer 激活 Clip 与槽位候选子树重复写入候选部件 `m_IsActive`，避免 AAO 合并 DirectBlendTree 后候选部件被错误加权。

## [0.3.12] - 2026-07-28

### Changed

- 将所有参数压缩选择域合并到一个共享 Animator Layer。
- 将普通部件和所有 Custom Mixer 槽位合并到一个 `Parts Control` Layer。
- 移除独立 `Parts Init` Layer，普通部件通过嵌套 `Simple1D BlendTree` 同时处理 Off/On 状态。
- 复用 Animator 必须存在的 Base Layer 作为首个 ACC Layer，不再留下空 Base Layer。
- 所有生成状态启用 Write Defaults，并禁止 AnyState Transition 过渡到自身。
- 生成摘要现在显示真实的压缩选择域数量、共享压缩 Layer 数量和 Controller 总 Layer 数。
- 将 Mixer 服装组激活进一步并入统一 `Parts Control` Layer，避免额外的 Mixer 激活 Layer。

## [0.3.11] - 2026-07-27

### Changed

- 参数压缩选项移动到混搭模式下方；参数预算改为直接显示各类别的最终 bit 总数。
- 合并参数压缩的编码层和解码层，每个有效选择域只新增一个 Animator Layer。
- Mixer 使用连续的服装数量作为入口值，不再使用固定的 `CustomMixerIndex`。
- 补充完整中文 README、组件帮助链接和 Runtime 组件双语 Tooltip。
- 统一服装标记 Inspector 中自动、持久分组、临时分组和排除状态的说明。
- 优化 Custom Mixer：入口默认关闭所有服装组，仅在该组至少一个部件槽位被选择时激活对应服装根对象和变体。
- 将 Custom Mixer 的服装组激活 Layer 合并为一个 `Direct BlendTree` Layer，减少按服装组重复创建的 Layer。
- 将主服装切换和 Mixer 槽位候选切换改为 `Simple1D BlendTree`，减少状态和 Transition 数量。

## [0.3.7] - 2026-07-26

### Fixed

- 生成菜单时，若父路径上已存在 `ModularAvatarMenuInstaller`，也会跳过挂载，避免嵌套 Modular Avatar 层级下产生重复 Installer。

## [0.3.6] - 2026-07-26

### Fixed

- 生成菜单时若根节点已存在 `ModularAvatarMenuInstaller`，将直接复用现有组件，不再重复创建并覆盖。

## [0.3.2] - 2026-07-26

### Added

- 新增 `ACCPartGroupMarker`，支持以持久化 Group Name 将任意部件或容器组合为一个开关控制项。
- `ACCPartGroupMarker` 新增 `Exclude` 模式，可显式排除不应由 ACC 控制的部件。
- `ACCOutfitMarker` 新增持久化自动部件菜单名称格式化，支持移除统一前缀/后缀及正则替换和 Inspector 预览。
- Outfit、Part Group、Material Variant 组件新增与 ACC 主窗口一致的 Auto 中英 Inspector 界面。

## [0.3.1] - 2026-07-26

### Added

- 新增 `ACCOutfitMarker`，支持将任意对象显式识别为服装根，包括没有独立骨架或网格的原版服装容器。

## [0.3.0] - 2026-07-26

### Added

- 生成菜单根节点会添加 `ModularAvatarMenuInstaller`。
- 新增 `ACCVariantMaterialOverride`，支持为对象变体或无 Mesh 材质变体生成材质替换曲线。
- 新增部件分组、Custom Mixer 部件隔离和 Auto / English / 中文编辑器界面语言。

### Changed

- 服装扫描改为基于骨架与网格结构，不再使用 Ignore Names 名称过滤。
- `Parameter Prefix` 现同时作为主服装 Int 参数、Animator Layer 和生成资产的唯一命名空间。
- 生成资产输出改为按 Avatar 与 Parameter Prefix 隔离，并在重新生成当前命名空间时清理旧资产。
- Custom Mixer 现在要求启用 Parts Control，且进入混搭时显式激活服装本体。
- 材质替换曲线合并进服装与 Mixer 变体切换 Clip，不再创建单独材质 Layer。
- 修复仅勾选变体时主参数默认值与 Animator 默认状态不一致的问题。
- 修复层级中同名 Mixer 变体组的 Layer / AnimationClip 命名冲突。
- 输出目录现仅接受安全的 `Assets/...` 相对路径，防止生成清理越界。
- 拒绝不含字母或数字的 Parameter Prefix，以及空的 Custom Mixer Name。
- 修复对象名称含 `/` 时菜单节点在重新生成中被误当作层级路径的问题。

## [0.2.1] - 2026-06-16

### Changed

- 当时不再生成 `ModularAvatarMenuInstaller` 组件（已在 0.3.0 恢复）
- 生成的 Animator Controller 文件名改为跟随服装根对象名称
- 参数前缀默认改为跟随服装根对象名称
- 服装切换参数名默认改为跟随服装根对象名称

## [0.2.0] - 2026-02-23

### Added

- 生成路径自动追加当前 Avatar 名称作为子目录，防止切换不同 Avatar 生成时互相覆盖

## [0.1.1] - 2026-02-23

### Fixed

- 修复服装变体未能正确识别的问题：移除 `Scanner.FindOutfits` 中对兄弟节点的 `HasMeshChild` 额外检查，恢复与原始实现一致的变体检测逻辑。深层嵌套 Mesh 的变体现在可以被正确发现。

## [0.1.0] - Initial Release

### Added

- 从 CostumesRoot 层级结构自动扫描服装、变体和部件
- 编辑器窗口预览与勾选控制
- 自动生成 Modular Avatar 菜单和 Animator Controller
- 部件独立开关控制（Parts Control）
- 混搭模式（Custom Mixer）
- 默认服装自动识别（按关键词匹配）
- 忽略名称过滤
