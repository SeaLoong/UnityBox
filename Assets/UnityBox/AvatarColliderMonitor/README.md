# Avatar Collider Monitor

记录并比较 `VRCAvatarDescriptor.collider_*` 配置，帮助定位 Avatar 构建前后的变化。
需要 Unity 2022.3 和 VRChat Avatars SDK >= 3.7.0；NDMF 是可选的独立 Editor 装配。
VPM 包名：`top.sealoong.unitybox.avatar-collider-monitor`。

## 使用

打开 `Tools > UnityBox > Avatar Collider Monitor`，选择带 Descriptor 的 Avatar。
当前配置面板可查看字段和关联对象，点击 **Take Snapshot** 手动采样。

| 设置 | 作用与默认值 |
| --- | --- |
| Enable Monitoring | 启用自动构建采样；默认关闭，关闭时仍可手动采样 |
| Log Changes | 同一 Avatar 的相邻样本有变化时输出日志；默认开启 |
| Verbose Logging | 输出各采样点日志；默认关闭 |
| Show Only Changes | 在历史视图中过滤没有变化的条目 |

设置使用 EditorPrefs 保存，实际采样时读取，切换开关无需重载脚本。历史保留最近
1000 条记录，**Clear** 清除历史。历史保存在编辑器静态内存中，脚本重载或关闭编辑器
后不保证保留，工具没有磁盘导出或跨会话历史恢复。

## 记录范围

每条记录保存捕获时的 Avatar 身份、字段对应路径、Transform 身份和 ColliderConfig
配置值。对象后续改名或销毁不会改写历史。仅与同一个 Avatar 实例的上一条样本比较，
半径等配置改变也可检测；展开记录可查看当时配置。

SDK 路径在 Avatar 预处理的早、晚 callbackOrder 位置采样，使用回调传入的实际构建
对象。安装 NDMF 后，还会在 Resolving / Generating / Transforming / Optimizing
各阶段执行一个观察点。SDK 构建副本与源场景 Avatar 是不同实例，其历史不会被强行
当作同一个对象比较。

## 边界

- 只监视 Descriptor 的 `ColliderConfig` 字段，不包含任意 PhysBoneCollider 的场景
  几何绘制，不会修改或自动恢复碰撞体。
- NDMF 观察点不是所有插件的全局阶段开始/结束；SDK 同 order 的回调也没有绝对
  前后顺序保证。采样之间发生又恢复的瞬时变化可能无法被记录。
- 没有 VRCFury 内部每个 feature 的可靠事件钩子；场景存在 VRCFury 组件不表示构建发生。
- SDK 后处理没有传入构建对象，所以不从场景中随意选一个 Avatar 伪造“构建后”快照。
  单凭差异记录不能确定哪个第三方插件造成了变化。

## 单元测试与验证状态

安装 Unity Test Framework，在 Test Runner 的 **EditMode** 运行
`UnityBox.AvatarColliderMonitor.Tests.ColliderSnapshotTests`。
通过 VPM 安装时，将本包名追加到项目 `Packages/manifest.json` 的 `testables`。

`Tests/Editor/ColliderSnapshotTests.cs` 检查历史路径在改名/销毁后保留、原 Transform
上的配置变化、跨 Avatar 历史隔离，以及关闭自动监控时仍可手动采样。
当前云环境没有执行 Unity 编译或测试。实际项目还需连续构建、切换 Avatar，并对照
SDK/NDMF 的真实回调顺序验证采样结果。
