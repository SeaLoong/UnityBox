# Blendshape Controller Generator

当前版本 **0.2.0**（2026-11-05），在 `master` 与其它 UnityBox 插件共同维护。

Unity2022.3工具，为多个SkinnedMeshRenderer生成Float、Bool和反向Bool动画。扫描、生成、合成与Animator分配均可在普通Unity项目使用。

## 使用

1. 打开 **Tools > UnityBox > Blendshape Controller Generator**，选择 **Animation Root**，Scan并勾选blendshape。
2. Float以0～1控制归一化动画时间；Bool使用Off/On，Inv交换两端值。
3. 选择Assets输出目录后Generate，窗口展示多Renderer合成的 **Generated Controller**。
4. 点击 **Assign to Animation Root Animator** 分配结果；该操作支持Undo/Redo。每个Renderer的原Controller/Clip与合成资产一起保留。

## 可选Avatar集成

安装SDK Base/Avatars≥3.7.3、MA≥1.13及其NDMF依赖后，启用 **Install MA Controls** 创建菜单、同步参数和FX。生成时使用MA同款NDMF ParameterInfo统计整个Avatar的256bit预算；参数改型按替换后的结果计算，超额恢复原安装。

**Remove Selected MA Controls** 按当前启用的Renderer移除对应菜单、FX和参数，保留其它Renderer；安装和卸载均支持Undo/Redo。重新生成写入新的Build，旧资产和引用持续有效。所有选中Renderer作为一次完整事务生成：后续Renderer失败或用户取消时，恢复整批生成前的安装与窗口结果，并删除本次新建的Build；成功后一次Undo/Redo恢复整批安装。

## 合成、测试与性能

`BlendshapeControllerComposer.Combine`统一参数、保留独立层并检查类型、默认值及重复动画绑定。普通窗口自动使用该API输出一个Animator-ready Controller。窗口的 `GenerateControllers()` API执行相同的整批事务；成功返回true，用户取消返回false，输入或生成错误直接抛出相应异常。

真实Animator测试检查Float采样、Bool反向和多Renderer合成结果；Standalone Player探针构建运行同一控制器。性能测试分别测量4/16/64个blendshape的资产生成与预热后的逐帧更新。

完整操作与输出结构见[使用手册](../../../Docs/BlendshapeControllerGenerator.md)，测试命令见[测试指南](../../../Docs/Testing.md)，测量值与环境见[性能记录](../../../Docs/Performance.md)。

参数类型冲突按声明组件归属检测，并使用实际重映射后的名称报告。父级 MA 参数重映射不会绕过 Bool/Float 冲突检查；失败时用户已有声明与整个批次的旧安装保持原状。
