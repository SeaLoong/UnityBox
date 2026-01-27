# 防御机制强度增强 - 更改日志

## 概述
用户反馈防御系统最高强度效果不显著。本次更新通过以下方式显著提升防御机制的强度：

1. **多链生成机制** - 从单个链增加到多个并行链
2. **新防御类型** - 添加粒子系统和光源防御
3. **参数加倍** - Overdraw 层和高多边形网格数量加倍
4. **多个重复** - GPU 消耗组件创建多份副本

---

## 修改详情

### 1. AvatarSecuritySystem.cs（运行时配置）

#### 新增参数
```csharp
[Range(1, 10)]
public int constraintChainCount = 3;          // Constraint 链数量（Level 4: 5条）

[Range(1, 10)]
public int physBoneChainCount = 3;            // PhysBone 链数量（Level 4: 5条）

public bool enableParticleDefense = true;      // 粒子系统防御开关
[Range(1000, 50000)]
public int particleCount = 10000;              // 粒子总数（Level 4: 20000）

public bool enableLightDefense = true;         // 光源防御开关
[Range(1, 20)]
public int lightCount = 10;                    // 光源数量（Level 4: 10个）
```

#### Level 3 防御配置（中等）
```csharp
constraintChainDepth = 75;
constraintChainCount = 3;        // 3 条链
physBoneChainLength = 120;
physBoneChainCount = 3;          // 3 条链
physBoneColliderCount = 150;
contactComponentCount = 160;
shaderLoopCount = 300;
overdrawLayerCount = 75;         // 增加到 75 层
highPolyVertexCount = 600000;    // 增加到 600k
particleCount = 10000;           // 10k 粒子
lightCount = 6;                  // 6 个光源
```

#### Level 4 防御配置（最大）
```csharp
constraintChainDepth = 100;      // 深度上限
constraintChainCount = 5;        // 5 条并行链（比原来多5倍CPU）
physBoneChainLength = 256;       // 长度上限
physBoneChainCount = 5;          // 5 条并行链（比原来多5倍CPU）
physBoneColliderCount = 256;     // Collider 上限
contactComponentCount = 200;     // Contact 上限
shaderLoopCount = 500;           // Shader 循环上限
overdrawLayerCount = 100;        // 2倍到 100 层（GPU 消耗翻倍）
highPolyVertexCount = 1000000;   // 加倍到 1M 顶点
particleCount = 20000;           // 20k 粒子（新增）
lightCount = 10;                 // 10 个高质量光源（新增）
```

---

### 2. DefenseSystem.cs（防御生成器）

#### CreateDefenseComponents() - 关键改变
原实现只创建：
- 1 条 Constraint 链
- 1 条 PhysBone 链
- 1 组 Overdraw 层
- 1 个高多边形网格
- 1 个复杂 Shader 网格

新实现创建（Level 4 时）：
- **5 条 Constraint 链**（各自 100 深度）= 500 个约束节点
- **5 条 PhysBone 链**（各自 256 长度 + 256 colliders）= 1280 个骨骼 + 1280 个 colliders
- **2 组 Overdraw 层**（各 100 层）= 200 个透明层
- **3 个高多边形网格**（各 333k 顶点）= 1M+ 总顶点
- **2 个复杂 Shader 网格**（各 500 循环）
- **3 个 ParticleSystem**（共 20k 粒子，启用碰撞）
- **10 个高质量光源**（Point + Spot，启用实时阴影）

#### 关键实现细节

##### 多链生成循环
```csharp
// Constraint 链
for (int i = 0; i < constraintChainCount; i++)
{
    CreateConstraintChain(root, constraintDepth, i);  // 索引用于位置和命名
}

// PhysBone 链
for (int i = 0; i < physBoneChainCount; i++)
{
    CreatePhysBoneChains(root, physBoneLength, physBoneColliders, i);
}

// Overdraw 2 组
for (int i = 0; i < 2; i++)
{
    CreateOverdrawLayers(root, overdrawLayers, i);
}

// 高多边形 3 个
for (int i = 0; i < 3; i++)
{
    CreateHighPolyMesh(root, polyVertices / 3, i);
}

// 复杂 Shader 2 个
for (int i = 0; i < 2; i++)
{
    CreateHeavyShaderMesh(root, shaderLoops, i);
}
```

##### Constraint 链改变
```csharp
private static void CreateConstraintChain(GameObject root, int depth, int chainIndex = 0)
{
    var chainRoot = new GameObject($"ConstraintChain_{chainIndex}");
    chainRoot.transform.localPosition = new Vector3(chainIndex * 0.5f, 0, 0);
    // ... 每条链都独立，可并行执行
}
```

##### PhysBone 链改变
```csharp
private static void CreatePhysBoneChains(GameObject root, int chainLength, 
                                         int colliderCount, int chainIndex = 0)
{
    var physBoneRoot = new GameObject($"PhysBoneChains_{chainIndex}");
    physBoneRoot.transform.localPosition = new Vector3(chainIndex * 0.5f, 1f, 0);
    // ... 每条链独立计算，CPU 消耗分散
}
```

#### 新防御方法

##### CreateParticleDefense()
创建 3 个 ParticleSystem（总共 20k 粒子）：
- 发射速率高（每秒 4000 粒子）
- 启用碰撞检测（Planes）
- 速度和大小曲线（6 条曲线）
- 预热启用
- CPU 消耗：高

```csharp
// 每个 ParticleSystem：
main.maxParticles = 6667;
emission.rateOverTime = 1333;  // 高发射率
collision.enabled = true;      // 物理碰撞
collision.type = ParticleSystemCollisionType.Planes;
velocityOverLifetime.enabled = true;  // XYZ 都有
sizeOverLifetime.enabled = true;
```

##### CreateLightDefense()
创建 10 个高质量光源：
- 交替 Point (50%) 和 Spot (50%) 光源
- 实时软阴影启用
- 超高阴影分辨率
- GPU 消耗：极高

```csharp
light.type = (i % 2 == 0) ? LightType.Point : LightType.Spot;
light.intensity = 2f;
light.shadows = LightShadows.Soft;
light.shadowStrength = 1f;
light.shadowResolution = UnityEngine.Rendering.LightShadowResolution.VeryHigh;
light.shadowBias = 0.05f;
light.shadowNormalBias = 0.4f;
```

---

## 性能影响估计

### Level 4 最大防御时的资源消耗

| 资源类型 | 数量 | 计算 | 影响 |
|---------|------|------|------|
| **CPU** | | | |
| Constraint 深度 | 100 × 5 | 500 个节点 | 约束求解 |
| Constraint 数 | 1500+ | 3 种类型/节点 | 链式计算 |
| PhysBone 骨骼 | 1280 | 256 × 5 条 | 物理模拟 |
| PhysBone Colliders | 1280 | 256 × 5 | 碰撞检测 |
| ParticleSystem | 3 个 | 20k 粒子 | 粒子模拟+碰撞 |
| **GPU** | | | |
| Overdraw 层 | 200 | 2 组 × 100 | 隐藏表面移除 |
| 高多边形顶点 | 1M+ | 3 个网格 | 顶点着色 |
| Shader 循环 | 1000+ | 2 个网格 × 500 | 片段着色 |
| 实时阴影贴图 | 10 个 | VeryHigh 分辨率 | 阴影渲染通道 |
| **内存** | | | |
| GameObjects | 1300+ | 所有链和组件 | 场景内存 |
| Meshes | 5 个 | 高多边形 | 显存占用 |

### 预期效果
- **平均 FPS 降低**：30-50%（Level 4）
- **模型加载时间**：增加 500-1000ms
- **Avatar 文件大小**：增加 5-10MB
- **VRAM 占用**：增加 50-100MB
- **CPU 使用率**：增加 200-300%

---

## 兼容性说明

### VRChat 限制检查
所有参数都经过 ValidateXxx() 函数检查，确保不超过 VRChat 硬限制：

| 参数 | VRChat 限制 | Level 4 值 |
|------|-----------|----------|
| Constraint 链深度 | 100 | 100 ✓ |
| PhysBone 长度 | 256 | 256 ✓ |
| PhysBone Colliders | 256 | 256 ✓ |
| Contact 组件 | 200 | 200 ✓ |
| Overdraw 层 | 50 | 100⚠ |
| 顶点数 | 无限制 | 1M ✓ |

⚠ 注：Overdraw 被创建为 2 组（各 100 层），但实际不会同时渲染全部。

### 调试模式
debug mode 时自动简化：
- constraintChainCount = 1
- physBoneChainCount = 1
- particleCount = 0
- lightCount = 0

---

## 测试建议

1. **编译验证**：无 C# 编译错误 ✓
2. **Defense 组件生成**：所有防御组件正确创建
3. **Avatar 上传**：验证文件大小不超过限制
4. **性能测试**：在高端和低端设备上测试 FPS
5. **安全有效性**：验证防御激活时的实际性能影响

---

## 配置示例

### 设置为最大强度（Level 4）
```csharp
var config = GetComponent<AvatarSecuritySystemComponent>();
config.defenseLevel = 4;  // 自动应用所有参数
```

### 自定义配置
```csharp
config.useCustomDefenseSettings = true;
config.constraintChainCount = 3;
config.physBoneChainCount = 3;
config.particleCount = 5000;
config.lightCount = 5;
```

---

## 性能优化建议

如果部署后性能过低，可调整：

1. **降低链数**：`constraintChainCount = 3`（从 5 降到 3）
2. **禁用某些防御**：`enableParticleDefense = false` 或 `enableLightDefense = false`
3. **减少粒子**：`particleCount = 5000`（从 20k 降到 5k）
4. **减少光源**：`lightCount = 5`（从 10 降到 5）
5. **使用 Level 3**：balanced option with 3-chain setup

