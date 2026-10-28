# 插件功能与验证场景

`refactor` 维护七个插件的独立 Unity 核心和 Avatar 适配。Animator Optimizer 的源码在
`feat/animator-optimizer`；组合测试直接装入两个 checkout 的包。
各次执行、内容指纹和输出计数集中记录于 [验证结果](VerificationResults.json)，
基准数据集中记录于 [性能报告](Performance.md)。

| 插件 | 已实现的工作流 | 功能场景与可观察输出 |
| --- | --- | --- |
| ACC | 服装/变体、Parts、共享/独立 Mixer、材质、图标、同步压缩、MA 安装 | 原生 Animator 切换可见对象；本地编码/远端解码图和 Driver 写入值；生成与重新生成的 Undo/Redo；真实图标像素 |
| Blendshape | Float/Bool/反向、多个 Renderer 的组合 Controller、Animator 分配、MA 安装/移除 | 原生 Animator 权重；组合后绑定和参数类型；整 Avatar 参数预算；安装、移除、重新生成的 Undo |
| 口型 | 15 口型、Voice、Auto/On/Off WD、OverrideController、旧字段迁移 | 多口型与音量组合的逐帧权重；SDK/AOC 与 NDMF 关联图策略；最终提交后的 Controller、重命名路径及后续处理 |
| ASS | 普通 Unity 运行时、连续手势保持、超时、解锁/重锁、Restart、SDK/NDMF 构建 | Animator 的密码/计时参数和对象缩放；解锁后重新锁定；内存/AOC 副本；原场景 GUID；构建服务提交和源图隔离 |
| Prefab | 属性、组件和子树选择；Variant 与嵌套 Prefab；布局、托管引用和自定义序列化 | 保存重载后的实际 Prefab 类型、嵌套源路径、组件与引用；数组/循环/共享引用；活动用户场景与 dirty 状态 |
| Collider | 3D/2D 与 PhysBoneCollider 快照、几何变化、缓存预览、历史 JSON、包围盒 | 多根对象和跨会话身份；同一 Mesh 引用重新烘焙后的几何差异；禁用/非活动对象；历史恢复和阶段采样 |
| Logger | Unity 导入/构建、SDK/NDMF、真实 Linker、Shader 去重和耗时轨迹 | 多次构建独立序号和时间；当前/最后完成轨迹 JSON；实际 Player 构建中的 Linker 与场景回调 |

## 本轮完成的关键修复

服装、Blendshape、口型和 ASS 生成层显式设置 `syncedLayerIndex=-1`，因此每层独立求值。
旧实现的 C# 默认值 0 会把后续层同步到基础层；本轮以真实 Animator 输出覆盖该问题。

ASS 提供 `StandaloneSecurityBuilder.Build` 与 `StandaloneSecurityInput`，普通 Unity 可直接
运行完整手势流程。组件脚本的原 GUID `1c51e6a139920bb4ca12700dc82627ff` 来自已提交场景的
三处序列化引用，恢复 metadata 后场景保持原内容。

Prefab 在 A 的实例上应用选择，保留 Variant 祖先与嵌套连接。Collider 将几何和历史比较
缓存在对应快照版本，Logger 将常用设置缓存并保留最后完成构建的轨迹；这些路径分别有
规模化基准，与生成类工具的资产写入、Animator 更新基准一起列入统一性能报告。

## 资产与输入契约

普通 Unity 的 Viseme、Voice 和手势由项目脚本写入 Animator 或对应输入 API；Avatar 模式
由真实平台服务提供。平台语义和可选包由明确入口选择。
生成器采用独立 Build 输出，源 Controller、Clip、参数资产和用户场景保持原引用身份。
已知 metadata 随包保留；重载、复制、Undo、Redo 和失败回滚通过输出对象及资产引用检查。

详细迁移步骤见 [变更清单](RefactorChanges.md)，程序集映射见
[依赖架构](DependencyArchitecture.md)，执行命令见 [测试指南](Testing.md)。
