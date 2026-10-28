# UnityBox 性能实测

使用 Unity 2022.3.22f1、Linux x86-64，在隔离工程中执行。测量与生成物、Animator 输出检查在同一原生测试范围执行。原始数据、运行命令、源码指纹和日志 SHA256 见 [PerformanceResults.json](PerformanceResults.json)。

时间单位为毫秒/次操作；表中的 p95 使用采样序列的 nearest-rank 分位数。Editor 生成耗时包含各测试明确计时的生成/保存操作。ACC 和 Blendshape 的 Editor 更新基准逐帧切换参数；优化器的合并对照与 Player 更新基准使用固定输入。

`allocated_bytes_per_op = -1` 表示该 Unity Mono 分配计数 API 不提供此计数；`controller_memory_bytes = -1` 表示 Release Player 的对象内存 API 不提供该对象大小。Player 的 `mono_heap_bytes` 是所有场景完成后执行 GC 的整体托管堆存量。

| 插件 / 工作负载 | 预热 | 采样方式 |
| --- | --- | --- |
| AnimatorOptimizer 分析 / 更新合并对照 | 3 次 / 30 次 | 9 组 × 1 次分析 / 300 次更新 |
| ACC、Blendshape 持久化控制器生成 / 参数切换更新 | 1 次 / 128 次 | 5 次生成 / 9 组 × 512 次更新 |
| AvatarSecuritySystem 锁定图生成 | 1 次 | 7 次；检查每节点 3 条缩放曲线 |
| Viseme 映射解析 / 持久化控制器重建 | 2 次 | 21 组 × 25 次解析 / 7 次重建 |
| PrefabDiffPatcher 分析与应用 | 2 次 | 9 次；保存后核对 Light 值与 Variant 继承 |
| Collider 捕获 / 历史与预览缓存读取 | 2 次 | 9 次捕获或重算 / 9 组 × 10,000 次缓存读取 |
| Logger 回调、设置读取、trace 记录与 JSON 导出 | 2 次 | 9 组；每组操作次数见 JSON |
| ACC 2 张 256×256 图标 | 1 次 | 5 次；核对实际红绿像素、原材质与 layer |

## Editor 基准

运行 `20261028T191629-258ea478`，范围 `benchmark`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 2 | 5 | 208.6296 | 230.4865 |
| AdvancedCostumeController / `animator_update` | 2 | 9 | 0.038133 | 0.043126 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 8 | 5 | 424.6678 | 557.7646 |
| AdvancedCostumeController / `animator_update` | 8 | 9 | 0.058289 | 0.060386 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 24 | 5 | 1340.5494 | 1409.2049 |
| AdvancedCostumeController / `animator_update` | 24 | 9 | 0.125481 | 0.131369 |
| AnimatorOptimizer / `analyze_static_layers` | 8 | 9 | 1.9215 | 4.5253 |
| AnimatorOptimizer / `analyze_static_layers` | 32 | 9 | 9.5663 | 10.5825 |
| AnimatorOptimizer / `analyze_static_layers` | 64 | 9 | 17.2026 | 22.4229 |
| AnimatorOptimizer / `animator_update_before_merge` | 8 | 9 | 0.034344 | 0.037723 |
| AnimatorOptimizer / `animator_update_after_merge` | 8 | 9 | 0.017508 | 0.018152 |
| AnimatorOptimizer / `animator_update_before_merge` | 32 | 9 | 0.060246 | 0.064738 |
| AnimatorOptimizer / `animator_update_after_merge` | 32 | 9 | 0.018719 | 0.020458 |
| AnimatorOptimizer / `animator_update_before_merge` | 64 | 9 | 0.111149 | 0.154134 |
| AnimatorOptimizer / `animator_update_after_merge` | 64 | 9 | 0.019445 | 0.02282 |
| AvatarColliderMonitor / `history_recompute_reference` | 100 | 9 | 0.2452 | 0.2652 |
| AvatarColliderMonitor / `history_cached_read` | 100 | 9 | 0.000014 | 0.00002 |
| AvatarColliderMonitor / `preview_cached_read` | 100 | 9 | 0.000019 | 0.000028 |
| AvatarColliderMonitor / `history_recompute_reference` | 1000 | 9 | 3.9628 | 8.4136 |
| AvatarColliderMonitor / `history_cached_read` | 1000 | 9 | 0.000014 | 0.00002 |
| AvatarColliderMonitor / `preview_cached_read` | 1000 | 9 | 0.000019 | 0.000029 |
| AvatarColliderMonitor / `capture_3d_colliders` | 32 | 9 | 0.6445 | 0.8232 |
| AvatarColliderMonitor / `capture_3d_colliders` | 256 | 9 | 5.0698 | 10.5657 |
| AvatarColliderMonitor / `capture_3d_colliders` | 1024 | 9 | 25.1942 | 40.6621 |
| AvatarSecuritySystem / `lock_graph_generation` | 16 | 7 | 1.288 | 2.2795 |
| AvatarSecuritySystem / `lock_graph_generation` | 64 | 7 | 2.3205 | 2.409 |
| AvatarSecuritySystem / `lock_graph_generation` | 256 | 7 | 8.1131 | 11.2543 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 4 | 5 | 103.6576 | 120.7609 |
| BlendshapeControllerGenerator / `animator_update` | 4 | 9 | 0.036015 | 0.03887 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 16 | 5 | 249.2061 | 273.0321 |
| BlendshapeControllerGenerator / `animator_update` | 16 | 9 | 0.055059 | 0.062308 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 64 | 5 | 909.1896 | 1031.211 |
| BlendshapeControllerGenerator / `animator_update` | 64 | 9 | 0.129543 | 0.230313 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 1000 | 9 | 0.000224 | 0.000289 |
| BuildPipelineLogger / `disabled_shader_callback` | 1000 | 9 | 0.000007 | 0.000055 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 10000 | 9 | 0.000245 | 0.000258 |
| BuildPipelineLogger / `disabled_shader_callback` | 10000 | 9 | 0.000004 | 0.000004 |
| BuildPipelineLogger / `record_callback_trace` | 128 | 9 | 0.000198 | 0.000327 |
| BuildPipelineLogger / `export_callback_trace_json` | 128 | 9 | 0.2537 | 0.3023 |
| BuildPipelineLogger / `record_callback_trace` | 1024 | 9 | 0.000172 | 0.000228 |
| BuildPipelineLogger / `export_callback_trace_json` | 1024 | 9 | 2.9144 | 7.1381 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 32 | 9 | 13.948 | 17.8302 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 256 | 9 | 121.4562 | 162.1646 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 16 | 9 | 22.5351 | 35.8766 |
| PrefabDiffPatcher / `apply_prefab_variant` | 16 | 9 | 8.7247 | 13.6941 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 64 | 9 | 75.6949 | 124.8431 |
| PrefabDiffPatcher / `apply_prefab_variant` | 64 | 9 | 14.2765 | 25.8296 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 1 | 7 | 25.2644 | 30.4189 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 5 | 7 | 30.2765 | 35.3206 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 15 | 7 | 35.6087 | 37.3383 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 15 | 21 | 0.01224 | 0.017852 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 150 | 21 | 0.01918 | 0.022164 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 1500 | 21 | 0.087448 | 0.097676 |

8 层合并：中位数 0.034344 → 0.017508 ms，减少 49.0%。
32 层合并：中位数 0.060246 → 0.018719 ms，减少 68.9%。
64 层合并：中位数 0.111149 → 0.019445 ms，减少 82.5%。
100 条 Collider 历史：完整重算中位数 0.2452 ms；读取已缓存的比较视图 0.000014 ms。
1000 条 Collider 历史：完整重算中位数 3.9628 ms；读取已缓存的比较视图 0.000014 ms。
Logger 每组 1000 次：直接读取 EditorPrefs 的单次中位数 0.000224 ms，缓存禁用回调 0.000007 ms。
Logger 每组 10000 次：直接读取 EditorPrefs 的单次中位数 0.000245 ms，缓存禁用回调 0.000004 ms。

## 图形输出

运行 `20261028T191046-9b9c02dc`，范围 `graphics`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `menu_icon_rendering_256px` | 2 | 5 | 174.3131 | 223.1427 |

图形设备：Renderer: llvmpipe (LLVM 19.1.7, 256 bits)；Vendor:   Mesa；Version:  4.5 (Core Profile) Mesa 25.0.7-2+deb13u1；OPENGL LOG: Creating OpenGL 4.5 graphics device ; Context level  <OpenGL 4.5> ; Context handle 44760448

## Linux Player

Player 每个 Animator 先预热 50 次，再采样 11 组，每组 200 次手动更新。输入使用对应功能场景最后一步的状态；逐步切换的正确性在计时前执行。

运行 `20261028T191509-16a49ce8`，范围 `linux`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| StandalonePlayer / `Unity.PlayerSmoke/Basic` | 1 | 11 | 0.041746 | 0.059526 |
| StandalonePlayer / `Blendshape Float and inverted Bool across combined renderers/Blendshape` | 1 | 11 | 0.042687 | 0.060263 |
| StandalonePlayer / `ASS gesture hold, unlock, released-layer rearm, relock and second unlock/ASS_StandaloneProbe` | 1 | 11 | 0.04343 | 0.057046 |
| StandalonePlayer / `ACC costume selection, Parts and shared Mixer/ACCShared` | 1 | 11 | 0.10115 | 0.265485 |
| StandalonePlayer / `ACC costume selection, Parts and independent Mixer/ACCIndependent` | 1 | 11 | 0.037584 | 0.053138 |
| StandalonePlayer / `Viseme15AndVoiceOff/VisemeProbeOff` | 1 | 11 | 0.02744 | 0.04132 |
| StandalonePlayer / `Viseme15AndVoiceOn/VisemeProbeOn` | 1 | 11 | 0.02831 | 0.049131 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Source` | 1 | 11 | 0.020539 | 0.026654 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Optimized` | 1 | 11 | 0.024909 | 0.03494 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Source_EntryOff` | 1 | 11 | 0.059571 | 0.119601 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Optimized_EntryOff` | 1 | 11 | 0.042306 | 0.12759 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Source_EntryOn` | 1 | 11 | 0.021698 | 0.024948 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Optimized_EntryOn` | 1 | 11 | 0.035103 | 0.038987 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Source_8` | 1 | 11 | 0.080569 | 0.382059 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Optimized_8` | 1 | 11 | 0.045666 | 0.082343 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Source_32` | 1 | 11 | 0.067366 | 0.184637 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Optimized_32` | 1 | 11 | 0.030928 | 0.039178 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Source_64` | 1 | 11 | 0.106453 | 0.130179 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Optimized_64` | 1 | 11 | 0.029882 | 0.032813 |

功能场景 13 个、输出检查 4807 项；完成后整体托管堆 1,662,976 bytes。

Linux 构建耗时 10.654096 秒，产物 58,454,182 bytes，0 个构建错误。

Player `AnimatorOptimizer/Bool selector`：1 → 1 层，2 → 2 个 Clip；中位数 0.020539 → 0.024909 ms，增加 21.3%。
Player `AnimatorOptimizer/AAP EntryOff`：1 → 2 层，2 → 10 个 Clip；中位数 0.059571 → 0.042306 ms，减少 29.0%。
Player `AnimatorOptimizer/AAP EntryOn`：1 → 2 层，2 → 10 个 Clip；中位数 0.021698 → 0.035103 ms，增加 61.8%。
Player `AnimatorOptimizer/layers 8`：8 → 1 层，8 → 1 个 Clip；中位数 0.080569 → 0.045666 ms，减少 43.3%。
Player `AnimatorOptimizer/layers 32`：32 → 1 层，32 → 1 个 Clip；中位数 0.067366 → 0.030928 ms，减少 54.1%。
Player `AnimatorOptimizer/layers 64`：64 → 1 层，64 → 1 个 Clip；中位数 0.106453 → 0.029882 ms，减少 71.9%。

本轮 `AnimatorOptimizer/Bool selector`、`AnimatorOptimizer/AAP EntryOn` 的稳定态更新耗时增加。这些场景通过了原始/转换后逐步输入与输出检查；完整对照同时展示功能结果和运行成本，转换范围按控制器结构与运行状态选择。
