# ASS 防御系统 - CPU/GPU 防御开关说明

## 新增功能

在**自定义防御模式**(`useCustomDefenseSettings = true`) 下，添加了两个新的高级开关：

### 1. `enableCpuDefense` - 启用/禁用 CPU 防御

**作用**：控制是否生成 CPU 消耗型防御组件

**包含组件**：
- ✅ Constraint 链（约束链）
- ✅ PhysBone 物理骨骼系统
- ✅ Contact Sender/Receiver（接触检测系统）

**关闭时的影响**：
- 所有 CPU 防御组件都会被禁用
- 减少的性能消耗：CPU 占用率 ↓
- 模型文件大小：略微减少

**使用场景**：
```
useCustomDefenseSettings = true
enableCpuDefense = false     // 仅使用GPU防御
enableGpuDefense = true      // 保留GPU防御
```
结果：仅有 Shader 复杂度和过度绘制，无物理计算消耗

---

### 2. `enableGpuDefense` - 启用/禁用 GPU 防御

**作用**：控制是否生成 GPU 消耗型防御组件

**包含组件**：
- ✅ Heavy Shader（复杂着色器）
- ✅ Overdraw 层堆叠（过度绘制）
- ✅ HighPoly Mesh（高多边形网格）

**关闭时的影响**：
- 所有 GPU 防御组件都会被禁用
- 减少的性能消耗：GPU 占用率 ↓
- 视觉效果：更清晰（无 Shader 扭曲）

**使用场景**：
```
useCustomDefenseSettings = true
enableCpuDefense = true      // 保留CPU防御
enableGpuDefense = false     // 仅使用CPU防御
```
结果：仅有物理计算消耗，无视觉干扰

---

## 防御级别中的 CPU/GPU 划分

| 防御方式 | CPU 类型 | GPU 类型 |
|---------|---------|---------|
| **Pure CPU** | Constraint + PhysBone + Contact | ✗ |
| **Pure GPU** | ✗ | Shader + Overdraw + HighPoly |
| **Mixed (推荐)** | Constraint + PhysBone + Contact | Shader + Overdraw |
| **Maximum** | 所有 CPU | 所有 GPU |

---

## 预设等级下的行为

使用预设防御等级（Level 0-4）时：
- **`enableCpuDefense`** 和 **`enableGpuDefense`** 会被**忽略**
- 系统自动根据等级配置所有参数
- 如需精细控制，切换到**自定义模式**

---

## 使用示例

### 示例 1：仅 CPU 防御（轻量级）
```csharp
useCustomDefenseSettings = true;
enableCpuDefense = true;      // CPU: ON
enableGpuDefense = false;     // GPU: OFF
enableConstraintChain = true;
constraintChainDepth = 50;
enablePhysBone = true;
physBoneChainLength = 30;
physBoneColliderCount = 80;
enableContactSystem = true;
contactComponentCount = 150;
```

**结果**：
- ✅ 倒计时结束时物理计算卡顿
- ✅ 模型保持正常显示
- ✅ 文件大小较小

---

### 示例 2：仅 GPU 防御（视觉威慑）
```csharp
useCustomDefenseSettings = true;
enableCpuDefense = false;     // CPU: OFF
enableGpuDefense = true;      // GPU: ON
enableHeavyShader = true;
shaderLoopCount = 300;
enableOverdraw = true;
overdrawLayerCount = 40;
enableHighPolyMesh = true;
highPolyVertexCount = 250000;
```

**结果**：
- ✅ 倒计时结束时屏幕严重花屏或变色
- ✅ CPU 消耗保持正常
- ✅ 强烈的视觉威慑

---

### 示例 3：混合防御（推荐）
```csharp
useCustomDefenseSettings = true;
enableCpuDefense = true;      // CPU: ON
enableGpuDefense = true;      // GPU: ON

// CPU 防御部分
enableConstraintChain = true;
constraintChainDepth = 80;
enablePhysBone = true;
physBoneChainLength = 60;
physBoneColliderCount = 120;

// GPU 防御部分
enableHeavyShader = true;
shaderLoopCount = 200;
enableOverdraw = true;
overdrawLayerCount = 30;
```

**结果**：
- ✅ CPU 和 GPU 同时承受压力
- ✅ 全方位防御
- ✅ 盗贼即使得到模型也无法正常使用

---

## 与预设等级的区别

| 配置方式 | 灵活性 | 易用性 | 推荐用途 |
|---------|--------|--------|---------|
| **预设等级 (0-4)** | ⭐ | ⭐⭐⭐⭐⭐ | 大多数用户 |
| **自定义 + CPU/GPU** | ⭐⭐⭐⭐⭐ | ⭐⭐ | 需要特殊防御配置 |
| **自定义 + 逐个参数** | ⭐⭐⭐⭐⭐ | ⭐ | 专家级用户 |

---

## 为什么需要这两个开关？

**之前的问题**：
- 防御模式逐个控制组件，配置繁琐
- 用户容易遗漏某个防御方式（只启用了CPU，忘了GPU）
- 无法快速切换纯CPU或纯GPU防御

**现在的改进**：
- ✅ 两个简单的布尔开关统一管理防御方式
- ✅ 保留逐个参数的灵活性
- ✅ 减少配置错误的可能性
- ✅ 更清晰的防御逻辑：CPU 防御 vs GPU 防御

---

## 常见问题

**Q: 关闭 enableCpuDefense 后，是否所有组件都会被禁用？**

A: 是的。OnValidate() 会自动禁用以下组件：
- `enableConstraintChain = false`
- `enablePhysBone = false`
- `enableContactSystem = false`

其他参数（如 `constraintChainDepth`）仍然会被保留，方便后续重新启用。

---

**Q: 能同时关闭 CPU 和 GPU 防御吗？**

A: 可以，但此时：
```csharp
enableCpuDefense = false;
enableGpuDefense = false;
```
结果是所有防御组件都被禁用，等同于 `Level 0`。通常不建议这样做。

---

**Q: 这两个开关只在自定义模式下有效吗？**

A: 是的。当 `useCustomDefenseSettings = false` 时，使用预设等级，这两个开关会被忽略。系统会根据 `defenseLevel` 自动配置所有参数。

---

*指南版本: 1.1*  
*对应 ASS 版本: 2.1+*  
*最后更新: 2026-01-24*
