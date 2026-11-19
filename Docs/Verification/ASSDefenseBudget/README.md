# ASS 0.9.13：防御粒子 / 网格 / 光源预算

## 现象与根因

上传面板把「最大粒子总数」「网格粒子三角形总数」「光源」作为三个独立统计，上限都是
`int.MaxValue`。0.9.12 及以前三项总是差一点到上限，且差值各不相同（用户实机：`int.MaxValue`
分别少 327,082 / 1,714 / 345,429）。逐项反推后确认是同一份预算被复用了三次：

- 粒子预算先按「`int.MaxValue − 已有粒子`」算出，随后又被网格预算反推
  （`particleTarget = meshPolyBudget / meshTriangles`）压低，差值 = 已有网格三角形 − 已有粒子。
- `GetExistingParticleStats` 给 billboard 粒子按每粒子 2 个三角形记账，而 VRChat 只统计
  Mesh 渲染模式，导致网格预算被高估，生成量再被间接压低。
- `lightsModule.maxLights = particlesForThis`，光源统计直接复制粒子数，而不是使用光源预算。

## 修复

- 三项各自计算预算：粒子 `int.MaxValue − 已有粒子`，网格三角形
  `int.MaxValue − 已有网格三角形`，光源 `int.MaxValue − 已有光源`；不再互相反推。
- 粒子计划拆段：需要补三角形时用「每粒子 1 三角形 + 每粒子 2 三角形」两段
  （`2P−M` 颗与 `M−P` 颗，合计 P 颗、M 个三角形）；三角形不足时改为「网格 + billboard」。
  这样粒子数与网格三角形数能同时精确达到 `int.MaxValue`。
- 只有第一个生成系统承载 `lightsModule.maxLights`，拆段不会把光源预算算两遍。
- billboard 不再按每粒子 2 个三角形计入已有统计。

## 实测

Windows 11、Unity 2022.3.22f1。探针在已有 5 个 billboard（各 1,234 粒）、2 个 Mesh 粒子
（各 500 粒、1 三角形/粒）和 1 个 Light 的头像上生成防御，按 VRChat 的统计口径汇总：

| 统计 | 生成后总量 | 与 `int.MaxValue` 差 |
| --- | ---: | ---: |
| 最大粒子总数 | 2,147,483,647 | 0 |
| 网格粒子三角形总数 | 2,147,483,647 | 0 |
| 光源 | 2,147,483,647 | 0 |

修复前同口径下粒子为 `int.MaxValue − 6,170`（被网格预算压低），光源把粒子数算两遍达到
`int.MaxValue × 2` 量级。原始日志与命令见 [results.json](results.json) 与 `probe.log.gz`。

## 测试

| 范围 | 结果 |
| --- | --- |
| core | 116/116 通过 |
| VRChat | 48/48 通过 |
| NDMF | 5/5 通过（含新增 `GeneratedDefenseBudgetKeepsEachStatisticAtItsOwnCap`） |

新增用例在带已有 billboard 粒子、Mesh 粒子和灯光的头像上生成防御，断言粒子统计与光源统计
精确等于 `int.MaxValue`、网格三角形不超过上限。SDK 套件 54/54 全部通过。

本次没有执行 Player 运行或真实上传，也没有测量运行时粒子开销：统计口径的变化来自生成参数，
不改变玩家端是否加载这些防御对象。
