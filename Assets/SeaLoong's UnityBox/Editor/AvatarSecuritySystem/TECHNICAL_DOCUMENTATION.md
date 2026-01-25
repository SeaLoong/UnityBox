# Avatar Security System (ASS) - 技术文档

## 系统概述

Avatar Security System (ASS) 是一个 VRChat Avatar 安全保护系统，通过手势密码验证来保护 Avatar 不被未授权使用。

---

## 功能模块

### 1. 初始锁定系统 (InitialLockSystem)

**文件**: `InitialLockSystem.cs`

**功能**:
- 锁定状态下隐藏 Avatar 内容
- 解锁状态下恢复 Avatar
- 参数驱动：锁定时反转参数，解锁时恢复
- 层权重控制：解锁后禁用其他 ASS 层

**技术实现**:

| 功能 | 实现方式 | WD 恢复 |
|------|----------|---------|
| 隐藏对象 | `Transform.localScale = 0` | ✅ 支持 |
| 材质锁定 | `m_Materials.Array.data[n] = null` | ✅ 支持 |
| 参数反转 | VRCParameterDriver | - |
| 层权重 | VRCAnimatorLayerControl | - |

**状态机**:
```
Locked (默认) ──[PARAM_PASSWORD_CORRECT]──> Unlocked
```

**层权重控制**:
- 锁定状态: 所有 ASS 层权重 = 1
- 解锁状态: 除 InitialLock 外所有 ASS 层权重 = 0

---

### 2. 手势密码系统 (GesturePasswordSystem)

**文件**: `GesturePasswordSystem.cs`

**功能**:
- 检测 VRChat 手势输入
- 尾部序列匹配（输入 123456 可匹配密码 456）
- 手势稳定时间检测（需保持手势一定时间）
- 容错机制（短暂误触不会重置）

**技术实现**:

**状态结构** (每步):
```
Wait_Input ──[正确手势]──> Step_N_Holding (0.15s)
                              ├── [保持正确+超时] → Step_N_Confirmed
                              ├── [Idle] → 自循环
                              └── [错误] → Wait_Input

Step_N_Confirmed ──[正确下一位]──> Step_N+1_Holding
                    ├── [Idle] → 自循环
                    └── [错误] → Step_N_ErrorTolerance (0.3s)

Step_N_ErrorTolerance ──[超时]──> Wait_Input
                        ├── [正确下一位] → Step_N+1_Holding
                        └── [Idle] → 自循环
```

**最后一步优化**:
- 最后一步没有 Confirmed/ErrorTolerance 状态
- Holding → Password_Success 直接转换

**时间参数**:
- `gestureHoldTime`: 手势保持时间 (默认 0.15s)
- `gestureErrorTolerance`: 容错时间 (默认 0.3s)

---

### 3. 倒计时系统 (CountdownSystem)

**文件**: `CountdownSystem.cs`

**功能**:
- 倒计时进度条显示
- 超时触发 TimeUp 参数
- 警告阶段循环播放音效

**层结构**:

**倒计时层 (ASS_Countdown)**:
```
Countdown (默认) ──[exitTime=1.0]──> TimeUp ──> [设置 PARAM_TIME_UP=1]
     └── [PARAM_PASSWORD_CORRECT] → Unlocked
```

**警告音效层 (ASS_WarningAudio)**:
```
Waiting (等待警告阶段) ──[exitTime=1.0]──> WarningBeep (循环播放)
                                              ├── [PARAM_TIME_UP] → Stop
                                              └── [exitTime=1.0] → 自循环
```

---

### 4. 防御系统 (DefenseSystem)

**文件**: `DefenseSystem.cs`

**功能**:
- 倒计时结束后激活防御
- 仅本地生效 (IsLocal)
- CPU/GPU 性能消耗防御

**防御组件**:

| 类型 | 组件 | 作用 |
|------|------|------|
| CPU | Constraint 链 | 深层嵌套约束计算 |
| CPU | PhysBone + Collider | 物理模拟消耗 |
| CPU | Contact Sender/Receiver | 碰撞检测消耗 |
| GPU | Overdraw 层叠 | 多层透明渲染 |
| GPU | 高面数 Mesh | 顶点处理消耗 |
| GPU | 复杂 Shader | 片段着色器循环 |

**状态机**:
```
Inactive (默认) ──[IsLocal && TimeUp]──> Active
```

---

### 5. 反馈系统 (FeedbackSystem)

**文件**: `FeedbackSystem.cs`

**功能**:
- 创建 HUD Canvas
- 倒计时进度条
- UI 锚定到头部

---

## Animator 参数

| 参数名 | 类型 | 用途 |
|--------|------|------|
| `ASS_PasswordCorrect` | Bool | 密码验证成功 |
| `ASS_TimeUp` | Bool | 倒计时结束 |
| `ASS_PasswordError` | Trigger | 密码输入错误 |
| `IsLocal` | Bool | VRChat 内置：是否本地用户 |
| `GestureLeft/Right` | Int | VRChat 内置：手势值 |

---

## Animator 层

| 层名 | 权重 | 功能 |
|------|------|------|
| ASS_InitialLock | 1.0 | 锁定/解锁控制 |
| ASS_PasswordInput | 1.0 | 密码验证 |
| ASS_Countdown | 1.0 | 倒计时 |
| ASS_WarningAudio | 1.0 | 警告音效 (可选) |
| ASS_Defense | 1.0 | 防御激活 (可选) |

---

## VRC State Behaviours

| 行为 | 使用位置 | 作用 |
|------|----------|------|
| VRCAnimatorPlayAudio | Password_Success, TimeUp_Failed, WarningBeep | 播放音效 |
| VRCAvatarParameterDriver | Locked, Unlocked, Password_Success, TimeUp | 设置参数 |
| VRCAnimatorLayerControl | Locked, Unlocked | 控制层权重 |

---

## Write Defaults 策略

所有状态启用 `writeDefaultValues = true`

| 属性类型 | WD 恢复 | 备注 |
|----------|---------|------|
| Transform (Scale) | ✅ | 用于隐藏对象 |
| Material Reference | ✅ | 用于材质锁定 |
| BlendShape | ✅ | - |
| GameObject.m_IsActive | ❌ | VRChat 限制，不使用 |

---

## 生成流程

```
AvatarSecurityPlugin.GenerateSystem()
├── 1. 创建 FX Controller
├── 2. 创建 UI Canvas
├── 3. 设置 AudioSource
├── 4. 创建各层
│   ├── InitialLockSystem.CreateLockLayer()
│   ├── GesturePasswordSystem.CreatePasswordLayer()
│   ├── CountdownSystem.CreateCountdownLayer()
│   ├── CountdownSystem.CreateWarningAudioLayer() [可选]
│   └── DefenseSystem.CreateDefenseLayer() [可选]
├── 5. 配置层权重控制
├── 6. 反转参数 [构建模式]
└── 7. 保存
```

---

## 已知问题和注意事项

1. **m_IsActive 不受 WD 影响**: VRChat 限制，改用 Scale=0 隐藏对象
2. **层权重控制时机**: 必须在所有层添加后才能配置
3. **音频对象**: 不应在解锁动画中禁用，否则成功音效无法播放
4. **参数反转**: 使用 Modular Avatar 或 VRCParameterDriver 实现

---

## 配置选项

| 选项 | 默认值 | 说明 |
|------|--------|------|
| gesturePassword | [1,7,2,4] | 密码序列 |
| useRightHand | false | 使用右手 |
| countdownDuration | 30s | 倒计时时长 |
| warningThreshold | 10s | 警告阈段 |
| gestureHoldTime | 0.15s | 手势保持时间 |
| gestureErrorTolerance | 0.3s | 容错时间 |
| invertParameters | true | 反转参数 |
| disableRootChildren | true | 隐藏根对象 |
| disableDefense | false | 禁用防御 |

---

## 文件结构

```
Editor/AvatarSecuritySystem/
├── AvatarSecurityPlugin.cs    # NDMF 插件入口
├── InitialLockSystem.cs       # 锁定系统
├── GesturePasswordSystem.cs   # 密码系统
├── CountdownSystem.cs         # 倒计时系统
├── DefenseSystem.cs           # 防御系统
├── FeedbackSystem.cs          # UI 反馈
├── AnimatorUtils.cs           # Animator 工具
├── AnimationClipGenerator.cs  # 动画生成
├── Constants.cs               # 常量定义
└── I18n.cs                    # 国际化

Runtime/
└── AvatarSecuritySystem.cs    # 组件定义

Editor/
└── AvatarSecuritySystemEditor.cs  # 自定义 Inspector
```

---

## 代码审查发现的问题

### ✅ 已修复问题（第一轮）

| 问题 | 文件 | 修复方式 |
|------|------|----------|
| `continue` 位置错误 | GesturePasswordSystem.cs | 移动到 else 块外部 |
| Canvas 使用 m_IsActive | InitialLockSystem.cs | 改用 Scale=0 |
| 未使用的 originalMaterial | InitialLockSystem.cs | 添加注释说明 |
| 未使用的常量 | Constants.cs | 移除 PARAM_LOCKED/UNLOCKED |
| 层名称硬编码 | CountdownSystem.cs | 使用 LAYER_WARNING_AUDIO 常量 |
| 注释编号不连续 | GesturePasswordSystem.cs | 4 → 3 |

### ✅ 已修复问题（第二轮 - 全面检查）

| 问题 | 文件 | 修复方式 |
|------|------|----------|
| 防御激活使用 m_IsActive | DefenseSystem.cs | 改用 Scale=0 控制 |
| 防御根对象 SetActive(false) | DefenseSystem.cs | 改为 Scale=0 + SetActive(true) |
| CreateGameObjectActiveClip 使用 m_IsActive | AnimationClipGenerator.cs | 添加 [Obsolete] 警告 |

### 🟡 设计决策说明

#### Runtime 组件 Tooltip 使用 `#{key}` 格式

**位置**: `Runtime/AvatarSecuritySystem.cs`

**说明**: 这些 `#{key}` 格式的 Tooltip 是设计意图标记，表示这些字段由 CustomEditor (`AvatarSecuritySystemEditor.cs`) 完全控制 UI 显示。用户不会看到这些原始文本，因为 Inspector 界面完全由自定义编辑器渲染。

#### I18n 系统设计

**特点**:
- 支持中文、英文、日文
- 自动检测系统语言
- 回退机制：繁体中文 → 简体中文 → 英文
- 1307 行翻译定义

### 🟢 剩余建议（可选）

1. **日志国际化**: 部分 Debug.Log 使用硬编码中文
2. **防御 Shader**: CreateHeavyShaderMaterial 使用 Standard Shader 作为占位符

---

## VRChat 属性行为总结

| 属性 | Write Defaults 恢复 | ASS 使用方式 |
|------|---------------------|--------------|
| Transform.localScale | ✅ 支持 | 隐藏对象、UI、防御组件 |
| Transform.position/rotation | ✅ 支持 | - |
| BlendShape | ✅ 支持 | - |
| Material Reference | ✅ 支持 | 材质锁定 |
| **GameObject.m_IsActive** | ❌ **不支持** | **禁止使用** |

---

## 修复摘要

**总计修复: 9 个问题**

- 严重问题: 3 个 (continue 位置、m_IsActive 相关)
- 中等问题: 4 个 (常量、命名一致性)
- 轻微问题: 2 个 (注释、警告标记)

---

## 代码审查发现的问题

### ✅ 已修复问题

#### 1. GesturePasswordSystem.cs:174 - `continue` 语句位置错误 ✅ 已修复

**原问题**: `if (isLastStep) continue;` 放在 `else` 块内，导致只对 `i > 0` 的最后一步生效

**修复**: 将 `continue` 移动到 `if`/`else` 块外部独立判断

#### 2. CreateUnlockClip - 使用 m_IsActive 禁用 Canvas ✅ 已修复

**原问题**: VRChat 的 `m_IsActive` 不受 Write Defaults 影响

**修复**: 改用 `Scale=0` 隐藏 UI Canvas

#### 3. MaterialSlotInfo.originalMaterial 未使用 ✅ 已修复

**修复**: 添加注释说明保留原因

#### 4. Constants 中未使用的常量 ✅ 已修复

**修复**: 移除未使用的 `PARAM_LOCKED` 和 `PARAM_UNLOCKED`

#### 5. CountdownSystem 警告音效层名称硬编码 ✅ 已修复

**修复**: 添加 `LAYER_WARNING_AUDIO` 常量并使用

#### 6. 注释编号不连续 ✅ 已修复

**修复**: 将 `4.` 改为 `3.`

### 🟢 剩余建议（可选）

#### 冗余的 Debug.Log 调用

多个地方有硬编码的中文日志字符串，没有通过 I18n 系统：
- `AvatarSecurityPlugin.cs:207`: `"[ASS] 使用现有 FX Controller: {controller.name}"`
- `InitialLockSystem.cs:133`: `"[ASS] 层权重控制：添加层 ..."`

**建议**: 使用 I18n 系统统一管理

---

## 修复摘要

| 问题 | 状态 | 修复方式 |
|------|------|----------|
| continue 位置错误 | ✅ 已修复 | 移动到 else 块外部 |
| m_IsActive 用于 Canvas | ✅ 已修复 | 改用 Scale=0 |
| 未使用的 originalMaterial | ✅ 已修复 | 添加注释说明 |
| 未使用的 PARAM_LOCKED/UNLOCKED | ✅ 已修复 | 已移除 |
| 层名称常量化 | ✅ 已修复 | 添加 LAYER_WARNING_AUDIO |
| 注释编号不连续 | ✅ 已修复 | 4 → 3 |
