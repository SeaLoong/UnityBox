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
| AvatarSecuritySystem 组件引用只读扫描 | 1 次 | 5 次；128/1024/4096 组件，核对识别数量和原文件字节 |
| Viseme 映射解析 / 持久化控制器重建 | 2 次 | 21 组 × 25 次解析 / 7 次重建 |
| PrefabDiffPatcher 分析与应用 | 2 次 | 9 次；保存后核对 Light 值与 Variant 继承 |
| Collider 捕获 / 历史与预览缓存读取 | 2 次 | 9 次捕获或重算 / 9 组 × 10,000 次缓存读取 |
| Logger 回调、设置读取、trace 记录与 JSON 导出 | 2 次 | 9 组；每组操作次数见 JSON |
| ACC 2 张 256×256 图标 | 1 次 | 5 次；核对实际红绿像素、原材质与 layer |

## Editor 基准

运行 `20261107T153452-cfabd428`，范围 `serial`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 2 | 5 | 166.8171 | 175.9464 |
| AdvancedCostumeController / `animator_update` | 2 | 9 | 0.043718 | 0.053419 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 8 | 5 | 475.875 | 553.6489 |
| AdvancedCostumeController / `animator_update` | 8 | 9 | 0.071258 | 0.079972 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 24 | 5 | 1660.8291 | 2090.6616 |
| AdvancedCostumeController / `animator_update` | 24 | 9 | 0.161822 | 0.460638 |
| AnimatorOptimizer / `analyze_static_layers` | 8 | 9 | 7.076 | 51.1092 |
| AnimatorOptimizer / `analyze_static_layers` | 32 | 9 | 11.0942 | 27.3153 |
| AnimatorOptimizer / `analyze_static_layers` | 64 | 9 | 27.288 | 123.4776 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 8 | 9 | 0.058313 | 0.074527 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 8 | 9 | 0.053041 | 0.116052 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 32 | 9 | 0.054549 | 0.069571 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 32 | 9 | 0.060433 | 0.086422 |
| AnimatorOptimizer / `animator_update_before_merge` | 8 | 9 | 0.042796 | 0.075528 |
| AnimatorOptimizer / `animator_update_after_merge` | 8 | 9 | 0.023792 | 0.033404 |
| AnimatorOptimizer / `animator_update_before_merge` | 32 | 9 | 0.063671 | 0.081064 |
| AnimatorOptimizer / `animator_update_after_merge` | 32 | 9 | 0.027879 | 0.061055 |
| AnimatorOptimizer / `animator_update_before_merge` | 64 | 9 | 0.115206 | 0.199466 |
| AnimatorOptimizer / `animator_update_after_merge` | 64 | 9 | 0.026728 | 0.037028 |
| AvatarColliderMonitor / `history_recompute_reference` | 100 | 9 | 0.2847 | 0.467 |
| AvatarColliderMonitor / `history_cached_read` | 100 | 9 | 0.000019 | 0.00003 |
| AvatarColliderMonitor / `preview_cached_read` | 100 | 9 | 0.000019 | 0.00002 |
| AvatarColliderMonitor / `history_recompute_reference` | 1000 | 9 | 3.9783 | 15.0999 |
| AvatarColliderMonitor / `history_cached_read` | 1000 | 9 | 0.000014 | 0.000036 |
| AvatarColliderMonitor / `preview_cached_read` | 1000 | 9 | 0.000022 | 0.000045 |
| AvatarColliderMonitor / `capture_3d_colliders` | 32 | 9 | 0.6448 | 0.7472 |
| AvatarColliderMonitor / `capture_3d_colliders` | 256 | 9 | 6.2077 | 11.452 |
| AvatarColliderMonitor / `capture_3d_colliders` | 1024 | 9 | 33.4678 | 546.0314 |
| AvatarSecuritySystem / `scan_component_references` | 128 | 5 | 132.4322 | 186.4349 |
| AvatarSecuritySystem / `scan_component_references` | 1024 | 5 | 817.4 | 1072.3577 |
| AvatarSecuritySystem / `scan_component_references` | 4096 | 5 | 2973.8568 | 4179.3896 |
| AvatarSecuritySystem / `gesture_graph_generation` | 2 | 7 | 4.0882 | 5.7031 |
| AvatarSecuritySystem / `gesture_graph_generation` | 8 | 7 | 28.1202 | 43.8269 |
| AvatarSecuritySystem / `gesture_graph_generation` | 32 | 7 | 518.4388 | 1140.4761 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 4 | 5 | 155.6051 | 188.4145 |
| BlendshapeControllerGenerator / `animator_update` | 4 | 9 | 0.044199 | 0.049909 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 16 | 5 | 407.9652 | 772.0845 |
| BlendshapeControllerGenerator / `animator_update` | 16 | 9 | 0.060396 | 0.07371 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 64 | 5 | 1740.0221 | 2552.6817 |
| BlendshapeControllerGenerator / `animator_update` | 64 | 9 | 0.146391 | 0.185801 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 1000 | 9 | 0.000241 | 0.000465 |
| BuildPipelineLogger / `disabled_shader_callback` | 1000 | 9 | 0.000004 | 0.000004 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 10000 | 9 | 0.00024 | 0.000277 |
| BuildPipelineLogger / `disabled_shader_callback` | 10000 | 9 | 0.000004 | 0.000014 |
| BuildPipelineLogger / `record_callback_trace` | 128 | 9 | 0.000226 | 0.001152 |
| BuildPipelineLogger / `export_callback_trace_json` | 128 | 9 | 0.2843 | 0.4348 |
| BuildPipelineLogger / `record_callback_trace` | 1024 | 9 | 0.000297 | 0.000669 |
| BuildPipelineLogger / `export_callback_trace_json` | 1024 | 9 | 2.1253 | 12.7568 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 32 | 9 | 29.8923 | 59.5475 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 256 | 9 | 165.5267 | 256.557 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 16 | 9 | 89.4626 | 203.2048 |
| PrefabDiffPatcher / `apply_prefab_variant` | 16 | 9 | 16.2434 | 22.5557 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 64 | 9 | 313.059 | 376.8988 |
| PrefabDiffPatcher / `apply_prefab_variant` | 64 | 9 | 23.3373 | 65.7908 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 1 | 7 | 102.6773 | 112.2938 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 5 | 7 | 106.4141 | 123.9878 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 15 | 7 | 127.158 | 201.4699 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 15 | 21 | 0.020348 | 0.02344 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 150 | 21 | 0.027228 | 0.03388 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 1500 | 21 | 0.095304 | 0.136088 |

8 个独立单层分组：自动保留原图 0.058313 ms，显式转换 0.053041 ms，减少 9.0%。
32 个独立单层分组：自动保留原图 0.054549 ms，显式转换 0.060433 ms，增加 10.8%。
8 层合并：中位数 0.042796 → 0.023792 ms，减少 44.4%。
32 层合并：中位数 0.063671 → 0.027879 ms，减少 56.2%。
64 层合并：中位数 0.115206 → 0.026728 ms，减少 76.8%。
100 条 Collider 历史：完整重算中位数 0.2847 ms；读取已缓存的比较视图 0.000019 ms。
1000 条 Collider 历史：完整重算中位数 3.9783 ms；读取已缓存的比较视图 0.000014 ms。
Logger 每组 1000 次：直接读取 EditorPrefs 的单次中位数 0.000241 ms，缓存禁用回调 0.000004 ms。
Logger 每组 10000 次：直接读取 EditorPrefs 的单次中位数 0.00024 ms，缓存禁用回调 0.000004 ms。

## 图形输出

运行 `20261107T150726-6d5d8452`，范围 `graphics`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `menu_icon_rendering_256px` | 2 | 5 | 594.5381 | 815.0682 |

图形设备：Renderer: llvmpipe (LLVM 19.1.7, 256 bits)；Vendor:   Mesa；Version:  4.5 (Core Profile) Mesa 25.0.7-2+deb13u1；OPENGL LOG: Creating OpenGL 4.5 graphics device ; Context level  <OpenGL 4.5> ; Context handle -1042536096

## Linux Player

Player 每个 Animator 先预热 50 次，再采样 11 组，每组 200 次手动更新。输入使用对应功能场景最后一步的状态；逐步切换的正确性在计时前执行。

运行 `20261107T153356-7d615710`，范围 `linux`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| StandalonePlayer / `Unity.PlayerSmoke/Basic` | 1 | 11 | 0.031499 | 0.121478 |
| StandalonePlayer / `Blendshape Float and inverted Bool across combined renderers/Blendshape` | 1 | 11 | 0.036175 | 0.078357 |
| StandalonePlayer / `ASS gesture hold, result, timeout restart and user animation preservation/ASS_StandaloneProbe` | 1 | 11 | 0.04206 | 0.04538 |
| StandalonePlayer / `ACC costume selection, Parts and shared Mixer/ACCShared` | 1 | 11 | 0.044827 | 0.089653 |
| StandalonePlayer / `ACC costume selection, Parts and independent Mixer/ACCIndependent` | 1 | 11 | 0.049355 | 0.102755 |
| StandalonePlayer / `Viseme15AndVoiceOff/VisemeProbeOff` | 1 | 11 | 0.033032 | 0.076123 |
| StandalonePlayer / `Viseme15AndVoiceOn/VisemeProbeOn` | 1 | 11 | 0.0276 | 0.044315 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Source` | 1 | 11 | 0.026544 | 0.031568 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Optimized` | 1 | 11 | 0.023276 | 0.033053 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Source_EntryOff` | 1 | 11 | 0.023176 | 0.03462 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Optimized_EntryOff` | 1 | 11 | 0.027761 | 0.030857 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Source_EntryOn` | 1 | 11 | 0.021871 | 0.026427 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Optimized_EntryOn` | 1 | 11 | 0.027698 | 0.032305 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Source_8` | 1 | 11 | 0.042292 | 0.045856 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Optimized_8` | 1 | 11 | 0.019336 | 0.022486 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Source_32` | 1 | 11 | 0.058338 | 0.065616 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Optimized_32` | 1 | 11 | 0.012216 | 0.048286 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Source_64` | 1 | 11 | 0.100842 | 0.123137 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Optimized_64` | 1 | 11 | 0.027778 | 0.033271 |

功能场景 13 个、输出检查 4807 项；完成后整体托管堆 1,658,880 bytes。

Linux 构建耗时 32.040072 秒，产物 58,547,694 bytes，0 个构建错误。

Player `AnimatorOptimizer/Bool selector`：1 → 1 层，2 → 2 个 Clip；中位数 0.026544 → 0.023276 ms，减少 12.3%。
Player `AnimatorOptimizer/AAP EntryOff`：1 → 2 层，2 → 10 个 Clip；中位数 0.023176 → 0.027761 ms，增加 19.8%。
Player `AnimatorOptimizer/AAP EntryOn`：1 → 2 层，2 → 10 个 Clip；中位数 0.021871 → 0.027698 ms，增加 26.6%。
Player `AnimatorOptimizer/layers 8`：8 → 1 层，8 → 1 个 Clip；中位数 0.042292 → 0.019336 ms，减少 54.3%。
Player `AnimatorOptimizer/layers 32`：32 → 1 层，32 → 1 个 Clip；中位数 0.058338 → 0.012216 ms，减少 79.1%。
Player `AnimatorOptimizer/layers 64`：64 → 1 层，64 → 1 个 Clip；中位数 0.100842 → 0.027778 ms，减少 72.5%。

本次测量中 `AnimatorOptimizer/AAP EntryOff`、`AnimatorOptimizer/AAP EntryOn` 的稳态更新耗时增加。这些场景通过了原始/转换后逐步输入与输出检查；完整对照同时展示功能结果和运行成本，转换范围按控制器结构与运行状态选择。
