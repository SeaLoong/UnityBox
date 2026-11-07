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
| Viseme 映射解析 / 持久化控制器重建 | 2 次 | 21 组 × 25 次解析 / 7 次重建 |
| PrefabDiffPatcher 分析与应用 | 2 次 | 9 次；保存后核对 Light 值与 Variant 继承 |
| Collider 捕获 / 历史与预览缓存读取 | 2 次 | 9 次捕获或重算 / 9 组 × 10,000 次缓存读取 |
| Logger 回调、设置读取、trace 记录与 JSON 导出 | 2 次 | 9 组；每组操作次数见 JSON |
| ACC 2 张 256×256 图标 | 1 次 | 5 次；核对实际红绿像素、原材质与 layer |

## Editor 基准

运行 `20261107T104413-979e75b9`，范围 `serial`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 2 | 5 | 416.8581 | 461.4552 |
| AdvancedCostumeController / `animator_update` | 2 | 9 | 0.211492 | 0.239672 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 8 | 5 | 589.9319 | 670.7785 |
| AdvancedCostumeController / `animator_update` | 8 | 9 | 0.214959 | 0.277299 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 24 | 5 | 2197.5278 | 3085.2197 |
| AdvancedCostumeController / `animator_update` | 24 | 9 | 0.128446 | 0.411479 |
| AnimatorOptimizer / `analyze_static_layers` | 8 | 9 | 6.3535 | 16.2014 |
| AnimatorOptimizer / `analyze_static_layers` | 32 | 9 | 28.705 | 45.156 |
| AnimatorOptimizer / `analyze_static_layers` | 64 | 9 | 28.8323 | 34.7829 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 8 | 9 | 0.04367 | 0.051182 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 8 | 9 | 0.045561 | 0.050728 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 32 | 9 | 0.064421 | 0.09178 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 32 | 9 | 0.075054 | 0.090243 |
| AnimatorOptimizer / `animator_update_before_merge` | 8 | 9 | 0.051244 | 0.059269 |
| AnimatorOptimizer / `animator_update_after_merge` | 8 | 9 | 0.020401 | 0.025442 |
| AnimatorOptimizer / `animator_update_before_merge` | 32 | 9 | 0.060195 | 0.081381 |
| AnimatorOptimizer / `animator_update_after_merge` | 32 | 9 | 0.025485 | 0.030087 |
| AnimatorOptimizer / `animator_update_before_merge` | 64 | 9 | 0.122973 | 0.151263 |
| AnimatorOptimizer / `animator_update_after_merge` | 64 | 9 | 0.018008 | 0.033442 |
| AvatarColliderMonitor / `history_recompute_reference` | 100 | 9 | 0.3134 | 3.6128 |
| AvatarColliderMonitor / `history_cached_read` | 100 | 9 | 0.000014 | 0.000049 |
| AvatarColliderMonitor / `preview_cached_read` | 100 | 9 | 0.000023 | 0.000037 |
| AvatarColliderMonitor / `history_recompute_reference` | 1000 | 9 | 3.8413 | 9.4586 |
| AvatarColliderMonitor / `history_cached_read` | 1000 | 9 | 0.000014 | 0.00004 |
| AvatarColliderMonitor / `preview_cached_read` | 1000 | 9 | 0.000021 | 0.00004 |
| AvatarColliderMonitor / `capture_3d_colliders` | 32 | 9 | 1.0501 | 1.7654 |
| AvatarColliderMonitor / `capture_3d_colliders` | 256 | 9 | 7.0868 | 10.0141 |
| AvatarColliderMonitor / `capture_3d_colliders` | 1024 | 9 | 32.7925 | 47.3472 |
| AvatarSecuritySystem / `gesture_graph_generation` | 2 | 7 | 3.2757 | 3.7956 |
| AvatarSecuritySystem / `gesture_graph_generation` | 8 | 7 | 32.1424 | 43.8293 |
| AvatarSecuritySystem / `gesture_graph_generation` | 32 | 7 | 609.5608 | 812.1656 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 4 | 5 | 140.2095 | 146.9846 |
| BlendshapeControllerGenerator / `animator_update` | 4 | 9 | 0.04453 | 0.062197 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 16 | 5 | 408.3611 | 473.7427 |
| BlendshapeControllerGenerator / `animator_update` | 16 | 9 | 0.067225 | 0.101368 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 64 | 5 | 1417.707 | 1481.5291 |
| BlendshapeControllerGenerator / `animator_update` | 64 | 9 | 0.159385 | 0.177017 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 1000 | 9 | 0.000246 | 0.000459 |
| BuildPipelineLogger / `disabled_shader_callback` | 1000 | 9 | 0.000004 | 0.000088 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 10000 | 9 | 0.000256 | 0.000505 |
| BuildPipelineLogger / `disabled_shader_callback` | 10000 | 9 | 0.000004 | 0.000004 |
| BuildPipelineLogger / `record_callback_trace` | 128 | 9 | 0.000191 | 0.000468 |
| BuildPipelineLogger / `export_callback_trace_json` | 128 | 9 | 0.318 | 0.4944 |
| BuildPipelineLogger / `record_callback_trace` | 1024 | 9 | 0.000204 | 0.000385 |
| BuildPipelineLogger / `export_callback_trace_json` | 1024 | 9 | 2.7125 | 5.8545 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 32 | 9 | 19.732 | 41.966 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 256 | 9 | 161.2995 | 277.393 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 16 | 9 | 36.1931 | 112.2596 |
| PrefabDiffPatcher / `apply_prefab_variant` | 16 | 9 | 12.6705 | 17.4838 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 64 | 9 | 90.6745 | 487.6031 |
| PrefabDiffPatcher / `apply_prefab_variant` | 64 | 9 | 25.3195 | 181.3134 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 1 | 7 | 111.3606 | 197.7043 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 5 | 7 | 129.0495 | 243.8783 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 15 | 7 | 132.8776 | 239.3493 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 15 | 21 | 0.016144 | 0.023292 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 150 | 21 | 0.027024 | 0.16936 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 1500 | 21 | 0.103692 | 0.117032 |

8 个独立单层分组：自动保留原图 0.04367 ms，显式转换 0.045561 ms，增加 4.3%。
32 个独立单层分组：自动保留原图 0.064421 ms，显式转换 0.075054 ms，增加 16.5%。
8 层合并：中位数 0.051244 → 0.020401 ms，减少 60.2%。
32 层合并：中位数 0.060195 → 0.025485 ms，减少 57.7%。
64 层合并：中位数 0.122973 → 0.018008 ms，减少 85.4%。
100 条 Collider 历史：完整重算中位数 0.3134 ms；读取已缓存的比较视图 0.000014 ms。
1000 条 Collider 历史：完整重算中位数 3.8413 ms；读取已缓存的比较视图 0.000014 ms。
Logger 每组 1000 次：直接读取 EditorPrefs 的单次中位数 0.000246 ms，缓存禁用回调 0.000004 ms。
Logger 每组 10000 次：直接读取 EditorPrefs 的单次中位数 0.000256 ms，缓存禁用回调 0.000004 ms。

## 图形输出

运行 `20261107T104153-87b4f81e`，范围 `icons`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `menu_icon_rendering_256px` | 2 | 5 | 532.4245 | 1301.7913 |

图形设备：Renderer: llvmpipe (LLVM 19.1.7, 256 bits)；Vendor:   Mesa；Version:  4.5 (Core Profile) Mesa 25.0.7-2+deb13u1；OPENGL LOG: Creating OpenGL 4.5 graphics device ; Context level  <OpenGL 4.5> ; Context handle 1267331696

## Linux Player

Player 每个 Animator 先预热 50 次，再采样 11 组，每组 200 次手动更新。输入使用对应功能场景最后一步的状态；逐步切换的正确性在计时前执行。

运行 `20261107T104248-b9103081`，范围 `linux`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| StandalonePlayer / `Unity.PlayerSmoke/Basic` | 1 | 11 | 0.01309 | 0.022746 |
| StandalonePlayer / `Blendshape Float and inverted Bool across combined renderers/Blendshape` | 1 | 11 | 0.0191 | 0.052839 |
| StandalonePlayer / `ASS gesture hold, result, timeout restart and user animation preservation/ASS_StandaloneProbe` | 1 | 11 | 0.031083 | 0.038583 |
| StandalonePlayer / `ACC costume selection, Parts and shared Mixer/ACCShared` | 1 | 11 | 0.029702 | 0.048747 |
| StandalonePlayer / `ACC costume selection, Parts and independent Mixer/ACCIndependent` | 1 | 11 | 0.025614 | 0.057913 |
| StandalonePlayer / `Viseme15AndVoiceOff/VisemeProbeOff` | 1 | 11 | 0.018776 | 0.028802 |
| StandalonePlayer / `Viseme15AndVoiceOn/VisemeProbeOn` | 1 | 11 | 0.018105 | 0.030842 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Source` | 1 | 11 | 0.013242 | 0.020098 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Optimized` | 1 | 11 | 0.013396 | 0.019485 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Source_EntryOff` | 1 | 11 | 0.014473 | 0.044573 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Optimized_EntryOff` | 1 | 11 | 0.025046 | 0.036753 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Source_EntryOn` | 1 | 11 | 0.012858 | 0.018922 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Optimized_EntryOn` | 1 | 11 | 0.019064 | 0.048694 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Source_8` | 1 | 11 | 0.032356 | 0.039709 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Optimized_8` | 1 | 11 | 0.013619 | 0.019545 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Source_32` | 1 | 11 | 0.067124 | 0.078049 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Optimized_32` | 1 | 11 | 0.014437 | 0.033063 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Source_64` | 1 | 11 | 0.11879 | 0.179387 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Optimized_64` | 1 | 11 | 0.016926 | 0.026516 |

功能场景 13 个、输出检查 4807 项；完成后整体托管堆 1,654,784 bytes。

Linux 构建耗时 54.7344 秒，产物 58,547,694 bytes，0 个构建错误。

Player `AnimatorOptimizer/Bool selector`：1 → 1 层，2 → 2 个 Clip；中位数 0.013242 → 0.013396 ms，增加 1.2%。
Player `AnimatorOptimizer/AAP EntryOff`：1 → 2 层，2 → 10 个 Clip；中位数 0.014473 → 0.025046 ms，增加 73.0%。
Player `AnimatorOptimizer/AAP EntryOn`：1 → 2 层，2 → 10 个 Clip；中位数 0.012858 → 0.019064 ms，增加 48.3%。
Player `AnimatorOptimizer/layers 8`：8 → 1 层，8 → 1 个 Clip；中位数 0.032356 → 0.013619 ms，减少 57.9%。
Player `AnimatorOptimizer/layers 32`：32 → 1 层，32 → 1 个 Clip；中位数 0.067124 → 0.014437 ms，减少 78.5%。
Player `AnimatorOptimizer/layers 64`：64 → 1 层，64 → 1 个 Clip；中位数 0.11879 → 0.016926 ms，减少 85.8%。

本次测量中 `AnimatorOptimizer/Bool selector`、`AnimatorOptimizer/AAP EntryOff`、`AnimatorOptimizer/AAP EntryOn` 的稳态更新耗时增加。这些场景通过了原始/转换后逐步输入与输出检查；完整对照同时展示功能结果和运行成本，转换范围按控制器结构与运行状态选择。
