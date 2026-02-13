# Avatar Security System (ASS) 技术文档

## 1. 系统概述

Avatar Security System (ASS) 是一个 VRChat Avatar 防盗保护系统。它在 Avatar 构建/上传时自动注入密码验证和防御机制，当密码未正确输入时，通过消耗盗用者客户端的 CPU/GPU 资源使被盗 Avatar 无法正常使用。

### 1.1 核心特性

- **手势密码验证**：通过 VRChat 左/右手手势组合作为密码
- **倒计时机制**：限时密码输入，超时自动触发防御
- **多层防御**：CPU 防御（Constraint、PhysBone、Contact）+ GPU 防御（Shader、Overdraw、粒子、光源）
- **视觉反馈**：全屏 Shader 覆盖（遮挡背景 + 倒计时进度条）+ 音频警告
- **本地/远端分离**：防御仅在本地端触发，远端玩家看到正常 Avatar
- **Write Defaults 兼容**：支持 Auto / WD On / WD Off 三种模式
- **国际化**：支持中文简体、英语、日语

### 1.2 设计原则

1. **构建时注入**：所有安全组件在 VRCSDK 构建流程中自动生成，不修改原始资产
2. **NDMF/VRCFury 兼容**：`callbackOrder = -1026`，在 NDMF Preprocess (-11000) 和 VRCFury 主处理 (-10000) 之后、NDMF Optimize (-1025) 之前执行。VRCFury 参数压缩 (ParameterCompressorHook, `int.MaxValue - 100`) 在 ASS 之后运行，确保参数被正确处理
3. **VRChat 限制遵守**：严格遵守 PhysBone (256)、Contact (200) 等组件数量上限
4. **无侵入式**：使用 `IEditorOnly` 组件，不影响运行时

---

## 2. 系统架构

### 2.1 文件结构

```
Editor/AvatarSecuritySystem/
├── Processor.cs              # 系统入口（VRCSDK 构建回调 IVRCSDKPreprocessAvatarCallback）
├── Lock.cs                   # 锁定/解锁层生成器
├── GesturePassword.cs        # 手势密码验证层生成器
├── Countdown.cs              # 倒计时 + 音频警告层生成器
├── Feedback.cs               # 视觉反馈（全屏 Shader 覆盖）生成器
├── Defense.cs                # CPU/GPU 防御组件生成器
├── Constants.cs              # 系统常量定义
├── I18n.cs                   # 国际化
└── README.md                 # 用户说明文档

Editor/
└── Utils.cs                  # 通用工具类（Animator 操作、VRC 行为、路径处理）

Runtime/
└── AvatarSecuritySystem.cs   # 运行时配置组件（AvatarSecuritySystemComponent : MonoBehaviour + IEditorOnly）

Resources/AvatarSecuritySystem/
├── PasswordSuccess.wav       # 密码成功音效
└── CountdownWarning.wav      # 倒计时警告音效

Shaders/AvatarSecuritySystem/
├── UI.shader                 # 全屏覆盖 UI Shader（遮挡背景 + 进度条）
└── DefenseShader.shader      # 防御 Shader（GPU 密集计算）
```

### 2.2 类依赖关系

```
Processor (入口, IVRCSDKPreprocessAvatarCallback)
│
├── Feedback.Generate()
│   创建全屏 Shader 覆盖 UI + 音频对象
│
├── Lock.Generate()
│   创建锁定/解锁层
│
├── GesturePassword.Generate()
│   创建手势密码验证层
│
├── Countdown.Generate()
│   创建倒计时层
│
├── Countdown.GenerateAudioLayer()
│   创建警告音效层
│
├── Defense.Generate()
│   创建防御层 + 防御组件
│
├── Lock.ConfigureLockLayerWeight()
│   配置 Lock 层自身权重控制
│
├── Lock.LockFxLayerWeights()
│   锁定非 ASS 的 FX 层权重
│
└── Processor.RegisterASSParameters()
    注册 ASS 参数到 VRCExpressionParameters

共享工具：
├── Utils (全局)       — Animator 操作、VRC 行为、路径处理、空 Clip 缓存
├── Constants          — 系统常量
└── I18n               — 国际化
```

### 2.3 生成的 Animator 层结构

构建完成后，FX AnimatorController 包含以下 ASS 层（按添加顺序）：

| 层名称              | 权重           | 功能                              |
| ------------------- | -------------- | --------------------------------- |
| `ASS_Lock`          | 1.0            | 锁定/解锁状态管理、对象可见性控制 |
| `ASS_PasswordInput` | 1.0            | 手势密码序列验证                  |
| `ASS_Countdown`     | 1.0            | 倒计时计时器、TimeUp 触发         |
| `ASS_Audio`         | 1.0            | 警告音效循环播放                  |
| `ASS_Defense`       | 1.0 (Override) | 防御组件激活控制                  |

### 2.4 Animator 参数

| 参数名                         | 类型 | 同步方式      | 说明                               |
| ------------------------------ | ---- | ------------- | ---------------------------------- |
| `IsLocal`                      | Bool | VRChat 内置   | 是否为本地玩家（VRChat 自动设置）  |
| `ASS_PasswordCorrect`          | Bool | networkSynced | 密码是否正确（本地设置，网络同步） |
| `ASS_TimeUp`                   | Bool | 仅本地        | 倒计时是否结束（本地设置，不同步） |
| `GestureLeft` / `GestureRight` | Int  | VRChat 内置   | 手势值 0-7                         |

---

## 3. 执行流程

### 3.1 构建时序 (`Processor`)

```
OnPreprocessAvatar(avatarGameObject)  [callbackOrder = -1026]
│
├─ 1. 获取 VRCAvatarDescriptor
│     获取 AvatarSecuritySystemComponent 配置
│     验证密码配置有效性 (IsPasswordValid)
│
├─ 2. 检查密码是否为空
│     gesturePassword 为空 (0位) → 跳过，不启用 ASS
│
├─ 3. 检查 PlayMode 禁用开关
│     disabledInPlaymode = true 且当前为 PlayMode → 跳过
│
└─ 4. ProcessAvatar() 主流程
      │
      ├─ GetFXController(descriptor)
      │   获取或创建 FX AnimatorController
      │
      ├─ AddParameterIfNotExists(IsLocal)
      │   注册 VRChat 内置参数
      │
      ├─ [可选] LoadAudioResources()
      │   从 Resources/AvatarSecuritySystem/ 加载音频
      │
      ├─ [可选] Feedback.Generate()
      │   创建 UI 根对象 (ASS_UI) + Shader 覆盖 Mesh + 音频对象
      │
      ├─ [可选] Lock.Generate()
      │   创建锁定层 (ASS_Lock)
      │
      ├─ [可选] GesturePassword.Generate()
      │   创建密码验证层 (ASS_PasswordInput)
      │
      ├─ [可选] Countdown.Generate()
      │   创建倒计时层 (ASS_Countdown)
      │
      ├─ [可选] Countdown.GenerateAudioLayer()
      │   创建音效层 (ASS_Audio)
      │
      ├─ [可选] Defense.Generate()
      │   创建防御层 (ASS_Defense) + 防御组件
      │
      ├─ Lock.ConfigureLockLayerWeight()
      │   配置 Lock 层自身权重（Locked=1, Unlocked=0）
      │
      ├─ [可选] Lock.LockFxLayerWeights(layerNames)
      │   锁定非 ASS 层权重（Locked=0, Unlocked=1）
      │   仅在非 PlayMode 且 lockFxLayers=true 时执行
      │
      ├─ RegisterASSParameters(descriptor)
      │   注册 ASS_PasswordCorrect(synced,saved) 和 ASS_TimeUp(local)
      │   到 VRCExpressionParameters
      │
      └─ SaveAndRefresh() + LogOptimizationStats()
          保存资产，输出统计
```

> 注：每个子系统都可通过 `debugSkip*` 选项单独跳过。

### 3.2 运行时状态流

```
Avatar 加载
│
├─ [远端玩家] Remote 状态
│   UI 隐藏，Avatar 正常显示
│   ┌─────────────────────────────┐
│   │ 当 PasswordCorrect = true  │──→ Unlocked
│   └─────────────────────────────┘
│
├─ [本地玩家] IsLocal = true
│   │
│   ├─ Locked 状态（初始）
│   │   全屏 Shader 覆盖（白色背景 + 红色进度条）
│   │   Avatar 对象被禁用/隐藏
│   │
│   │   同时：
│   │   ├─ Countdown 层开始计时
│   │   ├─ Password 层等待手势输入
│   │   └─ Audio 层等待警告时间
│   │
│   ├─ 手势输入正确 → PasswordCorrect = true
│   │   ├─ Lock 层：Locked → Unlocked（恢复 Avatar 显示）
│   │   ├─ Countdown 层：停止倒计时
│   │   ├─ Audio 层：停止警告
│   │   └─ Defense 层：保持 Inactive
│   │
│   └─ 倒计时结束 → TimeUp = true
│       ├─ Countdown 层：设置 TimeUp 参数，隐藏 UI
│       ├─ Password 层：Any State → TimeUp_Failed（禁止继续输入）
│       └─ Defense 层：Inactive → Active（防御组件激活）
```

---

## 4. 子系统详解

### 4.1 锁定系统 (Lock)

**文件**: `Lock.cs`

**功能**: 控制 Avatar 的可见性和层权重

#### 4.1.1 状态机

```
                    ┌──────────┐
           IsLocal──→│  Locked  │──PasswordCorrect──→┐
        !Password   │ (遮挡)   │←──!PasswordCorrect──┤
                    └──────────┘                      │
┌──────────┐                              ┌──────────┐
│  Remote  │──PasswordCorrect────────────→│ Unlocked │
│(默认状态) │←──!PasswordCorrect──────────│(恢复显示) │
└──────────┘                              └──────────┘
```

转换条件详解：

- **Remote → Locked**：`IsLocal = true` 且 `PasswordCorrect = false`
- **Remote → Unlocked**：`PasswordCorrect = true`（所有玩家都能进入解锁状态）
- **Locked → Locked**：自循环（`hasExitTime=true, exitTime=0`），`PasswordCorrect = false`
- **Locked → Unlocked**：`PasswordCorrect = true`
- **Unlocked → Remote**：`PasswordCorrect = false`（密码被重置）

#### 4.1.2 锁定动画 (CreateLockClip)

- **启用**: `ASS_UI` (全屏 Shader 覆盖)、`ASS_Audio_Warning`、`ASS_Audio_Success`
- **禁用**: `__ASS_Defense__` (防御根对象)
- **隐藏 Avatar**: 当 `disableRootChildren = true` 时，禁用所有非 ASS 根子对象 (`m_IsActive = 0`)

#### 4.1.3 解锁动画 (CreateUnlockClip)

- **禁用**: `ASS_UI`、`__ASS_Defense__`
- **启用**: `ASS_Audio_Warning`、`ASS_Audio_Success`（用于解锁音效播放）
- **恢复 Avatar**:
  - **WD On 模式**: 不显式恢复，由 WD 自动恢复默认值
  - **WD Off 模式**: 显式写入所有根子对象 `m_IsActive = 1`

#### 4.1.4 Remote 动画 (CreateRemoteClip)

- **禁用**: `ASS_UI`、`__ASS_Defense__`
- **WD Off**: 显式恢复所有根子对象
- 用途：远端玩家和密码重置后的默认状态

#### 4.1.5 Transform Mask

创建 `AvatarMask` (`ASS_LockLayerMask`) 限制 Lock 层仅影响以下对象：

- `ASS_UI`
- `__ASS_Defense__`
- `ASS_Audio_Warning`
- `ASS_Audio_Success`
- 当 `disableRootChildren = true` 时，包含所有非 ASS 根子对象

#### 4.1.6 FX 层权重锁定

- `ConfigureLockLayerWeight()`: Lock 层自身权重（Locked=1, Unlocked=0）
- `LockFxLayerWeights()`: 非 ASS 层权重（Locked=0, Unlocked=1）
  - 防止盗用者通过修改 FX Controller 绕过保护

#### 4.1.7 Write Defaults 自动检测 (ResolveWriteDefaults)

Auto 模式下扫描所有 Playable Layer 的 AnimatorController：

**跳过规则**:

1. `isDefault` 的 Playable Layer（未自定义）
2. `animatorController` 为 null 的层
3. VRChat 内置控制器（名称以 `vrc_` 开头）
4. ASS 自己生成的层（`ASS_` 前缀）
5. Additive 层（必须始终 WD On）
6. Direct BlendTree 单状态层（必须始终 WD On，参考 Modular Avatar）
7. 无 Motion 的空状态

**判断逻辑**: 只要存在任何 WD Off 状态就使用 WD Off；全部为 WD On（或无有效状态）才使用 WD On

---

### 4.2 手势密码系统 (GesturePassword)

**文件**: `GesturePassword.cs`

**功能**: 通过 VRChat 手势参数实现密码序列验证

#### 4.2.1 状态机设计

实现**尾部序列匹配**算法：用户输入的最后 N 位满足密码即可通过。

例如：密码 `[4, 5, 6]`，输入 `1, 2, 3, 4, 5, 6` 也算正确。

每个密码位有 3 个状态：

```
Wait_Input ──(手势=密码[0])──→ Step_1_Holding ──(保持 holdTime)──→ Step_1_Confirmed
                                    ↑                                    │
                                    │                               (手势=密码[1])
                                    │(错误手势)                          ↓
                               ←────┘                          Step_2_Holding ──→ ...
                                                                     │
                                                              (错误手势,非Idle)
                                                                     ↓
                                                          Step_1_ErrorTolerance
                                                          (容错 errorTolerance 秒)
                                                                     │
                                                              超时──→ Wait_Input
                                                              纠正──→ Step_2_Holding
```

#### 4.2.2 状态类型

| 状态                    | Motion                           | 功能                                                  |
| ----------------------- | -------------------------------- | ----------------------------------------------------- |
| `Wait_Input`            | SharedEmptyClip                  | 初始状态，等待第一位密码输入                          |
| `Step_N_Holding`        | ASS_Hold_N (gestureHoldTime 秒)  | 正在保持第 N 位手势，需保持指定时间                   |
| `Step_N_Confirmed`      | SharedEmptyClip                  | 第 N 位已确认，等待下一位                             |
| `Step_N_ErrorTolerance` | ASS_Tolerance_N (errorTolerance) | 容错缓冲，短暂误触后可继续                            |
| `Password_Success`      | SharedEmptyClip                  | 密码正确，设置 `PasswordCorrect = true`，播放成功音效 |
| `TimeUp_Failed`         | SharedEmptyClip                  | 倒计时结束，禁止继续输入（Any State → 此状态）        |

#### 4.2.3 转换规则

- **Idle 手势 (0) 自循环**: Holding/Confirmed/ErrorTolerance 状态遇到 Idle 手势时自循环，允许松开手指而不重置进度
- **错误手势容错**: 短暂误触进入 ErrorTolerance 状态，在容错时间内输入正确手势可继续
- **尾部匹配重启**: 在 Confirmed 和 ErrorTolerance 状态中，如果按下密码第一位手势，回到 Step_1_Holding 重新开始
- **TimeUp 全局中断**: Any State → TimeUp_Failed，条件为 `ASS_TimeUp = true`

#### 4.2.4 时间控制

手势保持和容错通过**定长动画剪辑**实现（使用 dummy curve `__ASS_Dummy__/m_IsActive`）：

- Holding 状态使用 `gestureHoldTime` 秒长度的动画，`exitTime=1.0` 表示动画播完后转换
- ErrorTolerance 状态使用 `gestureErrorTolerance` 秒长度的动画
- 最后一步 Holding → Password_Success 也通过 `exitTime=1.0` 确保手势保持足够时间

---

### 4.3 倒计时系统 (Countdown)

**文件**: `Countdown.cs`

**功能**: 提供限时机制和音频警告

#### 4.3.1 倒计时层 (`ASS_Countdown`)

```
Remote ──(IsLocal)──→ Countdown ──(exitTime=1.0)──→ TimeUp
                           │                           │
                      (PasswordCorrect)           (PasswordCorrect)
                           ↓                           ↓
                        Unlocked ←──────────────────────┘
```

- **Remote 状态**: SharedEmptyClip，`writeDefaultValues = true`
- **Countdown 状态**: 播放 `countdownDuration` 秒的进度条动画
  - 动画控制 `ASS_UI/Overlay` 的 `material._Progress` 从 1 到 0（驱动 Shader 进度条属性）
- **TimeUp 状态**: 通过 ParameterDriver 设置 `ASS_TimeUp = true`
  - 同时播放 TimeUp 动画，禁用 `ASS_UI` (`m_IsActive = 0`)
- **Unlocked 状态**: SharedEmptyClip，密码正确后停止倒计时

#### 4.3.2 音频层 (`ASS_Audio`)

```
Remote ──(IsLocal)──→ Waiting ──(动画播完)──→ WarningBeep ──(自循环,每秒)
                         │                         │
                    (PasswordCorrect)         (TimeUp 或 PasswordCorrect)
                         ↓                         ↓
                       Stop ←──────────────────────┘
```

- **Waiting 状态**: 播放 `(countdownDuration - warningThreshold + 0.1)` 秒的空动画
  - +0.1s 延迟防止最后一次循环在 TimeUp 之后触发
- **WarningBeep 状态**: 1 秒动画自循环，每次进入时通过 `VRCAnimatorPlayAudio` 播放警告蜂鸣
  - 音频对象: `ASS_Audio_Warning`
- **Stop 状态**: SharedEmptyClip，停止所有音效

---

### 4.4 反馈系统 (Feedback)

**文件**: `Feedback.cs`

**功能**: 创建全屏 Shader 覆盖显示元素和音频对象

#### 4.4.1 全屏覆盖 UI (`ASS_UI`)

- **渲染方式**: 使用自定义 Shader (`UnityBox/AvatarSecuritySystem/UI`) 直接渲染到摄像机全屏
  - Shader 在顶点着色器中将 Quad 顶点直接映射到裁剪空间覆盖整个屏幕
  - **不需要** VRCParentConstraint 绑定到头部骨骼
  - **不需要** 世界空间定位
- **位置**: 作为 Avatar 根对象的直接子对象
- **默认状态**: `SetActive(false)`，仅 Locked 状态时由动画启用
- **Mesh**: 简单 Quad（4 顶点），顶点位置无关紧要（由 Shader 重新映射）
- **材质属性**:
  - `_BackgroundColor`: 白色（遮挡背景）
  - `_BarColor`: 红色（进度条）
  - `_Progress`: 1.0（满进度，由倒计时动画驱动到 0）
- **Shader 回退**: `UnityBox/AvatarSecuritySystem/UI` → `Unlit/Color` → `Hidden/InternalErrorShader`

#### 4.4.2 音频对象

| 对象名              | 父对象      | 配置                                                     |
| ------------------- | ----------- | -------------------------------------------------------- |
| `ASS_Audio_Warning` | Avatar Root | `spatialBlend=0`, `volume=0.5`, `priority=0`, 无自动播放 |
| `ASS_Audio_Success` | Avatar Root | `spatialBlend=0`, `volume=0.5`, `priority=0`, 无自动播放 |

---

### 4.5 防御系统 (Defense)

**文件**: `Defense.cs`

**功能**: 创建消耗客户端资源的防御组件

#### 4.5.1 防御层状态机

```
Inactive ──(IsLocal && TimeUp)──→ Active
```

- 层 blending 模式: `Override`
- 两个状态均使用 SharedEmptyClip
- 防御组件挂载在 `__ASS_Defense__` 对象下，默认 `SetActive(false)`
- Active 状态由 Lock 层的动画控制激活

#### 4.5.2 防御等级参数表

| 参数                 | 等级 1 (CPU) | 等级 2 (CPU+GPU 中低) | 等级 3 (CPU+GPU 最高) | 调试模式(等级1) | 调试模式(等级2/3) |
| -------------------- | ------------ | --------------------- | --------------------- | --------------- | ----------------- |
| ConstraintDepth      | 100          | 100                   | 100                   | 3               | 3                 |
| ConstraintChainCount | 10           | 10                    | 10                    | 1               | 1                 |
| PhysBoneLength       | 256          | 256                   | 256                   | 3               | 3                 |
| PhysBoneChainCount   | 10           | 10                    | 10                    | 1               | 1                 |
| PhysBoneColliders    | 256          | 256                   | 256                   | 2               | 2                 |
| ContactCount         | 200          | 200                   | 200                   | 4               | 4                 |
| ShaderLoops          | 0            | 200                   | 1,000                 | 0               | 10                |
| OverdrawLayers       | 0            | 50                    | 200                   | 0               | 3                 |
| PolyVertices         | 0            | 50,000                | 200,000               | 0               | 1,000             |
| ParticleCount        | 0            | 10,000                | 100,000               | 0               | 100               |
| ParticleSystemCount  | 0            | 3                     | 20                    | 0               | 1                 |
| LightCount           | 0            | 5                     | 30                    | 0               | 1                 |
| MaterialCount        | 0            | 2                     | 5                     | 0               | 1                 |

#### 4.5.3 CPU 防御详解

**VRCConstraint 链** (`CreateConstraintChain`)

每条基础链由 `depth` 个嵌套节点组成，每个节点（首节点除外）附加 3 种 VRC 约束组件：

- VRCParentConstraint（所有节点）
- VRCPositionConstraint（非首节点）
- VRCRotationConstraint（非首节点）

约束属性：`IsActive = true`, `Locked = true`（通过 SerializedObject 设置）

**扩展约束链** (`CreateExtendedConstraintChains`，等级 3 独有)

额外创建的链，每个节点附加 4 种约束组件：

- VRCParentConstraint
- VRCPositionConstraint
- VRCRotationConstraint
- VRCScaleConstraint

链数量：`min(ConstraintChainCount, 5)` 条

**VRCPhysBone 链** (`CreatePhysBoneChains`)

每条链由 `chainLength` 个骨骼节点组成，配置：

- 积分类型：`Advanced`（最高计算复杂度）
- `Pull = 0.8`, `Spring = 0.8`, `Stiffness = 0.5`, `Gravity = 0.5`
- 每个参数都配有 AnimationCurve（共 6 条 Curve：pull/spring/stiffness/gravity/gravityFalloff/immobile）
- 限制：`LimitType.Angle`, `MaxAngleX/Z = 45°`, `LimitRotation = (15, 30, 15)`
- 拉伸：`MaxStretch = 0.5`
- 抓取：`AllowGrabbing = True`, `AllowPosing = True`, `GrabMovement = 0.8`, `SnapToHand = true`
- 每条链附加 `colliderCount` 个 VRCPhysBoneCollider（Capsule 类型，半径 0.3m，高度 1.0m，`InsideBounds = true`）
- 碰撞器圆形分布排列

**扩展 PhysBone 链** (`CreateExtendedPhysBoneChains`，等级 3 独有)

与基础链配置相同，额外创建 `min(defensePhysBoneCount, 3)` 条链。

**VRCContact 系统** (`CreateContactSystem`)

成对创建 Sender + Receiver（各 `ContactCount / 2` 个）：

- 形状：Capsule（半径 1.0m，高度 2.0m）
- 碰撞标签：`["Tag1", "Tag2", "Tag3", "Tag4", "Tag5"]`
- `localOnly = true`
- 圆形分布排列（Receiver 偏移半个角度）

**扩展 Contact 系统** (`CreateExtendedContactSystem`，等级 3 独有)

- 碰撞标签扩展为 10 个：`["Tag1" ~ "Tag10"]`
- 数量：`min((CONTACT_MAX_COUNT - ContactCount) / 2, 50)` 对

> **注意**: 系统自动检测已有 PhysBone 数量，确保总数不超过 `PHYSBONE_MAX_COUNT (256)`。

#### 4.5.4 GPU 防御详解

**材质防御** (`CreateMaterialDefense`)

1. 防御 Shader 获取（`CreateDefenseShader`）：优先使用 `UnityBox/DefenseShader`，回退到 `Standard`
2. 防御材质创建（`CreateDefenseMaterial`）：
   - 通过 `ApplyFixedShaderParameters` 设置 36 个 GPU 密集参数（循环计数、采样率、光线步进步数、后处理效果等）
   - `_LoopCount` = ShaderLoops（范围 0-1000）
   - 透明渲染队列 = 3000
3. 高面数球体 Mesh（`CreateHighDensitySphereMesh`）：
   - 2 个子网格（subMesh）增加 draw call
   - 双 UV 通道 + 顶点色
   - 顶点数通过 subdivisions 控制
4. Overdraw 层堆叠（`CreateOverdrawLayersWithMaterial`）：
   - 多层 Quad，z 间距 0.001m
   - 使用相同的防御材质
   - 等级 2: 2 组 Overdraw，等级 3: 2 组 + 1 额外组（总数 > 100 时）

**粒子防御** (`CreateParticleDefense`)

- 总粒子数分配到多个 ParticleSystem（每个系统至少 1000 粒子）
- 每个系统配置：
  - `loop = true`, `prewarm = true`, `startLifetime = 8s`, `startSpeed = 3`
  - 发射率：`particlesPerSystem / 3`
  - 启用模块：VelocityOverLifetime、SizeOverLifetime、RotationOverLifetime
  - 碰撞：`type = Planes`, `dampen = 0.8`, `bounce = 0.7~1.0`
  - 渲染器：Billboard 模式，HSV 色彩分布
  - `gravityModifier = 0.8`

**光源防御** (`CreateLightDefense`)

- 交替创建 Point（range=10）/ Spot（range=15, spotAngle=60°）光源
- 环形排列（360°/lightCount 间距，半径 2m）
- `intensity = 2`, HSV 色彩分布
- 全部启用 `Soft Shadow`，`shadowResolution = VeryHigh`

---

## 5. 配置参数详解

### 5.1 AvatarSecuritySystemComponent 参数

#### 基础配置

| 参数              | 类型           | 默认值    | 范围     | 说明                                   |
| ----------------- | -------------- | --------- | -------- | -------------------------------------- |
| `uiLanguage`      | SystemLanguage | Unknown   | —        | Inspector 界面语言（Unknown=自动检测） |
| `gesturePassword` | List\<int\>    | [1,7,2,4] | 每项 1-7 | 手势密码序列，Fist(1)~ThumbsUp(7)      |
| `useRightHand`    | bool           | false     | —        | 使用右手手势（false=左手）             |

#### 倒计时配置

| 参数                | 类型  | 默认值 | 范围   | 说明                                        |
| ------------------- | ----- | ------ | ------ | ------------------------------------------- |
| `countdownDuration` | float | 30     | 30-120 | 总倒计时时间（秒）                          |
| `warningThreshold`  | float | 10     | —      | 警告阶段时长（最后 N 秒开始蜂鸣，隐藏字段） |

#### 手势识别配置

| 参数                    | 类型  | 默认值 | 范围    | 说明                             |
| ----------------------- | ----- | ------ | ------- | -------------------------------- |
| `gestureHoldTime`       | float | 0.15   | 0.1-1.0 | 手势保持确认时间（秒），防止误触 |
| `gestureErrorTolerance` | float | 0.3    | 0.1-1.0 | 错误手势容错缓冲时间（秒）       |

#### 高级选项

| 参数                  | 类型              | 默认值 | 说明                                                       |
| --------------------- | ----------------- | ------ | ---------------------------------------------------------- |
| `disabledInPlaymode`  | bool              | true   | PlayMode 时是否跳过安全系统生成                            |
| `disableDefense`      | bool              | false  | 禁用防御组件（仅保留密码系统，用于测试）                   |
| `lockFxLayers`        | bool              | true   | 锁定时将非 ASS 的 FX 层权重设为 0                          |
| `disableRootChildren` | bool              | true   | 锁定时禁用 Avatar 根子对象                                 |
| `defenseLevel`        | int               | 3      | 防御等级 0-3（见 §4.5.2）                                  |
| `writeDefaultsMode`   | WriteDefaultsMode | Auto   | Auto = 自动检测 / On = 依赖自动恢复 / Off = 显式写入恢复值 |

#### 调试选项

| 参数                       | 类型 | 默认值 | 说明                        |
| -------------------------- | ---- | ------ | --------------------------- |
| `enableVerboseLogging`     | bool | false  | 详细日志输出                |
| `debugSkipLockSystem`      | bool | false  | 跳过锁定系统生成            |
| `debugSkipPasswordSystem`  | bool | false  | 跳过密码系统生成            |
| `debugSkipCountdownSystem` | bool | false  | 跳过倒计时系统生成          |
| `debugSkipFeedbackSystem`  | bool | false  | 跳过反馈系统（UI/音效）生成 |
| `debugSkipDefenseSystem`   | bool | false  | 跳过防御系统生成            |
| `debugValidateAfterBuild`  | bool | false  | 构建后验证动画控制器        |

#### 音频资源（隐藏字段，自动从 Resources 加载）

| 参数           | 类型      | 说明               |
| -------------- | --------- | ------------------ |
| `warningBeep`  | AudioClip | 倒计时警告蜂鸣音效 |
| `successSound` | AudioClip | 密码成功音效       |

---

## 6. 工具类

### 6.1 Utils (全局)

通用工具类，位于 `Editor/Utils.cs`，提供 Animator 操作、VRC 行为和路径处理方法。

#### Animator 层和参数

| 方法                         | 说明                                           |
| ---------------------------- | ---------------------------------------------- |
| `CreateLayer(name, weight)`  | 创建 AnimatorControllerLayer + StateMachine    |
| `AddParameterIfNotExists()`  | 添加 Animator 参数（避免重复）                 |
| `CreateTransition()`         | 创建状态转换，统一配置 hasExitTime/duration    |
| `CreateAnyStateTransition()` | 创建 Any State 转换                            |
| `GetOrCreateEmptyClip()`     | 获取或创建共享的空 AnimationClip（按路径缓存） |
| `OptimizeStates()`           | 将 null motion 替换为指定的空 clip             |

#### 子资产管理

| 方法                               | 说明                                                  |
| ---------------------------------- | ----------------------------------------------------- |
| `AddSubAsset(controller, asset)`   | 安全地将资产嵌入 Controller（自动检查重复和外部路径） |
| `AddSubAssets(controller, assets)` | 批量添加子资产                                        |

#### VRC 行为

| 方法                                 | 说明                                         |
| ------------------------------------ | -------------------------------------------- |
| `AddLayerControlBehaviour()`         | 添加 VRCAnimatorLayerControl（单层权重控制） |
| `AddMultiLayerControlBehaviour()`    | 添加多个层权重控制行为                       |
| `AddParameterDriverBehaviour()`      | 添加 VRCAvatarParameterDriver（单参数驱动）  |
| `AddMultiParameterDriverBehaviour()` | 添加多参数驱动                               |
| `AddPlayAudioBehaviour()`            | 添加 VRCAnimatorPlayAudio 行为               |

#### 路径和统计

| 方法                               | 说明                                             |
| ---------------------------------- | ------------------------------------------------ |
| `GetRelativePath(root, node)`      | 获取对象相对于 root 的路径                       |
| `SaveAndRefresh()`                 | 保存资产                                         |
| `LogOptimizationStats(controller)` | 输出 Controller 统计（状态数、转换数、文件大小） |

### 6.2 Constants

系统级常量，包括：

- 系统信息 (`SYSTEM_NAME`, `SYSTEM_SHORT_NAME`, `PLUGIN_QUALIFIED_NAME`)
- 资源路径 (`ASSET_FOLDER = "Assets/UnityBox/AvatarSecuritySystem/Generated/ASS"`, `AUDIO_RESOURCE_PATH`)
- Animator 参数名 (`PARAM_PASSWORD_CORRECT`, `PARAM_TIME_UP`, `PARAM_IS_LOCAL`, `PARAM_GESTURE_LEFT/RIGHT`)
- 层名称 (`LAYER_LOCK`, `LAYER_PASSWORD_INPUT`, `LAYER_COUNTDOWN`, `LAYER_AUDIO`, `LAYER_DEFENSE`)
- GameObject 名称 (`GO_ASS_ROOT`, `GO_UI`, `GO_AUDIO_WARNING`, `GO_AUDIO_SUCCESS`, `GO_PARTICLES`, `GO_DEFENSE_ROOT`)
- VRChat 组件上限 (`PHYSBONE_MAX_COUNT=256`, `CONTACT_MAX_COUNT=200`)
- 防御参数上限 (`CONSTRAINT_CHAIN_MAX_DEPTH=100`, `PHYSBONE_CHAIN_MAX_LENGTH=256`, `PHYSBONE_COLLIDER_MAX_COUNT=256`, `SHADER_LOOP_MAX_COUNT=3000000`)

---

## 7. VRChat 手势值对照

| 值  | 手势名称    | 说明                   |
| --- | ----------- | ---------------------- |
| 0   | Idle        | 空闲（不可作为密码位） |
| 1   | Fist        | 握拳                   |
| 2   | HandOpen    | 张手                   |
| 3   | Fingerpoint | 指向                   |
| 4   | Victory     | 胜利（剪刀手）         |
| 5   | RockNRoll   | 摇滚手势               |
| 6   | HandGun     | 手枪                   |
| 7   | ThumbsUp    | 竖拇指                 |

---

## 8. 生成的 GameObject 层级

```
Avatar Root
├── ASS_UI (默认禁用)
│   └── Overlay
│       MeshFilter (Quad) + MeshRenderer
│       Material: UnityBox/AvatarSecuritySystem/UI
│       _BackgroundColor=白, _BarColor=红, _Progress=1→0
│
├── ASS_Audio_Warning
│   AudioSource (spatialBlend=0, volume=0.5)
│
├── ASS_Audio_Success
│   AudioSource (spatialBlend=0, volume=0.5)
│
└── __ASS_Defense__ (默认禁用)
    ├── ConstraintChain_0/
    │   └── Constraint_0 ~ Constraint_{depth}
    │       每节点: VRCParentConstraint + VRCPositionConstraint + VRCRotationConstraint
    ├── ConstraintChain_1/ ...
    ├── ExtendedConstraintChain_0/ ... (等级3)
    │   └── 每节点: Parent + Position + Rotation + ScaleConstraint
    │
    ├── PhysBoneChains_0/
    │   ├── BoneChain_0/
    │   │   └── Bone_0 ~ Bone_{length} (VRCPhysBone, Advanced模式)
    │   └── Collider_0 ~ Collider_{count} (VRCPhysBoneCollider, Capsule)
    ├── ExtendedPhysBoneChains_0/ ... (等级3)
    │
    ├── ContactSystem/
    │   ├── Sender_0 ~ Sender_{half} (VRCContactSender, 5标签)
    │   └── Receiver_0 ~ Receiver_{half} (VRCContactReceiver, 5标签)
    ├── ExtendedContactSystem/ ... (等级3, 10标签)
    │
    ├── MaterialDefense/
    │   ├── DefenseMesh_0 ~ DefenseMesh_{count} (高面数球体, 2子网格)
    │   ├── OverdrawLayers_0/
    │   │   └── Layer_0 ~ Layer_{count} (透明 Quad, z间距0.001)
    │   └── OverdrawLayers_1/ ...
    │
    ├── ParticleDefense/
    │   └── ParticleSystem_0 ~ ParticleSystem_{count}
    │       (Billboard, 碰撞, 速度/大小/旋转随生命周期变化)
    │
    └── LightDefense/
        └── Light_0 ~ Light_{count}
            (Point/Spot交替, Soft Shadow, VeryHigh分辨率)
```

---

## 9. 注意事项

### 9.1 VRChat 组件限制

- PhysBone 总数（含模型自带）不超过 256，系统会自动检测已有数量并调整
- Contact Sender + Receiver 总数不超过 200
- Constraint 链深度上限 100

### 9.2 Write Defaults 模式

- **Auto（推荐）**：自动检测已有 Controller 的 WD 设置，遵循大多数状态的设置
- **WD On**：动画结束后参数自动恢复默认值，更简洁
- **WD Off**：每个状态需要显式写入所有受控属性的恢复值
- 系统自动根据配置选择对应的动画生成策略

### 9.3 参数同步

- `ASS_PasswordCorrect`：`networkSynced = true`，`saved = true`
  - 同步确保远端玩家也能看到解锁效果
  - `saved = true` 确保重新加载 Avatar 后保持状态
- `ASS_TimeUp`：`networkSynced = false`，`saved = false`
  - 仅本地使用，无需同步

### 9.4 构建流程兼容性

- `callbackOrder = -1026` 确保 ASS 在 NDMF Preprocess (-11000)/VRCFury 主处理 (-10000) 之后、NDMF Optimize (-1025) 之前执行
- VRCFury 参数压缩 (ParameterCompressorHook) 在 `int.MaxValue - 100` 执行，远在 ASS 之后，ASS 新增的参数会被正确识别和压缩
- ASS 获取现有 FX Controller 并追加层，不会覆盖已有内容
- 使用 `IEditorOnly` 接口，Runtime 组件不会出现在构建产物中
