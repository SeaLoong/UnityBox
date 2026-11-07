# UnityBox 性能实测

Editor、图形及全库 Player 数据来自 `7cd4b40` 的八插件基线（ASS0.9.4）。
ASS0.9.5 的音频导入与 SDK 上传校验、专项 Player 记录见
[音频修复验证](Verification/ASSAudioUpload/README.md)。

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

运行 `20261107T172435-8058dcff`，范围 `serial`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 2 | 5 | 169.3667 | 270.4517 |
| AdvancedCostumeController / `animator_update` | 2 | 9 | 0.051187 | 0.056354 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 8 | 5 | 506.9629 | 549.8794 |
| AdvancedCostumeController / `animator_update` | 8 | 9 | 0.067414 | 0.080179 |
| AdvancedCostumeController / `persistent_outfit_parts_generation` | 24 | 5 | 1812.0478 | 1962.0566 |
| AdvancedCostumeController / `animator_update` | 24 | 9 | 0.088889 | 0.194983 |
| AnimatorOptimizer / `analyze_static_layers` | 8 | 9 | 2.2489 | 5.6786 |
| AnimatorOptimizer / `analyze_static_layers` | 32 | 9 | 10.6198 | 127.4706 |
| AnimatorOptimizer / `analyze_static_layers` | 64 | 9 | 19.9104 | 32.3335 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 8 | 9 | 0.061372 | 0.084505 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 8 | 9 | 0.064907 | 0.10633 |
| AnimatorOptimizer / `automatic_retained_singleton_groups` | 32 | 9 | 0.087908 | 0.101483 |
| AnimatorOptimizer / `explicit_singleton_conversion` | 32 | 9 | 0.109777 | 0.302039 |
| AnimatorOptimizer / `animator_update_before_merge` | 8 | 9 | 0.06562 | 0.699096 |
| AnimatorOptimizer / `animator_update_after_merge` | 8 | 9 | 0.027528 | 0.037378 |
| AnimatorOptimizer / `animator_update_before_merge` | 32 | 9 | 0.07034 | 0.100092 |
| AnimatorOptimizer / `animator_update_after_merge` | 32 | 9 | 0.02945 | 0.040092 |
| AnimatorOptimizer / `animator_update_before_merge` | 64 | 9 | 0.116517 | 0.147931 |
| AnimatorOptimizer / `animator_update_after_merge` | 64 | 9 | 0.022294 | 0.029494 |
| AvatarColliderMonitor / `history_recompute_reference` | 100 | 9 | 0.2368 | 0.264 |
| AvatarColliderMonitor / `history_cached_read` | 100 | 9 | 0.000014 | 0.000057 |
| AvatarColliderMonitor / `preview_cached_read` | 100 | 9 | 0.000019 | 0.000023 |
| AvatarColliderMonitor / `history_recompute_reference` | 1000 | 9 | 3.0403 | 9.5204 |
| AvatarColliderMonitor / `history_cached_read` | 1000 | 9 | 0.000016 | 0.000142 |
| AvatarColliderMonitor / `preview_cached_read` | 1000 | 9 | 0.00002 | 0.000032 |
| AvatarColliderMonitor / `capture_3d_colliders` | 32 | 9 | 0.7108 | 4.4228 |
| AvatarColliderMonitor / `capture_3d_colliders` | 256 | 9 | 5.6964 | 8.2996 |
| AvatarColliderMonitor / `capture_3d_colliders` | 1024 | 9 | 29.835 | 41.8645 |
| AvatarSecuritySystem / `scan_component_references` | 128 | 5 | 89.8569 | 117.2593 |
| AvatarSecuritySystem / `scan_component_references` | 1024 | 5 | 683.6215 | 764.1582 |
| AvatarSecuritySystem / `scan_component_references` | 4096 | 5 | 3103.6207 | 6060.7482 |
| AvatarSecuritySystem / `discover_project_assets` | 1024 | 5 | 380.0913 | 435.2502 |
| AvatarSecuritySystem / `gesture_graph_generation` | 2 | 7 | 4.4826 | 10.5083 |
| AvatarSecuritySystem / `gesture_graph_generation` | 8 | 7 | 27.2399 | 32.9163 |
| AvatarSecuritySystem / `gesture_graph_generation` | 32 | 7 | 433.0616 | 653.3545 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 4 | 5 | 121.7298 | 124.3057 |
| BlendshapeControllerGenerator / `animator_update` | 4 | 9 | 0.040475 | 0.045087 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 16 | 5 | 328.8125 | 358.5056 |
| BlendshapeControllerGenerator / `animator_update` | 16 | 9 | 0.057681 | 0.096808 |
| BlendshapeControllerGenerator / `persistent_float_controller_generation` | 64 | 5 | 1557.2506 | 1769.634 |
| BlendshapeControllerGenerator / `animator_update` | 64 | 9 | 0.205093 | 0.246917 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 1000 | 9 | 0.00022 | 0.000822 |
| BuildPipelineLogger / `disabled_shader_callback` | 1000 | 9 | 0.000004 | 0.000005 |
| BuildPipelineLogger / `editorprefs_setting_read_reference` | 10000 | 9 | 0.00025 | 0.000265 |
| BuildPipelineLogger / `disabled_shader_callback` | 10000 | 9 | 0.000004 | 0.000005 |
| BuildPipelineLogger / `record_callback_trace` | 128 | 9 | 0.00019 | 0.000292 |
| BuildPipelineLogger / `export_callback_trace_json` | 128 | 9 | 0.2616 | 0.4666 |
| BuildPipelineLogger / `record_callback_trace` | 1024 | 9 | 0.000215 | 0.000309 |
| BuildPipelineLogger / `export_callback_trace_json` | 1024 | 9 | 2.9714 | 28.752 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 32 | 9 | 15.9971 | 64.059 |
| PrefabDiffPatcher / `analyze_serialized_hierarchy` | 256 | 9 | 166.2394 | 206.4344 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 16 | 9 | 76.7417 | 253.8416 |
| PrefabDiffPatcher / `apply_prefab_variant` | 16 | 9 | 19.3883 | 77.2043 |
| PrefabDiffPatcher / `apply_scene_composition_reference` | 64 | 9 | 236.404 | 422.2445 |
| PrefabDiffPatcher / `apply_prefab_variant` | 64 | 9 | 35.1555 | 73.9144 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 1 | 7 | 107.8981 | 126.7892 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 5 | 7 | 127.0965 | 194.328 |
| VisemeBlendshapeOverride / `BuildPersist15VoiceStates` | 15 | 7 | 140.0467 | 155.2787 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 15 | 21 | 0.029148 | 0.079852 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 150 | 21 | 0.027212 | 0.220928 |
| VisemeBlendshapeOverride / `Resolve15Mappings` | 1500 | 21 | 0.096112 | 0.150556 |

8 个独立单层分组：自动保留原图 0.061372 ms，显式转换 0.064907 ms，增加 5.8%。
32 个独立单层分组：自动保留原图 0.087908 ms，显式转换 0.109777 ms，增加 24.9%。
8 层合并：中位数 0.06562 → 0.027528 ms，减少 58.0%。
32 层合并：中位数 0.07034 → 0.02945 ms，减少 58.1%。
64 层合并：中位数 0.116517 → 0.022294 ms，减少 80.9%。
100 条 Collider 历史：完整重算中位数 0.2368 ms；读取已缓存的比较视图 0.000014 ms。
1000 条 Collider 历史：完整重算中位数 3.0403 ms；读取已缓存的比较视图 0.000016 ms。
Logger 每组 1000 次：直接读取 EditorPrefs 的单次中位数 0.00022 ms，缓存禁用回调 0.000004 ms。
Logger 每组 10000 次：直接读取 EditorPrefs 的单次中位数 0.00025 ms，缓存禁用回调 0.000004 ms。

## 图形输出

运行 `20261107T172232-c6d9886a`，范围 `graphics`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| AdvancedCostumeController / `menu_icon_rendering_256px` | 2 | 5 | 218.9669 | 293.1317 |

图形设备：Renderer: llvmpipe (LLVM 19.1.7, 256 bits)；Vendor:   Mesa；Version:  4.5 (Core Profile) Mesa 25.0.7-2+deb13u1；OPENGL LOG: Creating OpenGL 4.5 graphics device ; Context level  <OpenGL 4.5> ; Context handle 727499696

## Linux Player

Player 每个 Animator 先预热 50 次，再采样 11 组，每组 200 次手动更新。输入使用对应功能场景最后一步的状态；逐步切换的正确性在计时前执行。

运行 `20261107T172338-54ebb7a8`，范围 `linux`。

| 插件 / 场景 | 规模 | 样本 | 中位数 ms | p95 ms |
| --- | ---: | ---: | ---: | ---: |
| StandalonePlayer / `Unity.PlayerSmoke/Basic` | 1 | 11 | 0.028504 | 0.097444 |
| StandalonePlayer / `Blendshape Float and inverted Bool across combined renderers/Blendshape` | 1 | 11 | 0.031331 | 0.035498 |
| StandalonePlayer / `ASS gesture hold, result, timeout restart and user animation preservation/ASS_StandaloneProbe` | 1 | 11 | 0.038069 | 0.045437 |
| StandalonePlayer / `ACC costume selection, Parts and shared Mixer/ACCShared` | 1 | 11 | 0.037955 | 0.057925 |
| StandalonePlayer / `ACC costume selection, Parts and independent Mixer/ACCIndependent` | 1 | 11 | 0.038093 | 0.044156 |
| StandalonePlayer / `Viseme15AndVoiceOff/VisemeProbeOff` | 1 | 11 | 0.033781 | 0.04089 |
| StandalonePlayer / `Viseme15AndVoiceOn/VisemeProbeOn` | 1 | 11 | 0.032283 | 0.067349 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Source` | 1 | 11 | 0.027305 | 0.035007 |
| StandalonePlayer / `AnimatorOptimizer/Bool selector/AO_Selector_Optimized` | 1 | 11 | 0.029518 | 0.034734 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Source_EntryOff` | 1 | 11 | 0.029053 | 0.040881 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOff/AO_History_Optimized_EntryOff` | 1 | 11 | 0.040065 | 0.051448 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Source_EntryOn` | 1 | 11 | 0.029491 | 0.034181 |
| StandalonePlayer / `AnimatorOptimizer/AAP EntryOn/AO_History_Optimized_EntryOn` | 1 | 11 | 0.041374 | 0.043349 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Source_8` | 1 | 11 | 0.048327 | 0.070286 |
| StandalonePlayer / `AnimatorOptimizer/layers 8/AO_Layers_Optimized_8` | 1 | 11 | 0.030926 | 0.035466 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Source_32` | 1 | 11 | 0.051244 | 0.06876 |
| StandalonePlayer / `AnimatorOptimizer/layers 32/AO_Layers_Optimized_32` | 1 | 11 | 0.031225 | 0.036472 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Source_64` | 1 | 11 | 0.092294 | 0.138799 |
| StandalonePlayer / `AnimatorOptimizer/layers 64/AO_Layers_Optimized_64` | 1 | 11 | 0.032109 | 0.036865 |

功能场景 13 个、输出检查 4807 项；完成后整体托管堆 1,679,360 bytes。

Linux 构建耗时 27.766235 秒，产物 58,547,698 bytes，0 个构建错误。

Player `AnimatorOptimizer/Bool selector`：1 → 1 层，2 → 2 个 Clip；中位数 0.027305 → 0.029518 ms，增加 8.1%。
Player `AnimatorOptimizer/AAP EntryOff`：1 → 2 层，2 → 10 个 Clip；中位数 0.029053 → 0.040065 ms，增加 37.9%。
Player `AnimatorOptimizer/AAP EntryOn`：1 → 2 层，2 → 10 个 Clip；中位数 0.029491 → 0.041374 ms，增加 40.3%。
Player `AnimatorOptimizer/layers 8`：8 → 1 层，8 → 1 个 Clip；中位数 0.048327 → 0.030926 ms，减少 36.0%。
Player `AnimatorOptimizer/layers 32`：32 → 1 层，32 → 1 个 Clip；中位数 0.051244 → 0.031225 ms，减少 39.1%。
Player `AnimatorOptimizer/layers 64`：64 → 1 层，64 → 1 个 Clip；中位数 0.092294 → 0.032109 ms，减少 65.2%。

本次测量中 `AnimatorOptimizer/Bool selector`、`AnimatorOptimizer/AAP EntryOff`、`AnimatorOptimizer/AAP EntryOn` 的稳态更新耗时增加。这些场景通过了原始/转换后逐步输入与输出检查；完整对照同时展示功能结果和运行成本，转换范围按控制器结构与运行状态选择。
