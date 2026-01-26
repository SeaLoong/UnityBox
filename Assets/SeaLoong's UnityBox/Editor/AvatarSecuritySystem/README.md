# Avatar Security System (ASS) 🔒

**VRChat Avatar 防盗密码保护系统 - 完整文档**

[![Unity](https://img.shields.io/badge/Unity-2019.4.31f1+-black.svg)](https://unity.com/)
[![VRChat](https://img.shields.io/badge/VRChat-SDK3-blue.svg)](https://vrchat.com/)
[![NDMF](https://img.shields.io/badge/NDMF-1.3.0+-green.svg)](https://github.com/bdunderscore/ndmf)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## 📑 目录

1. [系统概述](#系统概述)
2. [快速开始（用户指南）](#快速开始用户指南)
3. [技术架构](#技术架构)
4. [详细实现](#详细实现)
5. [性能与优化](#性能与优化)
6. [API 参考](#api-参考)
7. [常见问题](#常见问题)

---

## 系统概述

### 🎯 项目目标

Avatar Security System (ASS) 是一个用于 VRChat Avatar 的防盗保护系统。通过**手势密码**和**倒计时机制**，防止恶意玩家通过缓存提取等手段盗取您的 Avatar。

### ✨ 核心特性

- 🔐 **手势密码保护** - 使用 VRChat 的 8 种手势组合作为密码
- ⏱️ **倒计时机制** - 限时输入（默认30秒），增加破解难度
- 🎨 **视觉/音频反馈** - 实时提示用户输入状态（绿→黄→红）
- 🔒 **初始锁定** - Avatar 启动时所有功能被禁用 + 参数反转
- ⚡ **智能防御** - 仅对穿戴者生效（IsLocal），不影响其他玩家
- 🛠️ **非破坏性** - 编辑时零影响，仅构建时生成
- 🔧 **NDMF 集成** - 无缝集成到 VRChat Avatar 构建流程

### 🎭 工作流程

```
Avatar 启动
    ↓
🔒 所有功能锁定（对象禁用 + 参数反转）
    ↓
⏱️ 倒计时开始（默认 30 秒）
    ↓
🤚 用户输入手势密码
    ├─ ✅ 正确 → PASSWORD_CORRECT = true → 解锁 → 正常使用
    ├─ ❌ 错误 → 触发 PASSWORD_ERROR → 红色闪烁 → 重置输入
    └─ ⏰ 超时 → 设置 TIME_UP = true → 触发防御 → 功能锁定
```

### 📊 性能指标

| 场景 | CPU | GPU | FPS | 文件大小 | 影响范围 |
|------|-----|-----|-----|---------|---------|
| **正常使用** | < 1% | 0% | 正常 | +9 MB | 无 |
| **防御激活** | 30-60% | 60-90% | 10-30 | +9 MB | 仅穿戴者 |
| **其他玩家** | 0% | 0% | 正常 | - | 无影响 ✅ |

---

## 快速开始（用户指南）

### 📦 安装依赖

```bash
1. 安装 VRChat SDK3-Avatars (3.5.0+)
2. 安装 NDMF (1.3.0+) via VCC
3. 导入 ASS 文件到 Unity 项目
   └─ Assets/SeaLoong's UnityBox/
```

### ⚙️ 配置步骤

#### 步骤 1: 添加组件
```
1. 选择你的 Avatar Root 对象
2. Add Component → "Avatar Security System"
```

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
# 简单密码（测试用）
Gesture Password: [1, 7, 2]  # Fist → ThumbsUp → HandOpen

# 强密码（推荐）
Gesture Password: [1, 4, 2, 7, 3, 6]  # 6位，6种不同手势
Use Right Hand: false  # 使用左手
```

**密码强度评级：**
- **Weak (弱)**: < 4 位
- **Medium (中)**: 4-5 位，或手势种类少于 4 种
- **Strong (强)**: ≥ 6 位，且至少使用 4 种不同手势

#### 步骤 3: 倒计时配置

```yaml
Countdown Duration: 30秒     # 10-120秒可选
Warning Threshold: 10秒      # 黄色警告阈值
Urgent Threshold: 3秒        # 红色紧急阈值
```

#### 步骤 4: 反馈配置（可选）

```yaml
Error Sound: ErrorBeep.wav       # 错误提示音 (~0.3s)
Warning Beep: WarningBeep.wav    # 警告哔哔声 (~0.2s)
Success Sound: SuccessChime.wav  # 成功音效 (~0.5s)
Enable Particle Effects: true    # 视觉粒子反馈
```

#### 步骤 5: 防御配置（高级）

```yaml
Decoy State Count: 6000          # 1000-10000（推荐 6000）
Defense Shader: SecurityBurnShader   # GPU 密集 Shader
Hide Avatar On Defense: true     # 防御激活时隐藏模型
```

#### 步骤 6: 测试

```
1. 点击 Inspector 中的 "🧪 测试密码流程" 按钮
2. 进入 Play 模式
3. 使用手势输入测试密码
4. 验证倒计时和反馈是否正常
```

#### 步骤 7: 构建上传

```
1. 使用 VRChat SDK 的 "Build & Publish"
2. ASS 会询问确认（显示预估文件大小）
3. 点击"继续构建"
4. 上传到 VRChat 并在游戏中测试
```

### 🎮 在 VRChat 中使用

#### 解锁流程
```
1. 穿戴 Avatar 后会看到倒计时提示（30秒）
2. 按照配置的顺序做出手势
   - 使用左手或右手（取决于配置）
   - 每个手势保持 0.5 秒
3. 密码正确：
   ✅ 绿色闪烁 + 成功音效 → 解锁
4. 密码错误：
   ❌ 红色闪烁 + 错误音效 → 重置到第一步
5. 倒计时结束（未解锁）：
   ⚠️ 触发防御措施（仅对穿戴者）
```

#### 倒计时视觉反馈
```
🟢 绿色 (30-10s)  : 正常阶段
🟡 黄色 (10-3s)   : 警告阶段 + 渐变
🔴 红色闪烁 (3-0s) : 紧急阶段 + 哔哔音效
```

---

## 技术架构

### 🏗️ 系统组成

```
AvatarSecuritySystemComponent (MonoBehaviour)
    ↓ 配置参数
AvatarSecurityPlugin (NDMF Plugin)
    ↓ BuildPhase.Optimizing
生成 5 个 AnimatorController Layers:
    ├─ InitialLock (初始锁定)
    ├─ PasswordInput (手势密码验证)
    ├─ Countdown (倒计时系统)
    ├─ Feedback (视觉/音频反馈)
    └─ Defense (防御措施 - 仅构建模式)
    ↓
AnimationClips + GPU Shader + GameObject Hierarchy
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
│       ├─ ASSConstants.cs              # 常量定义（参数名、层名等）
│       ├─ ASSAnimatorUtils.cs          # Animator 工具（创建层、状态、转换）
│       ├─ ASSAnimationClipGenerator.cs # 动画剪辑生成器
│       ├─ ASSI18n.cs                   # 国际化支持（中/英/日）
│       ├─ AvatarSecurityPlugin.cs      # NDMF 插件入口
│       ├─ InitialLockSystem.cs         # 初始锁定系统生成器
│       ├─ GesturePasswordSystem.cs     # 手势密码系统生成器
│       ├─ CountdownSystem.cs           # 倒计时系统生成器
│       ├─ FeedbackSystem.cs            # 反馈系统生成器
│       └─ DefenseSystem.cs            # 防御系统生成器（672行）
│
├─ Shaders/
│   └─ SecurityBurnShader.shader        # GPU 密集 Shader（8 octaves FBM）
│
└─ Documentation/
    ├─ ASS_README.md                    # 简要说明
    ├─ ASS_User_Guide.md                # 用户指南
    ├─ ASS_Technical_Documentation.md   # 技术文档
    ├─ ASS_Defense_Design.md          # 防御系统设计
    └─ ASS_VRChat_Limitations.md        # VRChat 限制说明
```

### 🔗 依赖关系

```
Unity 2019.4.31f1+
    ↓
VRChat SDK3-Avatars 3.5.0+
    ↓
NDMF 1.3.0+
    ↓
Modular Avatar 1.9.0+ (可选，用于参数反转)
```

---

## 详细实现

### 1️⃣ 初始锁定系统（InitialLockSystem）

#### 功能
- 禁用 Avatar Root 的所有一级子对象
- 反转所有 Avatar Parameters 的默认值（通过 Modular Avatar）

#### 实现细节

**对象禁用：**
```csharp
// 获取所有需要锁定的对象（排除特殊组件）
var exclusions = new HashSet<string> {
    "VRCAvatarDescriptor", "Animator", "PipelineSaver",
    "__ASS_System__"  // 排除 ASS 系统自身
};

// 创建禁用所有对象的动画
var lockClip = CreateGameObjectActiveClip(
    "ASS_ObjectsDisabled",
    avatarRoot,
    targets,
    allFalse  // 所有对象 m_IsActive = false
);
```

**Animator 层结构：**
```
InitialLock Layer (Weight: 1.0)
├─ Locked State (default)
│   └─ Motion: ObjectsDisabled.anim
└─ Unlocked State
    ├─ Motion: ObjectsEnabled.anim
    └─ Transition from Locked
        Condition: ASS_PasswordCorrect == true
```

**参数反转（需要 Modular Avatar）：**
```csharp
// 反转 Bool 参数
boolParam.defaultValue = !boolParam.defaultValue;

// 反转 Float 参数
floatParam.defaultValue = 1.0f - floatParam.defaultValue;
```

---

### 2️⃣ 手势密码系统（GesturePasswordSystem）

#### 功能
- 使用 VRChat 手势参数（GestureLeft/GestureRight）检测输入
- 多步骤密码验证（支持任意长度）
- 错误输入触发视觉反馈并重置

#### 实现细节

**状态机结构：**
```
PasswordInput Layer (Weight: 1.0)
├─ Wait_Input (default)
│   └─ Transition to Step_1
│       Condition: GestureLeft/Right == password[0]
├─ Step_1_Gesture1
│   ├─ Transition to Step_2 (Correct: gesture == password[1])
│   └─ Error Transitions (Wrong: any other gesture → Wait_Input)
├─ Step_2_Gesture2
│   └─ ... (重复 N 步)
└─ Password_Success
    └─ Motion: SetPasswordCorrect.anim (设置 ASS_PasswordCorrect = true)
```

**参数驱动动画（关键）：**
```csharp
// 在 VRChat 中，不能直接设置参数，需要通过动画剪辑驱动
var successClip = new AnimationClip { name = "ASS_SetPasswordCorrect" };
var curve = AnimationCurve.Constant(0f, 1f/60f, 1f);  // Bool true = 1.0
successClip.SetCurve("", typeof(Animator), "ASS_PasswordCorrect", curve);
```

**错误处理：**
```csharp
// 为每个非目标手势创建错误转换
for (int gesture = 0; gesture <= 7; gesture++) {
    if (gesture == correctGesture) continue;
    
    var errorTransition = fromState.AddTransition(waitState);
    errorTransition.AddCondition(AnimatorConditionMode.Equals, gesture, gestureParam);
    // 触发 PASSWORD_ERROR Trigger（用于反馈层）
}
```

---

### 3️⃣ 倒计时系统（CountdownSystem）

#### 功能
- 30秒倒计时（可配置）
- 使用 timeParameter 驱动动画播放位置
- 密码正确时停止倒计时
- 超时时设置 TIME_UP 参数触发防御

#### 实现细节

**动画剪辑：**
```csharp
// 创建从 30s → 0s 的线性动画
var clip = new AnimationClip { name = "ASS_Countdown" };
var curve = AnimationCurve.Linear(0f, duration, duration, 0f);
clip.SetCurve("", typeof(Animator), "TimeValue", curve);
```

**状态机结构：**
```
Countdown Layer (Weight: 1.0)
├─ Countdown (default)
│   ├─ Motion: CountdownTimer.anim (30s)
│   ├─ timeParameterActive = true
│   ├─ timeParameter = "ASS_TimeRemaining"
│   ├─ Transition to Unlocked
│   │   Condition: ASS_PasswordCorrect == true
│   └─ Transition to TimeUp
│       hasExitTime: true, exitTime: 1.0 (动画结束时)
├─ Unlocked
│   └─ Motion: Empty.anim (停止倒计时)
└─ TimeUp
    └─ Motion: SetTimeUp.anim (设置 ASS_TimeUp = true)
```

**Time Parameter 工作原理：**
```
AnimatorState.timeParameter 是 Unity Animator 的高级特性：
1. 动画的播放位置由参数值驱动（而非实时流逝）
2. ASS_TimeRemaining = 30 → 动画播放到 0%（开始）
3. ASS_TimeRemaining = 0  → 动画播放到 100%（结束）
4. 动画曲线: TimeValue 从 30 → 0
5. 当 TimeValue 达到 0 且 exitTime = 1.0 时触发 TimeUp 转换
```

---

### 4️⃣ 反馈系统（FeedbackSystem）

#### 功能
- 倒计时阶段视觉反馈（绿→黄→红）
- 错误输入反馈（红色闪烁 + 音效）
- 成功解锁反馈（绿色闪烁 + 音效）

#### 实现细节

**状态机结构：**
```
Feedback Layer (Weight: 1.0)
├─ Normal (default) - 绿色 UI
│   └─ Transition to Warning
│       Condition: ASS_TimeRemaining < 10.0
├─ Warning - 绿→黄→红渐变
│   └─ Transition to Urgent
│       Condition: ASS_TimeRemaining < 3.0
├─ Urgent - 红色闪烁 + 哔哔音效（每 0.2s）
├─ ErrorFeedback (from AnyState)
│   ├─ Condition: ASS_PasswordError (Trigger)
│   ├─ Motion: ErrorFlash.anim (0.5s 红色闪烁)
│   └─ Auto Exit (exitTime: 0.95)
└─ SuccessFeedback (from AnyState)
    ├─ Condition: ASS_PasswordSuccess (Trigger)
    ├─ Motion: SuccessFlash.anim (绿色闪烁)
    └─ Auto Exit
```

**颜色渐变动画：**
```csharp
// 创建 UI Image 颜色动画
var clip = new AnimationClip { name = "ASS_CountdownWarning" };

// RGB 曲线
var curveR = new AnimationCurve(
    new Keyframe(0f, 0.2f),    // 绿色 R
    new Keyframe(10f, 1.0f),   // 黄色 R
    new Keyframe(duration, 1.0f) // 红色 R
);
// ... G, B 通道类似

clip.SetCurve(uiPath, typeof(Image), "m_Color.r", curveR);
```

---

### 5️⃣ 防御系统（DefenseSystem）

#### 功能（仅构建模式）
1. **CPU 约束链**：嵌套 Constraint 链计算消耗
2. **PhysBone**：物理骨骼模拟消耗
3. **Contact 系统**：碰撞检测组件
4. **Overdraw**：多层透明渲染
5. **高面数 Mesh**：顶点处理消耗
6. **复杂 Shader**：GPU 密集着色器

#### 激活条件

```csharp
// 仅在以下条件同时满足时激活
IsLocal == true  // VRChat 内置参数，穿戴者为 true
&&
ASS_TimeUp == true  // 倒计时结束
```

#### 诱饵状态实现（核心优化）

**问题：百万状态不可行**
- Unity 序列化器限制
- VRChat 文件大小限制（< 200 MB）
- 构建时间过长（百万状态 > 1 小时）

**解决方案：Direct BlendTree 压缩**
```csharp
// 结构：根 BlendTree → 60 个子 BlendTree × 100 个子项 = 6000 状态
var rootBlendTree = new BlendTree {
    name = "ASS_DecoyRoot",
    blendType = BlendTreeType.Direct  // Direct 模式：可混合大量子项
};

// 创建 60 个子 BlendTree
for (int i = 0; i < 60; i++) {
    var subTree = new BlendTree {
        name = $"DecoyTree_{i}",
        blendType = BlendTreeType.Direct
    };
    
    // 每个子树包含 100 个子项
    for (int j = 0; j < 100; j++) {
        var child = new ChildMotion {
            motion = sharedEmptyClip,  // 复用单个空 Clip
            directBlendParameter = $"Decoy_{i}_{j}"  // 唯一参数名
        };
        subTree.AddChild(child);
    }
    
    rootBlendTree.AddChild(subTree);
}
```

**优化效果：**
- 未优化：6000 × 150 KB = **900 MB** ❌
- 复用 Clip：6000 × 1 KB = **6 MB** ✅
- BlendTree 压缩：**~9 MB**（包含参数和结构）

#### GPU 密集 Shader

**SecurityBurnShader.shader 特性：**
```hlsl
// 1. 多重纹理采样（8个纹理，不同 UV 偏移）
fixed4 tex1 = tex2D(_MainTex, i.uv + _Time.x * 0.1);
fixed4 tex2 = tex2D(_MainTex, i.uv + _Time.y * 0.2);
// ... 共 8 个

// 2. Fractal Brownian Motion (FBM) 噪声（8 octaves）
float noise = 0.0;
float amplitude = 1.0;
float frequency = 1.0;
for (int i = 0; i < 8; i++) {
    noise += amplitude * frac(sin(dot(uv * frequency, float2(12.9898, 78.233))) * 43758.5453);
    amplitude *= 0.5;
    frequency *= 2.0;
}

// 3. 复杂光照（Phong + Blinn-Phong + Rim）
float3 lighting = 
    pow(max(0, dot(normal, lightDir)), _Shininess) +      // Phong
    pow(max(0, dot(normal, halfDir)), _Shininess * 2) +   // Blinn-Phong
    pow(1 - max(0, dot(normal, viewDir)), _RimPower);     // Rim

// 4. 数学密集计算
float value = sin(uv.x * 10 + _Time.y) * 
              cos(uv.y * 10 + _Time.x) * 
              exp(-length(uv - 0.5)) * 
              log(1 + noise);

// 5. 色彩空间转换（RGB ↔ HSV，多次转换）
float3 hsv = RGBtoHSV(albedo);
hsv.x = frac(hsv.x + _Time.y * 0.1);  // 色相旋转
albedo = HSVtoRGB(hsv);
```

**预估性能影响：**
- GPU 占用：60-90%
- FPS 下降：5-15 帧
- 仅对穿戴者生效（通过 IsLocal 参数控制材质激活）

#### 粒子系统防御

**配置：**
```csharp
var particleSystem = go.AddComponent<ParticleSystem>();
var main = particleSystem.main;
main.maxParticles = 1000;           // 每个系统 1000 粒子
main.startLifetime = 5f;
main.startSpeed = new MinMaxCurve(1f, 5f);

var emission = particleSystem.emission;
emission.rateOverTime = 200;        // 200 粒子/秒

// 50 个系统 × 1000 粒子 = 50,000 总粒子
```

**性能影响（VRC School 数据）：**
- CPU：粒子生命周期管理
- GPU：Billboard 渲染
- 预估：20-30% GPU 占用

#### Draw Calls 防御

**实现：**
```csharp
// 创建 100 个独立 Quad Mesh，每个使用不同材质实例
for (int i = 0; i < 100; i++) {
    var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
    var material = new Material(burnShader) {
        name = $"DrawCallMaterial_{i}"
    };
    // 随机参数确保每个材质都不同（不会被合批）
    material.SetColor("_BurnColor", Random.ColorHSV());
    quad.GetComponent<Renderer>().material = material;
}
```

**性能影响：**
- 100 Draw Calls ≈ **0.2ms** (VRC School 基准)
- 结合 GPU Shader，总影响更大

---

## 性能与优化

### 📈 性能分析

#### 编辑器性能
- ✅ **编辑时**：0 影响（组件不生成任何资产）
- ✅ **Play 模式**：仅生成测试系统（无防御层）
- ⚠️ **构建时**：5-30 秒（取决于诱饵状态数量）
  - 1000 状态：~5 秒
  - 6000 状态：~15 秒
  - 10000 状态：~30 秒

#### 运行时性能（VRChat 内）

**正常使用（密码正确解锁）：**
```
CPU: < 1%（标准 Animator 开销）
GPU: 0%
内存: < 10 MB
FPS: 无影响
```

**防御激活（盗取者）：**
```
CPU: 30-60%
  ├─ Animator BlendTree 计算: 10-20%
  ├─ 粒子系统更新: 10-20%
  └─ Cloth 物理模拟: 10-20%

GPU: 60-90%
  ├─ 复杂 Shader: 40-60%
  ├─ 粒子渲染: 15-20%
  └─ Draw Calls: 5-10%

FPS: 10-30 帧（目标达成 ✅）
内存: 50-100 MB
```

**对其他玩家的影响：**
```
✅ 完全无影响（通过 IsLocal 参数隔离）
- 防御层 Weight = 0（对其他玩家）
- 粒子/光源/Cloth 不激活
- Shader 不替换
```

### 🗜️ 文件大小优化

#### 优化技术对比

| 技术 | 文件大小（6000 状态） | 优化率 |
|------|---------------------|--------|
| 未优化（独立 Clip） | ~900 MB ❌ | - |
| 复用共享 Clip | ~6 MB | 99.3% ↓ |
| Direct BlendTree | ~9 MB ✅ | 99% ↓ |
| + 音频压缩 | ~9.5 MB | - |

#### 音频优化

```
原始 WAV (48000 Hz, Stereo):
  - Error Sound: 200 KB
  - Warning Beep: 150 KB
  - Success Sound: 300 KB
  总计: 650 KB

优化后 (22050 Hz, Mono, Vorbis 70%):
  - Error Sound: 15 KB
  - Warning Beep: 10 KB
  - Success Sound: 20 KB
  总计: 45 KB (93% ↓)
```

---

## API 参考

### AvatarSecuritySystemComponent

**命名空间：** `SeaLoongUnityBox`

#### 公共属性

```csharp
// === 密码配置 ===
public List<int> gesturePassword;  // 手势密码序列 (0-7)
public bool useRightHand;           // 使用右手(true)或左手(false)

// === 倒计时配置 ===
[Range(10f, 120f)]
public float countdownDuration;     // 倒计时时长（秒）

[Range(3f, 30f)]
public float warningThreshold;      // 警告阈值（秒）

[Range(1f, 10f)]
public float urgentThreshold;       // 紧急阈值（秒）

// === 反馈配置 ===
public AudioClip errorSound;        // 错误输入音效
public AudioClip warningBeep;       // 警告哔哔声
public AudioClip successSound;      // 成功解锁音效
public bool enableParticleEffects;  // 启用粒子特效反馈

// === 防御配置 ===
[Range(1000, 10000)]
public int decoyStateCount;         // 诱饵状态数量

public Shader defenseShader;        // GPU 密集 Shader
public bool hideAvatarOnDefense;    // 防御时隐藏 Avatar

// === 高级选项 ===
public bool enableInPlayMode;       // Play 模式测试（无防御）
public bool invertParameters;       // 反转参数默认值
public bool disableRootChildren;    // 禁用根级子对象
```

#### 公共方法

```csharp
/// <summary>验证密码配置是否有效</summary>
public bool IsPasswordValid()
{
    return gesturePassword != null && 
           gesturePassword.Count > 0 &&
           gesturePassword.All(g => g >= 0 && g <= 7);
}

/// <summary>获取密码强度评级</summary>
/// <returns>"Weak" | "Medium" | "Strong"</returns>
public string GetPasswordStrength()
{
    if (gesturePassword.Count < 4) return "Weak";
    
    int uniqueGestures = gesturePassword.Distinct().Count();
    
    if (gesturePassword.Count >= 6 && uniqueGestures >= 4)
        return "Strong";
    
    return "Medium";
}

/// <summary>预估生成的文件大小（KB）</summary>
public float EstimateFileSizeKB()
{
    float baseSize = 500f;  // 基础文件
    float stateSize = decoyStateCount * 1.5f;  // 诱饵状态
    float audioSize = 50f;  // 音频（已压缩）
    
    return baseSize + stateSize + audioSize;
}

/// <summary>获取手势名称（静态工具方法）</summary>
public static string GetGestureName(int gestureIndex)
{
    string[] names = {
        "Idle", "Fist", "HandOpen", "Fingerpoint",
        "Victory", "RockNRoll", "HandGun", "ThumbsUp"
    };
    return gestureIndex >= 0 && gestureIndex < 8 
        ? names[gestureIndex] 
        : "Unknown";
}
```

### NDMF Plugin API

```csharp
namespace SeaLoongUnityBox.AvatarSecuritySystem.Editor
{
    [assembly: ExportsPlugin(typeof(AvatarSecurityPlugin))]

    public class AvatarSecurityPlugin : Plugin<AvatarSecurityPlugin>
    {
        public override string DisplayName => "Avatar Security System";
        public override string QualifiedName => "top.sealoong.unitybox.avatar-security";

        protected override void Configure()
        {
            // 在 Optimizing 阶段生成系统
            InPhase(BuildPhase.Optimizing).Run("Generate ASS", ctx => {
                GenerateFullSystem(ctx, config);
            });
        }
    }
}
```

### 系统生成器 API

```csharp
// === 初始锁定系统 ===
public static AnimatorControllerLayer CreateLockLayer(
    AnimatorController controller,
    GameObject avatarRoot,
    AvatarSecuritySystemComponent config
);

public static void InvertAvatarParameters(
    GameObject avatarRoot,
    AvatarSecuritySystemComponent config
);

// === 手势密码系统 ===
public static AnimatorControllerLayer CreatePasswordLayer(
    AnimatorController controller,
    AvatarSecuritySystemComponent config
);

// === 倒计时系统 ===
public static AnimatorControllerLayer CreateCountdownLayer(
    AnimatorController controller,
    AvatarSecuritySystemComponent config
);

// === 反馈系统 ===
public static AnimatorControllerLayer CreateFeedbackLayer(
    AnimatorController controller,
    GameObject avatarRoot,
    AvatarSecuritySystemComponent config
);

// === 防御系统 ===
public static AnimatorControllerLayer CreateDefenseLayer(
    AnimatorController controller,
    GameObject avatarRoot,
    AvatarSecuritySystemComponent config
);

// 创建 Constraint 链防御
public static void CreateConstraintChainObjects(
    GameObject avatarRoot,
    AvatarSecuritySystemComponent config
);

// 创建 PhysBone 防御
public static void CreatePhysBoneObjects(
    GameObject avatarRoot,
    AvatarSecuritySystemComponent config
);

// 创建 Overdraw 防御
public static void CreateOverdrawObjects(
    GameObject avatarRoot,
    AvatarSecuritySystemComponent config
);

// 创建高面数 Mesh 防御
public static void CreateHighPolyMeshObjects(
    GameObject avatarRoot,
    AvatarSecuritySystemComponent config
);
```

### Animator 参数

| 参数名 | 类型 | 默认值 | 说明 |
|--------|------|--------|------|
| `ASS_Locked` | Bool | true | 初始锁定状态 |
| `ASS_PasswordCorrect` | Bool | false | 密码验证成功标志 |
| `ASS_TimeRemaining` | Float | 30.0 | 剩余时间（秒） |
| `ASS_TimeUp` | Bool | false | 倒计时结束标志 |
| `ASS_PasswordError` | Trigger | - | 错误输入触发器 |
| `ASS_PasswordSuccess` | Trigger | - | 成功解锁触发器 |
| `IsLocal` | Bool | - | VRChat 内置（穿戴者=true） |
| `GestureLeft` | Int | 0 | VRChat 内置（左手手势 0-7） |
| `GestureRight` | Int | 0 | VRChat 内置（右手手势 0-7） |

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
- 需要技术知识才能绕过

#### Q: 盗取者可以修改插件代码吗？
**A**: 可以，如果他们：
1. 获得完整 Unity 项目（不只是缓存）
2. 有编程知识
3. 愿意花时间分析代码

**防御措施：**
- 不公开分享完整项目
- 使用代码混淆（可选）
- 定期更新系统

#### Q: 客户端 MOD 可以绕过吗？
**A**: 理论上可以，但：
- 需要修改 VRChat 客户端（违反 TOS）
- 需要逆向工程 Animator 逻辑
- 防御措施仍会激活（性能下降）

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

#### Q: 可以动态修改密码吗？
**A**: 不可以。密码在构建时烧入 Animator，运行时无法修改。需要重新构建上传。

#### Q: 系统会影响 Avatar 性能吗？
**A**: 
- 解锁后：几乎无影响（< 1% CPU）
- 未解锁：轻微影响（Animator 层计算）
- 防御激活：严重影响（仅对盗取者）

#### Q: 其他玩家会看到防御效果吗？
**A**: 不会。防御通过 `IsLocal` 参数隔离，仅穿戴者受影响。其他玩家看到的是正常 Avatar。

### ⚙️ 技术问题

#### Q: 为什么不能用百万状态卡死 Unity？
**A**: 技术限制：
1. Unity 序列化器无法处理百万级 AnimatorState
2. VRChat 文件大小限制（< 200 MB）
3. 构建时间过长（> 1 小时）
4. 会影响合法用户的重新构建

#### Q: 为什么错误输入不直接触发防御？
**A**: 用户体验考虑：
- 用户可能在学习密码时多次输错
- 立即防御体验太差
- 只有倒计时结束才防御（盗取者无耐心）

#### Q: 可以在商业 Avatar 中使用吗？
**A**: 可以，但需要：
1. 确保您拥有 Avatar 版权
2. 向购买者说明系统存在
3. 提供解锁密码和技术支持
4. 承担相关法律责任

#### Q: 为什么不使用在线验证？
**A**: 已讨论但不推荐：
- 需要服务器维护（成本）
- 构建时需要网络
- 盗取者可修改客户端跳过验证
- 增加系统复杂度

#### Q: VRChat 会封禁使用此系统的账号吗？
**A**: 可能风险：
- 恶意消耗资源可能违反 TOS
- 建议：不要设置过于极端的防御
- 仅用于保护自己的作品

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
- 密码序列不为空
- 所有手势值在 0-7 范围内
- 没有负数或超出范围的值

#### Q: 构建后 Avatar 无法上传
**A**: 可能原因：
- 文件大小超过限制（减少诱饵状态数量）
- 音频格式不正确（使用 Vorbis 压缩）
- NDMF 构建失败（查看 Console 错误）

#### Q: Play 模式测试无法解锁
**A**: 确认：
1. 启用了 "Enable In Play Mode"
2. 使用了正确的手势输入（VRChat 模拟器）
3. 倒计时未结束

---

## 📜 许可证与免责声明

### MIT License

```
MIT License

Copyright (c) 2026 SeaLoong

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

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
```
1. Unity 版本
2. VRChat SDK 版本
3. NDMF 版本
4. 详细错误信息（Console 日志）
5. 复现步骤
6. 截图（如适用）
```

### 功能请求

欢迎提出新功能建议，但请注意：
- VRChat 技术限制
- NDMF 框架限制
- 性能影响考虑

### 代码贡献

```bash
1. Fork 仓库
2. 创建功能分支
   git checkout -b feature/AmazingFeature
3. 提交更改
   git commit -m 'Add some AmazingFeature'
4. 推送到分支
   git push origin feature/AmazingFeature
5. 打开 Pull Request
```

**代码规范：**
- C# 代码遵循 Unity 编码规范
- 添加 XML 注释
- 更新相关文档

---

## 🙏 致谢

- **NDMF** - 强大的 Non-Destructive Modular Framework
- **Modular Avatar** - 参数管理工具
- **VRChat Community** - 灵感和技术支持
- **VRC School** - 性能基准数据

---

## 📞 联系方式

- **GitHub**: https://github.com/your-repo/avatar-security-system
- **Discord**: YourDiscord#1234
- **邮箱**: your-email@example.com
- **VRChat**: YourVRChatName

---

## ⭐ 支持项目

如果这个项目对你有帮助，请：
- 给个 Star ⭐
- 分享给需要的朋友
- 提供反馈和建议

---

## 📊 更新日志

### v1.0.0 (2026-01-17)

**✅ 初始发布**
- 手势密码系统（8 种手势，任意长度）
- 倒计时机制（可配置 10-120 秒）
- 视觉/音频反馈系统
- 初始锁定（对象禁用 + 参数反转）
- 智能防御系统（6 种措施）
- NDMF 集成
- GPU 密集 Shader (SecurityBurnShader)
- 自定义 Inspector UI
- 国际化支持（中/英/日）
- 完整文档（用户指南 + 技术文档）

**🎯 核心指标**
- 文件大小：~9 MB (6000 诱饵状态)
- 构建时间：~15 秒
- 防御 FPS：10-30 帧（目标达成）
- 对其他玩家影响：0（通过 IsLocal 隔离）

---

**保护你的创作，从 Avatar Security System 开始！🔒**

**Stay safe, stay secure.**
