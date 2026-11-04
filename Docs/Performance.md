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
| AvatarSecuritySystem 锁定图生成 | 1 次 | 7 次；检查每节点 3 条缩放曲线 |
| Viseme 映射解析 / 持久化控制器重建 | 2 次 | 21 组 × 25 次解析 / 7 次重建 |
| PrefabDiffPatcher 分析与应用 | 2 次 | 9 次；保存后核对 Light 值与 Variant 继承 |
| Collider 捕获 / 历史与预览缓存读取 | 2 次 | 9 次捕获或重算 / 9 组 × 10,000 次缓存读取 |
| Logger 回调、设置读取、trace 记录与 JSON 导出 | 2 次 | 9 组；每组操作次数见 JSON |
| ACC 2 张 256×256 图标 | 1 次 | 5 次；核对实际红绿像素、原材质与 layer |

## Editor 基准

运行 `20261104T194142-4dce9b18`，范围 `benchmark`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 2 | 5 | 116.8503 | 138.9675 |
| AdvancedCostumeController / `animator_update` | 2 | 9 | 0.05454 | 0.057645 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 8 | 5 | 345.7822 | 374.1722 |
| AdvancedCostumeController / `animator_update` | 8 | 9 | 0.074702 | 0.079711 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 24 | 5 | 1095.2869 | 1187.8551 |
| AdvancedCostumeController / `animator_update` | 24 | 9 | 0.119088 | 0.123229 |
| AnimatorOptimizer / `analyze_static_layers` | 8 | 9 | 1.4847 | 4.3 |
| AnimatorOptimizer / `analyze_static_layers` | 32 | 9 | 8.4456 | 10.1872 |
| AnimatorOptimizer / `analyze_static_layers` | 64 | 9 | 15.2429 | 16.2534 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 8 | 9 | 0.051949 | 0.057316 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 8 | 9 | 0.053517 | 0.058703 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 32 | 9 | 0.075196 | 0.080002 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 32 | 9 | 0.078742 | 0.08496 |
| AnimatorOptimizer / `animator_update_before_merge` | 8 | 9 | 0.047511 | 0.051806 |
| AnimatorOptimizer / `animator_update_after_merge` | 8 | 9 | 0.03057 | 0.031235 |
| AnimatorOptimizer / `animator_update_before_merge` | 32 | 9 | 0.071881 | 0.077783 |
| AnimatorOptimizer / `animator_update_after_merge` | 32 | 9 | 0.030538 | 0.032429 |
| AnimatorOptimizer / `animator_update_before_merge` | 64 | 9 | 0.144586 | 0.149623 |
| AnimatorOptimizer / `animator_update_after_merge` | 64 | 9 | 0.03137 | 0.035424 |
| AvatarColliderMonitor / `history_recompute_reference` | 100 | 9 | 0.2661 | 0.4515 |
| AvatarColliderMonitor / `history_cached_read` | 100 | 9 | 0.000014 | 0.000016 |
| AvatarColliderMonitor / `preview_cached_read` | 100 | 9 | 0.000019 | 0.000022 |
| AvatarColliderMonitor / `history_recompute_reference` | 1000 | 9 | 2.7405 | 6.1298 |
| AvatarColliderMonitor / `history_cached_read` | 1000 | 9 | 0.000014 | 0.000015 |
| AvatarColliderMonitor / `preview_cached_read` | 1000 | 9 | 0.000019 | 0.000026 |
| AvatarColliderMonitor / `capture_3d_colliders` | 32 | 9 | 0.5681 | 0.6827 |
| AvatarColliderMonitor / `capture_3d_colliders` | 256 | 9 | 4.6141 | 6.7255 |
| AvatarColliderMonitor / `capture_3d_colliders` | 1024 | 9 | 22.8351 | 27.121 |
| AvatarSecuritySystem / `lock_graph_generation` | 16 | 7 | 0.8946 | 1.2022 |
| AvatarSecuritySystem / `lock_graph_generation` | 64 | 7 | 1.9785 | 2.1674 |
| AvatarSecuritySystem / `lock_graph_generation` | 256 | 7 | 6.1654 | 11.4247 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 4 | 5 | 67.0408 | 72.7658 |
| BlendshapeControllerGenerator / `animator_update` | 4 | 9 | 0.0427 | 0.044983 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 16 | 5 | 181.4985 | 199.7952 |
| BlendshapeControllerGenerator / `animator_update` | 16 | 9 | 0.050017 | 0.053762 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 64 | 5 | 682.0536 | 690.5503 |
| BlendshapeControllerGenerator / `animator_update` | 64 | 9 | 0.10021 | 0.101187 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 1000 | 9 | 0.000221 | 0.000345 |
| BuildPipelineLogger / `disabled_shader_callback` | 1000 | 9 | 0.000004 | 0.000005 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 10000 | 9 | 0.000229 | 0.000263 |
| BuildPipelineLogger / `disabled_shader_callback` | 10000 | 9 | 0.000004 | 0.000012 |
| BuildPipelineLogger / `record_callback_trace` | 128 | 9 | 0.000158 | 0.000173 |
| BuildPipelineLogger / `export_callback_trace_json` | 128 | 9 | 0.2201 | 5.1601 |
| BuildPipelineLogger / `record_callback_trace` | 1024 | 9 | 0.000152 | 0.000216 |
| BuildPipelineLogger / `export_callback_trace_json` | 1024 | 9 | 1.8568 | 3.8146 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 32 | 9 | 11.4147 | 17.7729 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 256 | 9 | 104.9625 | 107.9518 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 16 | 9 | 16.6655 | 20.7332 |
| PrefabDiffPatcher / `apply_prefab_variant` | 16 | 9 | 6.6107 | 7.0285 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 64 | 9 | 58.4016 | 113.9643 |
| PrefabDiffPatcher / `apply_prefab_variant` | 64 | 9 | 11.2841 | 16.8843 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 1 | 7 | 30.1761 | 34.601 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 5 | 7 | 32.6675 | 35.2217 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 15 | 7 | 43.2875 | 49.2033 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 15 | 21 | 0.011444 | 0.0124 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 150 | 21 | 0.021564 | 0.0239 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 1500 | 21 | 0.107492 | 0.119724 |

8 个独立单层分组：自动保留原图 0.051949 ms，显式转换 0.053517 ms，增加 3.0%。
32 个独立单层分组：自动保留原图 0.075196 ms，显式转换 0.078742 ms，增加 4.7%。
8 层合并：中位数 0.047511 → 0.03057 ms，减少 35.7%。
32 层合并：中位数 0.071881 → 0.030538 ms，减少 57.5%。
64 层合并：中位数 0.144586 → 0.03137 ms，减少 78.3%。
100 条 Collider 历史：完整重算中位数 0.2661 ms；读取已缓存的比较视图 0.000014 ms。
1000 条 Collider 历史：完整重算中位数 2.7405 ms；读取已缓存的比较视图 0.000014 ms。
Logger 每组 1000 次：直接读取 EditorPrefs 的单次中位数 0.000221 ms，缓存禁用回调 0.000004 ms。
Logger 每组 10000 次：直接读取 EditorPrefs 的单次中位数 0.000229 ms，缓存禁用回调 0.000004 ms。

## 图形输出

运行 `20261104T193915-e87b4e70`，范围 `graphics`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `menu_icon_rendering_256px` | 2 | 5 | 145.8729 | 153.4682 |

图形设备：Renderer: llvmpipe (LLVM 19.1.7, 256 bits)；Vendor:   Mesa；Version:  4.5 (Core Profile) Mesa 25.0.7-2+deb13u1；OPENGL LOG: Creating OpenGL 4.5 graphics device ; Context level  <OpenGL 4.5> ; Context handle -1120789120

## Linux Player

Player 每个 Animator 先预热 50 次，再采样 11 组，每组 200 次手动更新。输入使用对应功能场景最后一步的状态；逐步切换的正确性在计时前执行。

运行 `20261104T194015-4b80a5f1`，范围 `linux`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| StandalonePlayer / `Unity.PlayerSmoke/Basic` | 1 | 11 | 0.017039 | 0.07211 |
| StandalonePlayer / `Blendshape Float and inverted Bool across combined renderers/Blendshape` | 1 | 11 | 0.022416 | 0.042161 |
| StandalonePlayer / `ASS gesture hold, unlock, released-layer rearm, relock and second unlock/ASS_StandaloneProbe` | 1 | 11 | 0.030028 | 0.035441 |
| StandalonePlayer / `ACC costume selection, Parts and shared Mixer/ACCShared` | 1 | 11 | 0.024502 | 0.030649 |
| StandalonePlayer / `ACC costume selection, Parts and independent Mixer/ACCIndependent` | 1 | 11 | 0.025201 | 0.026721 |
| StandalonePlayer / `Viseme15AndVoiceOff/VisemeProbeOff` | 1 | 11 | 0.019459 | 0.021962 |
| StandalonePlayer / `Viseme15AndVoiceOn/VisemeProbeOn` | 1 | 11 | 0.017278 | 0.020813 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Source` | 1 | 11 | 0.015446 | 0.017791 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Optimized` | 1 | 11 | 0.015942 | 0.037217 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Source_EntryOff` | 1 | 11 | 0.026621 | 0.040786 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Optimized_EntryOff` | 1 | 11 | 0.026308 | 0.028217 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Source_EntryOn` | 1 | 11 | 0.022686 | 0.02583 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Optimized_EntryOn` | 1 | 11 | 0.027637 | 0.029198 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Source_8` | 1 | 11 | 0.037078 | 0.039912 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Optimized_8` | 1 | 11 | 0.025504 | 0.028248 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Source_32` | 1 | 11 | 0.04923 | 0.058499 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Optimized_32` | 1 | 11 | 0.027825 | 0.029258 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Source_64` | 1 | 11 | 0.084859 | 0.107236 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Optimized_64` | 1 | 11 | 0.028241 | 0.03174 |

功能场景 13 个、输出检查 4807 项；完成后整体托管堆 1,654,784 bytes。

Linux 构建耗时 21.739886 秒，产物 58,454,186 bytes，0 个构建错误。

Player `AnimatorOptimizer/Bool selector`：1 → 1 层，2 → 2 个 Clip；中位数 0.015446 → 0.015942 ms，增加 3.2%。
Player `AnimatorOptimizer/AAP EntryOff`：1 → 2 层，2 → 10 个 Clip；中位数 0.026621 → 0.026308 ms，减少 1.2%。
Player `AnimatorOptimizer/AAP EntryOn`：1 → 2 层，2 → 10 个 Clip；中位数 0.022686 → 0.027637 ms，增加 21.8%。
Player `AnimatorOptimizer/layers 8`：8 → 1 层，8 → 1 个 Clip；中位数 0.037078 → 0.025504 ms，减少 31.2%。
Player `AnimatorOptimizer/layers 32`：32 → 1 层，32 → 1 个 Clip；中位数 0.04923 → 0.027825 ms，减少 43.5%。
Player `AnimatorOptimizer/layers 64`：64 → 1 层，64 → 1 个 Clip；中位数 0.084859 → 0.028241 ms，减少 66.7%。

本次测量中 `AnimatorOptimizer/Bool selector`、`AnimatorOptimizer/AAP EntryOn` 的稳态更新耗时增加。这些场景通过了原始/转换后逐步输入与输出检查；完整对照同时展示功能结果和运行成本，转换范围按控制器结构与运行状态选择。
