# 插件功能与验证场景

`master` 统一维护八个插件的 Unity 核心与可选 Avatar 适配，测试直接使用当前 checkout。
ASS 0.9.1 的源资源保全检查见[当前记录](Verification/ASSImportSafety/README.md)；
[全库验证结果](VerificationResults.json)与[性能报告](Performance.md)保留 0.9.0 对应快照的基线。

| 插件 | 已实现的工作流 | 功能场景与可观察输出 |
| --- | --- | --- |
| Animator Optimizer | 显式参数语义、独立层分析、静态合并、AAP、虚拟图提交与 Undo/Redo | Bool/Int/Float 与 WD/Mask 的逐帧对照；空 Motion 姿态历史；NDMF 最终提交与保存重载；原始/转换后更新成本 |
| ACC | 服装/变体、Parts、共享/独立 Mixer、材质、图标、同步压缩、MA 安装 | 原生 Animator 切换可见对象；本地编码/远端解码图和 Driver 写入值；生成与重新生成的 Undo/Redo；真实图标像素 |
| Blendshape | Float/Bool/反向、多个 Renderer 的组合 Controller、Animator 分配、MA 安装/移除 | 原生 Animator 权重；组合后绑定和参数类型；整 Avatar 参数预算；安装、移除、重新生成的 Undo |
| 口型 | 15 口型、Voice、Auto/On/Off WD、OverrideController、旧字段迁移 | 多口型与音量组合的逐帧权重；SDK/AOC 与 NDMF 关联图策略；最终提交后的 Controller、重命名路径及后续处理 |
| ASS | 0.9.1 自动构建、连续手势、超时、反馈、Restart 与源配置保全 | 连续保持/快速遍历/重复位释放；自动构建副本保留用户图和 WD；ASS 新状态 WD Off、Avatar 对象缩放保持；打开/导入保留原 GUID、字段和 Prefab；SDK/NDMF 交接 |
| Prefab | 属性、组件和子树选择；Variant 与嵌套 Prefab；布局、托管引用和自定义序列化 | 保存重载后的实际 Prefab 类型、嵌套源路径、组件与引用；数组/循环/共享引用；活动用户场景与 dirty 状态 |
| Collider | 3D/2D 与 PhysBoneCollider 快照、几何变化、缓存预览、历史 JSON、包围盒 | 多根对象和跨会话身份；同一 Mesh 引用重新烘焙后的几何差异；禁用/非活动对象；历史恢复和阶段采样 |
| Logger | Unity 导入/构建、SDK/NDMF、真实 Linker、Shader 去重和耗时轨迹 | 多次构建独立序号和时间；当前/最后完成轨迹 JSON；实际 Player 构建中的 Linker 与场景回调 |

## 共同运行语义

服装、Blendshape、口型和 ASS 生成层显式设置 `syncedLayerIndex=-1`，因此每层独立求值。
旧实现的 C# 默认值 0 会把后续层同步到基础层；原生 Animator 回归覆盖这些层的独立输出。

ASS 0.9.1 在正常 Player 构建的场景副本中自动调用后端，并配置 StandaloneSecurityInput；
源 Controller/AOC 与用户状态保持独立，ASS 新状态为 WD Off，原身体锁功能和选项移除。
输入以连续正确保持计时，默认 0.5s/0.3s 且保持严格大于容错；错误中断重计，快速
遍历不累积，重复位先释放，超时和 Restart 优先。API 使用 IsPasswordCorrect 查询结果。
0.9.1 移除旧 GUID 自动迁移及加载时参数纠正，打开与导入保留原文件和已存字段；
生成时在独立配置副本上处理时间约束。0.9.0 的迁移备份只作历史记录，不自动恢复。
混淆覆盖仅启用诱饵状态、关闭诱饵层以及无 Bool 池的组合；生成前声明条件所用参数，
缺少 Bool 时添加本地布尔守卫，保持转移条件类型一致。

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
