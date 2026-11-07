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

运行 `20261107T142145-b10c6a73`，范围 `serial`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 2 | 5 | 196.7852 | 220.5728 |
| AdvancedCostumeController / `animator_update` | 2 | 9 | 0.058077 | 0.096676 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 8 | 5 | 582.9914 | 732.1363 |
| AdvancedCostumeController / `animator_update` | 8 | 9 | 0.088652 | 0.146029 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 24 | 5 | 2447.0915 | 3076.5541 |
| AdvancedCostumeController / `animator_update` | 24 | 9 | 0.210096 | 0.252177 |
| AnimatorOptimizer / `analyze_static_layers` | 8 | 9 | 4.576 | 13.4542 |
| AnimatorOptimizer / `analyze_static_layers` | 32 | 9 | 18.4589 | 43.5284 |
| AnimatorOptimizer / `analyze_static_layers` | 64 | 9 | 41.8341 | 52.2796 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 8 | 9 | 0.058881 | 0.076454 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 8 | 9 | 0.080979 | 0.148102 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 32 | 9 | 0.179439 | 0.22378 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 32 | 9 | 0.17308 | 0.188132 |
| AnimatorOptimizer / `animator_update_before_merge` | 8 | 9 | 0.094771 | 0.188845 |
| AnimatorOptimizer / `animator_update_after_merge` | 8 | 9 | 0.054218 | 0.067728 |
| AnimatorOptimizer / `animator_update_before_merge` | 32 | 9 | 0.118102 | 0.189478 |
| AnimatorOptimizer / `animator_update_after_merge` | 32 | 9 | 0.037512 | 0.064275 |
| AnimatorOptimizer / `animator_update_before_merge` | 64 | 9 | 0.21963 | 0.293831 |
| AnimatorOptimizer / `animator_update_after_merge` | 64 | 9 | 0.039371 | 0.046983 |
| AvatarColliderMonitor / `history_recompute_reference` | 100 | 9 | 0.2354 | 0.4847 |
| AvatarColliderMonitor / `history_cached_read` | 100 | 9 | 0.000014 | 0.000021 |
| AvatarColliderMonitor / `preview_cached_read` | 100 | 9 | 0.000022 | 0.000117 |
| AvatarColliderMonitor / `history_recompute_reference` | 1000 | 9 | 3.6233 | 11.4145 |
| AvatarColliderMonitor / `history_cached_read` | 1000 | 9 | 0.000014 | 0.000055 |
| AvatarColliderMonitor / `preview_cached_read` | 1000 | 9 | 0.000021 | 0.000045 |
| AvatarColliderMonitor / `capture_3d_colliders` | 32 | 9 | 1.445 | 5.1893 |
| AvatarColliderMonitor / `capture_3d_colliders` | 256 | 9 | 6.8393 | 22.5589 |
| AvatarColliderMonitor / `capture_3d_colliders` | 1024 | 9 | 36.8793 | 64.5088 |
| AvatarSecuritySystem / `gesture_graph_generation` | 2 | 7 | 3.6614 | 6.4798 |
| AvatarSecuritySystem / `gesture_graph_generation` | 8 | 7 | 36.748 | 43.2175 |
| AvatarSecuritySystem / `gesture_graph_generation` | 32 | 7 | 537.3589 | 774.6512 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 4 | 5 | 194.2658 | 218.9393 |
| BlendshapeControllerGenerator / `animator_update` | 4 | 9 | 0.042336 | 0.069452 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 16 | 5 | 425.2981 | 580.8541 |
| BlendshapeControllerGenerator / `animator_update` | 16 | 9 | 0.097611 | 0.189914 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 64 | 5 | 1350.5245 | 1450.4039 |
| BlendshapeControllerGenerator / `animator_update` | 64 | 9 | 0.166735 | 0.20299 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 1000 | 9 | 0.000272 | 0.00036 |
| BuildPipelineLogger / `disabled_shader_callback` | 1000 | 9 | 0.000004 | 0.000004 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 10000 | 9 | 0.000287 | 0.000424 |
| BuildPipelineLogger / `disabled_shader_callback` | 10000 | 9 | 0.000004 | 0.000005 |
| BuildPipelineLogger / `record_callback_trace` | 128 | 9 | 0.000193 | 0.000248 |
| BuildPipelineLogger / `export_callback_trace_json` | 128 | 9 | 0.2571 | 0.4225 |
| BuildPipelineLogger / `record_callback_trace` | 1024 | 9 | 0.000208 | 0.000354 |
| BuildPipelineLogger / `export_callback_trace_json` | 1024 | 9 | 2.3308 | 6.3819 |
| ComponentReferenceMigration / `scan_component_references` | 128 | 5 | 92.6815 | 127.1509 |
| ComponentReferenceMigration / `scan_component_references` | 1024 | 5 | 862.4242 | 886.6368 |
| ComponentReferenceMigration / `scan_component_references` | 4096 | 5 | 3308.042 | 4415.9267 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 32 | 9 | 18.434 | 33.0857 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 256 | 9 | 150.2293 | 208.6382 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 16 | 9 | 113.9171 | 197.8538 |
| PrefabDiffPatcher / `apply_prefab_variant` | 16 | 9 | 20.7585 | 22.5581 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 64 | 9 | 308.777 | 409.2767 |
| PrefabDiffPatcher / `apply_prefab_variant` | 64 | 9 | 26.6728 | 50.6163 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 1 | 7 | 114.6294 | 157.1291 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 5 | 7 | 125.3959 | 148.2707 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 15 | 7 | 132.4154 | 145.5337 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 15 | 21 | 0.016716 | 0.027772 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 150 | 21 | 0.024208 | 0.032756 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 1500 | 21 | 0.122048 | 0.191864 |

8 个独立单层分组：自动保留原图 0.058881 ms，显式转换 0.080979 ms，增加 37.5%。
32 个独立单层分组：自动保留原图 0.179439 ms，显式转换 0.17308 ms，减少 3.5%。
8 层合并：中位数 0.094771 → 0.054218 ms，减少 42.8%。
32 层合并：中位数 0.118102 → 0.037512 ms，减少 68.2%。
64 层合并：中位数 0.21963 → 0.039371 ms，减少 82.1%。
100 条 Collider 历史：完整重算中位数 0.2354 ms；读取已缓存的比较视图 0.000014 ms。
1000 条 Collider 历史：完整重算中位数 3.6233 ms；读取已缓存的比较视图 0.000014 ms。
Logger 每组 1000 次：直接读取 EditorPrefs 的单次中位数 0.000272 ms，缓存禁用回调 0.000004 ms。
Logger 每组 10000 次：直接读取 EditorPrefs 的单次中位数 0.000287 ms，缓存禁用回调 0.000004 ms。

## 图形输出

运行 `20261107T141224-77d4b9ba`，范围 `graphics`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `menu_icon_rendering_256px` | 2 | 5 | 204.1901 | 224.6469 |

图形设备：Renderer: llvmpipe (LLVM 19.1.7, 256 bits)；Vendor:   Mesa；Version:  4.5 (Core Profile) Mesa 25.0.7-2+deb13u1；OPENGL LOG: Creating OpenGL 4.5 graphics device ; Context level  <OpenGL 4.5> ; Context handle 2036502992

## Linux Player

Player 每个 Animator 先预热 50 次，再采样 11 组，每组 200 次手动更新。输入使用对应功能场景最后一步的状态；逐步切换的正确性在计时前执行。

运行 `20261107T142030-3cf4b5b5`，范围 `linux`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| StandalonePlayer / `Unity.PlayerSmoke/Basic` | 1 | 11 | 0.031012 | 0.041802 |
| StandalonePlayer / `Blendshape Float and inverted Bool across combined renderers/Blendshape` | 1 | 11 | 0.037104 | 0.067712 |
| StandalonePlayer / `ASS gesture hold, result, timeout restart and user animation preservation/ASS_StandaloneProbe` | 1 | 11 | 0.036525 | 0.092948 |
| StandalonePlayer / `ACC costume selection, Parts and shared Mixer/ACCShared` | 1 | 11 | 0.052583 | 0.114445 |
| StandalonePlayer / `ACC costume selection, Parts and independent Mixer/ACCIndependent` | 1 | 11 | 0.054145 | 0.69933 |
| StandalonePlayer / `Viseme15AndVoiceOff/VisemeProbeOff` | 1 | 11 | 0.03597 | 0.067064 |
| StandalonePlayer / `Viseme15AndVoiceOn/VisemeProbeOn` | 1 | 11 | 0.035202 | 0.044527 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Source` | 1 | 11 | 0.031603 | 0.047552 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Optimized` | 1 | 11 | 0.026743 | 0.046996 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Source_EntryOff` | 1 | 11 | 0.014987 | 0.03434 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Optimized_EntryOff` | 1 | 11 | 0.038358 | 0.042007 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Source_EntryOn` | 1 | 11 | 0.026015 | 0.035463 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Optimized_EntryOn` | 1 | 11 | 0.041846 | 0.061817 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Source_8` | 1 | 11 | 0.042743 | 0.050925 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Optimized_8` | 1 | 11 | 0.021004 | 0.031804 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Source_32` | 1 | 11 | 0.084521 | 0.108841 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Optimized_32` | 1 | 11 | 0.03046 | 0.064431 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Source_64` | 1 | 11 | 0.158174 | 0.183741 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Optimized_64` | 1 | 11 | 0.029996 | 0.039525 |

功能场景 13 个、输出检查 4807 项；完成后整体托管堆 1,658,880 bytes。

Linux 构建耗时 41.563569 秒，产物 58,547,690 bytes，0 个构建错误。

Player `AnimatorOptimizer/Bool selector`：1 → 1 层，2 → 2 个 Clip；中位数 0.031603 → 0.026743 ms，减少 15.4%。
Player `AnimatorOptimizer/AAP EntryOff`：1 → 2 层，2 → 10 个 Clip；中位数 0.014987 → 0.038358 ms，增加 155.9%。
Player `AnimatorOptimizer/AAP EntryOn`：1 → 2 层，2 → 10 个 Clip；中位数 0.026015 → 0.041846 ms，增加 60.9%。
Player `AnimatorOptimizer/layers 8`：8 → 1 层，8 → 1 个 Clip；中位数 0.042743 → 0.021004 ms，减少 50.9%。
Player `AnimatorOptimizer/layers 32`：32 → 1 层，32 → 1 个 Clip；中位数 0.084521 → 0.03046 ms，减少 64.0%。
Player `AnimatorOptimizer/layers 64`：64 → 1 层，64 → 1 个 Clip；中位数 0.158174 → 0.029996 ms，减少 81.0%。

本次测量中 `AnimatorOptimizer/AAP EntryOff`、`AnimatorOptimizer/AAP EntryOn` 的稳态更新耗时增加。这些场景通过了原始/转换后逐步输入与输出检查；完整对照同时展示功能结果和运行成本，转换范围按控制器结构与运行状态选择。
