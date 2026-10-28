# Advanced Costume Controller

ACC 扫描 Costumes Root 下的服装、变体与部件，生成普通 Unity 可用的 Animator Controller
和 Clip。Avatar 工作流在相同动画核心之上安装 MA 菜单、骨架、FX、同步参数与256×256图标。
字段和完整操作见[包内手册](../Assets/UnityBox/AdvancedCostumeController/README.md)。

## 工作流

| 入口 | 功能 | 依赖 |
| --- | --- | --- |
| Tools > UnityBox > Advanced Costume Controller (Unity Assets) | 扫描、Controller/Clip、Parts、两种 Mixer、材质曲线 | Unity2022.3 |
| Tools > UnityBox > Advanced Costume Controller | MA 菜单、Setup Outfit、FX、参数压缩和图标 | SDK Base/Avatars≥3.7.3、MA≥1.18 |

1. 选择 Costumes Root，设置参数前缀与 `Assets/...` 输出目录，刷新服装预览。
2. 使用 `ACCOutfitMarker` 显式声明服装；配置本体、变体、部件与持久分组。
3. 按需启用 Parts Control、Custom Mixer、独立混搭参数和默认服装/变体。
4. 普通 Unity 生成后将 Controller 分配给 Costumes Root 上的 Animator，通过 Float 参数
   驱动选择与部件。Avatar 入口直接安装 MA 菜单、同步参数和 FX。

普通 Unity 入口和 Avatar 入口共用 `Scanner`、`OutfitData`、`AnimationBuilder` 和选择
算法。脚本可调用 `UnityCostumeGenerator.Generate(config, selectedOutfits)`；完整 Avatar
批处理可调用 `Generator.Execute(outfits, indexMap, defaultOutfit, false)`。

## Parts 与 Mixer

主选择从0连续编号，Mixer 值紧接最后一个普通服装值。正常选择激活对应服装组，Parts
参数分别控制部件。共享 Mixer 打开基础服装组并复用普通部件参数；独立 Mixer 的每个
槽位用0表示关闭、1..N选择本体或变体候选。切回普通选择会恢复相应服装组及其部件。

临时分组可保存为 `ACCPartGroupMarker`。材质规则由 `ACCVariantMaterialOverride`
提供，全局替换与 Renderer 槽位替换共同参与动画和图标，精准槽位规则优先。

Animator 使用 Float 读取离散选择，Avatar 表达式使用 Bool、Int或压缩位。普通 Unity
脚本使用 SetFloat；Avatar 的离散输入由平台参数服务提供。每个 ACC 实例使用独立前缀。

## 参数压缩与预算

压缩把多值选择编码为同步 Bool 位，Animator 内保留本地选择值。多个选择域共用一个
编码/解码层：本地状态写入本域位，远端状态从同步位恢复本域选择值。三选一占2bit，
七选一占3bit，普通同步 Int 占8bit；二选一直接使用1bit Bool。

窗口预览显示本 ACC 的同步位数和压缩结构。完整安装还通过 NDMF 的公开 ParameterInfo
查询整个 Avatar 的参数成本，包含 Descriptor、其它 MA 控件和本次结果，按256bit预算
检查。超过预算的生成会恢复原菜单及 Controller，并显示实际成本。

## 生成资产和 Undo

两种入口都在场景、根身份与前缀命名空间下创建新的 `Build[ n]`。Avatar Build 同时包含
Controller、Animations 和本次自动图标。旧 Build 持续保留，因此重新生成后的场景 Undo
可恢复旧 Controller、菜单、参数与图标引用，Redo 再切回新结果。

生成异常时撤回本次场景操作并清理本次 Build；既有输出保留。关闭自动图标时沿用菜单
展示快照中的原图标。更改菜单文字不改变参数，特殊路径字符使用稳定哈希区分身份。

## 图标与材质

图标在自有 Preview Scene 中克隆 Avatar，通过专用 Layer 与正交相机渲染原 Renderer，
输出透明256×256 PNG。服装图标使用共同取景框，部件按自身 Bounds 取景；材质变体逐项
恢复源材质后应用替换，源对象的材质、层与激活状态保持原值。新图标写入当前 Build 的
MenuIcons，历史图标继续支持 Undo。

## 行为测试和性能

真实 Animator 测试按普通选择→切换服装→Mixer→返回普通选择驱动参数，同时检查四个
部件和两个服装组；共享与独立 Mixer 分别运行。压缩测试在真实 Animator 中选择本地
编码、远端解码状态，并核对真实 SDK ParameterDriver 的位和选择值。

安装测试执行重复生成、Undo/Redo和预算超额回滚。图形场景实际渲染红/绿材质变体，
读取PNG验证尺寸、非透明像素与颜色差异。独立 Player 探针构建运行两种 Mixer 和 Parts。

性能场景测量2/8/24套服装及每套两个部件的完整资产生成；另在预热后采集9组、每组
512帧的 Animator.Update。图标场景预热后采集5组双图渲染和资产导入时延。
环境、测量值与命令见[性能记录](Performance.md)，测试执行见[测试指南](Testing.md)。
