# Blendshape Controller Generator

Unity 2022.3 编辑器工具，为 SkinnedMeshRenderer 生成 blendshape 动画与 Animator Controller。主窗口和完整动画生成流程不需要 VRChat SDK 或 Modular Avatar。

## 使用与可选集成

1. 打开 **Tools > UnityBox > Blendshape Controller Generator**，选择场景 **Animation Root**，Scan 并勾选需要控制的 blendshape。
2. Float 通过参数 0～1 控制动画归一化采样时间；Bool 使用 Off/On 值，Inv 交换开关对应的值。
3. 选择项目 `Assets/...` 输出目录并 Generate。普通 Unity 项目将生成的 Controller 赋给 Animation Root 上的 Animator，再通过 Animator 参数控制；多个 Renderer 各自生成一个 Controller，合并或选择使用由项目负责。
4. 需要 Avatar 菜单和 FX 安装时，另外安装 **VRChat SDK Base/Avatars >= 3.7.3** 与 **Modular Avatar >= 1.13.0**。满足版本后可选集成程序集自动启用，生成时同时创建 MA 菜单、同步参数与 FX 合并组件。窗口会显示当前模式；取消 **Install MA Controls** 可在已安装集成包时仍只生成 Unity 资产。包不再强制安装这些可选依赖。

## 生成与升级

不同 Renderer 以相对路径区分输出。每次先生成独立 Build 目录；启用 MA 时成功后才替换该 Renderer 的菜单、FX 引用和参数声明。普通 Unity 模式只输出资产，不自动替换场景 Animator，重新生成后需手动换用新 Controller。

旧 Build 资产保留供 Undo 和旧引用使用，确认无引用后再手动清理。取消全部勾选不会自动卸载旧结果；场景 Undo 不撤销文件生成/删除。启用 MA 时，旧公共菜单根上的对应声明会在重新生成时迁移。更改对象路径、参数命名时应检查旧引用和保存值。

## 文档与验证

完整操作、输出结构、程序集边界和限制见 [使用手册](../../../Docs/BlendshapeControllerGenerator.md)。`Tests/Core/Editor` 有 14 个不需要 SDK/MA 的 EditMode 用例，覆盖真实 Float/Bool 资产生成、绑定、曲线和路径边界；`Tests/Editor` 有 5 个 MA 集成用例，仅在安装真实依赖后启用。安装 Unity Test Framework 后在 Test Runner 运行；以包形式安装时将包名加入 manifest 的 `testables`。

测试范围与实际执行结果分别记录，不能由 Core 测试推断 MA FX 合并、Avatar 参数预算或多个插件驱动同一 blendshape 的行为已经验证。

MA 1.13/1.18 使用的双参数 `VRCConstraintSource` 构造函数从 SDK 3.7.3 才可用；SDK 3.7.0～3.7.2 不满足 MA 集成条件。该门槛不影响无 SDK 的 Unity 动画资产生成。
