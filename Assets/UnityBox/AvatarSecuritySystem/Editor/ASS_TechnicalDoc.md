# Avatar Security System (ASS) 技术文档

## 1. 系统概述

Avatar Security System (ASS) 是一个 VRChat Avatar 防盗保护系统。它在 Avatar 构建/上传时自动注入密码验证和防御机制，当密码未正确输入时，通过消耗盗用者客户端的 CPU/GPU 资源使被盗 Avatar 无法正常使用。

### 1.1 核心特性

- **手势密码验证**：通过 VRChat 左/右手手势组合作为密码
- **倒计时机制**：限时密码输入，超时自动触发防御
- **多层防御**：CPU 防御（Constraint、PhysBone、Contact）+ GPU 防御（Shader、粒子、光源、高面数Mesh）
- **视觉反馈**：全屏 Shader 覆盖（遮挡背景 + Logo + 倒计时进度条）+ 音频警告
- **本地/远端分离**：防御仅在本地端触发，远端玩家看到正常 Avatar
- **Write Defaults 兼容**：支持 Auto / WD On / WD Off 三种模式
- **国际化**：支持中文简体、英语、日语

### 1.2 设计原则

1. **构建时注入**：所有安全组件在 VRCSDK 构建流程中自动生成，不修改原始资产
2. **NDMF/VRCFury 兼容**：`callbackOrder = -1026`，在 NDMF Preprocess (-11000) 和 VRCFury 主处理 (-10000) 之后、NDMF Optimize (-1025) 之前执行。VRCFury 参数压缩 (ParameterCompressorHook, `int.MaxValue - 100`) 在 ASS 之后运行，确保参数被正确处理
3. **VRChat 限制遵守**：严格遵守 PhysBone (256)、Contact (200)、Constraint (2000)、PhysBone Collider (256) 等组件数量上限，自动检测已有组件预算
4. **无侵入式**：使用 `IEditorOnly` 组件，不影响运行时

---

## 2. 系统架构

### 2.1 文件结构

```
Editor/
├── Processor.cs              # 系统入口（VRCSDK 构建回调 IVRCSDKPreprocessAvatarCallback）
├── Lock.cs                   # 锁定/解锁层生成器
├── GesturePassword.cs        # 手势密码验证层生成器
├── Countdown.cs              # 倒计时 + 音频警告层生成器
├── Feedback.cs               # 视觉反馈（全屏 Shader 覆盖 + Logo）生成器
├── Defense.cs                # CPU/GPU 防御组件生成器
├── Constants.cs              # 系统常量定义
├── Utils.cs                  # 通用工具类（Animator 操作、VRC 行为、路径处理）
├── I18n.cs                   # 国际化
├── AvatarSecuritySystemEditor.cs  # Inspector 自定义编辑器
└── README.md                 # 用户说明文档

Runtime/
└── AvatarSecuritySystem.cs   # 运行时配置组件（AvatarSecuritySystemComponent : MonoBehaviour + IEditorOnly）

Resources/
├── Avatar Security System.png      # Logo 图片
├── PasswordSuccess.mp3             # 密码成功音效
├── CountdownWarning.mp3            # 倒计时警告音效
├── InputError.mp3                  # 输入错误音效
├── StepSuccess.mp3                 # 步骤成功音效
└── Materials/
    └── Avatar Security System.mat  # UI 材质

Shaders/
├── UI.shader                 # 全屏覆盖 UI Shader（UnityBox/ASS_UI）
└── DefenseShader.shader      # 防御 Shader（UnityBox/ASS_DefenseShader）
                              # 3 Pass: ForwardBase, ForwardAdd, ShadowCaster
                              # Texture2D + 共享 SamplerState（解决 16 sampler 寄存器限制）
                              # 16 张纹理使用 1 个采样器，#pragma target 5.0
```

### 2.2 类依赖关系

```
Processor (入口, IVRCSDKPreprocessAvatarCallback)
│
├── Feedback.Generate()
│   创建全屏 Shader 覆盖 UI（背景 + Logo + 进度条）+ 音频对象
│
├── Lock.Generate()
│   创建锁定/解锁层（对象可见性控制 + WD 检测）
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
      │   从 Resources/ 加载音频
      │
      ├─ [可选] Feedback.Generate()
      │   创建 UI 根对象 (ASS_UI) + Shader 覆盖 Mesh（含 Logo）+ 音频对象
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
      ├─ RegisterASSParameters(descriptor)
      │   注册 ASS_PasswordCorrect(synced,saved) 和 ASS_TimeUp(local)
      │   到 VRCExpressionParameters
      │
      └─ SaveAndRefresh() + LogOptimizationStats()
          保存资产，输出统计
```

> 注：防御系统可通过 `disableDefense` 选项单独禁用。

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
│   │   全屏 Shader 覆盖（白色背景 + Logo + 红色进度条）
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
│       ├─ Countdown 层：设置 TimeUp 参数（UI 保持显示作为遮罩）
│       ├─ Password 层：Any State → TimeUp_Failed（禁止继续输入）
│       └─ Defense 层：Inactive → Active（防御组件激活）
```

---

## 4. 子系统详解

### 4.1 锁定系统 (Lock)

**文件**: `Lock.cs`

**功能**: 控制 Avatar 的可见性

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
- **禁用**: `ASS_Defense` (防御根对象)
- **隐藏 Avatar**: 当 `disableRootChildren = true` 时，禁用所有非 ASS 根子对象 (`m_IsActive = 0`)

#### 4.1.3 解锁动画 (CreateUnlockClip)

- **禁用**: `ASS_UI`、`ASS_Defense`
- **启用**: `ASS_Audio_Warning`、`ASS_Audio_Success`（用于解锁音效播放）
- **恢复 Avatar**:
  - **WD On 模式**: 不显式恢复，由 WD 自动恢复默认值
  - **WD Off 模式**: 显式写入所有根子对象 `m_IsActive = 1`

#### 4.1.4 Remote 动画 (CreateRemoteClip)

- **禁用**: `ASS_UI`、`ASS_Defense`
- **WD Off**: 显式恢复所有根子对象
- 用途：远端玩家和密码重置后的默认状态

#### 4.1.5 Write Defaults 自动检测 (ResolveWriteDefaults)

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
                                    │                                    │
                               (错误手势)                         (手势=密码[1])
                                    ↓                                    ↓
                               Wait_Input                        Step_2_Holding ──→ ...
                               (Holding 错误
                                一律重置)

Step_N_Confirmed ──(错误手势,非Idle)──→ Step_N_ErrorTolerance
                                          (容错 errorTolerance 秒)
                                                │
                                         超时──→ Wait_Input
                                         纠正──→ Step_(N+1)_Holding
```

> 注意：Holding 状态遇到错误手势直接回到 `Wait_Input`（全部重置）。ErrorTolerance 状态仅从 Confirmed 状态可达。

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
  - 动画控制 `ASS_UI/Overlay` 的材质进度条属性从 1 到 0
- **TimeUp 状态**: 通过 ParameterDriver 设置 `ASS_TimeUp = true`
  - 使用 SharedEmptyClip（UI 保持显示作为遮罩，不再隐藏）
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

- **渲染方式**: 使用自定义 Shader (`UnityBox/ASS_UI`) 直接渲染到摄像机全屏
  - Shader 在顶点着色器中将 Quad 顶点直接映射到裁剪空间覆盖整个屏幕
  - **不需要** VRCParentConstraint 绑定到头部骨骼
  - **不需要** 世界空间定位
- **位置**: 作为 Avatar 根对象的直接子对象
- **默认状态**: `SetActive(false)`，仅 Locked 状态时由动画启用
- **Mesh**: 简单 Quad（4 顶点），顶点位置无关紧要（由 Shader 重新映射）
  - 三角形绕序：`{0, 1, 2, 0, 2, 3}`（统一逆时针，Unity 标准正面定义）
  - 调用 `RecalculateNormals()` 和 `RecalculateTangents()` 确保法线/切线正确
  - **Bounds 设置为 100000 单位**（`mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f)`），防止 Unity 视锥体剔除导致 UI 在特定视角不可见
- **材质属性**（已混淆，属性名不可读）:
  - 背景颜色: 白色（遮挡背景）
  - 进度条颜色: 红色
  - 进度值: 1.0（满进度，由倒计时动画驱动到 0）
  - Logo 纹理: 从 Resources 加载 `Avatar Security System.png`
  - Logo 大小: 占屏幕高度比例
  - 进度条高度: Range 0-0.5，默认 0.06
  - 进度条垂直偏移: Range -0.5-0.5，默认 -0.35
  - 进度条水平内边距: Range 0-0.4，默认 0.1
- **Logo 渲染**: 居中显示在进度条上方的可用空间中央，自动适配屏幕宽高比和纹理宽高比，支持 Alpha 透明混合
- **Shader 回退**: `UnityBox/ASS_UI` → `Unlit/Color` → `Hidden/InternalErrorShader`
- **Shader 渲染状态**:
  - Tags: `"RenderType"="Overlay" "Queue"="Overlay+5000" "IgnoreProjector"="True" "ForceNoShadowCasting"="True" "DisableBatching"="True"`
  - `ZTest Always`, `ZWrite Off`, `Cull Off`
  - `Blend SrcAlpha OneMinusSrcAlpha`（透明度混合）
  - `Stencil { Ref 255 Comp Always Pass Replace }`（强制覆盖所有 Stencil，防止被镜子/地图剔除）
  - `Offset -1, -1`（深度偏移确保最前）
  - Material `renderQueue = 5000`（C# 端强制设置）
  - MeshRenderer `sortingOrder = 32767`（最高排序优先级）
  - 禁用 ShadowCasting/ReceiveShadows/LightProbe/ReflectionProbe/OcclusionCulling

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
- Inactive 状态使用 SharedEmptyClip
- Active 状态使用 `ASS_DefenseActivate` 动画剪辑（设置 `ASS_Defense` 的 `m_IsActive = 1`）
- 防御组件挂载在 `ASS_Defense` 对象下，默认 `SetActive(false)`
- Active 状态通过激活动画启用防御根对象

#### 4.5.2 防御等级参数表

| 参数                   | 等级 1 (CPU) | 等级 2 (CPU+GPU) | 调试模式(等级1) | 调试模式(等级2) |
| ---------------------- | ------------ | ---------------- | --------------- | --------------- |
| ConstraintDepth        | 2000         | 2000             | 1               | 1               |
| ConstraintChainCount   | 2000         | 2000             | 1               | 1               |
| PhysBoneLength         | 256          | 256              | 1               | 1               |
| PhysBoneChainCount     | 256          | 256              | 1               | 1               |
| PhysBoneColliders      | 256          | 256              | 1               | 1               |
| PhysXRigidbodyCount    | 0            | 256              | 0               | 1               |
| PhysXColliderCount     | 0            | 1024             | 0               | 1               |
| ClothComponentCount    | 0            | 256              | 0               | 1               |
| AnimatorComponentCount | 256          | 256              | 1               | 1               |
| ContactCount           | 256          | 256              | 1               | 1               |
| PolyVertices           | 0            | 2,560,000        | 0               | 100             |
| ParticleCount          | 0            | 2,560,000        | 0               | 1               |
| ParticleSystemCount    | 0            | 256              | 0               | 1               |
| LightCount             | 0            | 256              | 0               | 1               |
| MaterialCount          | 0            | 4                | 0               | 1               |

> 所有参数目标值设为 `Constants.cs` 定义的组件上限，实际生成数量由预算系统动态截断。调试模式下所有参数均为 1（仅验证代码路径）。

#### 4.5.3 CPU 防御详解

所有 CPU 防御组件采用统一的预算填充分配方式：检测已有组件数量 → 计算剩余预算 → 按目标值与预算取较小值生成。

**VRCParentConstraint 链** (`FillConstraintChains`)

逐链顺序填充，每个节点仅附加 1 个 VRCParentConstraint（包含位置+旋转约束，CPU 开销最高的约束类型），每个节点消耗 1 个约束预算。预算耗尽即停止创建。

- 每节点的 ParentConstraint 源指向链中前一个节点
- 约束属性：`IsActive = true`, `Locked = true`（通过 `ActivateConstraint` 使用 SerializedObject 设置）
- ConstraintChainCount 设为 CONSTRAINT_MAX_COUNT，由预算截断实际链数
- ConstraintDepth=CONSTRAINT_MAX_COUNT，每链最大深度由预算截断

**VRCPhysBone 链** (`CreatePhysBoneChain`)

每条链由 `chainLength` 个骨骼节点组成，配置：

- 积分类型：`Advanced`（高计算复杂度）
- `Pull = 0.8`, `Spring = 0.8`, `Stiffness = 0.5`, `Gravity = 0.5`
- 每个参数都配有 AnimationCurve（共 6 条 Curve：pull/spring/stiffness/gravity/gravityFalloff/immobile）
- 限制：`LimitType.Angle`, `MaxAngleX/Z = 45°`, `LimitRotation = (15, 30, 15)`
- 拉伸：`MaxStretch = 0.5`
- 抓取：`AllowGrabbing = True`, `AllowPosing = True`, `GrabMovement = 0.8`, `SnapToHand = true`
- 每条链附加 `collidersPerChain` 个 VRCPhysBoneCollider（Capsule 类型，半径 0.3m，高度 1.0m，`InsideBounds = true`）
- Collider 按总链数均匀分配，余数依次分配给前几条链
- Collider 实际生成数量取 `min(PhysBoneColliders参数, colliderBudget)` 确保不超过参数目标值和系统预算
- PB Collider Check Count = `总碰撞器数 × physBoneLength`，上限 10000：先遍历现有 PhysBone 计算已占用的 Collision Check 数（每个 PB 的 colliders 数 × 链内 Transform 数），从预算中扣除后，若仍超限则自动减少总碰撞器数为 `colliderCheckBudget / physBoneLength`

**VRCContact 系统** (`FillContacts`)

成对创建 Sender + Receiver，总数不超过 Contact 预算：

- Sender 数 = `(componentCount + 1) / 2`，Receiver 数 = `componentCount / 2`
- 形状：Capsule（半径 1.0m，高度 2.0m）
- 碰撞标签：`["Tag1", "Tag2", "Tag3", "Tag4", "Tag5"]`
- `localOnly = true`

**Animator 组件** (`CreateAnimatorComponents`，等级 1+，`enableCpu` 保护)

受 Animator 预算限制：

- 每个 Animator 绑定空的 RuntimeAnimatorController
- `cullingMode = AlwaysAnimate`（禁用剔除强制更新）
- VRChat 同步系统会强制同步所有 Animator 参数状态

**PhysX Rigidbody + Collider** (`CreatePhysXComponents`，等级 2，`enableGpu` 保护)

利用物理引擎计算增加 CPU 开销，受 Rigidbody 预算限制：

- 每个 Rigidbody：`mass = 100`, `drag = 50`, `angularDrag = 50`, `useGravity = false`, `isKinematic = false`, `ContinuousSpeculative`, `FreezeAll`
- 每个 Rigidbody 附加 `PhysXColliderCount / rigidbodyCount` 个 Collider（BoxCollider 和 SphereCollider 交替）

**Cloth 布料** (`CreateClothComponents`，等级 2，`enableGpu` 保护)

布料模拟 CPU 密集，受 Cloth 预算限制：

- 每个布料为 10×10 顶点网格（121 顶点，200 三角形）
- `clothSolverFrequency = 240`，`damping = 0.9`，`selfCollisionStiffness = 0.2`，`worldVelocityScale = 0`
- SkinnedMeshRenderer 支持布料形变

> **注意**: 系统自动检测 Avatar 上已有的 PhysBone、PhysBone Collider、Constraint、Contact (Sender + Receiver)、Rigidbody、Cloth、Animator 数量，计算可用预算后动态调整防御组件数量，确保总数不超过配置上限。Animator 在等级 1+ (`enableCpu`) 下创建，PhysX、Cloth 组件仅在等级 2 (`enableGpu`) 下创建。

#### 4.5.4 GPU 防御详解

**防御 Shader** (`UnityBox/ASS_DefenseShader`)

极其 GPU 密集的自定义 Shader，设计目标为单材质即可耗尽所有 GPU 资源：

- **Shader Model**: `#pragma target 5.0`（Shader Model 5.0）
- **渲染通道**: 3 个 Pass（ForwardBase、ForwardAdd、ShadowCaster）
- **属性**: 100+ 个可配置属性（16 个纹理采样器、大量浮点/颜色参数），属性名全部混淆
- **GPU 密集特性**:
  - Mandelbrot 分形（8192 次迭代）
  - Julia 集（4096 次迭代）
  - Burning Ship 分形（4096 次迭代）
  - Tricorn 分形（2048 次迭代）
  - 路径追踪（128 次反弹，每次 6 个 fbm 调用）
  - 流体模拟（2048 次迭代）
  - 波动模拟（1024 步）
  - 球谐函数（O(n²)，32 阶）
  - BRDF 积分（1024 个采样）
  - 环境光遮蔽（512 个采样）
  - 屏幕空间反射（512 步）
  - 卷积模糊（O(n²) 核，128）
  - Bloom 后处理（128 轮 × 16 采样）
  - 体积云（嵌套循环）
  - 次表面散射（SSS）
  - 虹彩光泽
  - Parallax 视差 Mapping
  - Caustics 光线营造
  - 主循环 65536 次迭代（每次含 16 个纹理采样）
  - 顶点位移（128 次迭代）
  - 额外显存消耗循环（4096 次反复纹理采样）
  - 噪声累加循环（1024 次 fbm 计算）
  - 碰撞积分循环（512 次）
  - 软阴影累积（128 次）

**材质防御** (`CreateMaterialComponents`)

1. 防御 Shader 获取（`GetDefenseShader`）：优先使用 `UnityBox/ASS_DefenseShader`，回退到 `Standard`
2. 防御材质创建（`CreateMaterial`）：
   - 每个网格创建独立材质（不共享），附带独立大纹理
   - 透明渲染队列 = 3000
   - 透明渲染队列 = 3000
   - 每个材质从RenderTexture纹理池（16张 1024×1024 RGBA32，运行时占用显存但不膨胀 Asset Bundle）中取纹理
3. 高面数球体 Mesh（`GenerateSphereMesh`）：
   - 单 subMesh
   - 双 UV 通道 + 顶点色
   - 顶点数通过 subdivisions 控制（`clamp(ceil(sqrt(targetVertices/6)), 10, 200)`）
   - 使用 `sharedMesh` 赋值（避免不必要的实例化）

**粒子防御** (`CreateParticleComponents`)

- 总粒子数分配到多个 ParticleSystem（每个系统至少 1000 粒子）
- 每个主系统创建 1 个 SubEmitter，因此实际 ParticleSystem 数 = 主系统数 × 2，配置时自动将目标数除以 2
- SubEmitter 的 maxParticles 预先从总预算中扣除（`subEmitterMax = min(500, initialPerSystem/10)`），确保主系统+子系统总粒子数 ≤ 总预算
- 每个系统配置：
  - `loop = true`, `prewarm = true`, `startLifetime = 6~12s`, `startSpeed = 1~5`
  - 发射率：`particlesPerSystem / 2`，附带 Burst 发射（瞬间大量粒子）
  - 3D Start Size/Rotation（每轴独立随机范围）
  - World 模拟空间，随机重力修改器 0.3~1.2
  - **渲染器**：Mesh 模式（每个粒子渲染高面数球体 2000~10000 顶点，生成大量三角面）
    - Standard Shader + Metallic/Smoothness + Emission
    - GPU Instancing 启用
    - World 对齐，Distance 排序
  - **启用模块**：
    - VelocityOverLifetime（线性+轨道+径向+速度修改器）
    - ForceOverLifetime（随机化 3 轴力）
    - ColorOverLifetime（3 段 HSV 渐变 + Alpha 淡入淡出）
    - SizeOverLifetime（3 轴分离，AnimationCurve）
    - RotationOverLifetime（3 轴 ±360°）
    - **Noise**（4 octave 高质量湍流，影响位置/旋转/大小）
    - **Collision**（World 3D 碰撞，High 质量，256 碰撞体，全层碰撞，发送碰撞消息）
    - **Trails**（PerParticle 模式，80% 粒子产生拖尾，生成光照数据，自适应宽度曲线）
    - **TextureSheetAnimation**（4×4 网格，3 周期循环）
    - **LimitVelocityOverLifetime**（3 轴限制+阻力）
    - **InheritVelocity**（继承发射器速度）
    - **LifetimeByEmitterSpeed**
    - **Sub-emitters**（碰撞+死亡事件创建子粒子系统，子系统也使用 Mesh 模式+Trails+Noise+WorldCollision）
  - 每个主粒子系统创建 1 个子粒子发射器（Collision + Death 类型）
  - Trail Material 独立材质（不同 HSV 色相 + 自发光）

**光源防御** (`CreateLightComponents`)

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
| `disableRootChildren` | bool              | true   | 锁定时禁用 Avatar 根子对象                                 |
| `defenseLevel`        | int               | 2      | 防御等级 0-2（0=仅密码, 1=CPU, 2=CPU+GPU，见 §4.5.2）      |
| `writeDefaultsMode`   | WriteDefaultsMode | Auto   | Auto = 自动检测 / On = 依赖自动恢复 / Off = 显式写入恢复值 |

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

| 方法                                 | 说明                                        |
| ------------------------------------ | ------------------------------------------- |
| `AddParameterDriverBehaviour()`      | 添加 VRCAvatarParameterDriver（单参数驱动） |
| `AddMultiParameterDriverBehaviour()` | 添加多参数驱动                              |
| `AddPlayAudioBehaviour()`            | 添加 VRCAnimatorPlayAudio 行为              |

#### 路径和统计

| 方法                               | 说明                                             |
| ---------------------------------- | ------------------------------------------------ |
| `GetRelativePath(root, node)`      | 获取对象相对于 root 的路径                       |
| `SaveAndRefresh()`                 | 保存资产                                         |
| `LogOptimizationStats(controller)` | 输出 Controller 统计（状态数、转换数、文件大小） |

### 6.2 Constants

系统级常量，包括：

- 资源路径 (`ASSET_FOLDER = "Assets/UnityBox/AvatarSecuritySystem/Generated"`)
- 生成文件 (`CONTROLLER_NAME = "ASS_Controller.controller"`, `SHARED_EMPTY_CLIP_NAME = "ASS_SharedEmpty.anim"`)
- 音频资源 (`AUDIO_PASSWORD_SUCCESS`, `AUDIO_COUNTDOWN_WARNING`，直接按文件名从 Resources 加载，导入设置 `loadInBackground=true`)
- Animator 参数名 (`PARAM_PASSWORD_CORRECT`, `PARAM_TIME_UP`, `PARAM_IS_LOCAL`, `PARAM_GESTURE_LEFT/RIGHT`)
- 层名称 (`LAYER_LOCK`, `LAYER_PASSWORD_INPUT`, `LAYER_COUNTDOWN`, `LAYER_AUDIO`, `LAYER_DEFENSE`)
- GameObject 名称 (`GO_UI`, `GO_AUDIO_WARNING`, `GO_AUDIO_SUCCESS`, `GO_DEFENSE_ROOT`)
- VRChat 组件上限 (`PHYSBONE_MAX_COUNT=256`, `CONTACT_MAX_COUNT=256`, `CONSTRAINT_MAX_COUNT=2000`, `PHYSBONE_COLLIDER_MAX_COUNT=256`, `PHYSBONE_COLLIDER_CHECK_MAX_COUNT=10000`, `RIGIDBODY_MAX_COUNT=256`, `CLOTH_MAX_COUNT=256`, `ANIMATOR_MAX_COUNT=256`, `POLY_VERTICES_MAX_COUNT=2560000`, `PARTICLE_MAX_COUNT=2560000`, `PARTICLE_SYSTEM_MAX_COUNT=256`, `LIGHT_MAX_COUNT=256`, `MATERIAL_MAX_COUNT=4`)

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
│       Material: UnityBox/ASS_UI（属性已混淆）
│       背景=白, 进度条=红, 进度=1→0, Logo图片
│
├── ASS_Audio_Warning
│   AudioSource (spatialBlend=0, volume=0.5)
│
├── ASS_Audio_Success
│   AudioSource (spatialBlend=0, volume=0.5)
│
└── ASS_Defense (默认禁用)
    ├── Chain_0/
    │   └── C_0 ~ C_{depth} (每节点仅 VRCParentConstraint)
    ├── Chain_1/ ...
    │
    ├── PhysBone_0/
    │   ├── BoneChain_0/
    │   │   └── B_0 ~ B_{length} (VRCPhysBone, Advanced模式)
    │   └── Col_0 ~ Col_{count} (VRCPhysBoneCollider, Capsule)
    │
    ├── ContactSystem/
    │   ├── S_0 ~ S_{half} (VRCContactSender, 5标签)
    │   └── R_0 ~ R_{half} (VRCContactReceiver, 5标签)
    │
    ├── PhysXDefense/ (等级2)
    │   └── Rigidbody_0 ~ Rigidbody_{count}
    │       └── Collider_0 ~ Collider_{n} (Box/Sphere交替)
    │
    ├── ClothDefense/ (等级2)
    │   └── Cloth_0 ~ Cloth_{count} (10×10顶点网格)
    │
    ├── AnimatorDefense/ (等级1+)
    │   └── Animator_0 ~ Animator_{count} (空Controller, AlwaysAnimate)
    │
    ├── MaterialDefense/ (等级2)
    │   └── Mesh_0 ~ Mesh_{count} (高面数球体, 单subMesh)
    │
    ├── ParticleDefense/ (等级2)
    │   └── PS_0 ~ PS_{count}
    │       └── SubEmitter_0 (碰撞+死亡子发射器)
    │
    └── LightDefense/ (等级2)
        └── L_0 ~ L_{count}
            (Point/Spot交替, Soft Shadow, VeryHigh分辨率)
```

---

## 9. Inspector 编辑器布局

### 9.1 界面结构

```
┌─ 标题区域 ──────────────────────────────────┐
│  🔒 Avatar Security System (ASS)             │
│  [语言选择器]                                │
├─ 密码配置 ──────────────────────────────────┤
│  [左/右手切换]                               │
│  步骤1: [手势选择] [X]                       │
│  步骤2: [手势选择] [X]                       │
│  ...                                         │
│  [添加手势] [清空]                           │
│  密码强度: ████ (强/中/弱/无效)              │
├─ 倒计时配置 ────────────────────────────────┤
│  倒计时时间: [30━━━━━━━━━120]                │
├─ 防御配置 ──────────────────────────────────┤
│  防御等级: [0-2]                             │
│  ℹ️ 等级说明                                 │
├─ 高级设置 ──────────────────────────────────┤
│  调试选项                                    │
│    [✓] PlayMode 时禁用                       │
│    [✓] 禁用防御                              │
│  锁定选项                                    │
│  [✓] 隐藏对象                                │
│  WD模式: [Auto ▼]                          │
└──────────────────────────────────────────────┘
```

---

## 10. 注意事项

### 10.1 VRChat 组件限制

系统对所有 VRC 受限组件采用统一预算分配方式：检测已有数量 → 计算剩余预算 → `min(目标值, 预算)` 确定实际生成数量。

| 组件类型          | 配置上限 | 检测方式                                                                                     |
| ----------------- | -------- | -------------------------------------------------------------------------------------------- |
| PhysBone          | 256      | `GetComponentsInChildren<VRCPhysBone>()`                                                     |
| PhysBone Collider | 256      | `GetComponentsInChildren<VRCPhysBoneCollider>()`，按链数均匀分配，取 `min(参数目标值, 预算)` |
| PB Collider Check | 10000    | 总碰撞检查数 = 总Collider数 × physBoneLength，超限时自动减少总Collider数                     |
| Constraint        | 2000     | `GetComponentsInChildren<VRCConstraintBase>()`，仅使用 VRCParentConstraint                   |
| Contact           | 256      | `GetComponentsInChildren<VRCContactSender>() + <VRCContactReceiver>()`                       |
| Rigidbody         | 256      | `GetComponentsInChildren<Rigidbody>()`                                                       |
| Cloth             | 256      | `GetComponentsInChildren<Cloth>()`                                                           |
| Animator          | 256      | `GetComponentsInChildren<Animator>()`                                                        |

**预算检查代码逻辑** (`Defense.cs CreateDefenseComponents()`):

```csharp
int constraintBudget = Mathf.Max(0, Constants.CONSTRAINT_MAX_COUNT - existingConstraints);
int pbBudget = Mathf.Max(0, Constants.PHYSBONE_MAX_COUNT - existingPhysBones);
int colliderBudget = Mathf.Max(0, Constants.PHYSBONE_COLLIDER_MAX_COUNT - existingColliders);
// 遍历现有PhysBone计算已占用的Collision Check数
int existingCollisionChecks = Σ(pb.colliders.Count × CountTransforms(pb.root));
int colliderCheckBudget = Mathf.Max(0, Constants.PHYSBONE_COLLIDER_CHECK_MAX_COUNT - existingCollisionChecks);
int contactBudget = Mathf.Max(0, Constants.CONTACT_MAX_COUNT - existingContacts);
int rigidbodyBudget = Mathf.Max(0, Constants.RIGIDBODY_MAX_COUNT - existingRigidbodies);
int clothBudget = Mathf.Max(0, Constants.CLOTH_MAX_COUNT - existingCloth);
int animatorBudget = Mathf.Max(0, Constants.ANIMATOR_MAX_COUNT - existingAnimators);
```

这确保了即使 Avatar 本身已接近组件上限，ASS 也不会导致构建失败。所有组件类型遵循相同的预算分配原则：目标值设为配置上限（`Constants.cs` 定义），由预算截断实际生成数量。PB Collider 额外使用 `min(PhysBoneColliders参数, colliderBudget)` 双重限制。

### 10.2 Write Defaults 模式

- **Auto（推荐）**：自动检测已有 Controller 的 WD 设置，遵循大多数状态的设置
- **WD On**：动画结束后参数自动恢复默认值，更简洁
- **WD Off**：每个状态需要显式写入所有受控属性的恢复值
- 系统自动根据配置选择对应的动画生成策略

### 10.3 参数同步

- `ASS_PasswordCorrect`：`networkSynced = true`，`saved = true`
  - 同步确保远端玩家也能看到解锁效果
  - `saved = true` 确保重新加载 Avatar 后保持状态
- `ASS_TimeUp`：`networkSynced = false`，`saved = false`
  - 仅本地使用，无需同步

### 10.4 构建流程兼容性

- `callbackOrder = -1026` 确保 ASS 在 NDMF Preprocess (-11000)/VRCFury 主处理 (-10000) 之后、NDMF Optimize (-1025) 之前执行
- VRCFury 参数压缩 (ParameterCompressorHook) 在 `int.MaxValue - 100` 执行，远在 ASS 之后，ASS 新增的参数会被正确识别和压缩
- ASS 获取现有 FX Controller 并追加层，不会覆盖已有内容
- 使用 `IEditorOnly` 接口，Runtime 组件不会出现在构建产物中

### 10.5 防御系统安全实践

- **静态 Mesh 缓存**: 使用 Unity 的 `==` 运算符检测已销毁对象（而非 `??=` 的 C# null 语义），防止使用已销毁的 Mesh 导致原生崩溃
- **材质赋值**: 使用 `sharedMaterial` 而非 `material`，避免创建不必要的材质副本
- **对象清理**: 使用 `Object.DestroyImmediate`（而非 `Object.Destroy`），确保在编辑器同步回调中立即销毁对象
