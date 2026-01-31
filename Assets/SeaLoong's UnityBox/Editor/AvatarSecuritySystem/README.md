# Avatar Security System (ASS) 🔒

**VRChat Avatar 防盗密码保护系统**

[![Unity](https://img.shields.io/badge/Unity-2019.4.31f1+-black.svg)](https://unity.com/)
[![VRChat](https://img.shields.io/badge/VRChat-SDK3-blue.svg)](https://vrchat.com/)
[![NDMF](https://img.shields.io/badge/NDMF-1.3.0+-green.svg)](https://github.com/bdunderscore/ndmf)

---

## 📑 目录

1. [系统概述](#系统概述)
2. [快速开始](#快速开始)
3. [技术架构](#技术架构)
4. [功能模块](#功能模块)
5. [配置选项](#配置选项)
6. [API 参考](#api-参考)
7. [常见问题](#常见问题)

---

## 系统概述

### 🎯 项目目标

Avatar Security System (ASS) 是一个用于 VRChat Avatar 的防盗保护系统。通过**手势密码**和**倒计时机制**，防止恶意玩家通过缓存提取等手段盗取您的 Avatar。

### ✨ 核心特性

- 🔐 **手势密码保护** - 使用 VRChat 的 8 种手势组合作为密码
- ⏱️ **倒计时机制** - 限时输入（默认30秒），增加破解难度
- 🎨 **视觉/音频反馈** - 实时提示用户输入状态
- 🔒 **初始锁定** - Avatar 启动时所有功能被禁用
- ⚡ **智能防御** - 仅对穿戴者生效（IsLocal），不影响其他玩家
- 🛠️ **非破坏性** - 编辑时零影响，仅构建时生成
- 🔧 **NDMF 集成** - 无缝集成到 VRChat Avatar 构建流程

### 🎭 工作流程

```
Avatar 启动
    ↓
🔒 所有功能锁定（对象禁用 + 遮挡Mesh显示）
    ↓
⏱️ 倒计时开始（默认 30 秒）
    ↓
🤚 用户输入手势密码
    ├─ ✅ 正确 → PASSWORD_CORRECT = true → 解锁 → 正常使用
    ├─ ❌ 错误 → 容错机制 → 可继续输入
    └─ ⏰ 超时 → TIME_UP = true → 触发防御 → 性能下降
```

---

## 快速开始

### 📦 安装依赖

1. 安装 VRChat SDK3-Avatars (3.5.0+)
2. 安装 NDMF (1.3.0+) via VCC
3. 导入 ASS 文件到 Unity 项目

### ⚙️ 配置步骤

#### 步骤 1: 添加组件

1. 选择你的 Avatar Root 对象
2. Add Component → "Avatar Security System (ASS)"

#### 步骤 2: 设置密码

**VRChat 手势对照表：**
```
手势 0: Idle ✋        手势 4: Victory ✌
手势 1: Fist ✊        手势 5: RockNRoll 🤘
手势 2: HandOpen 🖐    手势 6: HandGun 🔫
手势 3: Fingerpoint ☝  手势 7: ThumbsUp 👍
```

**配置示例：**
```yaml
Gesture Password: [1, 7, 2, 4]  # Fist → ThumbsUp → HandOpen → Victory
Use Right Hand: false            # 使用左手
```

**密码强度评级：**
- **Weak (弱)**: < 4 位
- **Medium (中)**: 4-5 位，或手势种类少于 4 种
- **Strong (强)**: ≥ 6 位，且至少使用 4 种不同手势

#### 步骤 3: 倒计时配置

```yaml
Countdown Duration: 30秒     # 30-120秒可选
Warning Threshold: 10秒      # 固定值，警告阶段开始
```

#### 步骤 4: 防御配置（可选）

```yaml
Defense Level: 3            # 0=仅密码, 1=密码+CPU, 2=密码+CPU+GPU(中低), 3=密码+CPU+GPU(最高)
```

**防御等级说明：**
- **等级 0**: 仅密码系统，不生成任何防御组件
- **等级 1**: 密码 + CPU 防御（约束链、PhysBone、Contact - 最高参数）
- **等级 2**: 密码 + CPU 防御（最高）+ GPU 防御（材质、粒子、光源 - 中低参数）
- **等级 3**: 密码 + CPU 防御（最高）+ GPU 防御（所有参数最高）

#### 步骤 5: 构建上传

1. 使用 VRChat SDK 的 "Build & Publish"
2. ASS 会在构建时自动生成
3. 上传到 VRChat 并在游戏中测试

### 🎮 在 VRChat 中使用

#### 解锁流程

1. 穿戴 Avatar 后会看到倒计时提示（30秒）
2. 按照配置的顺序做出手势
   - 使用左手或右手（取决于配置）
   - 每个手势保持 0.15 秒（可配置）
3. 密码正确：✅ 成功音效 → 解锁
4. 密码错误：❌ 容错机制，可继续输入
5. 倒计时结束（未解锁）：⚠️ 触发防御措施（仅对穿戴者）

---

## 技术架构

### 🏗️ 系统组成

```
AvatarSecuritySystemComponent (MonoBehaviour)
    ↓ 配置参数
AvatarSecurityPlugin (NDMF Plugin)
    ↓ BuildPhase.Optimizing
生成 5 个 AnimatorController Layers:
    ├─ ASS_InitialLock (初始锁定)
    ├─ ASS_PasswordInput (手势密码验证)
    ├─ ASS_Countdown (倒计时系统)
    ├─ ASS_WarningAudio (警告音效，可选)
    └─ ASS_Defense (防御措施，可选)
    ↓
AnimationClips + GameObject Hierarchy + VRC Components
```

### 📁 文件结构

```
Assets/SeaLoong's UnityBox/
├─ Runtime/
│   ├─ AvatarSecuritySystem.cs          # 主组件类（配置）
│   └─ SeaLoong.Runtime.asmdef          # Runtime 程序集定义
│
├─ Editor/
│   ├─ AvatarSecuritySystemEditor.cs    # 自定义 Inspector UI
│   ├─ SeaLoong.asmdef                  # Editor 程序集定义
│   └─ AvatarSecuritySystem/
│       ├─ Constants.cs                 # 常量定义
│       ├─ AnimatorUtils.cs             # Animator 工具
│       ├─ AnimationClipGenerator.cs    # 动画剪辑生成器
│       ├─ I18n.cs                      # 国际化支持（中/英/日）
│       ├─ AvatarSecurityPlugin.cs      # NDMF 插件入口
│       ├─ InitialLockSystem.cs         # 初始锁定系统生成器
│       ├─ GesturePasswordSystem.cs     # 手势密码系统生成器
│       ├─ CountdownSystem.cs           # 倒计时系统生成器
│       ├─ FeedbackSystem.cs            # 反馈系统生成器
│       └─ DefenseSystem.cs             # 防御系统生成器
│
└─ Resources/
    └─ AvatarSecuritySystem/
        ├─ PasswordSuccess.mp3          # 成功音效
        └─ CountdownWarning.mp3        # 警告音效
```

### 🔗 依赖关系

```
Unity 2019.4.31f1+
    ↓
VRChat SDK3-Avatars 3.5.0+
    ↓
NDMF 1.3.0+
```

---

## 功能模块

### 1️⃣ 初始锁定系统 (InitialLockSystem)

#### 功能
- 锁定状态下显示遮挡 Mesh（覆盖视角）
- 隐藏 Avatar 原有对象（通过 Scale=0）
- 解锁状态下恢复 Avatar 显示
- 层权重控制：解锁后禁用其他 ASS 层

#### 技术实现

**状态机结构：**
```
ASS_InitialLock Layer
├─ Remote (默认) - 其他玩家看到的默认状态
│   └─ 遮挡Mesh隐藏，Avatar正常显示
├─ Locked - 本地玩家锁定时
│   ├─ 显示遮挡Mesh（m_IsActive=1）
│   ├─ 隐藏Avatar对象（Scale=0）
│   └─ 显示UI Canvas
└─ Unlocked - 解锁后
    ├─ 隐藏遮挡Mesh（m_IsActive=0）
    ├─ 恢复Avatar显示（Scale=1）
    ├─ 隐藏UI Canvas
    └─ 禁用其他ASS层（权重=0）
```

**转换条件：**
- Remote → Locked: `IsLocal == true && ASS_PasswordCorrect == false`
- Remote → Unlocked: `IsLocal == true && ASS_PasswordCorrect == true`
- Locked → Unlocked: `ASS_PasswordCorrect == true`

**关键实现：**
- 遮挡 Mesh：使用 VRCParentConstraint 绑定到头部，始终挡住视角
- 对象隐藏：使用 `Transform.localScale = 0`（支持 Write Defaults 恢复）
- ASS 对象控制：使用 `GameObject.m_IsActive`（ASS 完全控制的对象）

---

### 2️⃣ 手势密码系统 (GesturePasswordSystem)

#### 功能
- 检测 VRChat 手势输入（GestureLeft/GestureRight）
- 尾部序列匹配（输入 123456 可匹配密码 456）
- 手势稳定时间检测（需保持手势一定时间，默认0.15秒）
- 容错机制（短暂误触不会重置，容错时间0.3秒）

#### 技术实现

**状态结构（每步）：**
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
                        ├── [密码第一位] → Step_1_Holding (尾部匹配)
                        └── [Idle] → 自循环
```

**最后一步优化：**
- 最后一步没有 Confirmed/ErrorTolerance 状态
- Holding → Password_Success 直接转换

**参数驱动：**
- 成功时设置 `ASS_PasswordCorrect = true`（通过 VRCAvatarParameterDriver）
- 参数同步：`networkSynced = true`（其他玩家可以看到解锁状态）

---

### 3️⃣ 倒计时系统 (CountdownSystem)

#### 功能
- 倒计时进度条显示（3D UI，绑定到头部）
- 超时触发 TimeUp 参数
- 警告阶段循环播放音效（最后10秒）

#### 技术实现

**倒计时层 (ASS_Countdown)：**
```
Remote (默认) ──[IsLocal]──> Countdown ──[exitTime=1.0]──> TimeUp
   │                              │                           │
(其他玩家)                   [PARAM_PASSWORD_CORRECT]    [设置 PARAM_TIME_UP=1]
                                  ↓                           ↓
                              Unlocked                    [PARAM_PASSWORD_CORRECT]
                                                              ↓
                                                          Unlocked
```

**警告音效层 (ASS_WarningAudio)：**
```
Remote (默认) ──[IsLocal]──> Waiting ──[exitTime=1.0]──> WarningBeep (循环播放)
   │                                                                  ├── [PARAM_TIME_UP] → Stop
   │                                                                  └── [exitTime=1.0] → 自循环
(其他玩家)
```

**动画实现：**
- 倒计时动画：控制进度条的 `localScale.x`（从1到0）
- 警告音效：使用 VRCAnimatorPlayAudio 行为，每秒播放一次

---

### 4️⃣ 反馈系统 (FeedbackSystem)

#### 功能
- 创建 3D HUD Canvas（绑定到头部）
- 倒计时进度条（3D Quad，红色条）
- UI 锚定到头部（使用 VRCParentConstraint）

#### 技术实现

**UI 结构：**
```
ASS_UI (Canvas)
└─ CountdownBar
   ├─ Background (白色背景)
   └─ Bar (红色进度条，通过 localScale.x 控制长度)
```

**位置控制：**
- 使用 VRCParentConstraint 绑定到头部骨骼
- 位置偏移：头部前方15cm，下方2cm
- 默认禁用，锁定时通过动画启用

---

### 5️⃣ 防御系统 (DefenseSystem)

#### 功能
- 倒计时结束后激活防御（仅本地生效）
- CPU/GPU 性能消耗防御
- 多种防御机制组合

#### 防御机制

| 类型 | 组件 | 作用 | 配置参数 |
|------|------|------|---------|
| **CPU** | Constraint 链 | 深层嵌套约束计算 | `constraintChainDepth` (10-100), `constraintChainCount` (1-50) |
| **CPU** | PhysBone + Collider | 物理模拟消耗 | `physBoneChainLength` (10-256), `physBoneChainCount` (1-50), `physBoneColliderCount` (10-256) |
| **CPU** | Contact Sender/Receiver | 碰撞检测消耗 | `contactComponentCount` (10-200) |
| **GPU** | Overdraw 层叠 | 多层透明渲染 | `overdrawLayerCount` (5-10000) |
| **GPU** | 高面数 Mesh | 顶点处理消耗 | `highPolyVertexCount` (50000-100000000) |
| **GPU** | 防御 Shader | 片段着色器循环、视差映射、光线步进、次表面散射 | `shaderLoopCount` (0-1000000) |
| **GPU** | 粒子系统 | 粒子渲染消耗 | `particleCount` (1000-1000000), `particleSystemCount` (1-50) |
| **GPU** | 光源 | 实时阴影计算 | `lightCount` (1-50) |
| **GPU** | 材质球 | Draw Calls 增加 | `materialCount` (1-500) |

#### 防御等级预设

**Level 0**: 禁用所有防御
**Level 1**: 基础 CPU 防御
- Constraint: 深度30, 1条链
- PhysBone: 长度30, 1条链, 50个Collider
- Contact: 80个组件

**Level 2**: CPU + 基础 GPU 防御
- Level 1 的所有内容
- 防御 Shader: 150次循环
- Overdraw: 20层

**Level 3**: CPU + 增强 GPU 防御
- Constraint: 深度75, 3条链
- PhysBone: 长度120, 3条链, 150个Collider
- Contact: 160个组件
- 防御 Shader: 300次循环
- Overdraw: 75层
- High Poly: 600k顶点
- Particle: 10000个粒子, 1个系统
- Light: 6个光源

**Level 4**: 最大防御强度（默认）
- Constraint: 深度100, 50条链
- PhysBone: 长度256, 50条链, 256个Collider
- Contact: 200个组件
- 防御 Shader: 500k次循环
- Overdraw: 5000层
- High Poly: 5000万顶点（分散到3个Mesh）
- Particle: 500k个粒子, 50个系统
- Light: 50个光源
- Material: 500个材质球

#### 技术实现

**状态机：**
```
ASS_Defense Layer
├─ Inactive (默认) - 防御未激活
└─ Active - 防御激活
    └─ 转换条件: IsLocal == true && ASS_TimeUp == true
```

**防御对象控制：**
- 使用 `GameObject.m_IsActive` 控制防御根对象
- 锁定时禁用（`m_IsActive=0`），避免性能消耗
- 防御激活时启用（`m_IsActive=1`），触发所有防御组件

**防御 Shader：**
- 在构建时从模板文件 `DefenseShader.shader` 生成
- 包含多种GPU密集功能：
  - 主循环计算（可配置循环次数，最多100万次）
  - 视差映射（640-2560层迭代）
  - FBM噪声（128层Perlin噪声）
  - 光线步进（Ray Marching，64步）
  - 次表面散射（8次迭代）
  - 32个伪光源计算
  - 多次色彩空间转换（RGB↔HSV，100次）
  - 复杂数学运算（三角函数、对数、指数等）
- 如果模板不存在，回退到 `Standard` Shader

**VRChat 限制处理：**
- 自动验证参数不超过 VRChat 限制
- PhysBone Collider 数量考虑现有 Collider
- 高多边形顶点分散到多个 Mesh（避免单Mesh 65k限制）

---

## 配置选项

### AvatarSecuritySystemComponent 属性

#### 密码配置
```csharp
public List<int> gesturePassword;        // 手势密码序列 (1-7)
public bool useRightHand;                // 使用右手(true)或左手(false)
public float gestureHoldTime;            // 手势保持时间 (0.1-1.0秒)
public float gestureErrorTolerance;      // 容错时间 (0.1-1.0秒)
```

#### 倒计时配置
```csharp
[Range(30f, 120f)]
public float countdownDuration;          // 倒计时时长（秒）
public float warningThreshold;           // 警告阈值（固定10秒）
public float inputCooldown;              // 输入间隔（固定0.5秒）
```

#### 防御配置
```csharp
[Range(0, 4)]
public int defenseLevel;                  // 防御等级 (0-4)
public bool useCustomDefenseSettings;    // 使用自定义防御设置

// ==================== CPU 防御 ====================
public bool enableCpuDefense;            // 启用CPU防御

// --- 约束链 (Constraint Chain) ---
public bool enableConstraintChain;       // 启用约束链防御
public int constraintChainCount;         // 约束链数量 (1-50)
public int constraintChainDepth;         // 每条约束链深度 (10-100)

// --- PhysBone 链 ---
public bool enablePhysBone;              // 启用PhysBone链防御
public int physBoneChainCount;          // PhysBone链数量 (1-50)
public int physBoneChainLength;          // 每条PhysBone链长度 (10-256)
public int physBoneColliderCount;       // PhysBone Collider数量 (10-256)

// --- Contact ---
public bool enableContactSystem;        // 启用Contact系统防御
public int contactComponentCount;       // Contact组件数量 (10-200)

// ==================== GPU 防御 ====================
public bool enableGpuDefense;           // 启用GPU防御

// --- 材质防御 (Material Defense) ---
public bool enableMaterialDefense;      // 启用材质防御（使用防御Shader）
public int materialCount;               // 材质数量 (1-100)
public int shaderLoopCount;             // Shader循环次数 (0-1000000)
public int overdrawLayerCount;          // Overdraw层数 (5-1000)
public int highPolyVertexCount;         // 高面数顶点数 (10000-500000)

// --- 粒子防御 (Particle Defense) ---
public bool enableParticleDefense;      // 启用粒子防御
public int particleSystemCount;         // 粒子系统数量 (1-100)
public int particleCount;               // 粒子总数 (1000-500000)

// --- 光源防御 (Light Defense) ---
public bool enableLightDefense;         // 启用光源防御
public int lightCount;                  // 光源数量 (1-100)
```

#### 高级选项
```csharp
public bool enableInPlayMode;           // Play模式测试（无防御）
public bool disableDefense;             // 禁用防御生成
public bool lockFxLayers;               // 锁定FX层权重
public bool disableRootChildren;        // 隐藏根级子对象
public SystemLanguage uiLanguage;       // UI语言（Unknown=自动）
```

---

## API 参考

### Animator 参数

| 参数名 | 类型 | 默认值 | 同步 | 说明 |
|--------|------|--------|------|------|
| `ASS_PasswordCorrect` | Bool | false | ✅ 是 | 密码验证成功标志 |
| `ASS_TimeUp` | Bool | false | ❌ 否 | 倒计时结束标志（本地参数） |
| `IsLocal` | Bool | - | - | VRChat 内置（穿戴者=true） |
| `GestureLeft` | Int | 0 | - | VRChat 内置（左手手势 0-7） |
| `GestureRight` | Int | 0 | - | VRChat 内置（右手手势 0-7） |

### Animator 层

| 层名 | 权重 | 功能 |
|------|------|------|
| `ASS_InitialLock` | 1.0 | 锁定/解锁控制 |
| `ASS_PasswordInput` | 1.0 | 密码验证 |
| `ASS_Countdown` | 1.0 | 倒计时 |
| `ASS_WarningAudio` | 1.0 | 警告音效（可选） |
| `ASS_Defense` | 1.0 | 防御激活（可选） |

### VRC State Behaviours

| 行为 | 使用位置 | 作用 |
|------|----------|------|
| `VRCAnimatorPlayAudio` | Password_Success, WarningBeep | 播放音效 |
| `VRCAvatarParameterDriver` | Password_Success, TimeUp | 设置参数 |
| `VRCAnimatorLayerControl` | Locked, Unlocked | 控制层权重 |

---

## 常见问题

### 🔐 安全性问题

#### Q: 密码会被破解吗？
**A**: 可能，但难度较大：
- 8 种手势的 N 位密码：8^N 种组合
  - 4 位：4,096 种
  - 6 位：262,144 种
  - 8 位：16,777,216 种
- 配合 30 秒倒计时，暴力破解不现实

#### Q: 其他玩家会看到防御效果吗？
**A**: 不会。防御通过 `IsLocal` 参数隔离，仅穿戴者受影响。其他玩家看到的是正常 Avatar。

### 💡 使用问题

#### Q: 我忘记密码了怎么办？
**A**: 三种解决方案：
1. 在 Unity 项目中查看 Inspector 的密码配置
2. 重新上传没有 ASS 组件的 Avatar
3. 使用备份的未构建项目

#### Q: 朋友穿我的 Avatar 会被锁吗？
**A**: 会，但只要告诉他们密码就能解锁。建议：
- 为朋友设置简单密码（如 [1, 2, 3]）
- 或提供"朋友版本"（无 ASS 组件）

#### Q: 系统会影响 Avatar 性能吗？
**A**: 
- 解锁后：几乎无影响（< 1% CPU）
- 未解锁：轻微影响（Animator 层计算）
- 防御激活：严重影响（仅对盗取者，10-30 FPS）

### ⚙️ 技术问题

#### Q: 为什么不能用百万状态卡死 Unity？
**A**: 技术限制：
1. Unity 序列化器无法处理百万级 AnimatorState
2. VRChat 文件大小限制（< 25 MB）
3. 构建时间过长（> 1 小时）

#### Q: 可以在商业 Avatar 中使用吗？
**A**: 可以，但需要：
1. 确保您拥有 Avatar 版权
2. 向购买者说明系统存在
3. 提供解锁密码和技术支持
4. 承担相关法律责任

### 🛠️ 故障排除

#### Q: 构建时报错 "NDMF not found"
**A**: 
```bash
1. 安装 NDMF (通过 VCC 或 GitHub)
2. 重启 Unity
3. 检查 Package Manager 是否已加载
```

#### Q: Inspector 显示 "Password Invalid"
**A**: 检查：
- 密码序列不为空（0位密码表示禁用ASS）
- 所有手势值在 1-7 范围内（0=Idle，不能作为密码）

#### Q: Play 模式测试无法解锁
**A**: 确认：
1. 启用了 "Enable In Play Mode"
2. 使用了正确的手势输入
3. 倒计时未结束

---

## 📜 许可证与免责声明

### MIT License

详见项目根目录 LICENSE 文件。

### ⚠️ 免责声明

**重要提示：请负责任地使用此工具**

1. **法律合规**
   - 此工具仅供保护您自己创作的 Avatar
   - 不得用于保护盗版或侵权内容
   - 遵守当地法律法规

2. **用户体验**
   - 对合法用户也有轻微不便（需输入密码）
   - 建议向用户说明系统存在
   - 提供清晰的解锁指南

3. **技术风险**
   - 恶意消耗资源可能违反 VRChat TOS
   - 过度防御可能导致账号封禁
   - 建议设置合理的防御强度

4. **责任声明**
   - 作者不对任何滥用行为负责
   - 作者不对因使用此工具导致的账号封禁负责
   - 使用者自行承担所有风险

---

## 🤝 贡献与支持

### 报告问题

在 GitHub Issues 中报告问题时，请提供：
1. Unity 版本
2. VRChat SDK 版本
3. NDMF 版本
4. 详细错误信息（Console 日志）
5. 复现步骤
6. 截图（如适用）

---

## 🙏 致谢

- **NDMF** - 强大的 Non-Destructive Modular Framework
- **VRChat Community** - 灵感和技术支持

---

**保护你的创作，从 Avatar Security System 开始！🔒**
