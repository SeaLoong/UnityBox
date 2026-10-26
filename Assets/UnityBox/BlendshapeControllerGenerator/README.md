# Blendshape Controller Generator

Unity 编辑器工具，为 Avatar 内的 SkinnedMeshRenderer 生成 blendshape 动画、FX Animator、Modular Avatar 菜单和同步参数。

## 依赖与使用

- Unity 2022.3；VRChat SDK Avatars >= 3.7.0；Modular Avatar >= 1.13.0。
- 打开 **Tools > UnityBox > Blendshape Controller Generator**，选择场景 Avatar，Scan 并勾选需要控制的 blendshape。
- Float 使用径向菜单和归一化时间采样；Bool 使用 Off/On 值，Inv 交换开关对应的值。
- 输出必须位于项目的 `Assets/...`，参数前缀和 Renderer 路径应避免与其他控制器冲突。

## 生成与升级

不同 Renderer 以相对路径区分菜单和输出。每次生成先创建独立 Build 目录，成功后才替换该 Renderer 的菜单、FX 引用和参数声明。取消或失败不删除原控制器；已完成的其他 Renderer 不会回滚。

旧 Build 资产保留供 Undo 和旧引用使用，确认无引用后再手动清理。取消全部勾选不会自动卸载旧结果。场景 Undo 不撤销文件生成/删除。旧公共菜单根上的对应声明会在重新生成时迁移；同名 Mesh、清洗后重名参数或更换 Renderer 路径时，应检查新参数与菜单以及 VRChat 保存的值。

## 文档与验证

完整操作、输出结构和限制见 [使用手册](../../../Docs/BlendshapeControllerGenerator.md)。`Tests/Editor/BlendshapeGeneratorTests.cs` 包含 14 个 EditMode 用例；通过 VPM 安装时将本包加入项目 manifest 的 `testables`，在安装 Unity Test Framework 后运行。

当前云端未执行 Unity 编译、这些用例或 VRChat 构建。仍需验证最终 MA FX 合并、Avatar 总参数预算，以及多个插件驱动同一个 blendshape 的行为。
