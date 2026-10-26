# Animator Optimizer 测试指南

本指南属于 `feat/animator-optimizer`。其他 7 个插件的新增回归与更新后的完整文档在
`refactor`，该分支与本分支尚未合并。

## 环境

- Unity 2022.3、VRChat Avatars ≥3.7、NDMF ≥1.9。
- Unity Test Framework。按 VPM/UPM 安装时，在项目 manifest 的 `testables` 中加入
  `top.sealoong.unitybox.animator-optimizer`；直接放在 Assets 下时无需该条目。
- 恢复本地项目原有 `.meta`，特别是序列化组件的 GUID。不要用随机 GUID 替代原始引用。

## 执行

在 Test Runner 的 EditMode 运行 Animator Optimizer 测试装配。基础用例位于
`Assets/UnityBox/AnimatorOptimizer/Tests/Editor`，NDMF 专用用例在其 `NDMF` 子目录，
仅在满足依赖与测试条件时编译。测试装配名为 `UnityBox.AnimatorOptimizer.Tests`
和 `UnityBox.AnimatorOptimizer.NDMF.Tests`。本轮新增 49 个声明用例：Processor 15、
参数语义/Undo 14、AAP 6、NDMF 14；这不是实际运行通过数。

```sh
"$UNITY_EDITOR" -batchmode -nographics -projectPath "/path/to/test-project" \
  -runTests -testPlatform EditMode \
  -testResults "/path/to/results.xml" -logFile "/path/to/editor.log"
```

不追加 `-quit`。核对退出码、本次 XML、实际执行数及失败/跳过情况，零测试不算通过。
Python 发布工具测试可在仓库根运行：

```sh
python3 .github/scripts/test_package_vpm.py -v
```

## 覆盖与限制

现有及新增用例覆盖有限条件分析、生成树/参数、时间/Mask/层顺序边界、配置和目标
定位、资产副本、Undo、AAP 输入范围与虚拟图。构造图形正确不证明 Unity 逐帧输出
等价；AAP StateRegister、连续过渡、NDMF→SDK 交接、第三方插件合并顺序和实际
性能仍须在 Unity 与测试 Avatar 上验证。

本轮云端没有 Unity Editor/许可证，Unity 编译和所有 EditMode 用例均未执行。
本轮已通过：57 个 C# 文件语法树解析、25 个包/装配 JSON 校验、5 个 Python
发布工具测试，以及 8 个 VPM 包的构建、ZIP 内容/CRC、SHA256 和原有元数据保留检查。
这些检查只验证各自范围，不能替代 Unity 编译。
