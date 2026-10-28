# Advanced Costume Controller

ACC 扫描 Costumes Root 下的服装、变体和部件，生成 Animator Controller/Clip；完整 Avatar
工作流还可安装 MA 菜单、同步参数、骨架和图标。核心仅要求 Unity 2022.3，包不再强制
安装 SDK/MA。字段、扫描规则、压缩结构和所有组件操作详见
[包内完整手册](../Assets/UnityBox/AdvancedCostumeController/README.md)。

## 选择工作流

| 入口 | 功能 | 额外条件 |
| --- | --- | --- |
| Tools > UnityBox > Advanced Costume Controller (Unity Assets) | 真实服装扫描、Controller/Clip、Parts、两种 Mixer、材质曲线 | 无 SDK/MA |
| Tools > UnityBox > Advanced Costume Controller | Avatar 菜单、参数、Setup Outfit、图标和 FX 安装 | SDK Base/Avatars ≥3.7.3、MA ≥1.18 |

缺少完整集成时，原菜单也打开普通 Unity 窗口。独立 SDK Driver/Descriptor 适配最低仍为
Avatars 3.7；MA 使用的双参数 `VRCConstraintSource` 从 SDK 3.7.3 才提供，不应使用
3.7.0～3.7.2 进行完整 MA 集成。

## 普通 Unity

1. 选择场景中的 Costumes Root、参数前缀与 Assets 下输出目录，点击 Scan / Refresh。
2. 无独立骨架时可在服装根添加 `ACCOutfitMarker`；三个 ACC Runtime 标记均可在无 SDK
   项目中使用。MA 标记识别只在相应适配可用时加入。
3. 勾选服装组，按需打开 Parts Control、Custom Mixer、独立部件参数，指定默认服装/变体。
4. 点击 Generate Unity Assets，把输出 Controller 赋给 **Costumes Root 上的 Animator**。
5. 项目自己的 UI/脚本驱动 Animator Float 参数；主选择从 0 连续编号，开启 Mixer 后其值
   紧接最后一个普通服装值。参数的名称与默认值以生成 Controller 为准。

普通窗口提供服装组选择；更细的临时部件/变体选择可通过共享 OutfitData 模型传给
`UnityCostumeGenerator.Generate`，或使用完整 Avatar 窗口。材质规则可在
`ACCVariantMaterialOverride` 编辑，自动转换 Inspector 属于 Avatar 集成。

此入口不会自动分配 Animator、执行 Setup Outfit 或安装 MA。没有 SDK 时不能生成网络
压缩 Driver；即使有 SDK，表达式参数同步安装仍是 Avatar 工作流，普通游戏保持压缩关闭。

## Avatar 工作流

在完整窗口刷新预览，选择服装、本体/变体与部件；按需保存临时分组为
`ACCPartGroupMarker`。未保存编辑的刷新/切换有确认提示。Custom Mixer 要求 Parts Control。

- 共享部件模式复用普通部件 Bool；多对象组有整体变体选择域。
- 独立部件模式每个槽位用 0 表示关闭、1..N 表示候选，按当前预览勾选和临时分组生成。
- 默认服装可指定本体、对象变体或材质变体；无有效指定时使用首个启用对象。
- Animator BlendTree 使用 Float 读取离散数值；Avatar 表达式两值域使用 Bool、多值使用
  Int 或压缩位。这依赖 Avatar 输入语义，不能直接照搬成普通 Unity SetInteger 调用。
- 参数压缩默认关闭。多值域编码为同步 Bool 位和本地参数，并新增编码/解码状态；它减少
  同步位数，不保证降低 Animator 成本。

生成前检查参数预算与覆盖摘要。ACC_Menu 复用展示属性，重建控制项及本命名空间的参数，
不复用旧控件 Parameter/Value。图标使用 Preview Scene 渲染，透明输出仍可写入；需检查
实际材质、灯光和 Avatar 姿态。不同 ACC 实例使用不同 Parameter Prefix。

## 生成资产与迁移

普通 Unity 默认目录是 `Assets/UnityBox/Generated/AdvancedCostumeControllerUnity`，
每次创建新的 Build 子目录并保留旧结果；失败清理本次不完整生成，旧引用仍可用。
Avatar 工作流在其场景/Avatar/前缀专属目录内清理并重建资产；关闭自动图标时保留
MenuIcons。**不要让普通入口与 Avatar 清理目录重合，也不要把手工资产放入清理目录。**

共享容器修复会让普通切换和 Mixer 保持独立服装归属，恢复必要组织祖先，不控制
CostumesRoot/Avatar 根。菜单保留真实路径。特殊字符被清理/截断的参数和文件名加稳定
哈希，避免碰撞；更新后需重新生成并检查旧自定义引用和保存的菜单选择。

显示名相同的部件菜单会追加序号。材质变体转换前验证匹配槽位，无匹配保留来源；成功
转换仍会清理来源组件和子对象，需要保留对照时先复制。场景改动可 Undo，但 AssetDatabase
文件删除不是完整场景 Undo，旧输出没有通用自动迁移。

动画绑定仍受 Unity 路径规则约束：对象名中的 `/` 或同父下同名对象可能歧义；菜单能区分
不代表动画能正确绑定。Write Defaults、第三方合并和多个工具驱动同一属性需检查最终图。

## 开发与测试

Core/Editor 包含 Models、Scanner、AnimationBuilder、Localization、普通窗口及生成器；
Editor 保留完整 MA 工作流；Integrations/VRChat/Editor 提供真实平台 Driver/根识别。
Runtime 标记保留原类型、字段和已有 GUID。旧 Editor 扩展使用搬迁公开类型时增加
`UnityBox.AdvancedCostumeController.Core` 引用。

`Tests/Core/Editor` 声明 32 个用例，覆盖规则18、扫描7、完整资产生成7；平台
`Tests/Editor` 声明19个，另需实际SDK/MA。使用真实 Unity Test Framework，按包安装时将
本包名加入项目 manifest 的 testables。用例覆盖不等于图标/Undo/VRChat同步已运行。

真实依赖 Roslyn 类型检查已通过，Unity 测试因无许可证尚未执行。最新执行范围见
[测试指南](Testing.md)，全部变更及风险见 [RefactorChanges](RefactorChanges.md)。
