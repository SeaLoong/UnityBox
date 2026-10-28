# temp 变更的保留与修复

原临时提交 `75396e7 chore: TEMP` 保留于 `backup/temp-75396e7`，文件变更经 `bee33c6`
进入 `refactor`。原场景和实验二进制保留，当前源码继续在对应工作分支迭代并推送。
整体升级步骤见 [变更清单](RefactorChanges.md)。

## ACC 共享容器

原变更用 `BaseObject` 区分多套服装，解决 `OutfitObject` 去重带来的遗漏。
后续修复同时处理组根、组织祖先和菜单路径：

- `P/{A, A_variant, B}` 中，两套独立服装使用各自 Base；切换 B 时保持 P 的组织路径。
- `P/B` 与 `Q/B` 的菜单保留真实目录，各自拥有独立 Mixer 控制项。
- 当前预览的服装、变体和部件勾选进入生成模型，两种 Mixer 使用相同归属规则。
- 切换动画激活所需组织祖先，服装在初始关闭的父容器下也能正常显示。

`OutfitContainerRegressionTests` 的七个用例覆盖共享容器、变体归属、Mixer 目录与实际
动画采样。后续原生 Animator 场景进一步覆盖 Parts、共享/独立 Mixer 和生成结果重载。
本轮生成层显式独立求值，生成目录采用每次保留的 Build 与场景 Undo 事务。

## ASS 路径绑定实验归档

仓库保存的 `ass_path_probe` 与 `ass_path_probe_modified` 均为 **41,600 字节**，构建版本
Unity **2022.3.22f1**。两者只有 `ASS_Marker_OLD` → `ASS_Marker_NEW` 的 **14 字节**
等长替换；`PlainMarker` 是对照目标。历史运行日志记录：修改目标 scale 保持 1，对照目标
scale 到 2。字节偏移和具体构造保存在 [实验归档](../SESSION_HANDOFF_2026-10-24.md)。

探针菜单区分内存改名、原包和修改包，使用独立 `AssetBundleBuild[]`，生成时检查两个标记、
Controller 和对照动画；切换场景及清理生成资产支持保存/取消。
实验脚本和二进制留在源码归档，VPM 筛选器排除这些入口和固定 GUID fixture。

当前 ASS 的使用入口为完整普通 Unity 手势运行时与 Avatar 构建流程，见
[用户手册](../Assets/UnityBox/AvatarSecuritySystem/Editor/README.md)和
[技术参考](../Assets/UnityBox/AvatarSecuritySystem/Editor/ASS_TechnicalDoc.md)。

## 复现与结果

普通回归使用独立测试工程和真实依赖，执行命令见 [测试指南](Testing.md)。
测试框架记录实际程序集、非零执行数、输出断言、退出码和 NUnit XML；性能阶段独立串行
采样。当前各运行与内容指纹见 [验证结果](VerificationResults.json)。

研究探针通过 `Tools > UnityBox > ASS > Animation Path Binding Probe` 菜单重新生成
自己的实验资源，再分别运行 Original Bundle、Modified Bundle 和内存改名场景。
