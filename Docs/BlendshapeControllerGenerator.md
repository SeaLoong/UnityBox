# Blendshape Controller Generator

Unity 2022.3 工具，为多个 SkinnedMeshRenderer 生成 Float、Bool 与反向 Bool 控制，并把
各 Renderer 的动画合成为一个可直接分配的 Animator Controller。普通 Unity 使用完整
扫描、生成、合成和分配流程；可选 MA 适配提供 Avatar 菜单、同步参数和 FX 安装。

## 普通 Unity 使用

1. 打开 **Tools > UnityBox > Blendshape Controller Generator**，选择 **Animation Root**。
2. Scan 查找 Renderer，或 Add 手动添加。Filter 按对象名称筛选，Include Inactive 包含
   未激活对象。动画路径相对 Animation Root，同级对象使用唯一名称。
3. 勾选 blendshape，设置各 Mesh 前缀及 Float/Bool 类型。Float 参数输入 0～1；Bool
   false/true 对应 Off/On，**Inv** 交换两端输出。
4. 设置 `Assets/...` 输出目录后 Generate。窗口展示合成后的 **Generated Controller**。
5. 点击 **Assign to Animation Root Animator**，创建或更新根上的 Animator；该操作支持
   Undo/Redo。也可将输出 Controller 手工赋给其它同构层级上的 Animator。

每个选中 Renderer 保留自己的 Controller/Clip，并额外生成 `Combined/Build[ n]/`
下的 `Blendshapes.controller`。合成 Controller 保留各层参数、Mask、层权重和状态图引用，
同时驱动全部 Renderer。重新生成写入新的 Build，先前结果继续引用其原动画资产。

生成前按 Renderer＋blendshape 检查重复勾选。合成 API
`BlendshapeControllerComposer.Combine(sources, outputPath)` 统一参数并检查类型、默认值和
动画绑定冲突；一个具体 binding 由一个输入 Controller 提供，错误提示直接列出冲突项。

## 曲线和参数

- Float 以参数驱动状态的 normalized time，默认曲线从 0 到100。Duration 缩放动画时长，
  Value Scale 缩放输出值；零 Scale 保持阶梯切线并输出零。
- Bool 默认 false，Off/On 自定义两个输出值；Inv 改变 false/true 与两端值的对应关系。
- 生成层使用 Write Defaults Off，且各层显式保持独立同步索引。
- Curve、Duration、Loop、Value Scale 保存在 EditorPrefs；窗口显示生成后的完整参数名称。

## Modular Avatar 安装与参数预算

安装 SDK Base/Avatars **≥3.7.3** 和 MA **≥1.13** 后，勾选 **Install MA Controls**。
MA 的 NDMF 依赖随实际 MA 版本安装；本轮执行组合是 SDK3.8.2、MA1.18、NDMF1.11。

生成器在 `Blendshapes Menu` 下按 Renderer 身份安装菜单、FX 和参数。名称中的 `/` 构成
子菜单，`Smile` 与 `Smile/Big` 可同时作为独立控件。Float/Bool 同步成本分别为8/1bit。

安装使用与 MA 参数窗口相同的 NDMF `ParameterInfo` 查询，统计已有 Descriptor 参数、
其它 MA 控件和本次替换后的参数，总预算为256bit。重新生成以新参数类型重新计算，
超过预算时显示实际位数并恢复原菜单和 Controller。一次安装对应一次 Undo/Redo。

**Remove Selected MA Controls** 移除当前启用 Renderer 的菜单、FX 与参数声明，保留
其它 Renderer 的结果；移除同样支持 Undo。历史公共菜单根上的对应声明会随更新迁移，
仍被其它 Controller 使用的声明继续保留。

## 输出与维护

```text
{OutputFolder}/{AvatarIdentity}/
├── {MeshIdentity}/Build[ n]/
│   ├── {ControllerName}.controller
│   └── 所选 Float 或 Bool Off/On 动画
└── Combined/Build[ n]/Blendshapes.controller
```

合成 Controller 引用各 Renderer Build 中的状态图和 Clip，因此一起保留这些目录。
旧 Build 用于 Undo 和旧引用；清理时按项目引用情况删除。根与 Renderer 身份包含场景、
层级和相对路径，移动对象后重新 Scan，再生成并分配新结果。

## 行为测试和性能

测试直接运行真实 Animator，检查 Float 的0、0.25、0.5、0.75、1及返回值，反复切换
Bool/反向 Bool，以及合成 Controller 重新加载后的多 Renderer 输出。安装测试覆盖
整个 Avatar 的参数预算、参数改型、Undo/Redo、超额回滚和选择性卸载。

独立 Player 探针构建并运行同一份 Float＋反向 Bool 合成结果。性能场景分别测量
4/16/64个 blendshape 的持久化生成，以及预热后9组、每组512帧的真实 Animator.Update。
生成时延包含资产持久化；逐帧时延单独采集。数值、环境和复现命令见
[性能记录](Performance.md)，完整执行结果见[测试指南](Testing.md)。
