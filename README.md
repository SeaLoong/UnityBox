# UnityBox

此仓库用于存放开发中或可直接复用的 Unity 工具脚本，便于在多个项目间共享。

## 通过 VCC / ALCOM 安装

1. 将本仓库添加为 VPM 源：

   - **一键添加（推荐）**：点击链接 → [添加到 VCC](https://sealoong.github.io/UnityBox/)
   - **手动添加**：在 VCC / ALCOM 的设置中添加以下 VPM 源地址：

     ```
     https://sealoong.github.io/UnityBox/vpm.json
     ```

2. 在 VCC / ALCOM 中找到以下包并按需安装：

| 包名 | 说明 |
|------|------|
| Advanced Costume Controller | VRChat 换装控制器，通过 Modular Avatar 菜单快速配置衣装切换 |
| Blendshape Controller Generator | Blendshape 动画控制器生成器 |
| Avatar Collider Monitor | VRChat 模型碰撞体监视器 |
| Build Pipeline Logger | 构建管线日志记录器 |

## 目录结构

每个工具都是独立的 VPM 包，位于 `Assets/UnityBox/` 下：

```
Assets/UnityBox/
├── AdvancedCostumeController/        # top.sealoong.unitybox.advanced-costume-controller
├── BlendshapeControllerGenerator/    # top.sealoong.unitybox.blendshape-controller-generator
├── AvatarColliderMonitor/            # top.sealoong.unitybox.avatar-collider-monitor
└── BuildPipelineLogger/              # top.sealoong.unitybox.build-pipeline-logger
```

## 工具文档

- Advanced Costume Controller docs: `Docs/AdvancedCostumeController.md`
- Blendshape Controller Generator docs: `Docs/BlendshapeControllerGenerator.md`

## 许可证

本项目采用 MIT 许可证（MIT）。详见仓库根目录的 `LICENSE` 文件。简要说明：你可以自由地复制、修改、合并、发布、分发、再许可和/或出售本软件的副本，但必须在所有副本或重要部分中包含原始版权声明和本许可声明。软件按"原样"提供，不附带任何明示或暗示的担保。
