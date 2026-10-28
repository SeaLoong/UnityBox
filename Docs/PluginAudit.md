# 全仓插件审查

`refactor` 覆盖七个插件的源码、UI、依赖、生成与构建入口、资产身份和 VPM 打包。
已修复确定缺陷，并拆分可独立使用的 Unity 核心与可选 SDK/MA/NDMF 适配。逐项改动、
升级后果和未解决边界见 [RefactorChanges](RefactorChanges.md)，程序集迁移见
[DependencyArchitecture](DependencyArchitecture.md)。Animator Optimizer 在独立 feature
分支，未合入本分支，也不能使用本报告的七包结果作为它的验证证据。

## 结论

| 插件 | 当前完成的工作 | 仍需实际验证/限制 |
| --- | --- | --- |
| ACC | 共享容器归属/祖先激活、Mixer 选择与分组、路径/参数/菜单碰撞、失败转换保护；完整 Unity 生成窗口与可选 MA 安装 | 图标、完整 Undo、压缩网络行为、MA 最终构建；旧输出可能要重新生成并检查参数保存值 |
| Blendshape | Bool 反向、Renderer 身份、菜单/参数冲突、改型残留、失败原引用保护；无 SDK 完整生成，可选 MA Controls | 多 Controller 合并、总参数预算、同绑定冲突；旧 Build 保留后需手工清理 |
| 口型 | 映射继承/None、Voice 阈值、FX 创建/持久化、虚拟路径、禁用组件；独立核心和真实 SDK/NDMF；SDK 按实际完成消费配置去重 | 游戏内输入、Write Defaults、后续优化顺序；OverrideController 回退明确不支持 |
| ASS | 图条件/连续保持/超时、空配置；按实际完成状态交接、两路径源资产/复制身份与失败保护；独立图 API、默认禁用主动负载 | 原始缺失 metadata、NDMF 最终提交、多 Avatar 生成目录、释放层后动态重锁；无资源所有权保证 |
| Prefab | 部分/批量选择、局部 Transform、嵌套/数组引用映射、输出 GUID/输入保护 | 第三方自定义序列化、复杂引用图、嵌套/Variant 继承不保证保留 |
| Collider | 真实冻结快照、不同根隔离、设置与历史上限；普通 GameObject 3D/2D 和可选 SDK provider | 是阶段采样，无 PhysBone 几何或 VRCFury feature 内部钩子 |
| Logger | 每次构建重置 shader 去重；普通 Unity 日志和独立平台回调 | callbackOrder 是观察位置；Linker 未启用，无第三方内部完整追踪 |

SDK/MA/NDMF 不再是所有包的强制安装依赖。缺少它们时普通核心仍可编译；ASS 的完整手势
运行行为和口型的输入来源仍由平台或调用者提供，没有伪造通用 Unity 平台服务。
Runtime 配置保留已知 GUID，并在真实 SDK Base 可用时保留其 `IEditorOnly` 标记。

## 资产与发布

现有 `.meta` 随包保留，不再在安装时丢掉仓库已知 GUID。Generated、ASS 实验探针、固定
GUID fixture 和平台 bundle 被排除，源码仓库仍保留实验；Editor 回归测试继续随包。
ZIP 覆盖重建避免旧删除文件残留。未知原始脚本 GUID 不能从当前文件内容推导，仍需从原
项目恢复；本轮新增的 Editor 文件元数据不声称恢复了以前未提交的身份。

ASS 主动 GPU 负载仅保留历史实现，不增强；新建/Reset 默认关闭，Play Mode 跳过。
已有序列化配置不会自动迁移。名称混淆、已保存参数与动画路径探针不能证明资源所有权、
通用防热上传或客户端稳定性。

## 验证结论

现有 Personal 账号登录 Hub 后已完成机器激活。2026-10-29，真实 Unity 2022.3.22f1
无 SDK 核心 **93/93**、仅SDK **150/150**、SDK 3.8.2 + MA 1.18 + NDMF 1.11
完整范围 **179/179**
全部通过，零跳过、零失败、exit 0；完整范围 14 个预期测试程序集均发现。Prefab 的原生
组件覆写、子树命名和引用误报问题也在这次执行中暴露并修复，具体后果见变更清单。

真实 Unity 导入、编译与这些 EditMode 行为已有通过证据；它不证明所有第三方插件组合、
Avatar 最终提交/上传或游戏内逐帧行为都已验证。此前 Roslyn 核心19、仅SDK40、完整55个
程序集的类型检查是历史补充证据，不能替代本次原生执行。共享工具25项 Python 测试和
5项打包测试属于其它验证层次。

新增源码和交接修复的最终证据、仅SDK范围、用例清单及可执行命令见
[Testing](Testing.md)；环境安装、锁校验、合法激活和复用流程见
[UnityEnvironment](UnityEnvironment.md)。
