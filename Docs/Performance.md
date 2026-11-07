# UnityBox 性能实测

使用 Unity 2022.3.22f1、Linux x86-64，在隔离工程中执行。测量与生成物、Animator 输出检查在同一原生测试范围执行。原始数据、运行命令、源码指纹和日志 SHA256 见 [PerformanceResults.json](PerformanceResults.json)。

时间单位为毫秒/次操作；表中的 p95 使用采样序列的 nearest-rank 分位数。Editor 生成耗时包含各测试明确计时的生成/保存操作。ACC 和 Blendshape 的 Editor 更新基准逐帧切换参数；优化器的合并对照与 Player 更新基准使用固定输入。

`allocated_bytes_per_op = -1` 表示该 Unity Mono 分配计数 API 不提供此计数；`controller_memory_bytes = -1` 表示 Release Player 的对象内存 API 不提供该对象大小。Player 的 `mono_heap_bytes` 是所有场景完成后执行 GC 的整体托管堆存量。

| 插件 / 工作负载 | 预热 | 采样方式 |
| --- | --- | --- |
| AnimatorOptimizer 分析 | 3 次 | 9 组 × 1 次分析 |
| AnimatorOptimizer 层合并更新对照 | 测量函数 30 次 | 9 组 × 300 次更新；固定末态输入 |
| AnimatorOptimizer 自动保留 / 显式转换单层分组 | 共同预热 30 次，各测量函数再预热 30 次 | 9 组 × 300 次更新；先比较 60 帧切换输出，再固定末态输入 |
| ACC、Blendshape 持久化控制器生成 | 1 次 | 5 次生成，核对层、参数与 Clip 数 |
| ACC、Blendshape 参数切换更新 | 128 次 | 9 组 × 512 次更新 |
| AvatarSecuritySystem 手势判定图生成 | 1 次 | 7 次；按密码长度检查连续保持状态数、独立层与 WD Off |
| 组件引用工具只读扫描 | 1 次 | 5 次；128/1024/4096 组件，核对识别数量和原文件字节 |
| Viseme 映射解析 / 持久化控制器重建 | 2 次 | 21 组 × 25 次解析 / 7 次重建 |
| PrefabDiffPatcher 分析与应用 | 2 次 | 9 次；保存后核对 Light 值与 Variant 继承 |
| Collider 捕获 / 历史与预览缓存读取 | 2 次 | 9 次捕获或重算 / 9 组 × 10,000 次缓存读取 |
| Logger 回调、设置读取、trace 记录与 JSON 导出 | 2 次 | 9 组；每组操作次数见 JSON |
| ACC 2 张 256×256 图标 | 1 次 | 5 次；核对实际红绿像素、原材质与 layer |

## Editor 基准

运行 `20261107T134253-351a39f5`，范围 `serial`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 2 | 5 | 189.9691 | 193.0182 |
| AdvancedCostumeController / `animator_update` | 2 | 9 | 0.051298 | 0.060924 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 8 | 5 | 497.8241 | 595.8763 |
| AdvancedCostumeController / `animator_update` | 8 | 9 | 0.085778 | 0.115601 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 24 | 5 | 1364.2043 | 1958.624 |
| AdvancedCostumeController / `animator_update` | 24 | 9 | 0.099911 | 0.1248 |
| AnimatorOptimizer / `analyze_static_layers` | 8 | 9 | 2.6941 | 371.7655 |
| AnimatorOptimizer / `analyze_static_layers` | 32 | 9 | 11.515 | 36.7575 |
| AnimatorOptimizer / `analyze_static_layers` | 64 | 9 | 21.7422 | 30.0552 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 8 | 9 | 0.046661 | 0.070832 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 8 | 9 | 0.047297 | 0.057797 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 32 | 9 | 0.067218 | 0.09724 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 32 | 9 | 0.063457 | 0.079961 |
| AnimatorOptimizer / `animator_update_before_merge` | 8 | 9 | 0.04385 | 0.050012 |
| AnimatorOptimizer / `animator_update_after_merge` | 8 | 9 | 0.018201 | 0.01982 |
| AnimatorOptimizer / `animator_update_before_merge` | 32 | 9 | 0.073605 | 0.099875 |
| AnimatorOptimizer / `animator_update_after_merge` | 32 | 9 | 0.03291 | 0.060611 |
| AnimatorOptimizer / `animator_update_before_merge` | 64 | 9 | 0.136698 | 0.287055 |
| AnimatorOptimizer / `animator_update_after_merge` | 64 | 9 | 0.034534 | 0.036182 |
| AvatarColliderMonitor / `history_recompute_reference` | 100 | 9 | 0.2501 | 8.7667 |
| AvatarColliderMonitor / `history_cached_read` | 100 | 9 | 0.000014 | 0.000024 |
| AvatarColliderMonitor / `preview_cached_read` | 100 | 9 | 0.00005 | 0.00009 |
| AvatarColliderMonitor / `history_recompute_reference` | 1000 | 9 | 2.9587 | 9.039 |
| AvatarColliderMonitor / `history_cached_read` | 1000 | 9 | 0.000014 | 0.000023 |
| AvatarColliderMonitor / `preview_cached_read` | 1000 | 9 | 0.000019 | 0.000026 |
| AvatarColliderMonitor / `capture_3d_colliders` | 32 | 9 | 0.717 | 3.9168 |
| AvatarColliderMonitor / `capture_3d_colliders` | 256 | 9 | 5.2492 | 8.8679 |
| AvatarColliderMonitor / `capture_3d_colliders` | 1024 | 9 | 26.9177 | 38.8549 |
| AvatarSecuritySystem / `gesture_graph_generation` | 2 | 7 | 3.0446 | 4.0113 |
| AvatarSecuritySystem / `gesture_graph_generation` | 8 | 7 | 21.8497 | 31.412 |
| AvatarSecuritySystem / `gesture_graph_generation` | 32 | 7 | 365.9565 | 520.9445 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 4 | 5 | 108.6219 | 131.635 |
| BlendshapeControllerGenerator / `animator_update` | 4 | 9 | 0.039823 | 0.045675 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 16 | 5 | 304.3986 | 338.4519 |
| BlendshapeControllerGenerator / `animator_update` | 16 | 9 | 0.061437 | 0.088892 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 64 | 5 | 1428.1728 | 1627.8218 |
| BlendshapeControllerGenerator / `animator_update` | 64 | 9 | 0.149041 | 0.161697 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 1000 | 9 | 0.000218 | 0.000383 |
| BuildPipelineLogger / `disabled_shader_callback` | 1000 | 9 | 0.000004 | 0.000004 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 10000 | 9 | 0.000226 | 0.000253 |
| BuildPipelineLogger / `disabled_shader_callback` | 10000 | 9 | 0.000004 | 0.000004 |
| BuildPipelineLogger / `record_callback_trace` | 128 | 9 | 0.000216 | 0.001227 |
| BuildPipelineLogger / `export_callback_trace_json` | 128 | 9 | 0.2406 | 0.3494 |
| BuildPipelineLogger / `record_callback_trace` | 1024 | 9 | 0.000207 | 0.003085 |
| BuildPipelineLogger / `export_callback_trace_json` | 1024 | 9 | 2.0215 | 4.5714 |
| ComponentReferenceMigration / `scan_component_references` | 128 | 5 | 79.6564 | 101.9209 |
| ComponentReferenceMigration / `scan_component_references` | 1024 | 5 | 1036.0468 | 1772.0534 |
| ComponentReferenceMigration / `scan_component_references` | 4096 | 5 | 5275.2705 | 6183.1651 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 32 | 9 | 19.1321 | 40.587 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 256 | 9 | 185.851 | 330.4052 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 16 | 9 | 85.0126 | 161.0385 |
| PrefabDiffPatcher / `apply_prefab_variant` | 16 | 9 | 22.9854 | 33.4473 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 64 | 9 | 442.9328 | 944.957 |
| PrefabDiffPatcher / `apply_prefab_variant` | 64 | 9 | 41.5799 | 146.5634 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 1 | 7 | 180.9103 | 389.8523 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 5 | 7 | 312.8605 | 701.2679 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 15 | 7 | 289.9065 | 341.5167 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 15 | 21 | 0.100848 | 0.254808 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 150 | 21 | 0.05784 | 0.139628 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 1500 | 21 | 0.204084 | 0.349144 |

8 个独立单层分组：自动保留原图 0.046661 ms，显式转换 0.047297 ms，增加 1.4%。
32 个独立单层分组：自动保留原图 0.067218 ms，显式转换 0.063457 ms，减少 5.6%。
8 层合并：中位数 0.04385 → 0.018201 ms，减少 58.5%。
32 层合并：中位数 0.073605 → 0.03291 ms，减少 55.3%。
64 层合并：中位数 0.136698 → 0.034534 ms，减少 74.7%。
100 条 Collider 历史：完整重算中位数 0.2501 ms；读取已缓存的比较视图 0.000014 ms。
1000 条 Collider 历史：完整重算中位数 2.9587 ms；读取已缓存的比较视图 0.000014 ms。
Logger 每组 1000 次：直接读取 EditorPrefs 的单次中位数 0.000218 ms，缓存禁用回调 0.000004 ms。
Logger 每组 10000 次：直接读取 EditorPrefs 的单次中位数 0.000226 ms，缓存禁用回调 0.000004 ms。

## 图形输出

运行 `20261107T134054-ba1d65a2`，范围 `icons`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `menu_icon_rendering_256px` | 2 | 5 | 203.308 | 224.0537 |

图形设备：Renderer: llvmpipe (LLVM 19.1.7, 256 bits)；Vendor:   Mesa；Version:  4.5 (Core Profile) Mesa 25.0.7-2+deb13u1；OPENGL LOG: Creating OpenGL 4.5 graphics device ; Context level  <OpenGL 4.5> ; Context handle 1411866080

## Linux Player

Player 每个 Animator 先预热 50 次，再采样 11 组，每组 200 次手动更新。输入使用对应功能场景最后一步的状态；逐步切换的正确性在计时前执行。

运行 `20261107T134157-4d8c739e`，范围 `linux`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| StandalonePlayer / `Unity.PlayerSmoke/Basic` | 1 | 11 | 0.024244 | 0.034015 |
| StandalonePlayer / `Blendshape Float and inverted Bool across combined renderers/Blendshape` | 1 | 11 | 0.038055 | 0.081738 |
| StandalonePlayer / `ASS gesture hold, result, timeout restart and user animation preservation/ASS_StandaloneProbe` | 1 | 11 | 0.037084 | 0.068192 |
| StandalonePlayer / `ACC costume selection, Parts and shared Mixer/ACCShared` | 1 | 11 | 0.036011 | 0.09365 |
| StandalonePlayer / `ACC costume selection, Parts and independent Mixer/ACCIndependent` | 1 | 11 | 0.048984 | 0.10358 |
| StandalonePlayer / `Viseme15AndVoiceOff/VisemeProbeOff` | 1 | 11 | 0.041539 | 0.059375 |
| StandalonePlayer / `Viseme15AndVoiceOn/VisemeProbeOn` | 1 | 11 | 0.036097 | 0.058772 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Source` | 1 | 11 | 0.026995 | 0.033481 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Optimized` | 1 | 11 | 0.028657 | 0.052355 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Source_EntryOff` | 1 | 11 | 0.02884 | 0.062785 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Optimized_EntryOff` | 1 | 11 | 0.043535 | 0.07224 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Source_EntryOn` | 1 | 11 | 0.021337 | 0.037486 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Optimized_EntryOn` | 1 | 11 | 0.036777 | 0.136767 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Source_8` | 1 | 11 | 0.049148 | 0.062199 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Optimized_8` | 1 | 11 | 0.034117 | 0.052887 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Source_32` | 1 | 11 | 0.065452 | 0.156973 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Optimized_32` | 1 | 11 | 0.036442 | 0.053698 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Source_64` | 1 | 11 | 0.107361 | 0.127984 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Optimized_64` | 1 | 11 | 0.02221 | 0.029492 |

功能场景 13 个、输出检查 4807 项；完成后整体托管堆 1,654,784 bytes。

Linux 构建耗时 32.394475 秒，产物 58,547,694 bytes，0 个构建错误。

Player `AnimatorOptimizer/Bool selector`：1 → 1 层，2 → 2 个 Clip；中位数 0.026995 → 0.028657 ms，增加 6.2%。
Player `AnimatorOptimizer/AAP EntryOff`：1 → 2 层，2 → 10 个 Clip；中位数 0.02884 → 0.043535 ms，增加 51.0%。
Player `AnimatorOptimizer/AAP EntryOn`：1 → 2 层，2 → 10 个 Clip；中位数 0.021337 → 0.036777 ms，增加 72.4%。
Player `AnimatorOptimizer/layers 8`：8 → 1 层，8 → 1 个 Clip；中位数 0.049148 → 0.034117 ms，减少 30.6%。
Player `AnimatorOptimizer/layers 32`：32 → 1 层，32 → 1 个 Clip；中位数 0.065452 → 0.036442 ms，减少 44.3%。
Player `AnimatorOptimizer/layers 64`：64 → 1 层，64 → 1 个 Clip；中位数 0.107361 → 0.02221 ms，减少 79.3%。

本次测量中 `AnimatorOptimizer/Bool selector`、`AnimatorOptimizer/AAP EntryOff`、`AnimatorOptimizer/AAP EntryOn` 的稳态更新耗时增加。这些场景通过了原始/转换后逐步输入与输出检查；完整对照同时展示功能结果和运行成本，转换范围按控制器结构与运行状态选择。
