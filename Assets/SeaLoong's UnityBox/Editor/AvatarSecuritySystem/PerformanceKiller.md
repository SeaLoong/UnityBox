# VRChat Avatar 可控性能消耗方案指南

## 目录

1. [概述](#概述)
2. [基础知识](#基础知识)
3. [CPU 性能消耗方案](#cpu-性能消耗方案)
4. [GPU 性能消耗方案](#gpu-性能消耗方案)
5. [方案对比分析](#方案对比分析)
6. [综合实现方案](#综合实现方案)
7. [注意事项与限制](#注意事项与限制)

---

## 概述

本文档详细介绍在 VRChat Avatar 开发中，如何构建**可控的**性能消耗机制。这些方案可用于：
- 性能测试与压力测试
- 特殊效果实现（如"惩罚"机制）
- 了解 VRChat 性能瓶颈

所有方案都需要满足两个核心要求：
1. **符合 VRChat SDK 限制** - 不使用被禁止的组件或功能
2. **可控性** - 能够通过 VRC Parameters 动态开启/关闭

---

## 基础知识

### VRChat Playable Layers 限制

VRChat 严格限制 Avatar 只能使用官方指定的 5 种 Playable Layers：

| Layer 名称 | 用途 | 可自定义 |
|-----------|------|---------|
| Base | 基础移动动画 | ✅ |
| Additive | 叠加动画 | ✅ |
| Gesture | 手势控制 | ✅ |
| Action | 全身动作 | ✅ |
| FX | 特效/开关控制 | ✅ |

**重要限制**：
- ❌ 禁止动态加载非官方指定的 AnimatorController
- ❌ 禁止运行时修改 RuntimeAnimatorController
- ❌ 禁止使用 Playable API 自定义控制器

### Animator 启用状态与性能关系

| Animator 状态 | Layer 权重 | CPU 消耗 |
|--------------|-----------|---------|
| **Disabled** | - | ❌ 零消耗 |
| Enabled | = 0 | ⚠️ 少量消耗（状态机仍运行） |
| Enabled | > 0 | ✅ 正常消耗 |

### Shader/Material 加载状态与性能关系

| 状态 | 显存占用 | GPU 消耗 |
|------|---------|---------|
| 未加载（磁盘/AB中） | ❌ 无 | ❌ 无 |
| 已加载未使用 | ✅ 占用 | ❌ 无 |
| 正在渲染 | ✅ 占用 | ✅ 消耗 |

---

## CPU 性能消耗方案

### 方案一：Animator Controller 复杂结构

#### 1.1 深度嵌套子状态机

```
StateMachine (Root)
├── SubStateMachine_1
│   ├── SubStateMachine_1_1
│   │   ├── SubStateMachine_1_1_1
│   │   │   └── ... (深度 10+)
│   │   └── SubStateMachine_1_1_2
│   └── SubStateMachine_1_2
├── SubStateMachine_2
│   └── ...
└── SubStateMachine_N
```

**消耗原理**：每层子状态机都需要独立的状态追踪和转换评估

**可控性**：通过 VRC Parameter 控制状态切换，进入/退出复杂区域

#### 1.2 大量 Transition 条件检测

```yaml
State A → State B:
  Conditions:
    - Parameter1 > 0.5
    - Parameter2 == true
    - Parameter3 < 100
    - ... (50+ conditions)
    
State A → State C:
  Conditions:
    - ... (另外 50+ conditions)
    
# 重复 100+ transitions
```

**消耗原理**：每帧都需要评估所有 Transition 的所有条件

#### 1.3 复杂 2D BlendTree

```
BlendTree (2D Freeform Directional)
├── Motion 1 (position: 0, 0)
├── Motion 2 (position: 1, 0)
├── Motion 3 (position: 0, 1)
├── ... 
└── Motion 100+ (scattered positions)
```

**消耗原理**：2D 混合需要计算所有 Motion 的权重贡献

#### 1.4 Write Defaults + 大量属性

```yaml
Animation Clip 配置:
  - 绑定属性数量: 500+
  - Write Defaults: ON
  - 每个状态都有完整属性列表
```

**消耗原理**：Write Defaults 开启时，每帧需要重置所有属性

---

### 方案二：Constraint 组件堆叠

#### 2.1 链式 Constraint 结构

```
Object_A
  └─► Parent Constraint ──► Object_B
        └─► Position Constraint ──► Object_C
              └─► Rotation Constraint ──► Object_D
                    └─► Scale Constraint ──► Object_E
                          └─► ... (深度 50+)
```

**消耗原理**：
- 链式依赖导致无法并行计算
- 每个 Constraint 都需要矩阵运算

**可控性**：
- Animator 控制 `Constraint.weight` (0-1)
- Animator 控制 `Constraint.constraintActive`
- 控制整个 GameObject 的 Active 状态

#### 2.2 多 Source Constraint

```yaml
Parent Constraint:
  Sources:
    - Target_1 (weight: 0.1)
    - Target_2 (weight: 0.1)
    - Target_3 (weight: 0.1)
    - ... 
    - Target_50 (weight: 0.1)
```

**消耗原理**：每个 Source 都需要独立计算后加权混合

---

### 方案三：PhysBone 物理骨骼

#### 3.1 长链配置

```yaml
PhysBone Chain 配置:
  Root Transform: Bone_Root
  链长度: 20+ bones
  
  参数设置 (高消耗):
    Pull: 0.8
    Spring: 0.8  
    Stiffness: 0.5
    Gravity: 0.5
    Immobile: 0.3
```

#### 3.2 Collision 配置

```yaml
PhysBone Collision:
  Collision: 启用
  Radius: 0.1
  Allow Collision: Others/Self
  
PhysBone Collider 数量: 50+
  - 分布在全身各个位置
  - 每个 Collider 都与所有 Chain 进行碰撞检测
```

**消耗原理**：
- N 条链 × M 个 Collider = N×M 次碰撞检测
- 长链需要更多的物理迭代

**可控性**：
- 控制 PhysBone 组件的 `enabled` 属性
- 控制整个骨骼链 GameObject 的 Active

---

### 方案四：Contact Receiver/Sender

```yaml
配置:
  VRC Contact Sender: 100+ 个
  VRC Contact Receiver: 100+ 个
  
单个 Contact 设置:
  Shape: Capsule (计算量 > Sphere)
  Radius: 0.5
  Height: 1.0
  Collision Tags: ["Tag1", "Tag2", "Tag3", ...]
```

**消耗原理**：
- 每个 Sender 都需要与所有 Receiver 进行匹配检测
- Tag 匹配增加字符串比较开销

**可控性**：控制 GameObject Active 状态

---

## GPU 性能消耗方案

### 方案一：复杂 Shader

#### 1.1 多 Pass 渲染

```hlsl
SubShader {
    Tags { "RenderType"="Opaque" }
    
    Pass {
        Name "Pass1"
        // 第一次渲染
    }
    Pass {
        Name "Pass2" 
        // 第二次渲染
    }
    Pass {
        Name "Pass3"
        // 第三次渲染
    }
    // ... 添加更多 Pass (10+)
}
```

**消耗原理**：每个 Pass 都会独立绘制一次 Mesh

#### 1.2 复杂片元着色器

```hlsl
// 可控循环次数
uniform int _LoopCount; // 通过 Material Property 控制

fixed4 frag(v2f i) : SV_Target {
    fixed4 col = tex2D(_MainTex, i.uv);
    
    // 大量纹理采样
    [loop]
    for(int n = 0; n < _LoopCount; n++) {
        col += tex2D(_MainTex, i.uv + float2(n * 0.001, 0)) * 0.01;
    }
    
    // 复杂数学运算
    [loop]
    for(int j = 0; j < _LoopCount; j++) {
        col.rgb = sin(col.rgb * 3.14159 + _Time.y);
        col.rgb = pow(abs(col.rgb), 2.2);
        col.rgb = sqrt(col.rgb);
    }
    
    return col;
}
```

**可控性**：
```csharp
// 通过 Animator 控制 Material Property
material.SetInt("_LoopCount", 0);   // 低消耗
material.SetInt("_LoopCount", 100); // 高消耗
```

#### 1.3 实时光照计算

```hlsl
// 多光源逐像素计算
fixed4 frag(v2f i) : SV_Target {
    fixed4 col = 0;
    
    // 假设有多个点光源
    for(int lightIndex = 0; lightIndex < _LightCount; lightIndex++) {
        float3 lightDir = normalize(_LightPositions[lightIndex] - i.worldPos);
        float3 viewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
        float3 halfDir = normalize(lightDir + viewDir);
        
        // Blinn-Phong
        float NdotL = max(0, dot(i.normal, lightDir));
        float NdotH = max(0, dot(i.normal, halfDir));
        
        col.rgb += _LightColors[lightIndex] * NdotL;
        col.rgb += pow(NdotH, _Shininess) * _LightColors[lightIndex];
    }
    
    return col;
}
```

---

### 方案二：Overdraw（过度绘制）

#### 2.1 透明层堆叠

```
渲染顺序 (从后到前):
┌─────────────────────────┐
│     Layer 1 (Alpha)     │  ← 第一次绘制
│  ┌───────────────────┐  │
│  │   Layer 2 (Alpha) │  │  ← 第二次绘制
│  │  ┌─────────────┐  │  │
│  │  │  Layer 3    │  │  │  ← 第三次绘制
│  │  │   ...       │  │  │
│  │  │  Layer 20   │  │  │  ← 第 N 次绘制
│  │  └─────────────┘  │  │
│  └───────────────────┘  │
└─────────────────────────┘

同一像素被渲染 20+ 次
```

**实现方式**：
```yaml
Mesh 配置:
  - 20+ 个 Quad Mesh
  - 位置完全重叠或略微偏移
  - 使用 Transparent 渲染队列
  
Material 配置:
  - Render Queue: 3000+ (Transparent)
  - Blend Mode: Alpha Blend
  - ZWrite: Off
```

**可控性**：
- Animator 控制各层的 Alpha 值 (0 = 跳过混合计算)
- Animator 控制 MeshRenderer.enabled

---

### 方案三：高面数 Mesh

#### 3.1 Skinned Mesh Renderer

```yaml
高消耗配置:
  Mesh:
    Vertices: 100,000+
    Triangles: 200,000+
    
  Skinned Mesh Renderer:
    Bones: 200+
    Quality: Auto
    Update When Offscreen: ON  # 重要：即使不可见也更新
    
  BlendShapes:
    数量: 100+
    每帧激活多个 BlendShape
```

**消耗原理**：
- 顶点数量直接影响顶点着色器工作量
- Skinning 需要对每个顶点进行骨骼变换
- BlendShape 需要在 CPU 上计算顶点偏移

**可控性**：
- Animator 控制 `SkinnedMeshRenderer.enabled`
- Animator 控制各 BlendShape 的权重

---

### 方案四：GrabPass 屏幕抓取

```hlsl
SubShader {
    Tags { "Queue"="Transparent" }
    
    // 抓取当前屏幕内容
    GrabPass { "_GrabTexture" }
    
    Pass {
        CGPROGRAM
        sampler2D _GrabTexture;
        
        fixed4 frag(v2f i) : SV_Target {
            // 扭曲/模糊效果
            fixed4 col = 0;
            for(int x = -5; x <= 5; x++) {
                for(int y = -5; y <= 5; y++) {
                    col += tex2D(_GrabTexture, i.grabUV + float2(x, y) * 0.01);
                }
            }
            col /= 121.0;
            return col;
        }
        ENDCG
    }
}
```

**消耗原理**：
- GrabPass 需要将当前渲染结果复制到纹理
- 多个 GrabPass 会多次复制
- 后续采样增加带宽消耗

---

## 方案对比分析

### CPU 消耗方案对比

| 方案 | 消耗强度 | 实现难度 | 可控粒度 | VRC兼容性 | 推荐度 |
|-----|---------|---------|---------|----------|-------|
| **深度子状态机** | ⭐⭐⭐ | ⭐⭐ | 状态级 | ✅ | ⭐⭐⭐ |
| **大量 Transition** | ⭐⭐⭐⭐ | ⭐⭐⭐ | 状态级 | ✅ | ⭐⭐⭐⭐ |
| **复杂 BlendTree** | ⭐⭐⭐ | ⭐⭐⭐ | 参数级 | ✅ | ⭐⭐⭐ |
| **Constraint 链** | ⭐⭐⭐⭐ | ⭐⭐ | 组件级 | ✅ | ⭐⭐⭐⭐⭐ |
| **PhysBone 长链** | ⭐⭐⭐⭐⭐ | ⭐⭐ | 组件级 | ✅ | ⭐⭐⭐⭐⭐ |
| **Contact 组件** | ⭐⭐⭐ | ⭐ | 组件级 | ✅ | ⭐⭐⭐ |

#### 分析

1. **PhysBone 长链** - 最推荐
   - ✅ 实现简单，效果显著
   - ✅ 开启 Collision 后消耗倍增
   - ✅ 可通过 GameObject Active 精确控制
   - ⚠️ 受 VRChat PhysBone 限制 (Poor 级别限制)

2. **Constraint 链** - 高度推荐
   - ✅ 不受 VRC 特殊限制
   - ✅ 链式结构导致无法并行，消耗稳定
   - ✅ 可逐个控制权重

3. **Animator 复杂结构** - 推荐
   - ✅ 完全在官方框架内
   - ⚠️ 需要精心设计状态机结构
   - ⚠️ 消耗不如物理组件直接

---

### GPU 消耗方案对比

| 方案 | 消耗强度 | 实现难度 | 可控粒度 | VRC兼容性 | 推荐度 |
|-----|---------|---------|---------|----------|-------|
| **多 Pass Shader** | ⭐⭐⭐⭐ | ⭐⭐⭐ | Shader变体 | ✅ | ⭐⭐⭐⭐ |
| **复杂片元计算** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | 参数级 | ✅ | ⭐⭐⭐⭐⭐ |
| **Overdraw 堆叠** | ⭐⭐⭐⭐ | ⭐⭐ | 层级 | ✅ | ⭐⭐⭐⭐ |
| **高面数 Mesh** | ⭐⭐⭐ | ⭐ | 组件级 | ⚠️ 受限 | ⭐⭐⭐ |
| **GrabPass** | ⭐⭐⭐⭐ | ⭐⭐⭐ | Shader级 | ⚠️ 受限 | ⭐⭐⭐ |

#### 分析

1. **复杂片元计算** - 最推荐
   - ✅ 消耗可通过 Material Property 线性控制
   - ✅ 不增加 Avatar 多边形数
   - ✅ Animator 可直接控制 Material 属性
   - ⚠️ 需要 Shader 编程知识

2. **Overdraw 堆叠** - 高度推荐
   - ✅ 实现简单
   - ✅ 效果明显
   - ✅ 可通过 Alpha 或 Active 控制
   - ⚠️ 可能影响视觉效果

3. **多 Pass Shader** - 推荐
   - ✅ 消耗稳定可预测
   - ⚠️ 需要 Shader 变体切换
   - ⚠️ 不如片元计算灵活

---

## 综合实现方案

### 架构设计

```
┌────────────────────────────────────────────────────────────┐
│                    VRC Expression Menu                      │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  "Performance Test" Toggle                          │   │
│  │       │                                             │   │
│  │       ▼                                             │   │
│  │  VRC Parameter: "PerfKiller" (Bool/Int)            │   │
│  └─────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌────────────────────────────────────────────────────────────┐
│                      FX Layer                               │
│  ┌─────────────────────────────────────────────────────┐   │
│  │                    State Machine                     │   │
│  │                                                      │   │
│  │   ┌──────────┐    PerfKiller=1    ┌──────────────┐  │   │
│  │   │   Idle   │ ─────────────────► │ Performance  │  │   │
│  │   │  State   │ ◄───────────────── │   Hell       │  │   │
│  │   └──────────┘    PerfKiller=0    └──────────────┘  │   │
│  │                                                      │   │
│  └─────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┴───────────────┐
              ▼                               ▼
┌─────────────────────────┐     ┌─────────────────────────┐
│      CPU 消耗组           │     │      GPU 消耗组          │
├─────────────────────────┤     ├─────────────────────────┤
│                         │     │                         │
│  ┌───────────────────┐  │     │  ┌───────────────────┐  │
│  │ Constraint Chain  │  │     │  │  Heavy Shader     │  │
│  │    Active: ON     │  │     │  │  _LoopCount: 100  │  │
│  └───────────────────┘  │     │  └───────────────────┘  │
│                         │     │                         │
│  ┌───────────────────┐  │     │  ┌───────────────────┐  │
│  │ PhysBone Chains   │  │     │  │  Overdraw Layers  │  │
│  │    Active: ON     │  │     │  │  Alpha: 1.0       │  │
│  └───────────────────┘  │     │  └───────────────────┘  │
│                         │     │                         │
│  ┌───────────────────┐  │     │  ┌───────────────────┐  │
│  │ Contact ×100      │  │     │  │  Hi-Poly Mesh     │  │
│  │    Active: ON     │  │     │  │  Enabled: ON      │  │
│  └───────────────────┘  │     │  └───────────────────┘  │
│                         │     │                         │
└─────────────────────────┘     └─────────────────────────┘
```

### Animation Clip 配置示例

#### Idle State (低消耗)
```yaml
Animation: "Perf_Idle"
Properties:
  # CPU 组件禁用
  - ConstraintChain/Parent Constraint.m_Active: 0
  - PhysBoneRoot.m_IsActive: 0
  - ContactSenders.m_IsActive: 0
  
  # GPU 组件禁用
  - HeavyShaderMesh/SkinnedMeshRenderer.m_Enabled: 0
  - OverdrawLayers.m_IsActive: 0
  
  # Shader 参数
  - HeavyMaterial._LoopCount: 0
```

#### Performance Hell State (高消耗)
```yaml
Animation: "Perf_Hell"
Properties:
  # CPU 组件启用
  - ConstraintChain/Parent Constraint.m_Active: 1
  - PhysBoneRoot.m_IsActive: 1
  - ContactSenders.m_IsActive: 1
  
  # GPU 组件启用
  - HeavyShaderMesh/SkinnedMeshRenderer.m_Enabled: 1
  - OverdrawLayers.m_IsActive: 1
  
  # Shader 参数
  - HeavyMaterial._LoopCount: 100
```

### 分级控制（可选）

```
┌─────────────────────────────────────────────────────────┐
│              Performance Level (0-4)                     │
├─────────────────────────────────────────────────────────┤
│  Level 0: 正常状态                                       │
│           └─ 所有高消耗组件禁用                           │
│                                                         │
│  Level 1: 轻度消耗                                       │
│           └─ Constraint Chain ON                        │
│                                                         │
│  Level 2: 中度消耗                                       │
│           └─ + PhysBone Chains ON                       │
│                                                         │
│  Level 3: 重度消耗                                       │
│           └─ + Heavy Shader ON                          │
│                                                         │
│  Level 4: 极限消耗                                       │
│           └─ + Overdraw + Hi-Poly Mesh                  │
└─────────────────────────────────────────────────────────┘
```

---

## 注意事项与限制

### VRChat 限制

| 限制项 | 说明 | 影响 |
|-------|------|-----|
| PhysBone 数量 | Very Poor 允许更多 | 选择 Avatar 等级 |
| Polygon 数量 | 影响 Avatar 等级评定 | 高面数会降级 |
| Material 数量 | 影响性能评级 | 多 Pass 可能问题 |
| Skinned Mesh | 限制数量 | 注意合并 |

### 性能安全建议

1. **始终提供关闭选项**
   - 不要强制让其他玩家承受性能消耗
   - Expression Menu 提供明显的开关

2. **测试环境**
   - 在 Avatar 3.0 Emulator 中测试
   - 使用 Unity Profiler 验证实际消耗

3. **渐进式启用**
   - 提供分级选项
   - 让用户选择消耗程度

### 常见问题

**Q: 为什么我的 Constraint 没有消耗 CPU？**
A: 检查是否正确启用了 `constraintActive` 和 `weight > 0`

**Q: Shader 循环为什么没效果？**
A: 确保循环没有被编译器优化掉，使用 `[loop]` 属性强制循环

**Q: PhysBone 被限制了怎么办？**
A: 考虑使用 Constraint 替代，或接受 Very Poor 评级

---

## 总结

### 最佳实践组合

| 目标 | 推荐方案 | 控制方式 |
|-----|---------|---------|
| **CPU 消耗** | PhysBone 长链 + Constraint 链 | GameObject Active |
| **GPU 消耗** | 复杂 Shader + Overdraw | Material Property + Alpha |
| **综合消耗** | 上述所有方案组合 | 单一 VRC Parameter 统一控制 |

### 效果预估

| 消耗组合 | CPU 影响 | GPU 影响 | 帧率下降（估算）|
|---------|---------|---------|---------------|
| 仅 Constraint | ⭐⭐⭐ | ⭐ | 5-10% |
| 仅 PhysBone | ⭐⭐⭐⭐ | ⭐ | 10-20% |
| 仅 Shader | ⭐ | ⭐⭐⭐⭐ | 15-30% |
| 仅 Overdraw | ⭐ | ⭐⭐⭐⭐ | 20-40% |
| 全部组合 | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | 50%+ |

---

*文档版本: 1.0*  
*最后更新: 2026-01*
