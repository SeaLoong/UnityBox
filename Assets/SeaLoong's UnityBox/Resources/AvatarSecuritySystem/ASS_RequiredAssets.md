# Avatar Security System (ASS) - 所需素材清单

本文档列出了 ASS 系统需要的默认音效和图像素材。

---

## ✅ 音频素材已内置

**所有音频素材已经内置在插件中，无需用户手动配置！**

音频文件位置：`Assets/SeaLoong's UnityBox/Resources/AvatarSecuritySystem/`

### 内置音频列表

| 音效名称 | 文件名 | 用途 | 时长 |
|---------|--------|------|------|
| 每步输入成功提示音 | `StepSuccess.mp3` | 正确输入一位密码后播放 | ~0.1s |
| 密码成功音效 | `PasswordSuccess.mp3` | 完整密码输入正确 | ~0.3s |
| 错误输入音效 | `InputError.mp3` | 输入错误的手势 | ~0.3s |
| 倒计时警告音效 | `CountdownWarning.mp3` | 最后10秒警告哔哔声 | ~0.2s |

**原始音效来源**：
- `StepSuccess.mp3` ← button01a
- `PasswordSuccess.mp3` ← coin05
- `InputError.mp3` ← blip04
- `CountdownWarning.mp3` ← button04b

---

## 📢 音频素材详细说明 (Audio Assets)

### 1. 错误提示音 (Error Sound) - InputError.mp3
- **原始文件**: blip04.mp3
- **用途**: 输入错误的手势时播放
- **时长**: ~0.3 秒
- **频率**: 200Hz 短促"哔"声
- **音量**: -6dB
- **格式**: WAV / Vorbis 压缩 (70% 质量)
- **采样率**: 22050 Hz
- **声道**: 单声道 (Mono)
- **建议**: 类似电脑"错误提示音"，短促且明显

**音频波形示例**:
```
频率: 200Hz
波形: 正弦波或方波
包络: 快速攻击 (0.01s) → 短持续 (0.2s) → 快速释放 (0.09s)
```

---

### 2. 倒计时警告音效 (Countdown Warning) - CountdownWarning.mp3
- **原始文件**: button04b.mp3
- **用途**: 倒计时最后 10 秒时每秒播放
- **时长**: ~0.2 秒
- **特征**: 短促的哔哔声，具有紧迫感
- **格式**: MP3 / Vorbis 压缩
- **采样率**: 22050 Hz
- **声道**: 单声道 (Mono)

---

### 3. 密码成功音效 (Password Success) - PasswordSuccess.mp3
- **原始文件**: coin05.mp3
- **用途**: 完整密码输入正确时播放
- **时长**: ~0.3 秒
- **特征**: 悦耳的提示音，传达"解锁成功"
- **格式**: MP3 / Vorbis 压缩
- **采样率**: 22050 Hz
- **声道**: 单声道 (Mono)

---

### 4. 每步成功提示音 (Step Success) - StepSuccess.mp3
- **原始文件**: button01a.mp3
- **用途**: 每输入正确一位密码后播放
- **时长**: ~0.1 秒
- **特征**: 短促清脆的确认音，明确反馈
- **格式**: MP3 / Vorbis 压缩
- **采样率**: 22050 Hz
- **声道**: 单声道 (Mono)

---

## 🔧 自动加载机制

**插件会在构建时自动从 Resources 文件夹加载这些音频：**

```csharp
// 加载代码位置：ASSAudioLoader.cs
config.stepSuccessSound = Resources.Load<AudioClip>("AvatarSecuritySystem/StepSuccess");
config.successSound = Resources.Load<AudioClip>("AvatarSecuritySystem/PasswordSuccess");
config.errorSound = Resources.Load<AudioClip>("AvatarSecuritySystem/InputError");
config.warningBeep = Resources.Load<AudioClip>("AvatarSecuritySystem/CountdownWarning");
```

**用户无需手动配置，插件会自动处理！**

---

## 🖼️ 图像素材 (Image Assets)

### 5. 手势图标 (Gesture Icons)
- **文件名**: `gesture_0.png` ~ `gesture_7.png` (8 个文件)
- **用途**: Inspector 编辑器中显示 VRChat 手势
- **尺寸**: 64×64 像素
- **格式**: PNG (支持透明通道)
- **背景**: 透明
- **建议**: 参考 VRChat SDK 官方手势图标风格

**手势映射**:
```
gesture_0.png  → Neutral (握拳)
gesture_1.png  → Fist (握拳)
gesture_2.png  → HandOpen (张开手掌)
gesture_3.png  → FingerPoint (食指指向)
gesture_4.png  → Victory (V 型手势)
gesture_5.png  → RockNRoll (摇滚手势 🤘)
gesture_6.png  → HandGun (手枪手势 👉)
gesture_7.png  → ThumbsUp (竖起大拇指 👍)
```

**设计建议**:
- 线条清晰，易于识别
- 使用单色或双色配色
- 可添加微妙的阴影增强立体感
- 图标居中对齐

---

### 5. 倒计时进度条纹理 (Countdown Progress Texture)
- **文件名**: `countdownBar.png`
- **用途**: UI 显示倒计时进度（可选）
- **尺寸**: 512×32 像素
- **格式**: PNG (支持透明通道)
- **内容**: 渐变色条 (绿色 → 黄色 → 红色)
- **背景**: 半透明黑色背景 (Alpha = 128)

**颜色渐变**:
```
0%-30%:   RGB(0, 255, 0)   - 绿色 (安全)
30%-70%:  RGB(255, 255, 0) - 黄色 (警告)
70%-100%: RGB(255, 0, 0)   - 红色 (危险)
```

---

### 6. 警告图标 (Warning Icon)
- **文件名**: `warningIcon.png`
- **用途**: 倒计时最后 10 秒闪烁提示
- **尺寸**: 128×128 像素
- **格式**: PNG (支持透明通道)
- **内容**: 三角形警告标志 (⚠️)
- **颜色**: 黄色边框 + 黑色感叹号

**设计建议**:
- 使用标准警告符号
- 边缘可添加发光效果
- 高对比度，确保可见性

---

## ⚙️ 自动生成的资源

### GPU 密集型 Shader (Auto-Generated)
- **生成时机**: Avatar 构建时自动生成
- **文件名**: `SecurityBurnShader_{AvatarName}.shader`
- **位置**: `Assets/SeaLoong's UnityBox/Generated/AvatarSecurity/{AvatarName}/`
- **用途**: 惩罚激活时替换所有 Mesh 材质，显着降低 FPS
- **特性**: 
  - 8 次纹理采样
  - 8 阶 FBM 噪声计算
  - Phong + Blinn-Phong 双重光照
  - RGB ↔ HSV 颜色空间转换
- **性能影响**: FPS 降至 5-15（仅影响穿戴者）

### GPU 燃烧材质 (Auto-Generated)
- **生成时机**: Avatar 构建时自动生成
- **文件名**: `SecurityBurnMaterial_{AvatarName}.mat`
- **位置**: `Assets/SeaLoong's UnityBox/Generated/AvatarSecurity/{AvatarName}/`
- **用途**: 使用上述 Shader 的材质实例
- **参数**:
  - `_BurnColor`: 橙红色 (1.0, 0.3, 0.0)
  - `_BurnIntensity`: 2.0

**注意**: 
- Generated 文件夹中的资源**不应提交到版本控制**
- 每次构建时会自动重新生成
- 按 Avatar 名称隔离，避免冲突

---

## 📂 文件夹结构

建议将素材放置在以下目录：

```
Assets/
└── SeaLoong's UnityBox/
    └── Resources/
        └── AvatarSecuritySystem/
            ├── Audio/
            │   ├── errorSound.wav
            │   ├── warningBeep.wav
            │   └── successSound.wav
            ├── Icons/
            │   ├── gesture_0.png
            │   ├── gesture_1.png
            │   ├── gesture_2.png
            │   ├── gesture_3.png
            │   ├── gesture_4.png
            │   ├── gesture_5.png
            │   ├── gesture_6.png
            │   └── gesture_7.png
            └── UI/
                ├── countdownBar.png
                └── warningIcon.png
```

---

## 🔊 音频制作工具推荐

- **Audacity** (免费): https://www.audacityteam.org/
- **LMMS** (免费): https://lmms.io/
- **Bfxr** (在线): https://www.bfxr.net/ (适合快速生成音效)

---

## 🎨 图像制作工具推荐

- **GIMP** (免费): https://www.gimp.org/
- **Krita** (免费): https://krita.org/
- **Figma** (在线): https://www.figma.com/
- **Adobe Illustrator** (付费): 用于矢量图标

---

## 📝 Unity 导入设置

### 音频导入设置:
```
Compression Format: Vorbis
Quality: 70%
Load Type: Decompress On Load
Preload Audio Data: true
Ambisonic: false
```

### 图像导入设置:
```
Texture Type: Sprite (2D and UI)
Sprite Mode: Single
Pixels Per Unit: 100
Filter Mode: Bilinear
Compression: High Quality
Max Size: 
  - Icons: 128
  - UI Elements: 512
```

---

## 🔗 参考资源

- **VRChat 手势系统**: https://docs.vrchat.com/docs/animator-parameters#gestures
- **音效库**: 
  - Freesound.org: https://freesound.org/
  - OpenGameArt: https://opengameart.org/
- **图标库**:
  - Material Icons: https://fonts.google.com/icons
  - Font Awesome: https://fontawesome.com/

---

## ⚠️ 版权注意事项

如果使用第三方素材，请确保：
1. **商业使用许可** - 如果您的 Avatar 用于商业目的
2. **署名要求** - 遵守 CC-BY 等开源许可证的署名要求
3. **禁止使用有版权争议的素材** - 避免使用未经授权的品牌音效/图标

**推荐**: 使用 CC0 (公共领域) 或自己制作的素材以避免版权问题。

---

## 📞 联系与反馈

如有素材制作问题或建议，请联系：
- GitHub Issues: [您的仓库链接]
- Email: [您的邮箱]

---

**最后更新**: 2025-01-23  
**版本**: 1.0.0
