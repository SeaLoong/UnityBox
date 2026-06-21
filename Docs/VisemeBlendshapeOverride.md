# Viseme Blendshape Override

> 用 VRChat 内建 `Viseme` 与 `Voice` Animator 参数，生成一层可自定义权重、可独立响应区间的口型 BlendShape 覆盖层。

## 功能概述

| 功能 | 说明 |
|---|---|
| **替代默认 Viseme 绑定** | 构建时把 Avatar Descriptor 切到 `VisemeParameterOnly`，不再使用 VRChat 默认固定 100 的口型权重 |
| **逐口型限幅** | 每个 viseme 都可设置独立 `Weight` 上限 |
| **Voice 强度缩放** | 可用内建 `Voice` 参数对输出做二次缩放 |
| **独立响应区间** | 每个 viseme 可选择自己的 `Voice Min / Voice Max` |
| **映射继承或手动覆盖** | 可直接跟随 Avatar Descriptor 的 viseme 映射，也可手动指定 BlendShape 名 |

## 适用场景

- 默认 VRChat 口型幅度太大，想把嘴型整体收敛
- 不同 viseme 需要不同开口程度
- 想让 `aa`、`oh`、`ou` 等元音与 `PP`、`FF` 这类辅音有不同的响应节奏
- 想保留 VRChat 官方 viseme 输入链路，但接管最终输出表现

## 快速开始

1. 把 `VisemeBlendshapeOverrideComponent` 挂到 Avatar Root
2. 如果 Avatar Descriptor 已配置 Face Mesh / Viseme 映射，可直接保持 **Follow Avatar Descriptor mapping**
3. 在 Inspector 中：
   - 先点一次 **均衡** 权重预设
   - 保持 `Voice Modulation = Linear`
   - 从 `Voice Min = 0.05`、`Voice Max = 0.15` 起步
4. 如需更精细控制：
   - 对某些 viseme 关闭 `Use Global Voice Range`
   - 给它们单独设置 `Voice Min / Voice Max`
5. 构建 / 上传 Avatar

## 调参建议

### 元音建议

- `aa`：适合更高的 `Voice Min / Max`，避免一开口就“爆嘴”
- `oh / ou`：适合更低的 `Voice Min`，让口型更容易被声音带起来

### 辅音建议

- `PP / FF`：通常适合较小的 `Weight`，但可用较低 `Voice Min` 提高灵敏度
- `SS / nn`：一般建议更保守，避免持续开口感太强

## 构建行为说明

该工具在构建时会：

1. 读取 Avatar Descriptor 或组件上的 viseme 映射
2. 生成 / 复用插件专用 FX Controller 副本
3. 添加一层 `UB Viseme Blendshape Override`
4. 通过 `Viseme` 参数切换状态
5. 通过 `Voice` 1D BlendTree（可选）缩放输出强度

生成控制器使用插件专用产物路径，避免直接改脏用户原始 FX Controller。

## 包信息

- VPM 包名：`top.sealoong.unitybox.viseme-blendshape-override`
- 源码目录：`Assets/UnityBox/VisemeBlendshapeOverride/`
- 包内说明：`Assets/UnityBox/VisemeBlendshapeOverride/README.md`