# Animator Optimizer 审查

分支 `feat/animator-optimizer` 已从旧基线 rebase 到 `master` 的 `e9f0b36`。
原提交 `c24813b` 保存在 `backup/animator-optimizer-before-rebase`，未改写远端。
场景来自该分支，未在源码审查中重写。

## 完成程度

这不是只有窗口的空壳：已有普通状态机分析/转换、静态与受限动态 Motion 合并、
参数升级、空资源清理、Mask/MMD、同构状态图、NDMF 虚拟处理、离线面板和 AAP API。
不过 AAP StateRegister 反馈时序仍属于未完成验证的实验后端，默认关闭；任意有
副作用/历史/时间语义的图没有通用等价转换，仍应保留原图并报告原因。

## 本轮修正

- 按上游 API 核实 NDMF 最低版本为 1.9：`PlatformFinish` 从 1.8 提供，
  `VirtualBlendTree.NormalizedBlendValues` 从 1.9 提供；参数更新改用该版本支持的 API。
  修正旧的 ≥1.5 依赖和编译条件；测试装配仅在启用测试条件时编译，NDMF 用例单独隔离。
- Bool 升级时同步处理保留图中的等值条件；保留图的 Int 等值判断不能直接留在
  Float 上，相关转换保守拒绝。普通与虚拟路径均检查这些约束。
- 原地转换补齐子资产 Undo 与脏标记，避免撤销或重新加载后参数和条件不一致。
- 配置捕获不再受禁用根组件阻断，开始新构建清理旧状态；外部、失效或歧义 Animator
  不再静默改用另一个目标。Explicit Controller 没有接收对象时明确失败。
- NDMF 的非 Playable 目标以实际 Animator 为上下文字典键；显式源与其当前 Controller 不一致时走物理回退，避免找不到或改错虚拟图。
- SDK fallback 对内存 Controller 建立独立、可持久化副本，复用状态图复制逻辑，
  隔离瞬态 Clip/Mask；跳过 `customizeAnimationLayers=false` 时残留的自定义层，
  避免优化器启用原本关闭的配置。构建清理恢复原引用和开关。
- NDMF 手动选层按原状态机身份映射到合并后的虚拟层；无法唯一定位时跳过，
  同时阻止 SDK 使用过期索引再次处理。手动选层的 AAP 构建路径暂不支持。
- AAP 状态寄存器拒绝 VRChat 内置参数名、与输入参数同名的寄存器和无效默认状态，
  在写入子资产前报告错误；数值/范围等原有校验继续保留。
- 生成目录及默认输出名使用跨平台单路径段，拒绝点目录和分隔符造成越界。

各项均补充针对性的 EditMode 回归。测试执行方式和验证边界见 [测试指南](Testing.md)。

## 尚未验证或不支持

- AAP StateRegister 的初始化、逐帧反馈、层顺序和实际成本未做 Unity/VRChat 实测。
- NDMF 虚拟 AAP 后端未实现；自动选层可走物理 fallback，手动选层在 NDMF 构建中
  会跳过并提示，因为跨阶段的层身份桥接尚未实现。交接仍需实机验证。
- 不能无损转换的 Trigger、未知 Behaviour、任意嵌套/历史/时间逻辑会保留原图。
- Float 近似、启发式、AlwaysOne 归一依赖显式假设，不是无条件等价保证。
- 老分支缺失的原始 `.meta` 需要从原项目恢复。旧场景也依赖未提交的 SDK/第三方资产。
- 云端没有 Unity，因此语法/打包成功不代表编译、回归、运行时等价性或性能通过。
