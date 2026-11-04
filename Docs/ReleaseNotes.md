# 2026-11-05 主分支更新

八个插件统一维护于 `master`。本次合并包含原 `refactor` 的七插件实现、
`feat/animator-optimizer` 的优化器，以及合并后的功能修复、测试和文档。
当前执行结果见[验证记录](VerificationResults.json)，全部测量见[性能报告](Performance.md)。

## 包版本

本轮按各包 `package.json` 独立升级版本，供 VCC/ALCOM 识别更新：

| 插件 | 原版本 | 新版本 |
| --- | --- | --- |
| Advanced Costume Controller | 0.4.13 | 0.5.0 |
| Animator Optimizer | 0.2.0 | 0.3.0 |
| Avatar Collider Monitor | 0.1.0 | 0.2.0 |
| Avatar Security System | 0.7.24 | 0.8.0 |
| Blendshape Controller Generator | 0.1.1 | 0.2.0 |
| Build Pipeline Logger | 0.1.0 | 0.2.0 |
| Prefab Diff Patcher | 0.1.1 | 0.2.0 |
| Viseme Blendshape Override | 0.1.14 | 0.2.0 |

Unity 核心与可选 SDK/MA/NDMF 集成通过各自程序集启用。外部 Editor 扩展若引用旧程序集，
按[程序集与依赖映射](DependencyArchitecture.md)调整引用。现有脚本 GUID、序列化字段和
用户场景按原身份保留；旧 Voice 映射由显式序列化迁移处理。

## 本轮功能改进

- **Animator Optimizer**：自动推荐按真实 Mask、BlendMode 与状态标签分组，只有至少两个
  来源层的普通合并组才进入默认多层推荐。不同分组的单层保持原状态机和参数类型；
  手动明确转换和显式启用的 AAP 继续提供。NDMF 使用相同选择规则，并保留覆盖顺序约束。
- **Advanced Costume Controller**：整 Avatar 参数检查同时核对预算与同名类型冲突。
  失败时撤销本次场景安装，继续使用此前的 Controller、菜单和资产。
- **Blendshape Controller Generator**：多个 Renderer 共用一次生成事务；后续 Renderer
  失败或用户取消时，整批恢复旧安装与 Controller 引用，清理本批新 Build。
  成功后一次 Undo/Redo 对应整个批次。
- **Viseme Blendshape Override**：重生成从旧生成层的实际引用图回收资产，排除其它层仍
  使用的 Motion。共享 Clip/BlendTree 和仅名称相似的用户资产保留，重复生成的输出稳定。
- **Avatar Security System**：运行时只在参数赋值或本地输入判断需要时解析参数类型。
  音频动作支持根节点 AudioSource 的空相对路径，也继续支持子节点路径。
- **Prefab Diff Patcher**：输出路径同时避开 A/B 本身、A 的 Variant 祖先及嵌套来源，
  在写入前处理冲突，保持来源资产内容和 GUID。
- **Avatar Collider Monitor**：历史 JSON 完整解析成功后一次提交；后段数据错误时原历史、
  导出结果和比较缓存保持一致，追加和替换导入使用同一事务边界。
- **Build Pipeline Logger**：Player 准备阶段和 preprocess 使用同一次构建轨迹，
  LastCompleted 保留完整准备、场景、Linker 和完成顺序；下一次构建重新建立轨迹。

各包手册说明具体入口、配置、API 与迁移步骤；[变更与升级影响](RefactorChanges.md)
同时记录之前分支迭代的生成层、独立 Unity 工作流、Undo/Redo 和资源持久化改进。

## 场景和源码历史

`Assets/UnityBox.unity` 保留用户后来上传的 temp/refactor 场景原字节，来源提交
`409eb5b4fe6400a7306de9535a3be0dcc835fa90`，大小 2,924,530 bytes，SHA256 为
`97bd78f2791abea3c101fe99b4cba3becc301f0e1cf7cfe7f9cbb85755baa202`。
旧优化器分支的示例场景仍保留在 `53fa22fe453bb683928e88c1ce1aa4b83a6a6e9a` 历史中。
合并不混合两份场景的对象 ID，也不重写用户配置。

## 安装和验证

更新已有项目时先在 VCC/ALCOM 查看对应的新包版本，更新后按所需工作流重新生成输出；
此前生成资产继续供场景引用与 Undo 使用。程序集依赖、参数类型与生成路径的变化见
对应手册及升级说明。

仓库脚本支持纯 Unity、仅 SDK、完整 MA/NDMF、图标像素、真实 Linux Player 和串行
性能验证。逐帧输出、保存重载、Undo/Redo、失败回滚与源资产保留均由实际用例检查。
不同依赖范围重复执行核心用例，测试数分别记录。

向 `master` 推送触发 VPM 构建与 Pages 工作流。发布按各包清单版本生成归档；
历史版本和已发布的 ZIP 保持可下载，同版本的实际内容改变需要先升级版本。
新的 ZIP 使用确定的排序、时间戳和权限规则；相同内容可重复得到相同 SHA256。
旧 ZIP 经内容与 SHA256 核对后沿用原归档，保留既有版本身份。
