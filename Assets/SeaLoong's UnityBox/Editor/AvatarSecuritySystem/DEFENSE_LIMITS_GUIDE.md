# ASS 防御系统 - VRChat 限制与补满策略指南

## 概述

本指南说明如何在 Avatar Security System (ASS) 中安全地使用防御功能，避免模型因超过 VRChat 限制而无法上传。

---

## VRChat 组件限制

### 1. PhysBone 限制

| 限制项 | 值 | 说明 |
|-------|-----|------|
| **单个 PhysBone 链最大骨骼数** | 256 | 链越长，性能消耗越大 |
| **PhysBone Collider 最大数** | 256 | 单个 PhysBone 可引用的 Collider 数量 |
| **Avatar 等级影响** | Very Poor | 允许更多 PhysBone，但仍需限制 |

**补满策略**：
- 启用 **`physBoneFillToLimit`** 参数
- 系统会自动将链长度和 Collider 数量增加到接近 256
- 同时保留一定余量以兼容用户模型本身的 PhysBone

### 2. Constraint 链限制

| 限制项 | 值 | 说明 |
|-------|-----|------|
| **Constraint 链最大深度** | 100 | 过深会导致计算链过长 |

**补满策略**：
- 启用 **`constraintFillToLimit`** 参数
- 系统会将深度增加到 100
- 每层都会创建一个新的 Constraint 对象

### 3. Contact 系统限制

| 限制项 | 值 | 说明 |
|-------|-----|------|
| **Contact 组件总数** | 200 | Sender + Receiver 总计不超过 200 |
| **每个 Contact 的 Tag** | 无限制 | 但更多 Tag 会增加计算量 |

**补满策略**：
- 启用 **`contactFillToLimit`** 参数
- 系统会创建 100 个 Sender + 100 个 Receiver = 200 个组件
- 每个组件会配置多个碰撞 Tag 以增加处理复杂度

### 4. Shader 和 GPU 限制

| 限制项 | 值 | 说明 |
|-------|-----|------|
| **Shader 循环次数** | 500 | 过多循环导致 GPU 过载 |
| **单个 Mesh 顶点数** | 500,000 | 影响模型等级评定 |
| **Overdraw 层数** | 50 | 过多会严重影响帧率 |

**补满策略**：
- **`shaderFillToLimit`**: 将循环次数增加到 500
- **`highPolyFillToLimit`**: 将顶点数增加到 500k
- 这些参数可单独启用以最大化 GPU 消耗

---

## "重型 Shader" 解释

### 含义

**"重型 Shader"** (Heavy Shader) 是指一种包含复杂计算的着色器程序，会显著增加 GPU 工作量。

### 工作原理

```
输入像素
    ↓
┌─────────────────────────┐
│ 采样多张纹理 (4-16次)    │  ← 每次纹理采样都是 GPU 操作
└─────────────────────────┘
    ↓
┌─────────────────────────┐
│ 执行大量浮点运算         │  ← sin(), pow(), sqrt() 等
│  • 三角函数              │
│  • 指数函数              │  每个像素都要计算
│  • 根号                  │
└─────────────────────────┘
    ↓
┌─────────────────────────┐
│ 循环处理 (100-500 次)    │  ← 每次迭代都做上面的计算
└─────────────────────────┘
    ↓
┌─────────────────────────┐
│ 多 Pass 渲染             │  ← 同一个 Mesh 多次绘制
│ (3-10 个 Pass)          │
└─────────────────────────┘
    ↓
输出像素
```

### 性能影响

| 实现方式 | GPU 消耗 | 视觉效果 | 使用场景 |
|---------|---------|--------|---------|
| 标准 Shader | ⭐ | 正常 | 正常使用 |
| 复杂计算 | ⭐⭐⭐⭐ | 可能变色 | 防御机制 |
| 多 Pass 渲染 | ⭐⭐⭐⭐⭐ | 可能变色 | 极限防御 |

### 为什么不能直接防止盗模

1. **盗模过程**：盗贼只需提取 Avatar 文件即可，与 Shader 无关
2. **真正作用**：让没有正确密码的人**无法正常使用**该 Avatar
3. **威慑效果**：
   - FPS 从 144 → 15 (下降 90%)
   - 玩家角色可能显示为紫色或其他错误颜色
   - 主观体验极差，自然不会使用

### ASS 中的应用

```csharp
// 防御未激活时：Shader loop = 0（无消耗）
material._LoopCount = 0;

// 密码错误或倒计时到期时：Shader loop = 500（最大消耗）
material._LoopCount = 500;  // GPU 彻底卡顿
```

---

## 防御等级配置

### 默认配置（不启用补满）

| 等级 | Constraint | PhysBone 链 | PhysBone Collider | Contact | Shader Loop | Overdraw |
|-----|-----------|-----------|-------------------|---------|-------------|----------|
| 0 | ❌ | ❌ | ❌ | ❌ | 0 | ❌ |
| 1 | 40 | 20 | 50 | 80 | 0 | ❌ |
| 2 | 60 | 30 | 80 | 120 | 100 | 15 |
| 3 | 80 | 60 | 120 | 160 | 200 | 35 |
| 4 | 100 | 100 | 150 | 200 | 300 | 50 |

### 启用补满上限后的配置

| 等级 | Constraint | PhysBone 链 | PhysBone Collider | Contact | Shader Loop | 顶点数 |
|-----|-----------|-----------|-------------------|---------|------------|---------|
| 1 | 100 | 100 | 150 | 200 | - | - |
| 2 | 100 | 150 | 200 | 200 | 500 | - |
| 3 | 100 | 200 | 250 | 200 | 500 | 500k |
| 4 | 100 | **256** | **256** | 200 | 500 | 500k |

> **注意**：Level 4 + 所有补满选项 = 最大防御，但仍保持模型可上传

---

## 使用指南

### 场景 1：保守型防御（确保可上传）

```
useCustomDefenseSettings = false  // 使用预设等级
defenseLevel = 3                  // 中度防御

// 所有补满选项禁用
constraintFillToLimit = false
physBoneFillToLimit = false
contactFillToLimit = false
shaderFillToLimit = false
highPolyFillToLimit = false
```

**结果**：
- ✅ 确保 Avatar 可上传
- ⚠️ 防御强度中等
- ✅ 性能消耗可控

### 场景 2：攻击型防御（最大化防御，仍可上传）

```
useCustomDefenseSettings = false
defenseLevel = 4  // 最大防御

// 全面启用补满
constraintFillToLimit = true      // 深度 → 100
physBoneFillToLimit = true        // 链长 → 256, Collider → 256
contactFillToLimit = true         // Contact → 200
shaderFillToLimit = true          // 循环 → 500
highPolyFillToLimit = true        // 顶点 → 500k
```

**结果**：
- ✅✅ 最大防御强度
- ✅ 系统自动验证参数，确保可上传
- ⚠️ 模型本身需要兼容这些防御（留出余量）

### 场景 3：自定义防御（精细控制）

```
useCustomDefenseSettings = true

// 手动设置各个参数
enableConstraintChain = true
constraintChainDepth = 80        // 自定义深度

enablePhysBone = true
physBoneChainLength = 120        // 自定义链长
physBoneColliderCount = 180      // 自定义 Collider 数

// ... 其他参数
```

**结果**：
- ✅ 完全由用户控制
- ⚠️ 需要自己保证不超限
- ⚠️ 需要理解各参数的影响

---

## 参数验证流程

系统生成防御时会自动执行以下验证：

```
用户设置参数
    ↓
1. 检查是否启用 FillToLimit
    ├─ 是 → 自动提升到 VRChat 限制
    └─ 否 → 保持用户设置
    ↓
2. 强制限制到安全范围
    ├─ Constraint: [10, 100]
    ├─ PhysBone 链: [10, 256]
    ├─ PhysBone Collider: [10, 256]
    ├─ Contact: [10, 200]
    ├─ Shader 循环: [0, 500]
    └─ 顶点数: [50k, 500k]
    ↓
3. 日志输出参数调整
    └─ 如果参数被修改，输出 Warning 或 Log
    ↓
生成防御组件
```

---

## 常见问题

### Q: 为什么我的模型上传失败？

**A**: 检查以下几点：
1. 启用了 `highPolyFillToLimit` 但模型本身已经接近 500k 顶点
   - 解决：禁用此选项，或减少高多边形顶点数
2. 启用了 `physBoneFillToLimit` 但模型本身有大量 PhysBone
   - 解决：禁用此选项，或减少模型自身的 PhysBone
3. 防御等级太高
   - 解决：降低到 Level 2 或 3

### Q: 补满上限会影响性能吗？

**A**: 取决于选项：
- **`constraintFillToLimit`**: 会增加 CPU 消耗
- **`physBoneFillToLimit`**: 会增加 CPU 消耗
- **`shaderFillToLimit`**: 会增加 GPU 消耗（仅防御激活时）
- **`highPolyFillToLimit`**: 会增加顶点处理时间（仅防御激活时）

> **重点**：防御默认是禁用的，只有在倒计时结束后才会激活，不会影响正常使用

### Q: 为什么 PhysBone 限制是 256？

**A**: 
- VRChat 规范允许"Very Poor"等级 Avatar 有大量组件
- 但 256 是一个合理的上限，避免极端性能问题
- 建议模型本身保持在 100-150 以内，为防御系统留出空间

### Q: 重型 Shader 会不会损坏模型？

**A**: 不会。Shader 只是**视觉效果**的改变，不会修改：
- 模型结构
- 骨骼权重
- 纹理数据
- 网格几何

### Q: 可以同时启用所有补满选项吗？

**A**: 可以，但需要验证：
```csharp
// 系统会自动执行 EnforceDefenseParameterLimits()
// 确保所有参数不超过上限
// 在 Unity 编辑器的 Console 中会看到日志：
// [ASS] PhysBone链长度: 300 → 256 (启用补满上限)
// [ASS] 高多边形顶点数: 600000 → 500000 (启用补满上限)
```

---

## 总结

| 目标 | 推荐配置 | 特点 |
|-----|---------|------|
| **标准防御** | Level 2, 无补满 | 平衡性能与防御 |
| **强防御** | Level 3, 无补满 | 有效威慑，可靠上传 |
| **极限防御** | Level 4, 所有补满 | 最大防御，系统验证确保可上传 |
| **精细控制** | 自定义模式 | 完全灵活，需要用户负责验证 |

---

*指南版本: 1.0*  
*对应 ASS 版本: 2.0+*  
*最后更新: 2026-01-24*
