# Avatar Security System (ASS)

**VRChat Avatar 防盗密码保护系统**

[![Unity](https://img.shields.io/badge/Unity-2022.3-black.svg)](https://unity.com/)
[![VRChat](https://img.shields.io/badge/VRChat_SDK-≥3.7.0-blue.svg)](https://vrchat.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](../../../LICENSE)

---

## 目录

1. [系统概述](#系统概述)
2. [安装](#安装)
3. [快速开始](#快速开始)
4. [技术架构](#技术架构)
5. [功能模块](#功能模块)
6. [配置选项](#配置选项)
7. [API 参考](#api-参考)
8. [常见问题](#常见问题)

---

## 系统概述

### 项目目标

Avatar Security System (ASS) 是一个用于 VRChat Avatar 的防盗保护系统。通过**手势密码**和**倒计时机制**，防止恶意玩家通过缓存提取等手段盗取您的 Avatar。

### 核心特性

- **手势密码保护** — 使用 VRChat 的 8 种手势组合作为密码
- **倒计时机制** — 限时输入（默认 30 秒），增加破解难度
- **视觉/音频反馈** — 全屏 Shader 遮罩 + 进度条 + 警告音效（支持 VR 立体渲染）
- **初始锁定** — Avatar 启动时所有功能被禁用
- **智能防御** — 仅对穿戴者生效（IsLocal），不影响其他玩家
- **非破坏性** — 编辑时零影响，仅在 VRChat 构建时自动生成
- **VRCSDK 集成** — 通过 `IVRCSDKPreprocessAvatarCallback` 无缝接入构建流程
- **兼容性** — 与 NDMF、VRCFury、Modular Avatar 等工具链兼容

### 工作流程

```
Avatar 启动
    ↓
所有功能锁定（对象禁用 + 全屏遮罩覆盖视角）
    ↓
倒计时开始（默认 30 秒）
    ↓
用户输入手势密码
    ├─ 正确 → ASS_PasswordCorrect = true → 解锁 → 正常使用
    ├─ 错误 → 容错机制 → 可继续输入
    └─ 超时 → ASS_TimeUp = true → 触发防御（仅对穿戴者）
```

---

## 安装

### 依赖

- Unity 2022.3
- VRChat Creator Companion (VCC) 或 ALCOM
- VRChat SDK Avatars ≥ 3.7.0

### 通过 VCC / ALCOM 安装

1. 添加 VPM 源：
   - **一键添加**：[添加到 VCC](https://sealoong.github.io/UnityBox/)
   - **手动添加**：在 VCC / ALCOM 设置中添加 VPM 源地址：

     ```

     https://sealoong.github.io/UnityBox/vpm.json
     ```

2. 在 VCC / ALCOM 中找到 **Avatar Security System** 并安装

---

## 快速开始

### 步骤 1：添加组件

1. 选择你的 Avatar Root 对象
2. Add Component → **Avatar Security System (ASS)**

### 步骤 2：设置密码

**VRChat 手势对照表：**

| 手势 | 名称        | 手势 | 名称      |
| ---- | ----------- | ---- | --------- |
| 0    | Idle        | 4    | Victory   |
| 1    | Fist        | 5    | RockNRoll |
| 2    | HandOpen    | 6    | HandGun   |
| 3    | Fingerpoint | 7    | ThumbsUp  |

**配置示例：**

- Gesture Password: `[1, 7, 2, 4]`（Fist → ThumbsUp → HandOpen → Victory）
- Use Right Hand: `false`（使用左手）

**密码强度评级：**

- **Weak (弱)**: < 4 位
- **Medium (中)**: 4-5 位，或手势种类少于 4 种
- **Strong (强)**: ≥ 6 位，且至少使用 4 种不同手势

### 步骤 3：配置倒计时

- **Countdown Duration**: 30-120 秒（默认 30 秒）
- **Warning Threshold**: 固定 10 秒（最后 10 秒播放警告音效）

### 步骤 4：手势识别调整（可选）

- **Gesture Hold Time**: 0.1-1.0 秒（默认 0.15 秒），手势需保持的最短时间
- **Gesture Error Tolerance**: 0.1-1.0 秒（默认 0.3 秒），短暂误触的容错时间

### 步骤 5：防御配置（可选）

- **Defense Level 0**: 仅密码系统（不生成任何防御组件）
- **Defense Level 1**: 密码 + CPU 防御（所有 CPU 组件填满至 VRChat 上限）
- **Defense Level 2**（默认）: 密码 + CPU + GPU 防御（所有 CPU+GPU 组件填满至 VRChat 上限，包括 MAX_INT 粒子、256 光源等）

### 步骤 6：构建上传

1. 使用 VRChat SDK 的 **Build & Publish**
2. ASS 会在构建时自动生成（不修改场景中的原始对象）
3. 上传到 VRChat 并在游戏中测试

### 在 VRChat 中使用

1. 穿戴 Avatar 后看到全屏遮罩和倒计时进度条
2. 按照配置的顺序做出手势（使用左手或右手取决于配置）
3. 每个手势保持至少 0.15 秒（可配置）
4. 密码正确 → 成功音效 → 解锁
5. 密码错误 → 容错机制，可继续输入
6. 倒计时结束未解锁 → 触发防御措施（仅对穿戴者）

---

## 技术架构

### 构建流程

```
AvatarSecuritySystemComponent (MonoBehaviour)
    ↓ 配置参数
Processor (IVRCSDKPreprocessAvatarCallback, callbackOrder=-1026)
    ↓ VRChat Build & Publish 时自动执行
生成 5 个 Animator Layer 到 FX Controller:
    ├─ ASS_Lock          (锁定/解锁)
    ├─ ASS_PasswordInput (手势密码验证)
    ├─ ASS_Countdown     (倒计时系统)
    ├─ ASS_Audio         (警告音效)
    └─ ASS_Defense       (防御措施，可选)
    ↓
AnimationClips + GameObject Hierarchy + VRC Components
```

**构建管线执行顺序：**

```
-11000 : NDMF PreprocessHook
-10000 : VRCFury VrcPreuploadHook
 -1026 : ★ ASS（本插件）
 -1025 : NDMF OptimizeHook
 -1024 : VRCFury RemoveEditorOnlyObjects / VRCSDK RemoveAvatarEditorOnly
   MAX : VRCFury ParameterCompressorHook / Cleanup
```

ASS 在 NDMF/VRCFury 主处理完成后注入，VRCFury 参数压缩在远后执行，因此 ASS 参数会被正确识别。

### 文件结构

```
Assets/UnityBox/AvatarSecuritySystem/
├─ package.json                                          # VPM 包清单
├─ Runtime/
│   ├─ AvatarSecuritySystem.cs                           # 主组件类（配置字段）
│   └─ UnityBox.AvatarSecuritySystem.Runtime.asmdef
│
├─ Editor/
│   ├─ AvatarSecuritySystemEditor.cs                     # 自定义 Inspector UI
│   ├─ Processor.cs                                      # VRCSDK 构建处理器（入口点）
│   ├─ Utils.cs                                          # Animator 工具方法
│   ├─ Lock.cs                                           # 锁定系统生成器
│   ├─ GesturePassword.cs                                # 手势密码状态机生成器
│   ├─ Countdown.cs                                      # 倒计时 + 音频层生成器
│   ├─ Feedback.cs                                       # 视觉反馈生成器（全屏 Shader 遮罩）
│   ├─ Defense.cs                                        # 防御系统生成器
│   ├─ Constants.cs                                      # 常量定义（参数名、VRChat 上限等）
│   ├─ I18n.cs                                           # 国际化（中/英/日）
│   ├─ README.md                                         # 本文档
│   └─ UnityBox.AvatarSecuritySystem.asmdef
│
├─ Resources/
│   ├─ Avatar Security System.png                        # Logo
│   ├─ PasswordSuccess.mp3                               # 成功音效
│   ├─ CountdownWarning.mp3                              # 倒计时警告音效
│   ├─ StepSuccess.mp3                                   # 步骤确认音效
│   └─ InputError.mp3                                    # 输入错误音效
│
└─ Shaders/
    ├─ UI.shader                                         # 全屏遮罩 + 进度条 Shader
    └─ DefenseShader.shader                              # 防御 Shader（GPU 密集）
```

### 依赖关系

```
Unity 2022.3
    └─ VRChat SDK Avatars ≥ 3.7.0
```

无其他必要依赖。兼容 NDMF、VRCFury、Modular Avatar 等工具（但不依赖它们）。

---

## 功能模块

### 1. 锁定系统 (Lock)

#### 功能

- 锁定状态下显示全屏 Shader 遮罩覆盖视角
- 隐藏 Avatar 根级子对象（通过 `m_IsActive` 控制）
- 解锁后恢复 Avatar 显示，禁用其他 ASS 层
- Remote 玩家直接跳过锁定状态

#### 状态机结构

```
ASS_Lock Layer
├─ Remote (默认) — 所有玩家的初始状态
│   └─ 遮罩Mesh 隐藏，Avatar 正常显示
├─ Locked — 仅本地玩家锁定时
│   ├─ 显示遮罩 Mesh（m_IsActive=1）
│   ├─ 隐藏 Avatar 根级子对象（m_IsActive=0）
│   └─ 显示全屏 Shader 遮罩 + 进度条
└─ Unlocked — 解锁状态
    ├─ 隐藏遮罩 Mesh（m_IsActive=0）
    ├─ 恢复 Avatar 对象（m_IsActive=1）
    └─ 设置其他 ASS 层权重为 0（释放控制）
```

**转换条件：**

- Remote → Locked: `IsLocal == true && ASS_PasswordCorrect == false`
- Remote → Unlocked: `ASS_PasswordCorrect == true`（参数同步）
- Locked → Unlocked: `ASS_PasswordCorrect == true`
- Unlocked → Remote: `ASS_PasswordCorrect == false`

**关键实现：**

- 全屏遮罩使用自定义 Shader（`UnityBox/ASS_UI`），顶点着色器直接映射到裁剪空间全屏
- 进度条通过动画驱动 Shader 材质属性控制
- 对象控制使用 `GameObject.m_IsActive` 而非 `Transform.localScale`
- 变换遮罩（Transform Mask）仅启用被锁定的根对象与 ASS 对象
- 解锁后将 ASS_Lock 层权重设为 0，释放 Transform 影响

---

### 2. 手势密码系统 (GesturePassword)

#### 功能

- 检测 VRChat 手势输入（GestureLeft / GestureRight）
- 尾部序列匹配（输入 123456 可匹配密码 456）
- 手势稳定时间检测（需保持手势一定时间，默认 0.15 秒）
- 容错机制（短暂误触不会重置，容错时间默认 0.3 秒）
- 本地隔离：入口转换要求 `IsLocal == true`，远端客户端不执行密码检测和音效播放

#### 状态结构（每步）

```
Wait_Input ───[IsLocal + 正确手势]───> Step_N_Holding (gestureHoldTime)
                                ├── [保持正确 + 超时] → Step_N_Confirmed
                                ├── [Idle] → 自循环
                                └── [错误] → Wait_Input

Step_N_Confirmed ───[正确下一位]───> Step_N+1_Holding
                     ├── [Idle] → 自循环
                     └── [错误] → Step_N_ErrorTolerance (gestureErrorTolerance)

Step_N_ErrorTolerance ───[超时]───> Wait_Input
                          ├── [正确下一位] → Step_N+1_Holding
                          ├── [密码第一位] → Step_1_Holding（尾部匹配）
                          └── [Idle] → 自循环
```

**最后一步优化：** 最后一步没有 Confirmed/ErrorTolerance 状态，Holding → Password_Success 直接转换。

**参数驱动：**

- 成功时通过 `VRCAvatarParameterDriver` 设置 `ASS_PasswordCorrect = true`
- 参数同步：`networkSynced = true`，其他玩家可见解锁状态

---

### 3. 倒计时系统 (Countdown)

#### 功能

- 倒计时进度条（全屏 Shader 渲染）
- 超时触发 `ASS_TimeUp` 参数
- 最后 10 秒循环播放警告音效

#### 倒计时层 (ASS_Countdown)

```
Remote (默认) ──[PasswordCorrect]──> Unlocked（已保存的解锁状态，跳过倒计时）
   │
   └──[IsLocal && !PasswordCorrect]──> Countdown ──[exitTime=1.0]──> TimeUp
                                            │                           │
                                       [ASS_PasswordCorrect]    [设置 ASS_TimeUp=1]
                                            ↓                           ↓
                                        Unlocked                    [ASS_PasswordCorrect]
                                                                        ↓
                                                                    Unlocked
```

#### 音频层 (ASS_Audio)

```
Remote (默认) ──[PasswordCorrect]──> Stop（已保存的解锁状态，跳过音效）
   │
   └──[IsLocal && !PasswordCorrect]──> Waiting ──[exitTime=1.0]──> WarningBeep（循环播放）
                                                                       ├── [ASS_TimeUp] → Stop
                                                                       └── [exitTime] → 自循环
```

**动画实现：**

- 倒计时动画控制 Shader 材质属性（1→0）
- 警告音效使用 `VRCAnimatorPlayAudio`，每秒播放一次
- `ASS_Audio_Warning` 与 `ASS_Audio_Success` 分离独立 AudioSource

---

### 4. 视觉反馈系统 (Feedback)

#### 功能

- 创建全屏 Shader 覆盖遮罩（`UnityBox/ASS_UI`）
- 倒计时进度条（通过 Shader 材质属性动画驱动）
- 不依赖 Canvas 或 VRCParentConstraint，通过 Shader 顶点变换直接覆盖全屏
- VR 立体渲染支持（Single Pass Instanced 模式）
- 表情镜遮挡（通过 Stencil 写入 255）

#### UI 结构

```
ASS_UI (默认禁用，锁定时通过动画启用)
├─ Overlay (MeshRenderer + MeshFilter)
│   └─ Material: UnityBox/ASS_UI Shader（全屏遮罩 + 进度条）
│       ├─ GPU Instancing 启用
│       └─ renderQueue = 5000 (Overlay+5000)
├─ ASS_Audio_Success (AudioSource)
└─ ASS_Audio_Warning (AudioSource)
```

#### VR 渲染方案

Shader 使用反向投影→正向投影方式确保 VR 下正确渲染：

1. **顶点着色器**：从 UV 计算目标 NDC，反推 view-space 位置（距相机 100m），再用逐眼 `UNITY_MATRIX_P` 正向投影回 clip space。此方式同时产生正确的深度值（使 Stencil 和深度交互正常工作）。
2. **片段着色器**：通过 `eyeShift = P[0][2] * 0.5` 补偿左右眼的投影偏移（SPI 模式下逐眼 P 矩阵含不对称偏移），使两眼看到居中的相同内容（零视差 HUD 效果）。
3. **FOV 自适应缩放**：使用 `unity_CameraProjection[1][1]`（中心眼，双眼相同）计算缩放因子。桌面端（60° FOV）内容接近全屏，VR（100°+ FOV）内容缩到屏幕中央 ~40%，远离镜片边缘畸变区。
4. **表情镜遮挡**：反向投影产生的真实深度值 + Stencil Ref 255 写入，破坏了 VRChat 表情镜的渲染条件。

---

### 5. 防御系统 (Defense)

#### 功能

- 倒计时结束后激活防御（仅本地生效）
- 根据防御等级自动计算参数，填充到 VRChat 组件上限
- 惰性预算统计：仅在对应功能启用时才统计已有组件数量
- 所有组件类型均有已有数量统计和预算控制，包括：
  - CPU: Constraint、PhysBone、PhysBoneCollider、Contact、Animator
  - GPU: Material、Vertex、Light、ParticleSystem、ParticleMeshTriangle、Rigidbody、Collider、Cloth

#### 防御机制

| 类型    | 组件                       | 等级 | 作用                                      |
| ------- | -------------------------- | ---- | ----------------------------------------- |
| **CPU** | VRCParentConstraint 链     | ≥1   | 填充到上限 2000                           |
| **CPU** | VRCPhysBone + Collider     | ≥1   | PhysBone 填充到 256，Collider 填充到 256  |
| **CPU** | VRCContact Sender/Receiver | ≥1   | 填充到上限 256                            |
| **CPU** | Animator                   | ≥1   | 填充到上限 256                            |
| **GPU** | Rigidbody + Collider       | 2    | Rigidbody 256，Collider 1024              |
| **GPU** | Cloth                      | 2    | 填充到上限 256                            |
| **GPU** | 粒子系统                   | 2    | 最大粒子数 × 355 系统（自适应 Mesh 面数） |
| **GPU** | 光源                       | 2    | 256 个实时光源                            |
| **GPU** | 防御 Shader                | 2    | 8 个 GPU 密集材质                         |

#### 防御等级

**Level 0**: 仅密码系统（不生成任何防御组件）

**Level 1**: CPU 防御（所有 CPU 组件填满至 VRChat 上限）

- Constraint: 最多 2000
- PhysBone: 最多 256 条 × 256 骨骼 + 256 碰撞器
- Contact: 最多 256
- Animator: 最多 256

**Level 2**（默认）: CPU + GPU 防御（所有 CPU+GPU 组件填满至 VRChat 上限，包括 MAX_INT 粒子、256 光源等）

- Level 1 所有 CPU 防御
- Rigidbody: 256，Collider: 1024，Cloth: 256
- 粒子: MAX_INT 粒子 × 355 系统（自适应 Mesh 复杂度）
- 光源: 256
- 防御 Shader: 8 个 GPU 密集材质

> 调试模式（Play Mode）下会生成同类型防御，但使用最小参数值（每类 1 个）以便测试。

#### 状态机

```
ASS_Defense Layer
├─ Inactive (默认) — 防御未激活
└─ Active
    └─ 转换条件: IsLocal == true && ASS_TimeUp == true
```

防御根对象默认 `m_IsActive=0`，激活时设为 1。

---

## 配置选项

### AvatarSecuritySystemComponent 属性

#### 密码配置

| 属性              | 类型        | 默认值         | 说明                                     |
| ----------------- | ----------- | -------------- | ---------------------------------------- |
| `gesturePassword` | `List<int>` | `[1, 7, 2, 4]` | 手势密码序列（1-7），0 位密码 = 禁用 ASS |
| `useRightHand`    | `bool`      | `false`        | 使用右手（true）或左手（false）          |

#### 手势识别配置

| 属性                    | 类型    | 范围     | 默认值 | 说明                     |
| ----------------------- | ------- | -------- | ------ | ------------------------ |
| `gestureHoldTime`       | `float` | 0.1-1.0s | `0.15` | 手势需保持的最短识别时间 |
| `gestureErrorTolerance` | `float` | 0.1-1.0s | `0.3`  | 短暂误触的容错时间       |

#### 倒计时配置

| 属性                | 类型    | 范围    | 默认值 | 说明                                    |
| ------------------- | ------- | ------- | ------ | --------------------------------------- |
| `countdownDuration` | `float` | 30-120s | `30`   | 倒计时时长                              |
| `warningThreshold`  | `float` | —       | `10`   | 警告阶段阈值（HideInInspector，固定值） |

#### 防御配置

| 属性             | 类型   | 范围 | 默认值  | 说明                       |
| ---------------- | ------ | ---- | ------- | -------------------------- |
| `defenseLevel`   | `int`  | 0-2  | `2`     | 0=仅密码, 1=CPU, 2=CPU+GPU |
| `disableDefense` | `bool` | —    | `false` | 完全禁用防御生成           |

#### 高级选项

| 属性                  | 类型                | 默认值    | 说明                                               |
| --------------------- | ------------------- | --------- | -------------------------------------------------- |
| `disabledInPlaymode`  | `bool`              | `true`    | Play Mode 跳过 ASS 生成                            |
| `disableRootChildren` | `bool`              | `true`    | 锁定时隐藏 Avatar 根级子对象                       |
| `hideUI`              | `bool`              | `false`   | 不生成全屏覆盖 UI（遮罩 + 进度条），仅保留音频反馈 |
| `overflowTrick`       | `bool`              | `false`   | 额外 +1 粒子使 VRChat 统计溢出显示 -2147483648     |
| `writeDefaultsMode`   | `WriteDefaultsMode` | `Auto`    | Auto=自动检测 / On / Off                           |
| `uiLanguage`          | `SystemLanguage`    | `Unknown` | Inspector 语言（Unknown=自动检测）                 |

---

## API 参考

### Animator 参数

| 参数名                | 类型 | 默认值 | 同步     | 说明                       |
| --------------------- | ---- | ------ | -------- | -------------------------- |
| `ASS_PasswordCorrect` | Bool | false  | Synced   | 密码验证成功标志           |
| `ASS_TimeUp`          | Bool | false  | Local    | 倒计时结束标志             |
| `IsLocal`             | Bool | —      | Built-in | VRChat 内置（穿戴者=true） |
| `GestureLeft`         | Int  | 0      | Built-in | VRChat 内置（左手 0-7）    |
| `GestureRight`        | Int  | 0      | Built-in | VRChat 内置（右手 0-7）    |

### Animator 层

| 层名                | 权重 | 功能             |
| ------------------- | ---- | ---------------- |
| `ASS_Lock`          | 1.0  | 锁定/解锁控制    |
| `ASS_PasswordInput` | 1.0  | 密码验证         |
| `ASS_Countdown`     | 1.0  | 倒计时           |
| `ASS_Audio`         | 1.0  | 警告音效         |
| `ASS_Defense`       | 1.0  | 防御激活（可选） |

### VRC State Behaviours

| 行为                       | 使用位置                      | 作用       |
| -------------------------- | ----------------------------- | ---------- |
| `VRCAnimatorPlayAudio`     | Password_Success, WarningBeep | 播放音效   |
| `VRCAvatarParameterDriver` | Password_Success, TimeUp      | 设置参数   |
| `VRCAnimatorLayerControl`  | Locked, Unlocked              | 控制层权重 |

---

## 常见问题

### 安全性

#### Q: 密码会被破解吗？

可能，但难度较大。8 种手势的 N 位密码有 8^N 种组合：4 位 = 4,096，6 位 = 262,144，8 位 = 16,777,216。配合 30 秒倒计时，暴力破解不现实。

#### Q: 其他玩家会看到防御效果吗？

不会。防御通过 `IsLocal` 参数隔离，仅穿戴者受影响。其他玩家看到的是正常 Avatar。

### 使用

#### Q: 我忘记密码了怎么办？

1. 在 Unity 项目中查看 Inspector 的密码配置
2. 重新上传没有 ASS 组件的 Avatar
3. 使用备份的未构建项目

#### Q: 朋友穿我的 Avatar 会被锁吗？

会。告诉他们密码即可解锁，或提供无 ASS 组件的版本。

#### Q: 系统会影响 Avatar 性能吗？

- 解锁后：几乎无影响（仅额外的 Animator 层）
- 未解锁：轻微影响（Animator 层计算 + 全屏 Shader）
- 防御激活：严重影响（仅对盗取者生效）

### 故障排除

#### Q: Inspector 显示 "Password Invalid"

- 密码序列不为空，且所有手势值在 1-7 范围内（0=Idle 不能作为密码）
- 0 位密码表示禁用 ASS，也会正常构建但不生成任何内容

#### Q: Play 模式测试无法解锁

1. 确认未勾选 "Disabled In Playmode"（默认勾选，即 Play Mode 跳过生成）
2. 使用正确的手势输入顺序和正确的手

#### Q: 构建后 Avatar 不正常

检查 Unity Console 的 `[ASS]` 日志。如果存在组件冲突，尝试降低 Defense Level 或勾选 Disable Defense。

---

## 许可证

MIT License — 详见项目根目录 [LICENSE](../../../LICENSE)。

---

## 免责声明

1. **法律合规** — 此工具仅供保护您自己创作的 Avatar，不得用于保护侵权内容
2. **技术风险** — 恶意消耗资源可能违反 VRChat TOS，建议设置合理的防御强度
3. **责任声明** — 作者不对任何滥用或因使用导致的后果负责，使用者自行承担风险
