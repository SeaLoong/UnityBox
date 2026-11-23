# ASS 0.9.14：防御粒子 / 网格 / 光源预算

## 现象与根因

上传面板把「最大粒子总数」「网格粒子三角形总数」「光源」作为三个独立统计，上限都是
`int.MaxValue`。用户游戏内实测三项总是差一点到上限，且差值各不相同。反编译
`VRCSDKBase.dll` 的 `AvatarPerformance` / `LightPerformanceScanner` 后确认根因：

- 粒子统计：`min(Σ(maxParticles + emission), int.MaxValue)`；0.9.12 及以前粒子预算又被
  网格预算反推压低（`particleTarget = meshPolyBudget / meshTriangles`）。
- 网格三角形：`min(Σ(maxParticles × meshTriangles), int.MaxValue)`；已有统计把 billboard
  按每粒子 2 三角形虚报（SDK 只算 Mesh 渲染模式），高估了占用。
- 光源：`真实Light组件数 + Σ min(main.maxParticles, lights.maxLights)`，用 `checked`
  累加、溢出钳到 `int.MaxValue`。**不是**直接加 `maxLights`。

## 修复

- 三项各自计算预算：粒子 `int.MaxValue − 已有粒子`，网格三角形
  `int.MaxValue − 已有网格三角形`，光源 `int.MaxValue − 已有光源`；不再互相反推。
- 粒子计划拆段：需要补三角形时用「每粒子 1 三角形 + 每粒子 2 三角形」两段
  （`2P−M` 颗与 `M−P` 颗，合计 P 颗、M 个三角形）；三角形不足时改为「网格 + billboard」。
  粒子总数与网格三角形总数能同时精确达到 `int.MaxValue`。
- `GetExistingParticleStats` 只为 Mesh 渲染模式计三角形，billboard 不再虚报。
- **0.9.14 的光源修正**：0.9.13 把光源预算挂在拆分后的主粒子系统上，但该系统
  `maxParticles` 只有约半个 `int.MaxValue`，SDK 的 `min(maxParticles, maxLights)` 把光源
  统计卡在约一半。0.9.14 增加专用光载体系统，其 `maxParticles` 与 `lights.maxLights`
  都等于光源预算，`min()` 即饱和；主系统与 sub-emitter 的 `maxLights` 归零，避免重复贡献。

## 关键教训：自检口径错误

0.9.13 的测试按 `lights.maxLights` 直接累加断言光源达标，漏掉了 SDK 的
`min(maxParticles, maxLights)` 口径，导致**测试通过但游戏内不达标**（假阳性）。
0.9.14 的 `GeneratedDefenseBudgetKeepsEachStatisticAtItsOwnCap` 改为调用真实 SDK
`AvatarPerformance.CalculatePerformanceStats`，用 SDK 算出的 `lightCount` 断言。

## 真实 SDK 验证

Windows 11、Unity 2022.3.22f1，SDK fixture 使用 VRChat Base/Avatars 3.10.6 + NDMF 1.15
（同版本家族，`CountParticleSystemLights` 算法与锁定的 3.8.2 一致，均已反编译核对）。
系统 TEMP 统一 scratch 根下的隔离工程，在已有 5 个 billboard（各 1,234 粒）、2 个 Mesh 粒子
（各 500 粒、1 三角形/粒）和 1 个 Light 的头像上生成防御，用反射调用真实 SDK API：

```
BUDGET existing particles=7170 meshTris=1000 lights=1
BUDGET processed=True
BUDGET SDKSTAT lightCount=2147483647 intMax=2147483647
BUDGET SDKSTAT particleTotalCount=2147483647 intMax=2147483647
BUDGET SDKSTAT particleMaxMeshPolyCount=2147483647 intMax=2147483647
```

| 统计 | SDK 实测 | 与 `int.MaxValue` 差 |
| --- | ---: | ---: |
| 最大粒子总数 | 2,147,483,647 | 0 |
| 网格粒子三角形总数 | 2,147,483,647 | 0 |
| 光源 | 2,147,483,647 | 0 |

探针脚本随仓库保存在
[ASSBudgetProbe.cs](../../../../scripts/unity/verification/Editor/ASSBudgetProbe.cs)，
用 `-executeMethod ASSBudgetProbe.Run` 运行，不进入 VPM 包。原始日志 `sdk-probe.log.gz`。

## 功能测试

| 范围 | 结果 |
| --- | --- |
| core（短路径 scratch） | 116/116 通过 |
| VRChat + NDMF | 0.9.13 时 54/54；0.9.14 本地 Windows fixture 因 Oculus XR 源码编译缺 AndroidJNI 未能编译测试程序集，改由真实 SDK 探针验证三项统计；云 CI 会执行测试程序集 |

core 在长路径（AppData\Local\Temp）下仍有 2 个既有 Windows `MAX_PATH` 迁移用例失败，与本次
改动无关；换用短 scratch 路径 `C:\Temp\ubs` 后 116/116。SDK 测试程序集在本地因
`com.unity.xr.oculus@4.2.0` 源码编译错误（`UnityEngine.AndroidJNIModule` 引用）无法运行，
这是 fixture 环境问题（云 Linux 工具链可编译），不是插件回归。

本次没有执行 Player 运行或真实上传，也没有测量运行时粒子开销：统计口径变化来自生成参数，
不改变玩家端是否加载这些防御对象。
