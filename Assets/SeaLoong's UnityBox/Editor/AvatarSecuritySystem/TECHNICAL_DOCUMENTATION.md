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
| 遮挡 Mesh | `GameObject.m_IsActive = 0/1` | ❌ 不依赖 WD |
| 防御对象 | `GameObject.m_IsActive = 0/1` | ❌ 不依赖 WD |
| 参数反转 | VRCParameterDriver | - |
| 层权重 | VRCAnimatorLayerControl | - |

**状态机**:
```
Remote (默认) ──[IsLocal]──> Locked ──[PARAM_PASSWORD_CORRECT]──> Unlocked
     │                           │                                    │
 (其他玩家)                  (显示遮挡Mesh)                       (隐藏遮挡Mesh)
 (空动画)                    (隐藏Avatar)                         (显示Avatar)
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
Remote (默认) ──[IsLocal]──> Countdown ──[exitTime=1.0]──> TimeUp
   │                              │                           │
(其他玩家)                   [PARAM_PASSWORD_CORRECT]    [设置 PARAM_TIME_UP=1]
                                  ↓                           ↓
                              Unlocked                    [PARAM_PASSWORD_CORRECT]
                                                              ↓
                                                          Unlocked
```

**警告音效层 (ASS_WarningAudio)**:
```
Remote (默认) ──[IsLocal]──> Waiting (等待警告阶段) ──[exitTime=1.0]──> WarningBeep (循环播放)
   │                                                                        ├── [PARAM_TIME_UP] → Stop
   │                                                                        └── [exitTime=1.0] → 自循环
(其他玩家)
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

1. **m_IsActive 策略**: ASS 完全控制的对象（遮挡Mesh、防御对象、UI Canvas）使用 m_IsActive 控制；Avatar 原有对象使用 Scale=0 隐藏
2. **层权重控制时机**: 必须在所有层添加后才能配置
3. **音频对象**: 使用 spatialBlend=0 确保只有本地玩家能听到
4. **IsLocal 条件**: 所有可能影响其他玩家观感的层都需要 IsLocal 条件

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
| 未使用的 originalMaterial | InitialLockSystem.cs | 添加注释说明 |
| 未使用的常量 | Constants.cs | 移除 PARAM_LOCKED/UNLOCKED |
| 层名称硬编码 | CountdownSystem.cs | 使用 LAYER_WARNING_AUDIO 常量 |
| 注释编号不连续 | GesturePasswordSystem.cs | 4 → 3 |

> **注意**: 第一轮中"Canvas 使用 m_IsActive → 改用 Scale=0"的修复已被第二轮策略覆盖。最终策略：ASS 完全控制的对象（Canvas、遮挡Mesh、防御对象）统一使用 m_IsActive 控制。

### ✅ 已修复问题（第二轮 - 全面检查）

| 问题 | 文件 | 修复方式 |
|------|------|----------|
| 防御激活使用 Scale 控制 | DefenseSystem.cs | 改用 m_IsActive 控制 |
| 防御根对象 Scale=0 | DefenseSystem.cs | 改为 SetActive(false) |
| 遮挡 Mesh 使用 Scale 控制 | InitialLockSystem.cs | 改用 m_IsActive 控制 |
| 倒计时层缺少 IsLocal | CountdownSystem.cs | 添加 Remote 状态和 IsLocal 条件 |
| 警告音效层缺少 IsLocal | CountdownSystem.cs | 添加 Remote 状态和 IsLocal 条件 |
| 锁定层缺少 IsLocal | InitialLockSystem.cs | 添加 Remote 状态和 IsLocal 条件 |
| 失败音效在密码系统 | GesturePasswordSystem.cs | 移除（由防御系统处理） |
| 音效音量过大 | AnimatorUtils.cs | 降低到 0.5，优先级设为 0 |

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
| Transform.localScale | ✅ 支持 | 隐藏 Avatar 原有对象 |
| Transform.position/rotation | ✅ 支持 | - |
| BlendShape | ✅ 支持 | - |
| Material Reference | ✅ 支持 | - |
| **GameObject.m_IsActive** | ❌ **不支持** | **仅用于 ASS 完全控制的对象**（遮挡Mesh、防御对象、UI Canvas）|

**m_IsActive 使用策略**:
- ASS 创建的对象（遮挡Mesh、防御根对象、UI Canvas）可以使用 m_IsActive，因为：
  1. 这些对象初始状态由 ASS 代码控制
  2. 不需要依赖 Write Defaults 恢复
  3. 可以完全禁用 PhysBone/Constraint 等组件避免性能消耗
- Avatar 原有对象必须使用 Scale=0 隐藏

---

## 修复摘要

**总计修复: 13 个问题**

- 严重问题: 5 个 (IsLocal 条件、m_IsActive 策略)
- 中等问题: 5 个 (常量、命名一致性、注释)
- 轻微问题: 3 个 (音量、优先级、文档更新)

---

## 剩余建议（可选）

### 冗余的 Debug.Log 调用

多个地方有硬编码的中文日志字符串，没有通过 I18n 系统：
- `AvatarSecurityPlugin.cs:207`: `"[ASS] 使用现有 FX Controller: {controller.name}"`
- `InitialLockSystem.cs:133`: `"[ASS] 层权重控制：添加层 ..."`

**建议**: 使用 I18n 系统统一管理
