# temp 分支审查与修正

> 历史审查记录：下述提交操作发生于最初的 temp 审查。保留的变更及后续修复现位于 `refactor`，当前总览见 [插件审查](PluginAudit.md) 和 [测试指南](Testing.md)。

## 提交处理

已在本地 `temp` 撤掉 `75396e7 chore: TEMP` 的提交记录，保留其全部文件变更。
原提交可通过 `backup/temp-75396e7` 恢复；未改写或推送远端。
原有场景、实验二进制及 ASS changelog 保持原样。

## ACC：问题真实，原修复不完整

原改动针对多个独立服装共用容器时，菜单误嵌套、参数路径冲突以及 Mixer
按 `OutfitObject` 去重而漏掉服装的问题。改用 `BaseObject` 标识服装是合理的。
但原修复有两个遗漏：

- `P/{A, A_variant, B}` 中 A 有变体，A 的组根是 P，B 的组根是 B。
  两个组根不相等，所以原规范化不会处理它们。选 B 的动画会关闭 P，
  即使 B 自身开启也不可见。
- 菜单路径删掉其他服装组根后，`P/B` 与 `Q/B` 都变成 `B`，Mixer 复用同一个
  子菜单，将两套服装混在一起。只修菜单外观不能修正对象控制归属。

本次修正：容器内只要包含其他独立服装的 Base，就回退到当前 Base 作为组根；
正常单 Base 加变体的专属容器保持不变。菜单保留真实组织路径。
普通切换、Mixer 入口、共享变体及独立 Mixer 激活动画会启用所需组织祖先，
避免初始关闭的父容器继续隐藏选中的服装；不控制 CostumesRoot 或 Avatar 根。

新增 `OutfitContainerRegressionTests` 共 7 个 EditMode 测试用例，覆盖共享组根、
祖先冲突、正常变体分组、两种 Mixer 参数模式的目录隔离，以及父容器初始
开启/关闭时生成曲线和实际 `SampleAnimation` 后的可见性。

## ASS：实验探针，不是生产防护实现

提供的两个 41,600 字节 bundle（Unity 2022.3.22f1）之间，仅有
`ASS_Marker_OLD` 到 `ASS_Marker_NEW` 的等长替换，其他字节相同。
这验证了 fixture 的改动范围；动画是否断绑仍须在 Unity 实际加载后观察。
该实验不能证明精准编辑会失败、所有热上传工具都受影响，或平台会拒绝上传。

本次修正实验代码的三个问题：

- 切换场景或删除生成资产之前提示保存，允许取消。
- 显式区分内存改名、原包、修改包三种模式，检查两个标记、控制器及对照动画；
  不再把任意未驱动结果误报为实验成功。
- 使用显式 `AssetBundleBuild[]`，只构建探针，不构建项目其他带标签的 bundle。

现存实验资产没有对应的已提交 `.meta`，不能依赖其固定 GUID 直接导入复现；
应从菜单重新生成。实验脚本和二进制仍保留在源码仓库；后续 refactor 的打包筛选器已将它们和固定 GUID
fixture 排除，正式 VPM 包保留 Editor 回归测试，不再包含这些实验入口。

## 验证状态与本地复测

- 通过：C# 语法解析、7 个 VPM 包的现有 CI 打包流程、ZIP 完整性/内容及 SHA256。
- 通过：二进制替换范围检查、用户场景/changelog 与原 temp 提交逐字节一致检查。
- 未执行：Unity 编译、7 个 EditMode 用例、三种探针运行及 VRChat 构建/游戏内验证。
  上述历史检查时尚无 Editor；现已安装 2022.3.22f1，但实际启动因无许可证失败。
  后续真实依赖类型检查见 [测试指南](Testing.md)，仍不代表 Unity 行为测试通过。

在已有 VRChat/Modular Avatar 的 Unity 2022.3 项目中，安装 Unity Test Framework，
在 Test Runner 的 EditMode 中运行 `OutfitContainerRegressionTests`。若按 VPM 包安装，
需将 `top.sealoong.unitybox.advanced-costume-controller` 加入项目 manifest 的
`testables`。然后依次运行 Tools > UnityBox > ASS > Animation Path Binding Probe
下的 Original Bundle、Modified Bundle、Create & Run，确认各自 PASS。
旧内存探针场景缺少对照标记，必须从菜单重新生成，不能直接沿用。
