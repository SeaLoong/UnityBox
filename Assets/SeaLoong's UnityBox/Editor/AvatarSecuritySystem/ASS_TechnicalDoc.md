# Avatar Security System (ASS) 技术文档

## 1. 系统概述

Avatar Security System (ASS) 是一个 VRChat Avatar 防盗保护系统。它在 Avatar 构建/上传时自动注入密码验证和防御机制，当密码未正确输入时，通过消耗盗用者客户端的 CPU/GPU 资源使被盗 Avatar 无法正常使用。

### 1.1 核心特性

- **手势密码验证**：通过 VRChat 左/右手手势组合作为密码
- **倒计时机制**：限时密码输入，超时自动触发防御
- **多层防御**：CPU 防御（Constraint、PhysBone、Contact）+ GPU 防御（Shader、Overdraw、粒子、光源）
- **视觉反馈**：3D HUD 倒计时进度条 + 音频警告
- **本地/远端分离**：防御仅在本地端触发，远端玩家看到正常 Avatar
- **Write Defaults 兼容**：支持 WD On / WD Off 两种模式
- **国际化**：支持中文简体、英语、日语

### 1.2 设计原则

1. **构建时注入**：所有安全组件在 VRCSDK 构建流程中自动生成，不修改原始资产
2. **NDMF/VRCFury 兼容**：`callbackOrder = -1026`，在 NDMF Preprocess (-11000) 和 VRCFury (-10000) 之后、NDMF Optimize (-1025，含 VRCFury 参数压缩) 之前执行
3. **VRChat 限制遵守**：严格遵守 PhysBone (256)、Contact (200) 等组件数量上限
4. **无侵入式**：使用 `IEditorOnly` 组件，不影响运行时

---

## 2. 系统架构

### 2.1 文件结构

```
Editor/AvatarSecuritySystem/
├── AvatarSecurityBuildProcessor.cs   # 系统入口（VRCSDK 回调）
├── LockSystem.cs                     # 锁定/解锁层生成器
├── GesturePasswordSystem.cs          # 手势密码验证层生成器
├── CountdownSystem.cs                # 倒计时 + 音频警告层生成器
├── FeedbackSystem.cs                 # 视觉反馈（3D HUD）生成器
├── DefenseSystem.cs                  # CPU/GPU 防御组件生成器
├── AnimatorUtils.cs                  # Animator 操作工具类
├── AnimationClipGenerator.cs         # 动画剪辑生成工具
├── Constants.cs                      # 系统常量定义
├── I18n.cs                           # 国际化
└── AvatarSecuritySystemEditor.cs     # Inspector 编辑器 UI

Runtime/
└── AvatarSecuritySystem.cs           # 运行时配置组件（MonoBehaviour + IEditorOnly）

Resources/AvatarSecuritySystem/
├── PasswordSuccess.wav               # 密码成功音效
└── CountdownWarning.wav              # 倒计时警告音效
```

### 2.2 类依赖关系

```
AvatarSecurityBuildProcessor (入口)
├── LockSystem.CreateLockLayer()
│   └── FeedbackSystem.CreateHUDCanvas() / CreateCountdownBar()
├── GesturePasswordSystem.CreatePasswordLayer()
├── CountdownSystem.CreateCountdownLayer()
├── CountdownSystem.CreateAudioLayer()
└── DefenseSystem.CreateDefenseLayer()

共享工具：
├── AnimatorUtils        (Animator 操作)
├── AnimationClipGenerator (动画剪辑生成)
├── Constants            (常量)
└── I18n                 (国际化)
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

### 3.1 构建时序 (`AvatarSecurityBuildProcessor`)

```
OnPreprocessAvatar(avatarGameObject)
│
├─ 1. ExtractASSConfiguration()
│     提取 AvatarSecuritySystemComponent 组件
│     验证密码配置有效性 (IsPasswordValid)
│
├─ 2. HasValidPassword()
│     检查 gesturePassword 列表非空
│
├─ 3. ShouldGenerateInCurrentMode()
│     PlayMode 时检查 enableInPlayMode 开关
│
└─ 4. ExecuteGeneration()
      │
      ├─ LoadAudioResources()
      │   从 Resources/AvatarSecuritySystem/ 加载音频
      │
      └─ GenerateSystem()
          │
          ├─ GetOrCreateFXController()
          │   获取 VRCAvatarDescriptor 的 FX Controller，不存在则创建
          │
          ├─ AddVRChatBuiltinParameters()
          │   注册 IsLocal 参数
          │
          ├─ CreateVisualFeedback()
          │   ├─ FeedbackSystem.CreateHUDCanvas()
          │   └─ FeedbackSystem.CreateCountdownBar()
          │
          ├─ SetupAudioSources()
          │   创建 ASS_Audio_Warning / ASS_Audio_Success 对象
          │
          ├─ GenerateSystemLayers()
          │   ├─ [1] LockSystem.CreateLockLayer()
          │   ├─ [2] GesturePasswordSystem.CreatePasswordLayer()
          │   ├─ [3] CountdownSystem.CreateCountdownLayer()
          │   ├─ [4] CountdownSystem.CreateAudioLayer()
          │   └─ [5] DefenseSystem.CreateDefenseLayer()
          │
          ├─ LockSystem.ConfigureLockLayerWeight()
          │   配置 Lock 层自身权重（Locked=1, Unlocked=0）
          │
          ├─ RegisterASSParameters()
          │   注册 ASS_PasswordCorrect(synced) 和 ASS_TimeUp(local)
          │   到 VRCExpressionParameters
          │
          ├─ LockSystem.LockFxLayerWeights()
          │   锁定非 ASS 层权重（Locked=0, Unlocked=1）
          │
          └─ SaveAndOptimize()
              保存资产，输出统计
```

### 3.2 运行时状态流

```
Avatar 加载
│
├─ [远端玩家] Remote 状态
│   遮挡 Mesh 隐藏，Avatar 正常显示
│   ┌─────────────────────────────┐
│   │ 当 PasswordCorrect = true  │──→ Unlocked
│   └─────────────────────────────┘
│
├─ [本地玩家] IsLocal = true
│   │
│   ├─ Locked 状态（初始）
│   │   遮挡 Mesh 显示（白屏），Avatar 隐藏
│   │   UI Canvas 显示（倒计时进度条）
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
│       ├─ Countdown 层：设置 TimeUp 参数
│       ├─ Password 层：Any State → TimeUp_Failed（禁止继续输入）
│       └─ Defense 层：Inactive → Active（防御组件激活）
```

---

## 4. 子系统详解

### 4.1 锁定系统 (LockSystem)

**文件**: `LockSystem.cs`

**功能**: 控制 Avatar 的可见性和层权重

#### 4.1.1 状态机

```
                    ┌──────────┐
           IsLocal──→│  Locked  │──PasswordCorrect──→┐
                    │(显示遮挡) │←──!PasswordCorrect──┤
                    └──────────┘                      │
┌──────────┐                              ┌──────────┐
│  Remote  │──PasswordCorrect────────────→│ Unlocked │
│(默认状态) │←──!PasswordCorrect──────────│(恢复显示) │
└──────────┘                              └──────────┘
```

#### 4.1.2 锁定动画 (CreateLockClip)

- **启用**: UI Canvas (`ASS_UI`)、遮挡 Mesh (`ASS_OcclusionMesh`)、音频对象
- **禁用**: 防御根对象 (`__ASS_Defense__`)
- **隐藏 Avatar**: 当 `disableRootChildren = true`
  - 非 Armature 子对象：`Scale = (0, 0, 0)`
  - Armature 子对象：`Scale = (0, 0, 0)` + `Position.y = -9999` + `m_IsActive = false`

#### 4.1.3 解锁动画 (CreateUnlockClip)

- **禁用**: UI Canvas、遮挡 Mesh、防御根对象
- **启用**: 音频对象
- **恢复 Avatar**:
  - **WD On 模式**: 仅显式恢复 Armature（Scale/Position/Active = 原始值），其他依赖 WD 自动恢复
  - **WD Off 模式**: 显式写入所有根子对象的原始 Scale/Position/Active

#### 4.1.4 遮挡 Mesh

- 在 Avatar 根对象下创建 `ASS_OcclusionMesh`
- 使用 VRCParentConstraint 绑定到头部骨骼（Head），偏移 Z=+0.18m（前方 18cm）
- 材质：Unlit/Color 白色不透明
- 大小：0.5×0.5，覆盖标准 FOV
- 默认 `SetActive(false)`，由锁定动画控制显示

#### 4.1.5 Transform Mask

创建 `AvatarMask` 限制 Lock 层仅影响 ASS 自身对象和根子对象，不干扰骨骼动画。

#### 4.1.6 FX 层权重锁定

- `ConfigureLockLayerWeight()`: Lock 层自身权重（Locked=1, Unlocked=0）
- `LockFxLayerWeights()`: 非 ASS 层权重（Locked=0, Unlocked=1）
  - 防止盗用者通过修改 FX Controller 绕过保护

---

### 4.2 手势密码系统 (GesturePasswordSystem)

**文件**: `GesturePasswordSystem.cs`

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

| 状态                    | 功能                                                     |
| ----------------------- | -------------------------------------------------------- |
| `Wait_Input`            | 初始状态，等待第一位密码输入                             |
| `Step_N_Holding`        | 正在保持第 N 位手势，需保持 `gestureHoldTime` 秒         |
| `Step_N_Confirmed`      | 第 N 位已确认，等待下一位                                |
| `Step_N_ErrorTolerance` | 容错缓冲（`gestureErrorTolerance` 秒），短暂误触后可继续 |
| `Password_Success`      | 密码正确，设置 `PasswordCorrect = true`                  |
| `TimeUp_Failed`         | 倒计时结束，禁止继续输入                                 |

#### 4.2.3 转换规则

- **Idle 手势 (0) 自循环**: 允许松开手指后回到 Idle 而不重置进度
- **错误手势容错**: 短暂误触进入 ErrorTolerance 状态，在容错时间内输入正确手势可继续
- **尾部匹配重启**: 在错误后如果按下密码第一位手势，回到 Step_1_Holding 重新开始
- **TimeUp 全局中断**: Any State → TimeUp_Failed，倒计时结束立即禁止输入

#### 4.2.4 时间控制

手势保持和容错通过**定长动画剪辑**实现：

- Holding 状态使用 `gestureHoldTime` 秒长度的空动画，`exitTime=1.0` 表示动画播完后转换
- ErrorTolerance 状态使用 `gestureErrorTolerance` 秒长度的空动画

---

### 4.3 倒计时系统 (CountdownSystem)

**文件**: `CountdownSystem.cs`

**功能**: 提供限时机制和音频警告

#### 4.3.1 倒计时层 (`ASS_Countdown`)

```
Remote ──(IsLocal)──→ Countdown ──(exitTime=1.0)──→ TimeUp
                           │                           │
                      (PasswordCorrect)           (PasswordCorrect)
                           ↓                           ↓
                        Unlocked ←──────────────────────┘
```

- **Countdown 状态**: 播放 `countdownDuration` 秒的进度条动画
  - 动画控制 `ASS_UI/CountdownBar/Bar` 的 `localScale.x` 从 1 到 0
- **TimeUp 状态**: 通过 ParameterDriver 设置 `ASS_TimeUp = true`

#### 4.3.2 音频层 (`ASS_Audio`)

```
Remote ──(IsLocal)──→ Waiting ──(动画播完)──→ WarningBeep ──(自循环,每秒)
                                                  │
                                            (TimeUp 或 PasswordCorrect)
                                                  ↓
                                                Stop
```

- **Waiting 状态**: 播放 `(countdownDuration - warningThreshold + 0.1)` 秒的空动画
  - +0.1s 延迟防止最后一次循环在 TimeUp 之后触发
- **WarningBeep 状态**: 1 秒动画自循环，每次进入时通过 `VRCAnimatorPlayAudio` 播放警告蜂鸣

---

### 4.4 反馈系统 (FeedbackSystem)

**文件**: `FeedbackSystem.cs`

**功能**: 创建 3D HUD 显示元素

#### 4.4.1 HUD Canvas (`ASS_UI`)

- 位置：头部下方 2cm、前方 15cm（通过 VRCParentConstraint 绑定到 Head 骨骼）
- 默认 `SetActive(false)`，仅 Locked 状态时启用

#### 4.4.2 倒计时进度条 (`CountdownBar`)

- 结构：`ASS_UI / CountdownBar / Background(白) + Bar(红)`
- 尺寸：15cm 宽 × 3cm 高 × 3cm 厚（3D 条形）
- 通过 `localScale.x` 动画控制 Bar 长度（1 → 0）

---

### 4.5 防御系统 (DefenseSystem)

**文件**: `DefenseSystem.cs`

**功能**: 创建消耗客户端资源的防御组件

#### 4.5.1 防御层状态机

```
Inactive ──(IsLocal && TimeUp)──→ Active
```

防御组件挂载在 `__ASS_Defense__` 对象下，默认 `SetActive(false)`。
Active 状态由 Lock 层的动画控制激活。

#### 4.5.2 防御等级参数表

| 参数                 | 等级 1 (CPU) | 等级 2 (CPU+GPU 中低) | 等级 3 (CPU+GPU 最高) | 调试模式 |
| -------------------- | ------------ | --------------------- | --------------------- | -------- |
| ConstraintDepth      | 100          | 100                   | 100                   | 3        |
| ConstraintChainCount | 10           | 10                    | 10                    | 1        |
| PhysBoneLength       | 256          | 256                   | 256                   | 3        |
| PhysBoneChainCount   | 10           | 10                    | 10                    | 1        |
| PhysBoneColliders    | 256          | 256                   | 256                   | 2        |
| ContactCount         | 200          | 200                   | 200                   | 4        |
| ShaderLoops          | —            | 200                   | 1,000                 | 10       |
| OverdrawLayers       | —            | 50                    | 200                   | 3        |
| PolyVertices         | —            | 50,000                | 200,000               | 1,000    |
| ParticleCount        | —            | 10,000                | 100,000               | 100      |
| ParticleSystemCount  | —            | 3                     | 10                    | 1        |
| LightCount           | —            | 5                     | 20                    | 1        |
| MaterialCount        | —            | 2                     | 3                     | 1        |

#### 4.5.3 CPU 防御详解

**VRCConstraint 链** (`CreateConstraintChain` / `CreateExtendedConstraintChains`)

每条链由 `depth` 个嵌套节点组成，每个节点附加 5 种 VRC 约束组件：

- VRCParentConstraint
- VRCPositionConstraint
- VRCRotationConstraint
- VRCScaleConstraint
- VRCAimConstraint

约束属性：`IsActive = true`, `Locked = true`, `FreezeToWorld = true`

扩展链（等级 3）：额外创建多条链，使用更多约束类型组合。

**VRCPhysBone 链** (`CreatePhysBoneChains` / `CreateExtendedPhysBoneChains`)

每条链由 `chainLength` 个骨骼节点组成，配置：

- `Pull = 0.8`, `Stiffness = 0.5`, `Gravity = 0.2`
- `MaxStretch = 0.5`, `MaxAngleX/Z = 30°`
- 每条链附加 `colliderCount` 个 VRCPhysBoneCollider（Sphere 类型，半径 0.1m）
- 参数禁止用户运行时修改：`parameter = ""`, `allowPosing/Grabbing/Collision = false`

扩展链（等级 3）：额外链使用更极端参数（Pull=1, Stiffness=0, Gravity=0.5）。

**VRCContact 系统** (`CreateContactSystem` / `CreateExtendedContactSystem`)

成对创建 Sender + Receiver：

- Sender：`collisionTags = ["ASS_Defense_{i}"]`
- Receiver：`collisionTags = ["ASS_Defense_{i}"]`, `receiverType = Constant`, `minVelocity = 0.05f`
- 球体碰撞半径：0.1m（基础）/ 0.15m（扩展）

#### 4.5.4 GPU 防御详解

**材质防御** (`CreateMaterialDefense`)

1. 创建防御 Shader（优先使用 `SeaLoong/DefenseShader`，回退到 `Standard`）
2. 创建高循环次数材质（`ApplyFixedShaderParameters` 设置 36 个 GPU 密集参数）
3. 创建高面数球体 Mesh（`CreateHighDensitySphereMesh`）
   - 2 个子网格（subMesh）增加批次
   - 双 UV 通道 + 顶点色
4. Overdraw 层堆叠（`CreateOverdrawLayersWithMaterial`）
   - 多层半透明 Quad，z 间距 0.001m
   - 使用相同的防御材质，透明渲染队列 3000

**粒子防御** (`CreateParticleDefense`)

- 总粒子数分配到多个 ParticleSystem
- 每个系统配置：
  - 发射形状：Sphere，半径 2m，随机方向
  - Burst 发射：0.5s 内发射 `particlesPerSystem` 个粒子
  - 持续发射：`rateOverTime = particlesPerSystem / 2`
  - 生命周期：10s
  - 模拟空间：World

**光源防御** (`CreateLightDefense`)

- 交替创建 Point / Spot 光源
- 环形排列（360°/lightCount 间距，半径 2m）
- 全部启用 `Soft Shadow`，`shadowResolution = VeryHigh`
- HSV 色彩分布（色相随索引等分）

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

| 参数                  | 类型 | 默认值 | 说明                                           |
| --------------------- | ---- | ------ | ---------------------------------------------- |
| `enableInPlayMode`    | bool | true   | PlayMode 时是否生成安全系统                    |
| `disableDefense`      | bool | false  | 禁用防御组件（仅保留密码系统，用于测试）       |
| `lockFxLayers`        | bool | true   | 锁定时将非 ASS 的 FX 层权重设为 0              |
| `disableRootChildren` | bool | true   | 锁定时隐藏 Avatar 根子对象                     |
| `defenseLevel`        | int  | 3      | 防御等级 0-3（见 §4.5.2）                      |
| `writeDefaultsMode`   | enum | On     | WD On = 依赖自动恢复 / WD Off = 显式写入恢复值 |

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

### 6.1 AnimatorUtils

最核心的工具类，提供 Animator 层、参数、转换、VRC 行为的创建方法。

| 方法                            | 说明                                             |
| ------------------------------- | ------------------------------------------------ |
| `SharedEmptyClip`               | 共享的空 AnimationClip（磁盘缓存，节省文件大小） |
| `CreateLayer()`                 | 创建 AnimatorControllerLayer + StateMachine      |
| `AddParameterIfNotExists()`     | 避免重复添加参数                                 |
| `CreateTransition()`            | 创建状态转换，统一配置 hasExitTime/duration      |
| `CreateAnyStateTransition()`    | 创建 Any State 转换                              |
| `GetRelativePath()`             | 获取对象相对于 Avatar 根的路径                   |
| `AddSubAsset()`                 | 安全地将资产嵌入 Controller（防重复）            |
| `SetupAudioSource()`            | 创建 AudioSource 对象并配置                      |
| `AddPlayAudioBehaviour()`       | 添加 VRCAnimatorPlayAudio 状态行为               |
| `AddLayerControlBehaviour()`    | 添加 VRCAnimatorLayerControl 状态行为            |
| `AddParameterDriverBehaviour()` | 添加 VRCAvatarParameterDriver 状态行为           |
| `LogOptimizationStats()`        | 输出 Controller 统计（状态数、转换数、文件大小） |

### 6.2 AnimationClipGenerator

| 方法                                                 | 说明                   |
| ---------------------------------------------------- | ---------------------- |
| `CreateParameterDriverClip(name, param, bool/float)` | 创建驱动参数的动画剪辑 |
| `CreateAudioTriggerClip(name, path, clip, duration)` | 创建音频触发动画       |

### 6.3 Constants

系统级常量，包括：

- 资源路径 (`ASSET_FOLDER`, `AUDIO_RESOURCE_PATH`)
- Animator 参数名 (`PARAM_*`)
- 层名称 (`LAYER_*`)
- GameObject 名称 (`GO_*`)
- VRChat 组件上限 (`PHYSBONE_MAX_COUNT=256`, `CONTACT_MAX_COUNT=200`)
- 防御参数上限 (`CONSTRAINT_CHAIN_MAX_DEPTH=100`, `SHADER_LOOP_MAX_COUNT=3000000`)

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
│   └── CountdownBar
│       ├── Background (白色 Quad)
│       └── Bar (红色 Quad, Scale.x 由动画控制)
│
├── ASS_OcclusionMesh (默认禁用)
│   VRCParentConstraint → Head (+0.18m z)
│
├── ASS_Audio_Warning (默认禁用)
│   AudioSource
│
├── ASS_Audio_Success (默认禁用)
│   AudioSource
│
└── __ASS_Defense__ (默认禁用)
    ├── ConstraintChain_0/
    │   └── Node_0 ~ Node_{depth} (5种VRC约束)
    ├── ConstraintChain_1/ ...
    ├── PhysBoneChain_0/
    │   ├── Bone_0 ~ Bone_{length} (VRCPhysBone)
    │   └── Collider_0 ~ Collider_{count} (VRCPhysBoneCollider)
    ├── ContactSystem/
    │   ├── Sender_0 + Receiver_0
    │   └── ...
    ├── MaterialDefense/
    │   ├── DefenseMesh_0 ~ DefenseMesh_{count} (高面数球体)
    │   ├── OverdrawLayers_0/
    │   │   └── Layer_0 ~ Layer_{count} (透明 Quad)
    │   └── OverdrawLayers_1/ ...
    ├── ParticleDefense/
    │   └── ParticleSystem_0 ~ ParticleSystem_{count}
    └── LightDefense/
        └── Light_0 ~ Light_{count} (Point/Spot, Soft Shadow)
```

---

## 9. 注意事项

### 9.1 VRChat 组件限制

- PhysBone 总数（含模型自带）不超过 256，系统会自动检测已有数量并调整
- Contact Sender + Receiver 总数不超过 200
- Constraint 链深度上限 100

### 9.2 Write Defaults 模式

- **WD On（推荐）**：动画结束后参数自动恢复默认值，更简洁
- **WD Off（兼容性）**：每个状态需要显式写入所有受控属性的恢复值
- 系统自动根据配置选择对应的动画生成策略

### 9.3 参数同步

- `ASS_PasswordCorrect`：`networkSynced = true`，`saved = true`
  - 同步确保远端玩家也能看到解锁效果
  - `saved = true` 确保重新加载 Avatar 后保持状态
- `ASS_TimeUp`：`networkSynced = false`，`saved = false`
  - 仅本地使用，无需同步

### 9.4 构建流程兼容性

- `callbackOrder = -1026` 确保 ASS 在 NDMF Preprocess/VRCFury 主处理之后、VRCFury 参数压缩 (NDMF Optimize) 之前执行
- ASS 获取现有 FX Controller 并追加层，不会覆盖已有内容
- 使用 `IEditorOnly` 接口，Runtime 组件不会出现在构建产物中
