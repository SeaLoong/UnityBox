# 全仓插件审查与修复

本轮以 `master` 为基线，修复保存在 `refactor`，保留从 `temp` 带来的全部改动。审查覆盖 7 个插件的源码、
配置 UI、依赖声明、生成与构建入口、资产引用，以及 VPM 发布流程。
下面的“已修复”指代码修正完成；当前云环境没有 Unity Editor、许可证和完整
VRChat 项目，不能据此宣称插件已经通过 Unity 编译或游戏内验证。

## 各插件结果

| 插件 | 已确认的问题与本轮修正 | 仍需关注 |
| --- | --- | --- |
| Viseme Blendshape Override | NDMF 最低版本由 1.5 修正为实际所需的 1.7；使用虚拟路径映射，缺 FX 时创建；跳过禁用组件；优先 Descriptor 面部网格；修复映射继承、Voice 阈值重叠；SDK 回退启用动画层并隔离、保存生成资产 | 回退模式不支持 AnimatorOverrideController，现会明确报错；NDMF/MA 构建顺序及游戏内口型仍待测试 |
| Advanced Costume Controller | 延续共享组根修复；修正 Mixer 使用过期分组/选择、同显示名菜单覆盖、参数和文件路径清洗碰撞；MA 实际最低版本为 1.18；补上材质变体转换前的匹配检查 | 含特殊字符的路径可能生成新参数名，需重新生成并重新设置已保存选择；图标渲染、Undo、完整构建仍待实测 |
| Blendshape Controller Generator | 修复 Bool 反向无效、同名 mesh 输出互相覆盖、清洗后参数重名、菜单路径冲突、参数改型与取消选择残留；生成失败/取消保留旧控制器，不再吞掉 MA 集成异常；MA 最低版本修正为 1.13 | 新生成目录保留旧资产以支持旧引用/Undo，确认无引用后才能清理；新菜单和参数需重新生成 |
| Prefab Diff Patcher | 修复部分选择重绘丢失、批量选择漏根组件、Transform 选中但不应用、父对象位移带来的局部坐标错误；比较与重映射覆盖嵌套字段和引用数组；保留覆盖输出的 GUID，阻止覆盖输入及非法输出路径 | 复杂第三方组件的自定义序列化及真实 Prefab 合并仍待测试 |
| Avatar Collider Monitor | 修复设置开关；快照冻结对象路径/身份/配置并隔离不同 Avatar；SDK 使用实际构建对象采样；限制历史数量；将 NDMF 拆为可选装配 | 只提供声明清楚的阶段采样，没有可靠的 VRCFury 内部 feature 钩子，也没有 PhysBone 场景几何可视化 |
| Build Pipeline Logger | shader/compute 去重状态按每次构建重置；NDMF 拆为可选装配；第三方名称的 callbackOrder 观察点不再冒充第三方插件真实执行日志 | Linker 回调仍未启用；相同 order 不能保证绝对前后顺序 |
| Avatar Security System | 隔离原始 FX 和 ExpressionParameters 资产，保留第三方内存控制器；修复永不触发的手势错误条件、连续保持计时、超时竞争和空配置；新组件默认禁用主动 GPU 负载，Play Mode 跳过该负载 | 已有序列化配置不会自动迁移；主动负载的保护效果不成立为已验证结论，也不保证客户端稳定；见下述限制 |

更具体的功能与操作说明见各插件 README，以及 `Docs/AdvancedCostumeController.md`、
`Docs/BlendshapeControllerGenerator.md`、`Docs/VisemeBlendshapeOverride.md`。

## 发布与资源身份

旧流程把所有 `.meta` 从包中排除，连仓库已经保存的脚本 GUID 也丢弃。
现在发布工具保留已有元数据，忽略生成目录；ASS 的实验脚本、固定 GUID 的测试
资产和平台相关 bundle 不再混入正式安装包。实验源文件仍保留在仓库。
各插件 Editor 回归测试源码继续随包提供。

构建 ZIP 使用覆盖模式，避免再次构建时把已经删除的文件留在旧归档中。
5 项 Python 测试验证 GUID 保留、实验排除、生成目录排除、旧文件移除和其他包
测试资产的保留。

仓库仍缺少部分原始 `.meta`，例如 ASS 的 `Runtime/Component.cs.meta`。
需要从原 Unity 开发项目恢复这些文件并纳入版本控制，才能保留已有场景/Prefab
的脚本引用。不能凭空补一个新 GUID 来恢复原引用。`.gitignore` 已允许跟踪插件
元数据；不会覆盖已知 GUID。用户原始大场景和实验二进制未因本轮审查重写。

## ASS 的功能边界

密码、锁定、反馈和构建资产隔离可独立于主动负载功能使用。Defense 当前实现
包含高负载粒子/着色器，不能把它描述为可靠的防盗措施或安全的 Play Mode 测试。
本轮未增强这类负载；新建/Reset 组件默认关闭它，已有配置需自行检查。
`defaultEnableDefense` 配合关闭 Lock 的组合仍有参数注册缺口，本轮没有补强
负载激活路径。动画路径探针只能验证特定包和替换行为，不能证明通用防热上传有效。

## 验证与本地回归

本轮已通过 5 项 Python 发布测试、7 个包实际构建及 ZIP/清单/SHA256 校验、
当时所有 C# 文件语法解析、asmdef 的 JSON/仓库内装配引用检查，以及
元数据 GUID 唯一性检查（后续新增用例不改变此处的执行边界）。已直接检查最终安装包保留口型组件原 GUID、
排除 ASS 探针/bundle 并保留 Editor 回归测试。它们不替代 Unity 编译。
新增的 EditMode 回归用例调用实际插件方法，但在此环境中尚未执行。

建议本地依次执行：

1. 恢复原项目元数据，在 Unity 2022.3 中安装各包声明的依赖，并检查 Console 编译错误。
2. 安装 Unity Test Framework，在 Test Runner 的 EditMode 运行各插件 `Tests/Editor`。
   按 VPM 包安装时，将需测试的包名加入项目 manifest 的 `testables`。
3. 用自有测试 Avatar 分别跑 SDK 回退和 NDMF/Modular Avatar 构建。口型验证
   `Viseme` 0–14、Voice 区间、Face/Body 共存、禁用组件及多次构建；服装验证
   普通切换、两种 Mixer 参数模式、材质变体、撤销与重新生成。
4. Prefab 合并使用副本，检查骨骼数组/嵌套引用、Transform、重复组件和保留 GUID；
   监控工具连续构建两次并切换不同 Avatar，检查历史与日志。
5. ASS 关闭主动负载，测试错误手势、连续保持、成功/超时边界和构建两次后的
   原始资产引用，再做游戏内功能验证。

在上述检查完成前，状态是“全仓代码审查与已确认缺陷修正完成，Unity 实测待执行”，
不是“所有插件已证实可用”。

各包文档与当前源码已同步；具体测试命令见 [测试指南](Testing.md)。Animator Optimizer 的独立审查在 `feat/animator-optimizer` 分支，不能将本报告的 7 包结果视为优化器已验证。
