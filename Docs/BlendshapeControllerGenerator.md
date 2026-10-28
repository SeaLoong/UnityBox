# Blendshape Controller Generator

Unity 2022.3 工具，为 SkinnedMeshRenderer 生成 blendshape 动画和 Animator Controller。
完整扫描窗口及生成流程不需要 VRChat SDK/MA；可选 MA 适配增加 Avatar 菜单、FX 与同步
参数安装。包说明见 [README](../Assets/UnityBox/BlendshapeControllerGenerator/README.md)。

## 使用

1. 打开 Tools > UnityBox > Blendshape Controller Generator，选择场景 **Animation Root**。
2. Scan 查找 Renderer，或 Add 手动加入。Scan Filter 按对象名过滤，Include Inactive 包含
   未激活对象；Renderer 必须属于 Animation Root。
3. 勾选 blendshape，设置每个 Mesh 的前缀和 Float/Bool 类型。清洗后重名会得到不同后缀，
   跨 Mesh 的参数冲突也会提示调整，以预览的实际名称为准。
4. 设置安全的 `Assets/...` 输出目录及 Controller Name，点击 Generate。
5. 普通 Unity 中将结果分配给 Animation Root 上的 Animator，再由项目代码/UI 驱动参数。
   每个 Renderer 分别生成一个 Controller，使用多个时须自行合并或选择。

若需 MA，另外安装 SDK Base/Avatars **≥3.7.3**、MA **≥1.13.0**。满足条件后集成程序集
启用，勾选 **Install MA Controls** 才安装菜单/FX/同步参数；取消可继续只输出 Unity 资产。
包本身不强制安装这些平台依赖。MA 1.13/1.18 使用的双参数 VRCConstraintSource 构造器
从 SDK 3.7.3 才存在，3.7.0～3.7.2 不满足该集成路径。

## 参数与曲线

- Float 输入 0～1 控制动画归一化采样时间。Duration 和 Value Scale 缩放曲线，默认
  从 0 到100；Duration必须大于0，输入必须有限。零Scale生成全零，阶梯无限切线保留。
- Bool false/true 分别使用 Off/On，**Inv** 交换输出；默认参数值为false。
- 生成层使用 Write Defaults Off。MA 模式匹配 Avatar 的 WD 策略，并将 Float/Bool
  注册为保存/同步参数，分别占8/1bit；普通 Unity 不产生网络同步服务。
- Curve、Duration、Loop、Value Scale 存在 EditorPrefs，不是 Avatar 上的持久组件配置。
- 名称中的 `/` 在 MA 菜单中形成层级，`Smile` 与 `Smile/Big` 可同时保留独立控件。

## 输出、重复生成与 Undo

```text
{OutputFolder}/{AvatarIdentity}/{MeshIdentity}/Build[ n]/
├── {ControllerName}.controller
├── 0000_Float.anim
└── 其它已选 Float 或 Bool Off/On 动画
```

普通窗口仍沿用此内部目录命名。根身份包括场景/层级位置，Renderer 身份包含相对路径；
同名 Mesh 不再覆盖。对象改名或移动可能得到新目录，需检查旧资产、菜单和参数引用。

每次先写独立 Build，成功后 MA 模式才替换对应 Renderer 的菜单、FX 与参数声明；普通
模式不修改场景 Animator，需手动换用新结果。取消/异常清理本次未完成目录，保留旧引用；
在失败前已完成的其它 Renderer 结果保留。MA 安装异常会报告，不再被当作成功忽略。

改型或取消部分选择后，MA 模式移除本生成器旧声明；原公共菜单根的对应声明迁移，仍被
其它控制器使用的项保留。全部取消不会自动卸载以前生成的内容，需手工移除相应根。

旧Build保留以支持场景Undo和旧引用，确认无引用再清理；场景Undo不撤销资产文件生成/
删除。输出不得离开Assets，拒绝点路径、空路径段、反斜线及冒号。

## 验证和限制

同父同名对象或对象名含 `/` 会造成Unity动画绑定歧义，应先修正层级。更换sharedMesh或
删除blendshape后重新Scan。多个表情、口型或生成器同时写同一属性时，检查最终图和参数预算。

`Tests/Core/Editor` 声明14个真实资产/曲线/路径用例；`Tests/Editor` 声明5个MA集成用例，
仅在实际SDK/MA存在时启用。VPM/UPM安装时加入manifest testables。真实Unity
2022.3.22f1已执行核心14项和SDK/MA集成5项，全部通过、零跳过；完整环境使用
SDK3.8.2、MA1.18、NDMF1.11。最终Avatar构建、合并后的绑定与参数预算仍需项目验证。
最新证据见 [Testing](Testing.md)，迁移影响见 [RefactorChanges](RefactorChanges.md)。
