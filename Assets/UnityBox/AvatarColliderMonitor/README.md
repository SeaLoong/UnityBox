# Avatar Collider Monitor

捕获根 GameObject 下的 Unity 3D/2D 碰撞体，比较阶段变化、查看世界包围盒，并保存历史记录。
需要 Unity 2022.3；普通 Unity 项目可以直接使用。VPM 包名：
`top.sealoong.unitybox.avatar-collider-monitor`。

## 使用

打开 `Tools > UnityBox > Avatar Collider Monitor`，在 **Root Object** 选择根对象。
预览包括根对象及未激活子对象中的 `Collider` / `Collider2D`。点击箭头定位 Transform，
点击 **Take Snapshot** 手动采样；**Refresh** 立即更新预览。

| 设置或操作 | 行为 |
| --- | --- |
| Enable Monitoring | 开启 Unity 构建及已安装平台适配的自动采样；默认关闭 |
| Log Changes | 输出同一根对象相邻样本的变化；默认开启 |
| Verbose Logging | 输出采样阶段名称；默认关闭 |
| Scene Bounds | 在 Scene View 用青色线框显示 Unity 报告的世界包围盒 |
| Show Only Changes / Search | 按变化标记和阶段名称过滤历史 |
| Export JSON / Import JSON | 保存或恢复冻结的历史数据 |
| Clear | 清除当前历史 |

预览在对象切换、层级/Undo 变化、手动刷新时重新采样，并以 0.5 秒间隔更新组件属性。
IMGUI 的重复 Layout/Repaint 复用同一个预览快照。历史差异和统计在记录变化后计算一次，
后续读取复用缓存；滚动窗口直接使用缓存的键、变化集合和计数。

## 捕获内容

每个碰撞体按组件实例区分。同一 Transform 上的多个组件各自记录序列化配置、世界矩阵、
激活状态、Layer、Transform 身份、层级路径和世界包围盒。Unity 报告的包围盒反映当前
物理组件状态；禁用或未激活碰撞体的几何配置仍保存在组件快照中。

比较覆盖配置/变换/状态变化，以及组件增加或移除。对象之后改名或销毁，历史中的路径与
配置保持捕获时的值。记录始终与同一 Editor 会话、同一根实例的上一条样本比较；交错采样
多个根对象时分别形成历史。

历史保存最近 1000 条记录。JSON 保留时间、阶段、路径、配置、包围盒、身份和会话标记；
导入恢复冻结数据，导入条目与实时对象连接分开。不同 Editor 会话的实例编号即使相同，也
分别形成历史。代码可使用 `ExportHistoryJson`、`ImportHistoryJson(json, append)`；
`append=true` 合并记录后仍保留最新 1000 条。

## 可选适配与 API

| 环境 | 采样内容 | 程序集 |
| --- | --- | --- |
| Unity | 3D/2D 碰撞体、手动记录、场景处理回调 | `UnityBox.AvatarColliderMonitor` |
| VRChat Avatars SDK ≥3.7 | Descriptor 的 `collider_*`、PhysBoneCollider 配置与根变换、早/晚 Avatar preprocess 采样 | `.VRC` |
| NDMF | Resolving、Generating、Transforming、Optimizing 阶段采样 | `.NDMF` |

各阶段记录当前回调收到的根对象。SDK 配置使用 `VRC/` 键，与 `Unity/` 组件键并列显示。PhysBoneCollider 在普通根对象上也可
采样，包含未激活/禁用组件，冻结半径、高度等序列化配置及解析后的 rootTransform 世界矩阵。
扩展 provider 可订阅 `CollectAdditionalColliders`，使用 `snapshot.Add` 添加自己的冻结配置。
`RecordSnapshot` 接受 GameObject 或 Component。`CaptureSnapshot` 返回独立样本，
`GetHistoryComparisons` 和 `GetChangeStatistics` 返回当前历史的缓存视图。

## 测试与性能

核心程序集 `UnityBox.AvatarColliderMonitor.Tests` 覆盖 3D/2D、未激活对象、多组件、路径冻结、
配置/包围盒变化、跨根与跨会话隔离、JSON 往返、1000 条历史上限、预览/统计缓存。
`.VRC.Tests` 覆盖真实 Descriptor provider 和平台采样。

`UnityBoxPerformance` 类别测量 32/256/1024 个碰撞体捕获，以及 100/1000 条记录的完整
历史重算与缓存读取对照。每个场景预热后采样 9 次，缓存读取使用批量操作记录每次耗时和
分配量。实际数据及命令见[性能报告](../../../Docs/Performance.md)，运行回归见
[测试指南](../../../Docs/Testing.md)。UPM/VPM 安装时将本包名加入 manifest 的 `testables`。

本包在 Unity 2022.3.22f1 的执行记录与程序集结果统一保存在
[验证结果](../../../Docs/VerificationResults.json)，性能场景由独立运行采样。
