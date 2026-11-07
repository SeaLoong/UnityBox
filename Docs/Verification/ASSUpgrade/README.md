# ASS 旧版升级的真实归档验证

本归档复现同一份配置的三次实际 Unity Editor 执行：

| 阶段 | 输入与判定 | 记录 |
| --- | --- | --- |
| 旧版正常 | 原样导入线上 0.7.24 ZIP；包内没有 `Runtime/Component.cs.meta`，由 Unity 自动生成实际随机 GUID，再保存 Scene、Prefab 和实例覆盖 | [旧版创建](legacy-0.7.24-create.json) |
| 升级损坏 | 整包换成线上 0.8.0 ZIP；原 Scene、Prefab 和带覆盖的实例都出现 Missing Script，原配置文件字节仍完整 | [0.8.0 回归复现](published-0.8.0-missing.json) |
| 自动恢复 | 整包换成当前 0.9.0；由生产代码的初始化／导入回调自动恢复，测试没有手动调用修复 API | [0.9.0 自动恢复](recovered-0.9.0.json) |

第二阶段的 `passed: true` 表示成功复现预期的旧版回归，含义与第三阶段的功能恢复区分记录。
实际旧 GUID 为 `ca3ed01327e4ffa4e9e907c90419f826`；恢复后使用稳定 GUID
`1c51e6a139920bb4ca12700dc82627ff`。测试使用新建的合成对象、静音 WAV 和非默认配置，
没有载入用户工程，也没有手工伪造旧 GUID。

自动恢复后，密码、手侧、倒计时、连续保持时长、最大时长、选项、项目内 AudioClip 引用、
Prefab 实例覆盖、资产 GUID 和组件 GlobalObjectID 都通过原值检查。Scene／Prefab 原文件
只替换脚本 GUID，并在项目 `UserSettings/UnityBox/ASSMigration/` 留下原字节备份。

0.9.0 新配置默认保持／容错为 **0.5／0.3 秒**。归档中的已有配置保持为 **0.22 秒**，
不会被新默认值覆盖；旧容错 **0.44 秒**按当前连续保持契约归一化为 **0.21 秒**，这项有意
变化单独记录并断言。新默认值与其它控制器功能验证见[完整测试指南](../../Testing.md)。

## 归档内容

[summary.json](summary.json) 汇总结果；[source-fingerprint.json](source-fingerprint.json)
记录实际导入的完整固定包 SHA256、运行时／迁移脚本 SHA256，以及 107 个非 Markdown
源文件和资源的 SHA256。归档生成时逐项与最终 checkout 对照，全部相同。

[published-inputs.json](published-inputs.json) 保存两个实际发布 ZIP 的来源、SHA256 与旧
`.meta` 缺失情况；[legacy-generated-component.meta](legacy-generated-component.meta)
保存 Unity 实际生成的旧脚本身份。
[原 Scene](legacy-scene.unity.gz)和[原 Prefab](legacy-prefab.prefab.gz)是未修复前的合成数据，
使用标准 gzip 压缩，`mtime=0`。它们的组件 ID、密码与资源引用可直接和结果记录核对。
[sha256.json](sha256.json) 是归档文件索引。

实际执行的 Unity 验证源码保存在 [ASSRealZipUpgrade.cs](ASSRealZipUpgrade.cs)。
通用启动器 [run_upgrade.py](run_upgrade.py) 将项目、Editor、XDG 和三个 ZIP 路径全部作为
参数，不使用当前机器的固定路径，也不包含账号或许可证内容。
三阶段原始 JSON 保留实际执行命令与当时路径作为来源记录；完整 Editor 启动日志留在
测试工作目录中，未收入本归档。

## 在另一台已配置的 Unity 机器复现

使用 Python 3.12+、Unity 2022.3.22f1，以及仓库锁定的真实 SDK 3.8.2、MA 1.18.0、
NDMF 1.11.0。先按[环境流程](../../UnityEnvironment.md)准备该机器的 Editor、依赖和合法
机器激活。下列 `UNITYBOX_*` 路径需设置为该机器的实际目录；测试工程必须在源码 checkout
之外，使用新的空目录。

```bash
python3 "$UNITYBOX_REPO/scripts/unity/test_project.py" prepare \
  --repo "$UNITYBOX_REPO" --project "$UNITYBOX_TEST_PROJECT" \
  --package AvatarSecuritySystem --profile vrc --offline \
  --dependency-cache "$UNITYBOX_DEPENDENCY_CACHE" \
  --dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar

mkdir -p "$UNITYBOX_INPUT_DIR"
curl -fL 'https://sealoong.github.io/UnityBox/top.sealoong.unitybox.avatar-security-system-0.7.24.zip' \
  -o "$UNITYBOX_INPUT_DIR/ass-0.7.24.zip"
curl -fL 'https://sealoong.github.io/UnityBox/top.sealoong.unitybox.avatar-security-system-0.8.0.zip' \
  -o "$UNITYBOX_INPUT_DIR/ass-0.8.0.zip"
python3 "$UNITYBOX_REPO/.github/scripts/package_vpm.py" \
  "$UNITYBOX_REPO/Assets/UnityBox/AvatarSecuritySystem" \
  "$UNITYBOX_INPUT_DIR/ass-fixed.zip"

python3 "$UNITYBOX_REPO/Docs/Verification/ASSUpgrade/run_upgrade.py" \
  --project "$UNITYBOX_TEST_PROJECT" --unity "$UNITYBOX_EDITOR" \
  --xdg-root "$UNITYBOX_XDG" \
  --legacy-zip "$UNITYBOX_INPUT_DIR/ass-0.7.24.zip" \
  --broken-zip "$UNITYBOX_INPUT_DIR/ass-0.8.0.zip" \
  --fixed-zip "$UNITYBOX_INPUT_DIR/ass-fixed.zip"
```

启动器仅接受测试工具拥有的独立项目；每阶段完整替换该项目中的 ASS 包，再运行实际
Editor。三个阶段都由断言方法自行退出，以便初始化回调先执行完，不使用会提前结束
延迟回调的 `-quit`。结果写入测试工程 `.unitybox-real-upgrade/`，包含各阶段命令、退出码、
包 SHA256、断言 JSON 和 `three-stage-summary.json`。
首次三阶段运行保留全部输入与输出；再次复现时准备新的测试工程目录。
