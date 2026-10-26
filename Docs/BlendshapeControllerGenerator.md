# Blendshape Controller Generator

需要 Unity 2022.3、VRChat SDK Avatars 3.7.0 或更高版本，以及 Modular Avatar **1.13.0 或更高版本**（菜单生成使用 PortableControl API）。

这个编辑器窗口为 Avatar 内选中的 SkinnedMeshRenderer blendshape 生成动画、FX Animator、MA 菜单和同步参数。每个 blendshape 一层，支持 Float 径向菜单或 Bool 开关。

## 使用方法

1. 打开 Tools → UnityBox → Blendshape Controller Generator。
2. 将场景中的 Avatar 根对象拖入 Avatar 字段。
3. 用 Scan 查找 Renderer，或 Add 手动添加。Scan Filter 按对象名过滤，Include Inactive 可包含未激活对象。
4. 展开 Mesh，选择 blendshape、参数前缀、Float/Bool 类型。Mesh 的前缀可独立编辑；跨 Mesh 的参数名冲突会提示自动追加前缀。同一 Mesh 中清洗后重名的 blendshape 会得到不同后缀，预览显示实际名称。
5. 设置项目 Assets 内的 Output Folder；Controller Name 可覆盖控制器文件名，不改变按 Renderer 隔离的身份。点击 Generate 后，新控制器、动画和 MA 菜单会连接到 Avatar。

## 参数行为

- **Float**：菜单值 0～1 控制动画的归一化采样时间。曲线时间和值按 Duration 与 Value Scale 缩放；默认从 0 到 100。Duration 必须大于 0，输入值必须有限；零 Value Scale 允许生成全零曲线，阶梯曲线的无限切线会保留，避免 `0 × Infinity` 产生 NaN。每个参数同步成本为 8 bit。
- **Bool**：关闭使用 Off，开启使用 On；勾选 **Inv** 后关闭使用 On、开启使用 Off。默认参数为 false。每个参数同步成本为 1 bit。
- 参数默认值显式设为 0，菜单参数保存并同步。生成层使用 Write Defaults Off；MA Merge Animator 按 Avatar 的 Write Defaults 策略匹配。
- blendshape 名中的 `/` 生成子菜单。`Smile` 和 `Smile/Big` 同时存在时，各自保留独立控制项，不会互相覆盖。
- 同名 Renderer 位于不同层级时，输出目录和菜单按其真实相对路径区分。Avatar 输出身份还包括场景路径及层级位置；移动或重命名 Avatar/Renderer 后可能产生新的身份和目录，需要检查旧菜单及旧资产引用。动画绑定仍使用 Avatar 到 Renderer 的原始路径。

## 生成结构

菜单位于 Avatar 下的 `Blendshapes Menu`；每个 Renderer 的子根具有独立 `ModularAvatarMergeAnimator`（FX、相对 Avatar 根）、`ModularAvatarParameters` 和控制项。显示名称沿用 Renderer 名称，内部身份由名字和相对路径哈希组成。

```text
{OutputFolder}/{AvatarIdentity}/{MeshIdentity}/Build[ n]/
├── {ControllerName}.controller
├── 0000_Float.anim
└── 其他选中 blendshape 的 Float 或 Bool Off/On 动画
```

只为勾选的 blendshape 生成层与参数。Float 曲线、Duration、Loop、Value Scale 会保存到当前 Unity 编辑器的 EditorPrefs；它们不是 Avatar 上的持久组件配置。

## 重复生成、取消与 Undo

同一个 Renderer 再次生成会替换其生成的菜单/合并控制器和参数声明，改参数类型或取消部分选择后不会继续保留旧声明。旧版生成器位于公共菜单根上的对应参数声明会迁移；其他控制器仍在使用的声明会保留。

每次生成先写入新的 `Build` 目录（同名时自动追加序号），完成后才替换场景引用。取消或发生异常时，本次尚未使用的目录会删除，原控制器仍可用。此前已完成的其他 Mesh 会保留。

旧生成资产会保留，以保证场景 Undo 后的控制器引用有效；确认不需要旧版本后可手动清理未使用的 Build 目录。场景组件修改支持 Undo，资产文件的生成与删除不由 Unity 场景 Undo 撤销。

## 检查与限制

- Renderer 必须属于选中的 Avatar；如果扫描后替换了 Mesh 或删除了 blendshape，请重新 Scan。
- Renderer 路径上的同名兄弟对象或名称中的 `/` 会导致 Unity 动画绑定歧义，生成前会提示修正。
- 输出路径必须是 `Assets/...` 相对路径，使用 `/`，不能包含 `.`、`..`、空路径段、反斜线或冒号。
- 多个生成器、口型控制器或 Avatar 自带 FX 同时驱动同一个 blendshape 时，仍需检查最终 FX 合并结果和表达式参数总预算。
- 所有选择都取消时不会自动卸载以前生成的内容；需要卸载时删除对应的生成菜单根及其引用资产。

## 回归验证

`Tests/Editor/BlendshapeGeneratorTests.cs` 提供 14 个 EditMode 用例，覆盖 Bool 反转、同名 Mesh 隔离、重复生成改型与旧资产保留、菜单目录/叶子冲突、清洗后参数名冲突、零 Value Scale 与阶梯切线，以及输出路径边界。

本次已进行 C# 语法解析与源代码检查；云环境没有 Unity，尚未执行这些用例或 VRChat 构建验证。在已安装依赖和 Unity Test Framework 的项目中运行 Test Runner → EditMode → `BlendshapeGeneratorTests`。按 VPM 包安装时，将 `top.sealoong.unitybox.blendshape-controller-generator` 加入项目 manifest 的 `testables`。
