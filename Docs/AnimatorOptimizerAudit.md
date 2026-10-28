# Animator Optimizer 实现与运行记录

`feat/animator-optimizer` 从 `master` 的 `e9f0b36` 继续开发。其它七插件在独立 `refactor` 分支；本页记录优化器的实现与实际运行流程。

## 本轮功能迭代

| 问题 | 完成的实现 | 实际检查 |
| --- | --- | --- |
| AAP 可见输出滞后一帧 | Update 与 Output 共享下一状态决策编译器，输出直接选取当帧目标 Motion | 首帧 Entry、Bool/Int 历史、Float 重叠 AnyState 逐帧对照 |
| NDMF AAP 只走物理回退，手动选择跳过 | 当前虚拟图建立物理编译快照，复用核心 AAP 计划，复制回独立虚拟 Motion DAG | 自动/手动选层、插层后身份、修改后的当前曲线、最终提交及资产重载 |
| 生成子资产未随 Undo 回收 | Clip、BlendTree、StateMachine 与 State 都登记创建 Undo | 常规合并和 AAP 的完整资产数量、参数类型、Undo/Redo 后原生运行 |
| 平台 Bool/Int 输入被错误放入 Float 决策轴 | 保留平台原生条件层与参数类型，继续优化独立层 | IsLocal / Viseme 的 90 帧源/输出对照，含 NDMF 最终提交与资产重载 |
| 混合空 Motion 与动画状态被静态化 | 常规、有限图与 AAP 规划前保留原生状态机，保持首次场景姿态及后续输出历史 | 原生 AnyState / RootExit 各 90 帧，NDMF 最终提交重载后 90 帧对照 |
| 用户场景中的组件脚本 metadata 缺失 | 从现有场景恢复精确脚本 GUID，补齐其余稳定 metadata | GUID → MonoScript → AnimatorOptimizerComponent 的实际导入类型检查 |
| 原生 Animator 图通用 Instantiate 断言 | 具体图类型构造器 + CopySerialized + 共享引用映射 | 内存图、Controller 子资产、复制与重新导入 |
| Float 覆盖间隙被近似选最近分支 | 状态保持由 StateRegister 表示，无历史选择器使用完整输入覆盖 | 严格/近似间隙用例及 Float 历史运行轨迹 |
| 虚拟来源属性名错误 | 读取 NDMF 1.11 的 OriginalObject，核对来源类型 | 源 Clip 身份、事件元数据、生成节点空来源、错误类型分支 |

## 同一帧的 AAP 语义

旧版 Update 层写入下一状态，而 Output 只按当前寄存器选 Motion。实际原生 Animator 在图求值结束时更新动画参数，导致 Gate 切换帧的寄存器已经显示 1、属性输出仍为 0。

现在两层使用同一个不可变符号程序：当前寄存器、输入参数和初始化标记形成快照，Update 写出下一状态，Output 使用相同条件路径选出下一状态的 Motion。首帧 Entry 和后续转移使用同一机制。内部 DAG 节点复用，输出 Motion 保留原状态的 Speed、CycleOffset 和 Mirror 元数据。

`BuildStateRegisterUpdate` 与新增 `BuildStateRegisterOutput` 共享物化函数。生成预算包含两套决策分支、Entry 分支及播放设置包装。`BuildStateOutputTree` 保留读取现有寄存器的独立 API。

原生测试逐帧覆盖：

| 场景 | 每个用例的轨迹 |
| --- | --- |
| Bool / Int / Float，WD On 与覆盖完整的 WD Off | 120 帧，连续切换输入与阈值边界 |
| Override 重复绑定 | 120 帧，遍历上下层输入组合 |
| Transform Mask | 90 帧，包含和排除的路径同时检查 |
| 同时间基准的动态 Clip | 240 帧，多个循环 |
| AAP Entry 默认 / 条件入口 | 各 90 帧，从第一帧检查输出 |
| AAP Int 历史与前后层顺序 | 120 帧，输入回到 0 时保留状态 |
| AAP Float AnyState 重叠优先级 | 120 帧，验证随当前状态变化的选择 |
| 加、减、乘、A/(1+B) | 各 60 帧，对照数值公式 |
| 保存副本重新导入 | 60 帧，对照源 Controller |
| Undo/Redo 常规及 AAP | 完整资产数量 + 恢复后的 60 帧输出 |
| 平台 Bool/Int 层与已优化独立层 | IsLocal / Viseme 各 90 帧，原生参数类型保持不变 |
| RootExit、AnyState 与空 Motion 姿态历史 | 各 90 帧，初始场景姿态、状态输出和切回空状态后的保留姿态 |

## NDMF 完整虚拟流程

配置捕获记录实际源状态机身份，手动索引在处理前映射到 VirtualLayer。虚拟编译快照来自当前虚拟状态/条件/曲线，因而包含此前插件的编辑。

生成 Update 插在源层之前，原 VirtualLayer 继续作为 Output，保持引用身份和对应的 Mask/BlendMode。多个源层使用互不重名的寄存器。生成 Clip/BlendTree 复制到拥有独立 backing object 的虚拟节点；临时资产随后删除。

测试执行真实 AnimatorServices deactivate/final commit，持久化提交图，复制后重新导入，再运行原生 Animator。源 Controller 的 Bool 参数和原曲线保持原值；生成图使用修改后的虚拟曲线，输入关闭后保持目标状态。

NDMF 最低版本为 1.11：该版本提供所需原对象元数据。普通 Unity 核心和仅 SDK 入口分别保留独立程序集与依赖范围。

## Player 与性能

`ConfigurePlayerProbe` 为测试工程建立真实持久化 Controller 与场景对象，生成普通 Selector、AAP EntryOn/EntryOff，以及 8/32/64 层原始/优化后成对场景。独立 Linux Player 重新绑定 Animator，先写输入，再手动推进帧并检查输出；性能阶段逐个测量 Animator。

Editor 基准使用 8、32、64 层，先预热再采集 9 次样本，报告每次分析或 Animator.Update 的 median、p95、min、max。前后图使用相同曲线、对象和参数轨迹。功能检查在计时之前执行。

实际采样值、机器与执行命令集中在 [性能实测](Performance.md)；功能与集成执行记录在 [测试指南](Testing.md) 和 [原生结果](UnityNativeResults.json)。不同依赖范围中重复运行的用例分别记录。

## 参数、目标与资产契约

- 普通 Unity 要求声明本地参数；显式 VRChat 模式识别平台内建输入。Float 平台参数直接用于生成树；Bool/Int 平台参数继续由原生条件状态机读取，其它独立层仍可优化。Animator/Explicit 目标不因 SDK 安装而自动切换语义。
- Bool/Int 升级后调用方使用 SetFloat，分别维持 0/1 和整数输入。已有参数曲线或 Behaviour 写入的参数保持原类型与顺序。
- 无历史图合并到决策 BlendTree；历史图启用 StateRegister；包含时间、回调、Trigger、IK 或独立同步状态的图保留原生状态机执行。
- 动态 Clip 按时间基准合并，Mask 与 BlendMode 保持分组顺序。空层清理保留首层、AAP 和实际 Behaviour。
- Explicit Controller 由明确接收 Animator 使用；Playable 来自实际自定义层。SDK 生成副本在构建后恢复原引用，NDMF 使用按需服务生命周期。
- 离线复制按 localFileID 选定 Controller，Undo/Redo 包含生成子资产。用户场景与已有脚本 metadata 保持原内容；缺失的 Runtime 组件 metadata 按用户场景中的 GUID `a157d3d35699dd84abde9e5412bd7017` 恢复。

## 运行入口

普通 Unity 用离线窗口或核心 API；VRCSDK 使用构建回调；NDMF 使用虚拟图与最终提交。Runtime、核心 Editor、VRC、NDMF 和对应测试装配独立。

完整操作说明见 [包说明](../Assets/UnityBox/AnimatorOptimizer/README.md)，云 Editor、依赖锁和登录复用见 [环境流程](UnityEnvironment.md)。
