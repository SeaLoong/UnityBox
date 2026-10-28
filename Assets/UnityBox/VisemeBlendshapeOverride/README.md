# Viseme Blendshape Override

在 Unity 中生成由 `Viseme` Int（0–14）和可选 `Voice` Float（0–1）驱动的口型
BlendShape Animator。核心不依赖 VRChat SDK；安装 SDK 后可使用 Avatar 构建适配，
安装兼容 NDMF 后可生成虚拟 FX。

## 安装与程序集

- 核心：Unity 2022.3。复制本包或通过 UPM/VPM 安装，无需 SDK。
- 可选 VRChat 适配：`com.vrchat.avatars >= 3.7.0`。
- 可选 NDMF 适配：同时安装以上 SDK 和 `nadena.dev.ndmf >= 1.7.0`。
- 包名：`top.sealoong.unitybox.viseme-blendshape-override`。包不会强制安装这些适配依赖。

Runtime 保留组件、15 个口型 ID 和映射数据；Editor 核心提供 Inspector、曲线、
Voice BlendTree、Write Defaults 判断和 Controller 构造。`Editor/VRChat` 只处理
Descriptor 默认值、FX 分配和上传回调；`Editor/NDMF` 只处理虚拟 Animator。
未安装对应包时，仅这些适配程序集不编译，核心功能和核心测试仍可使用。

## 普通 Unity 使用

1. 在计划放置 Animator 的根对象添加 **UnityBox > Viseme Blendshape Override**。
2. 指定根对象或其子对象上的 `Renderer`，逐行选择实际存在的 BlendShape。
   未安装平台适配时，空映射不输出嘴型；`None` 始终显式禁用该行。
3. 设置 Weight、Voice Mode 和每行 Custom Settings。Weight 默认 100；Voice 默认
   Linear，Min=0、Max=1。没有 Voice 输入来源时可选择 Disabled。
4. 点击 **Generate Controller Asset**，选择输出位置，然后将生成的 Controller
   指定给根对象的 Animator。生成按钮总是创建新资产，不覆盖现有 Controller。
5. 由项目脚本或动画输入设置 `Viseme`、`Voice`。工具不做语音识别，也不自动读取麦克风。

代码入口为 `VisemeBlendshapeOverrideProcessor.TryCreateBuildPlan(root, out plan)` 和
`BuildController(controller, plan)`。后者明确修改调用者提供的、保存在 `Assets/` 下
的控制器；只重建本插件保留名称的层和子资产。需要保留已有控制器时，先创建副本。

## VRChat 使用

在带 `VRCAvatarDescriptor` 的 Avatar Root 添加组件。Renderer 留空时，适配器每次
读取 Descriptor 的 Face Mesh；显式指定时使用该 Renderer。映射中的
`Platform default` 每次读取 Descriptor 映射，打开 Inspector 不会固定成旧名字。
`None` 禁用该行，具体名字为固定覆盖值。渲染器必须属于该 Avatar。

按原流程构建或上传。成功生成后，构建对象的 Lip Sync 改为 `VisemeParameterOnly`，
继续由 VRChat 提供参数输入。安装 SDK Base >= 3.7.0 时，组件通过条件编译实现真实
`VRC.SDKBase.IEditorOnly`，兼容上传前白名单检查；SDK/NDMF 适配还显式清理构建配置。
没有 SDK 时组件照常编译，源对象和普通 Unity 项目的组件不会因此删除。

NDMF 在 Transforming 阶段排在 Modular Avatar 后，使用虚拟 FX 和对象路径。
没有配置、禁用配置或无有效生成计划时，本插件不请求动画服务，也不改动 Descriptor。
需要生成时才启用服务；已有的其他插件服务保持有效，本插件自行启用的服务会自行释放。
没有兼容 NDMF，或 NDMF 已安装但本次构建未执行（如关闭 Apply on Build/Play）时，
SDK 回调仍走回退：复制持久化 `AnimatorController` 或创建新 FX，保存
状态、过渡和 Motion 子资产，并开启 Descriptor 的自定义动画层。回退不支持
`AnimatorOverrideController`；这种情况应安装 NDMF。输出位于
`Assets/UnityBox/VisemeBlendshapeOverride/Generated/`，不自动删除旧输出。
SDK 与 NDMF 只在生成成功后消费构建对象上的配置组件，以此防止同次重复生成；不会
凭“已安装 NDMF”跳过。失败时保留配置并让构建回调返回失败，新的配置可再次构建。
直接调用 `ProcessAvatar` 仍保留配置，供调用者明确重复生成；自动上传回调才消费配置。
Descriptor 关闭自定义动画层时，数组里残留的 Controller 和 Mask 不属于有效输入。
回退生成或本插件首次启用 NDMF 动画服务时，先在构建对象恢复这些层的默认状态，再
添加生成的 FX；不会把残留动画一起激活。源 Controller/Mask 资产不会因此修改。
源文件含多个 Controller 时按被选中对象的 local file ID 解析副本，不会误用同文件的
主 Controller；无法解析时终止构建，避免悄悄换成其它动画图。

## 行为与限制

生成层 `Viseme Blendshape Override` 包含 15 个口型状态。每个状态重置本插件控制的
其它嘴型，再输出当前权重；未映射的 BlendShape 不在控制范围。Voice Linear 在
Min–Max 之间线性混合，区间外使用端点。`Write Defaults = Auto` 是对现有控制器的
启发式判断，也可显式选 On/Off；不能保证所有混合 WD 或第三方动画组合的效果。

组件必须位于生成根对象；禁用组件、无效 Renderer 或无有效映射会跳过生成。删除组件
不会自动恢复旧版本已修改的 FX/Lip Sync，也不会删除 Generated 资产；确认没有引用
后再清理。SDK 回退只负责构建对象，不能把直接调用构建 API 当成场景预览撤销功能。

组件脚本 GUID 与口型 0–14 的序列化数值保持不变。代码 API 的枚举现为本包的
`VisemeBlendshapeOverrideComponent.Viseme`；Descriptor 入口移至
`VisemeBlendshapeOverrideVrcProcessor` / `VisemeBlendshapeOverrideVrcUtils`。

## 测试

安装 Unity Test Framework 后运行 EditMode：

- 无 SDK：程序集 `UnityBox.VisemeBlendshapeOverride.Editor.Tests`，类 `VisemeCoreTests`。
  覆盖旧数字数据、可选默认映射、完整 Controller/曲线生成、Voice、重复生成和组件禁用。
- 有 SDK：额外运行 `UnityBox.VisemeBlendshapeOverride.VRChat.Tests`，类 `VisemeRegressionTests`。
  覆盖 Descriptor、FX、源资产隔离、上传配置移除和输出路径边界；另覆盖 NDMF 未执行时的
  SDK 回退、成功去重、同一对象的新配置、失败重试及禁用的历史动画层。当前核心 9 项、
  SDK 25 项声明用例。
- 有 SDK + NDMF：独立条件程序集 `UnityBox.VisemeBlendshapeOverride.NDMF.Tests`，5 项
  声明用例检查调度不强制启用动画服务、无配置/禁用配置、历史无效层和服务所有权。

通过包安装时，将本包名加入项目 `Packages/manifest.json` 的 `testables`。
本次使用真实 Unity/SDK/NDMF 引用的 Roslyn 类型检查通过；这不等于 Unity Editor
导入、IL 后处理或 EditMode 执行。当前云环境无 Unity 许可，上述 39 项用例尚未执行。
NDMF 最终提交和 VRChat 客户端口型仍需实际项目验证。

详细说明见仓库的 `Docs/VisemeBlendshapeOverride.md`。
