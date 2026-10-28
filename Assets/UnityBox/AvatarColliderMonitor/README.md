# Avatar Collider Monitor

记录并比较根 GameObject 下的 Unity `Collider` / `Collider2D`，帮助定位场景和构建中的变化。
需要 Unity 2022.3；独立 Unity 项目不需要 VRChat SDK、Modular Avatar 或 NDMF。
VPM 包名：`top.sealoong.unitybox.avatar-collider-monitor`。

## 使用

打开 `Tools > UnityBox > Avatar Collider Monitor`，在 **Root Object** 选择要检查的
GameObject。预览包含根对象及其所有子对象（包括未激活对象）的 Unity 碰撞体。
点击 **Take Snapshot** 手动采样；对象旁的箭头可定位关联 Transform。

| 设置 | 作用与默认值 |
| --- | --- |
| Enable Monitoring | 启用自动采样；默认关闭，关闭时仍可手动采样 |
| Log Changes | 同一根对象的相邻样本有变化时输出日志；默认开启 |
| Verbose Logging | 输出各采样点日志；默认关闭 |
| Show Only Changes | 在历史视图中过滤没有变化的条目 |

设置使用原有 EditorPrefs 键保存，切换开关无需重载脚本。历史保留最近 1000 条记录，
**Clear** 清除历史。历史保存在编辑器静态内存中；脚本重载会清空记录，没有磁盘导出
或跨会话历史恢复。窗口和包名保留 `Avatar Collider Monitor` 以兼容原来的使用入口。

## 捕获和比较

Unity 碰撞体按组件实例区分，同一 Transform 上的多个组件不会互相覆盖。快照冻结
组件序列化配置、世界变换矩阵、激活状态、Layer、Transform 身份和层级路径。
比较能检测配置、对象状态、路径的变化，以及碰撞体组件的添加和移除；对象后续改名
或销毁不会改写历史。窗口展示路径和变化标记，完整配置保存在快照数据中。

历史仅与同一个根 GameObject 实例的上一条样本比较。构建副本和源场景对象是不同
实例，不会被强行当作同一对象比较。开启监控后，Unity 的 `IProcessSceneWithReport`
回调为正在处理的每个场景根对象记录样本；手动采样不依赖构建回调。

## 可选集成

| 安装内容 | 增加的功能 | 程序集 |
| --- | --- | --- |
| 仅 Unity | Unity 碰撞体预览、手动历史比较、场景处理采样 | `UnityBox.AvatarColliderMonitor` |
| VRChat Avatars SDK（使用 >= 3.7.0） | 根对象 Descriptor 的 `collider_*` 配置、Avatar 预处理早/晚观察点 | `UnityBox.AvatarColliderMonitor.VRC` |
| NDMF | Resolving / Generating / Transforming / Optimizing 各一个观察点 | `UnityBox.AvatarColliderMonitor.NDMF` |

集成程序集分别按 `com.vrchat.avatars` / `nadena.dev.ndmf` 包的存在启用；未安装时
核心照常工作。VRC provider 把 Descriptor 的 `ColliderConfig` 加到相同快照，使用
`VRC/` 键区分 Unity 组件。NDMF 集成只使用根 GameObject，不直接依赖 VRC 类型。
代码扩展可订阅 `AvatarColliderMonitor.CollectAdditionalColliders` 添加独立配置。
原 `RecordSnapshot` 调用仍接受 Component（包括 Descriptor），也接受 GameObject。

## 边界

- 这是采样诊断工具，不进行持续逐帧记录，也不修改、恢复或绘制碰撞体几何。
  不会自动采集 PhysBoneCollider 等其它 SDK 自定义组件。
- NDMF 观察点不是所有插件的全局阶段开始/结束；SDK 同 order 的回调也没有绝对
  前后顺序保证。采样之间发生又恢复的瞬时变化可能无法被记录。
- 没有 VRCFury 内部每个 feature 的可靠事件钩子；场景存在组件不表示构建发生。
- SDK 后处理没有传入构建对象，所以不从场景中随意选一个对象伪造构建后快照。
  单凭差异不能确定哪个第三方插件造成变化。外部资源本身的内容变化也不等于
  碰撞体组件序列化配置发生变化。

## 单元测试

安装 Unity Test Framework，在 Test Runner 的 **EditMode** 运行：

- `UnityBox.AvatarColliderMonitor.Tests`：只需要 Unity，覆盖 3D/2D 碰撞体、未激活
  子对象、多组件添加/移除、配置与状态变化、冻结历史、跨根对象隔离、手动采样和扩展接口。
- `UnityBox.AvatarColliderMonitor.VRC.Tests`：额外需要真实 VRChat Avatars SDK，覆盖
  Descriptor 配置、SDK 自动采样开关及历史隔离；缺少 SDK 时该程序集不会编译。

通过 VPM/UPM 安装时，将本包名追加到 `Packages/manifest.json` 的 `testables`。
源码位于 `Tests/Editor/ColliderSnapshotTests.cs` 和 `Tests/Editor/VRC/`。
这些测试不能代替真实 SDK/NDMF 构建顺序验证；本次改动的 Unity 执行结果须以实际
Test Runner 报告为准，源码和配置检查不代表测试通过。
