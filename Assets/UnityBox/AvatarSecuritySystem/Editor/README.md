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

- **手势密码保护** — 使用 VRChat 的 8 种手势（含 Idle）组合作为密码
- **手势保持时间控制** — 最小/最大保持时间 + 容错时间，防止猜测与误触
- **倒计时机制** — 限时输入（默认 30 秒），增加破解难度
- **视觉/音频反馈** — 全屏 Shader 遮罩 + 进度条 + 警告音效（支持 VR 立体渲染）
- **初始锁定** — Avatar 启动时所有功能被禁用
- **本地/远端分离** — 防御仅本地触发；远端看到身体隐藏(Concealed)但无遮罩，仅穿戴者本地看到全屏遮罩(Locked)
- **默认启用防御** — 上传无密码的防御版本，利用已保存的参数状态区分合法用户与盗模者
- **智能防御** — 仅对穿戴者生效（IsLocal），不影响其他玩家
- **非破坏性** — 编辑时零影响，仅在 VRChat 构建时自动生成
- **VRCSDK 集成** — 通过 `IVRCSDKPreprocessAvatarCallback` 无缝接入构建流程
- **兼容性** — 与 NDMF、VRCFury、Modular Avatar 等工具链兼容

### 工作流程

```
标准模式：
Avatar 启动
    ↓
所有客户端：Remote → Concealed（身体隐藏，无遮罩）
仅本地玩家：Concealed → Locked（全屏遮罩覆盖视角）
    ↓
倒计时开始（默认 30 秒）
    ↓
用户输入手势密码
    ├─ 正确 → ASS_PasswordCorrect = true → 解锁 → 正常使用
    ├─ 错误 → 容错机制 → 可继续输入
    └─ 超时 → ASS_TimeUp = true → 触发防御（仅对穿戴者）

默认启用防御模式：
Avatar 启动
    ├─ PasswordCorrect = true（已保存）→ 正常使用（防御关闭）
    └─ PasswordCorrect = false（盗模者）
        ├─ 远端：身体隐藏（Concealed）
        ├─ 本地：全屏遮罩（Locked）+ 防御组件立即激活
        └─ 无法关闭，持久防御
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
- 支持 Idle(0) 作为密码值（下拉菜单可选 `0: Idle`）
- 密码强度：Weak(<4位) / Medium(4-5位或手势<4种) / Strong(≥6位且≥4种手势)

### 步骤 3：配置手势识别

- **Min Hold Time**: 0.1-1.0 秒（默认 0.15 秒），手势需保持的最短时间
- **Max Hold Time**: 1-10 秒（默认 3 秒），手势可保持的最长时间，超过则输入重置
- **Error Tolerance**: 0.1-1.0 秒（默认 0.3 秒），短暂误触的容错时间

### 步骤 4：配置倒计时

- **Countdown Duration**: 10-30 秒（默认 30 秒）
- **Warning Threshold**: 固定 10 秒（最后 10 秒播放警告音效）

### 步骤 5：防御选项（可选）

- 构建时自动生成 GPU 防御负载：以共享 `UB_Defense` 材质的粒子系统为主，默认不再生成 PhysX / Cloth / 独立 ShaderDefense 网格
- 普通模式下，粒子光源优先复用 1 个外部 Light；若没有可复用光源，则仅补 1 个后备 Light
- 轻量模式下，不主动新增光源 / 粒子碰撞 / 粒子拖尾；如果 Avatar 本来就已经在使用这些功能，则 ASS 生成的粒子会直接继承对应特性
- 可通过 `Disable Defense` 选项完全禁用防御生成
- 可通过 `Enable Overflow` 选项启用溢出模式（仅 Build & Publish 生效，Play Mode 始终最小防御）；建议与轻量模式一起开启，以更容易保持绿色参数显示
- 可通过 `Lightweight Mode` 选项启用轻量模式：尽量减少粒子系统数量，并在保持溢出的前提下尽量贴近绿模

### 步骤 6：高级选项

- **Enable in Play Mode**: 在 Unity Play 模式下启用 ASS 生成（用于测试）
- **Disable Overlay**: 不生成全屏覆盖遮罩（仍保留音频反馈）
- **Mute Warning Sound**: 不生成倒计时警告音效（仍保留视觉反馈）
- **Default Enable Defense**: ⚠️ 默认启用防御模式，不生成密码系统，利用已保存的参数状态工作
- **Hide Objects**: 锁定时隐藏所有根级子对象
- **Write Defaults Mode**: 动画 WD 模式（Auto/On/Off）

### 步骤 7：构建上传

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
ASSComponent (MonoBehaviour)
    ↓ 配置参数
Processor (IVRCSDKPreprocessAvatarCallback, callbackOrder = -1023, 无 NDMF 时使用)
NDMFPlugin (仅存在 NDMF 时编译，通过 NDMF Plugin API 注册到 BuildPhase.PlatformFinish)
    ↓ VRChat Build & Publish 时自动执行
生成 5 个 Animator Layer 到 FX Controller（混淆启用时层名变为 `_{描述词}_{4位hex}`）:
    ├─ 锁定层         (Lock/Concealed/Unlocked)
    ├─ 密码验证层     (手势序列检测)
    ├─ 倒计时层       (限时 + TimeUp 触发)
    ├─ 音频层         (警告音效 2D 循环)
    └─ 防御层         (GPU 组件激活，可选)
    ↓
AnimationClips + GameObject Hierarchy + VRC Components
```

**是否存在 NDMF 由编译期常量而非运行期反射决定：**

`Editor/NDMF` 子程序集通过 `defineConstraints: ["NDMF_AVAILABLE"]` 声明，仅当项目安装了 NDMF 包（`versionDefines` 检测到 `nadena.dev.ndmf`）时才会被编译，无需在运行期用反射判断：

- **无 NDMF**（仅 VRCFury 或纯 VRCSDK）：`Editor/NDMF` 子程序集不参与编译，`Processor` 作为 `IVRCSDKPreprocessAvatarCallback` 以固定的 `callbackOrder = -1023` 执行全部处理逻辑
- **存在 NDMF**：`Processor.OnPreprocessAvatar` 直接跳过（返回 `true` 不做任何处理），改由 `NDMFPlugin` 通过 NDMF 官方 Plugin API（`InPhase(BuildPhase.PlatformFinish).Run(...)`）注册到 NDMF 概念中真正的最后一个 BuildPhase（`PlatformFinish`，在 `Optimizing` 之后）执行，在 Modular Avatar / VRCFury 等所有 NDMF pass 完成之后、NDMF 把结果写回真实资产（`context.Finish()`）之前处理头像。这样完全避免了依赖 VRCSDK 对相同 `callbackOrder` 钩子（例如 VRCFury 自身固定在 `-1024` 的 `RemoveEditorOnlyObjectsHook`）的不确定相对顺序。

**构建管线执行顺序：**

```
无 NDMF 时：
-10000 : VRCFury VrcPreuploadHook（若安装了 VRCFury）
 -1023 : ★ ASS（本插件，Processor.OnPreprocessAvatar）
 -1024 : VRCFury RemoveEditorOnlyObjects / VRCSDK RemoveAvatarEditorOnly
   MAX : VRCFury ParameterCompressorHook / Cleanup

存在 NDMF 时（ASS 不再使用 VRCSDK callbackOrder，而是注册进 NDMF 自身的 Pass 队列）：
-11000 : NDMF PreprocessHook
-10000 : VRCFury VrcPreuploadHook（若同时安装了 VRCFury）
 -1025 : NDMF OptimizeHook 开始执行
          │  BuildPhase.Optimizing
          │    ... Modular Avatar / Avatar Optimizer 等 NDMF pass ...
          │  BuildPhase.PlatformFinish（NDMF 概念中真正的最后一个阶段）
          └─   ★ ASS（本插件，NDMFPlugin，在该阶段执行）
        context.Finish() 把结果写回真实资产
 -1024 : VRCFury RemoveEditorOnlyObjects / VRCSDK RemoveAvatarEditorOnly
   MAX : VRCFury ParameterCompressorHook / Cleanup
```

无 NDMF 时，ASS 以固定 `callbackOrder = -1023` 注入（晚于 VRCFury 主处理 `-10000`，早于其 `RemoveEditorOnlyObjects` `-1024`），并自行复制 Playable 层控制器，避免修改原始资产。
存在 NDMF 时，ASS 改为在 **NDMF 自身的 BuildPhase.PlatformFinish 阶段**（在 `Optimizing` 之后，仍在虚拟/克隆状态、`context.Finish()` 提交之前）处理，直接在 NDMF 已克隆的控制器上原地追加层，无需再复制/追踪控制器副本。
两种场景下，VRCFury 参数压缩都在远后执行，因此 ASS 参数会被正确识别。


### 文件结构

```
Assets/UnityBox/AvatarSecuritySystem/
├─ package.json                                          # VPM 包清单
├─ Runtime/
│   ├─ Component.cs                                         # 主组件类（配置字段）
│   └─ UnityBox.AvatarSecuritySystem.Runtime.asmdef
│
├─ Editor/
│   ├─ Inspector.cs                                         # 自定义 Inspector UI
│   ├─ Processor.cs                                      # VRCSDK 构建处理器（入口点，无 NDMF 时使用）
│   ├─ Utils.cs                                          # Animator 工具方法
│   ├─ Lock.cs                                           # 锁定系统生成器
│   ├─ GesturePassword.cs                                # 手势密码状态机生成器
│   ├─ Countdown.cs                                      # 倒计时 + 音频层生成器
│   ├─ Feedback.cs                                       # 视觉反馈生成器（全屏 Shader 遮罩）
│   ├─ Defense.cs                                        # 防御系统生成器
│   ├─ Obfuscator.cs                                     # 名称/Shader 混淆引擎
│   ├─ Constants.cs                                      # 常量定义（参数名、VRChat 上限等）
│   ├─ I18n.cs                                           # 国际化（中/英/日）
│   ├─ README.md                                         # 本文档
│   ├─ ASS_TechnicalDoc.md                               # 技术详细文档
│   ├─ UnityBox.AvatarSecuritySystem.asmdef
│   └─ NDMF/                                             # 仅当项目安装了 NDMF 时才会被编译（defineConstraints）
│       ├─ NDMFPlugin.cs                                 # NDMF Plugin 入口，注册到 BuildPhase.PlatformFinish
│       └─ UnityBox.AvatarSecuritySystem.NDMF.asmdef
│
├─ Resources/
│   ├─ Avatar Security System.png                        # Logo
│   ├─ PasswordSuccess.mp3                               # 成功音效
│   ├─ CountdownWarning.mp3                              # 倒计时警告音效
│   ├─ StepSuccess.mp3                                   # 预留：步骤确认音效（未来功能）
│   └─ InputError.mp3                                    # 预留：输入错误音效（未来功能）
│
└─ Shaders/
    ├─ Overlay.shader                                         # 全屏遮罩 + 进度条 Shader
    └─ DefenseShader.shader                              # 防御 Shader（GPU 密集）
```

### 依赖关系

```
Unity 2022.3
    └─ VRChat SDK Avatars ≥ 3.7.0
```

无其他必要依赖。兼容 NDMF、VRCFury、Modular Avatar、Avatar Optimizer 等工具（但不依赖它们）。

> **v0.3.1 兼容性修复**：当 VRCFury 的 SPS/Toggle/ActionClip 等功能在 blend tree 中使用 `IsLocal` 参数时，VRCFury 会将其从 Bool 升级为 Float。ASS 现在会自动检测 `IsLocal` 的实际类型并使用兼容的条件模式（`Greater > 0.5` 代替 `If`），确保在任何参数类型下都能正常工作。

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
├─ Remote (默认) — 所有玩家的初始状态（身体正常显示）
├─ Concealed — !PasswordCorrect 时的中间状态
│   └─ 身体隐藏（m_IsActive=0 + localScale=0），无遮罩
│   └─ 远端观众停留在此状态
├─ Locked — 仅本地玩家（Concealed 后经由 IsLocal）
│   ├─ 显示遮罩 Mesh（m_IsActive=1）
│   ├─ 隐藏 Avatar 根级子对象（m_IsActive=0 + localScale=0）
│   └─ 显示全屏 Shader 遮罩 + 进度条
└─ Unlocked — 解锁状态（PasswordCorrect=true）
    ├─ 隐藏遮罩 Mesh（m_IsActive=0）
    ├─ 恢复 Avatar 对象
    └─ 设置其他 ASS 层权重为 0（释放控制）
```

**转换条件：**

- Remote → Concealed: `ASS_PasswordCorrect == false`（所有客户端）
- Remote → Unlocked: `ASS_PasswordCorrect == true`（参数同步）
- Concealed → Locked: `IsLocal == true && ASS_PasswordCorrect == false`（仅穿戴者）
- Concealed → Unlocked: `ASS_PasswordCorrect == true`
- Locked → Unlocked: `ASS_PasswordCorrect == true`
- Unlocked → Remote: `ASS_PasswordCorrect == false`

**关键实现：**

- 全屏遮罩使用自定义 Shader（`UnityBox/UB_Overlay`），顶点着色器直接映射到裁剪空间全屏
- 进度条通过动画驱动 Shader 材质属性控制
- 双重保护隐藏：同时设置 `m_IsActive=0` + `localScale=0`，任一被覆盖另一仍生效
- ASS 对象（Overlay、Audio、Defense）不参与隐藏
- WD On 模式由 WriteDefaults 自动恢复属性；WD Off 模式显式写入原始值

---

### 2. 手势密码系统 (GesturePassword)

#### 功能

- 检测 VRChat 手势输入（GestureLeft / GestureRight, 0-7 含 Idle）
- 尾部序列匹配（输入 123456 可匹配密码 456）
- 手势稳定时间检测（需保持手势一定时间，默认 0.15 秒，最大保持时间默认 3 秒）
- 容错机制（短暂误触不会重置，容错时间默认 0.3 秒）
- Avatar-hash 噪声扰动（noiseLow/noiseHigh 0/1 随机），攻击者无法确定精确手势值
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

- 创建全屏 Shader 覆盖遮罩（`UnityBox/UB_Overlay`）
- 倒计时进度条（通过 Shader 材质属性动画驱动）
- 不依赖 Canvas 或 VRCParentConstraint，通过 Shader 顶点变换直接覆盖全屏
- VR 立体渲染支持（Single Pass Instanced 模式）
- 表情镜遮挡（通过 Stencil 写入 255）

#### 全屏覆盖结构

```
ASS_Overlay (默认禁用，锁定时通过动画启用)
├─ Overlay (MeshRenderer + MeshFilter)
│   └── Material: UnityBox/UB_Overlay Shader（全屏遮罩 + 进度条）
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
- 自动计算参数，在尽量少对象的前提下制造 GPU 负载
- 惰性预算统计：统计已有粒子/光源并控制预算或复用策略
- 组件类型：ParticleSystem、可选 Light、共享防御 Shader（PhysX / Cloth 默认不再生成）

#### 防御机制

| 组件                 | 作用                                      |
| -------------------- | ----------------------------------------- |
| 粒子系统             | 最少系统数策略：非溢出通常 1 个；溢出时按 1/2 系统规则补满上限 |
| 光源 / 碰撞 / 拖尾   | 普通模式主动启用；轻量模式不主动新增，但若 Avatar 已经在用，则 ASS 粒子会继承这些特性 |
| 防御 Shader          | 粒子渲染器共享 1 份 `UB_Defense` 材质     |

#### 防御组件

- 粒子：
    - 非溢出模式：通常生成 1 个粒子系统，尽量把剩余粒子 / Mesh 预算补满但不超限
    - 溢出模式：若 Avatar 已有粒子，则补 1 个顶满系统；否则补 2 个顶满系统以保持溢出
- 光源：
    - 普通模式：复用 1 个外部 Light，若没有则补 1 个后备 Light
    - 轻量模式：不主动生成新光源；若 Avatar 本来就有 Light，则允许直接复用
- 粒子碰撞 / 拖尾：
    - 普通模式：主动启用
    - 轻量模式：默认不主动启用；若 Avatar 现有粒子系统已经启用了对应模块，则 ASS 粒子会跟随启用
- 防御 Shader：所有粒子渲染器共享 1 份 `UB_Defense` 材质
- PhysX / Cloth / 独立 ShaderDefense 网格：默认不生成

> 调试模式（Play Mode）下会生成同类型防御，但使用最小参数值（每类 1 个）以便测试。

#### 状态机

**标准模式（enableStandardDefense=true）**：
```
ASS_Defense Layer
├─ Inactive (默认) — 防御未激活
└─ Active
    └─ 转换条件: IsLocal == true && ASS_TimeUp == true
```

**默认防御模式**：
```
ASS_Defense Layer
├─ Inactive (默认) — 防御未激活
└─ Active
    └─ 转换条件: IsLocal == true && ASS_PasswordCorrect == false（立即激活）
```

防御根对象默认 `m_IsActive=0`，激活时设为 1。

---

### 6. 混淆与伪装系统 (Obfuscation)

#### 功能

- **名称混淆** — 所有 ASS 生成的参数、层、GameObject、Clip、状态名替换为 `_{无害描述词}_{4位hex}` 格式
- **Shader 混淆** — 构建时复制 Overlay/Defense Shader 并赋予混淆名，AssetBundle 中无 `UnityBox/` 前缀
- **假动画层** — 生成 1 个 weight=1 的伪装层（如 `_FaceTracking`、`_EyeLookAt`）
- **假状态注入** — 每个真层注入 5-8 个假状态，形成伪功能子图
- **迷惑参数** — 28 个假参数（伪装为 MA/VRCFury 风格的技术缩写）
- **指令式提示词注入** — Clip 名和状态名中包含伪装为构建产物的语义载荷，对抗 AI 辅助逆向分析

#### 配置

| 属性                   | 默认值  | 说明                       |
| ---------------------- | ------- | -------------------------- |
| `disableObfuscation`   | `false` | 禁用名称混淆（仅用于调试） |
| `enableDecoyLayers`    | `true`  | 生成假动画层               |
| `enableDecoyStates`    | `true`  | 在真层中注入假状态         |

> 当 `disableObfuscation=true` 时，`enableDecoyLayers` 和 `enableDecoyStates` 自动失效。

---

## 配置选项

### ASSComponent 属性

#### 密码配置

| 属性              | 类型        | 默认值         | 说明                                     |
| ----------------- | ----------- | -------------- | ---------------------------------------- |
| `gesturePassword` | `List<int>` | `[1, 7, 2, 4]` | 手势密码序列（0-7，Idle(0) 可作为密码位），0 位密码 + defaultEnableDefense = 防御版本 |
| `useRightHand`    | `bool`      | `false`        | 使用右手（true）或左手（false）          |

#### 手势识别配置

| 属性                    | 类型    | 范围     | 默认值 | 说明                     |
| ----------------------- | ------- | -------- | ------ | ------------------------ |
| `gestureHoldTime`       | `float` | 0.1-1.0s | `0.15` | 手势需保持的最短识别时间 |
| `gestureMaxHoldTime`    | `float` | 1-10s    | `3.0`  | 单步手势最大保持时间，超时则输入重置 |
| `gestureErrorTolerance` | `float` | 0.1-1.0s | `0.3`  | 短暂误触的容错时间       |

#### 倒计时配置

| 属性                | 类型    | 范围    | 默认值 | 说明                                    |
| ------------------- | ------- | ------- | ------ | --------------------------------------- |
| `countdownDuration` | `float` | 10-30s | `30`   | 倒计时时长                              |
| `warningThreshold`  | `float` | —       | `10`   | 警告阶段阈值（HideInInspector，固定值） |

#### 防御选项

| 属性             | 类型   | 默认值  | 说明                                                                      |
| ---------------- | ------ | ------- | ------------------------------------------------------------------------- |
| `disableDefense` | `bool` | `false` | 禁用防御生成（仅保留密码系统）                                            |
| `enableOverflow` | `bool` | `true`  | 启用溢出（Build&Publish）：通过粒子数 / Mesh 面数溢出更容易保持绿色参数显示。建议与轻量模式一起开启。Play Mode 忽略此选项 |
| `lightweightDefense` | `bool` | `false` | 轻量模式：不主动新增新的光源 / 粒子碰撞 / 拖尾；若 Avatar 已经在使用这些功能，则允许直接继承。并使用最少粒子系统策略尽量贴近绿模 |

#### 锁定选项

| 属性                  | 类型                | 默认值 | 说明                                              |
| --------------------- | ------------------- | ------ | ------------------------------------------------- |
| `disableRootChildren` | `bool`              | `true` | 锁定时隐藏 Avatar 根级子对象                      |
| `writeDefaultsMode`   | `WriteDefaultsMode` | `Auto` | Auto=自动检测（优先复用 VRCFury 设置） / On / Off |

#### 高级选项

| 属性                | 类型             | 默认值    | 说明                                               |
| ------------------- | ---------------- | --------- | -------------------------------------------------- |
| `enabledInPlaymode` | `bool`           | `false`   | 播放模式中启用 ASS 生成                            |
| `disableOverlay`    | `bool`           | `false`   | 不生成全屏覆盖（遮罩 + 进度条），仅保留音频反馈 |
| `muteWarningSound`  | `bool`           | `false`   | 不生成倒计时警告音效（仍保留视觉反馈）             |
| `uiLanguage`        | `SystemLanguage` | `Unknown` | Inspector 语言（Unknown=自动检测）                 |

---

## API 参考

### Animator 参数

| 参数名                | 类型         | 默认值 | 同步     | 说明                                                                     |
| --------------------- | ------------ | ------ | -------- | ------------------------------------------------------------------------ |
| `ASS_PasswordCorrect` | Bool         | false  | Synced   | 密码验证成功标志                                                         |
| `ASS_TimeUp`          | Bool         | false  | Local    | 倒计时结束标志                                                           |
| `IsLocal`             | Bool/Float\* | —      | Built-in | VRChat 内置（穿戴者=true）。\*VRCFury 可能将其升级为 Float，ASS 自动适配 |
| `GestureLeft`         | Int          | 0      | Built-in | VRChat 内置（左手 0-7）                                                  |
| `GestureRight`        | Int          | 0      | Built-in | VRChat 内置（右手 0-7）                                                  |

### Animator 层

| 层名（非混淆默认）    | 权重 | 功能             |
| --------------------- | ---- | ---------------- |
| `ASS_Lock`            | 1.0  | 锁定/解锁控制（Remote/Concealed/Locked/Unlocked） |
| `ASS_PasswordInput`   | 1.0  | 密码验证         |
| `ASS_Countdown`       | 1.0  | 倒计时           |
| `ASS_Audio`           | 1.0  | 警告音效         |
| `ASS_Defense`         | 1.0  | 防御激活（可选） |

> 混淆启用时层名替换为 `_{描述词}_{4位hex}` 格式。

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

不会。防御通过 `IsLocal` 参数隔离，仅穿戴者受影响。其他玩家看到的是身体隐藏但无遮罩（Concealed 状态），不会看到全屏白色遮罩。

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

- 密码序列不为空，且所有手势值在 0-7 范围内（Idle(0) 可作为密码位）
- 0 位密码 + 未启用默认防御 = 跳过 ASS 生成；0 位密码 + 启用默认防御 = 防御版本

#### Q: Play 模式测试无法解锁

1. 确认未勾选 "Disabled In Playmode"（默认勾选，即 Play Mode 跳过生成）
2. 使用正确的手势输入顺序和正确的手

#### Q: 构建后 Avatar 不正常

检查 Unity Console 的 `[ASS]` 日志。如果存在组件冲突，尝试降低 Defense Level 或勾选 Disable Defense。

#### Q: 有 VRCFury 时 ASS 不生效

**已在 v0.3.1 修复。** 此问题由 VRCFury 的参数类型升级机制引起：当 Avatar 使用了 VRCFury 的 SPS（Haptic Socket）、Toggle（带 Local State）或 ActionClip（带 localOnly/remoteOnly）等功能时，VRCFury 会在 blend tree 中以 Float 方式使用 `IsLocal` 参数，其 `UpgradeWrongParamTypes` 会将 `IsLocal` 从 Bool 升级为 Float。旧版 ASS 使用 `AnimatorConditionMode.If`（仅对 Bool 有效）添加 `IsLocal` 条件，在 Float 类型下会被 VRCFury 的 `RemoveWrongParamTypes` 替换为始终为 false 的无效条件，导致 ASS 的锁定/密码/倒计时/防御全部失效。新版已自动适配所有参数类型。

#### Q: 有 NDMF（Modular Avatar / Avatar Optimizer 等）时 ASS 何时注入？

ASS 是否使用 NDMF 由编译期常量决定，而不是运行期反射：`Editor/NDMF` 子程序集声明了 `defineConstraints: ["NDMF_AVAILABLE"]`，只有当项目安装了 NDMF 包时才会参与编译。

- **未安装 NDMF**：`Editor/NDMF` 子程序集不参与编译，`Processor` 使用固定的 `callbackOrder = -1023`，在 VRCFury 主处理（`-10000`）之后、`RemoveEditorOnlyObjects`（`-1024`）之前注入，并自行复制 Playable 层控制器到 Generated 目录。
- **安装了 NDMF**：`Processor.OnPreprocessAvatar` 直接跳过，改由 `NDMFPlugin` 通过 NDMF 官方 Plugin API 注册到 **NDMF 概念中真正的最后一个 BuildPhase（`PlatformFinish`，在 `Optimizing` 之后）**，在 Modular Avatar / Avatar Optimizer / VRCFury 等所有 NDMF pass 都已生成最终 FX Controller 之后（仍在 NDMF 的虚拟/克隆状态、`context.Finish()` 提交为真实资产之前）执行，ASS 直接在该控制器上原地追加安全层。

这样彻底避免了依赖 VRCSDK 对相同 `callbackOrder` 钩子（例如 VRCFury 自身固定在 `-1024` 的 `RemoveEditorOnlyObjectsHook`）的不确定相对顺序：存在 NDMF 时 ASS 根本不通过 VRCSDK 的 `callbackOrder` 数轴与 VRCFury 的收尾钩子比较顺序。

无论哪种模式，VRCFury 的参数压缩（ParameterCompressorHook）都在极后（`int.MaxValue - 100`）执行，ASS 新增的参数始终会被正确识别和压缩。

---

## 许可证

MIT License — 详见项目根目录 [LICENSE](../../../LICENSE)。

---

## 免责声明

1. **法律合规** — 此工具仅供保护您自己创作的 Avatar，不得用于保护侵权内容
2. **技术风险** — 恶意消耗资源可能违反 VRChat TOS，建议设置合理的防御强度
3. **责任声明** — 作者不对任何滥用或因使用导致的后果负责，使用者自行承担风险
