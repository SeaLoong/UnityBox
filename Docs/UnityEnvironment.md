# Unity 云环境与测试流程

工具链使用 Linux x86-64 **Unity 2022.3.22f1**、Python **3.12+** 和真实依赖包。
当前 Editor 位于 `/workspace/unitybox-setup/unity/editor/opt/unity/Editor/Unity`，
依赖缓存位于 `/workspace/unitybox-setup/unity/dependencies`。
用户已通过官方 Unity Hub 登录现有 Personal 账号，在这台机器完成激活；Editor 已实际执行
EditMode 测试和 Linux Player 构建。测试入口见[Testing.md](Testing.md)，
运行记录见[VerificationResults.json](VerificationResults.json)，测量见[Performance.md](Performance.md)。

## 安装与复用

配置另一台云机器时，使用[可复制的完整提示词](UnityCloudSetupPrompt.md)。

从仓库根目录执行：

```bash
python3 scripts/unity/install_editor.py
python3 scripts/unity/fetch_dependencies.py --profile core
python3 scripts/unity/fetch_dependencies.py --profile vrc
```

安装器校验固定 GameCI OCI 镜像层的 SHA256，再提取 Editor。首次解包准备至少 **13 GiB**
可用空间；已有正确版本时直接复用。依赖工具按[锁文件](../scripts/unity/dependencies.lock.json)
核对归档、解包树、包身份、依赖和模块；完整缓存可离线准备测试工程。
安装和激活是两个步骤：安装器准备二进制，账号授权由用户在官方 Hub 中完成。

| 依赖 | 固定版本 | 使用范围 |
| --- | --- | --- |
| Unity Test Framework / NUnit | 1.1.33 / 1.0.6 | 所有 EditMode 工程 |
| VRChat Base / Avatars | 3.8.2 / 3.8.2 | SDK 适配器 |
| Modular Avatar | 1.18.0 | 服装、Blendshape 的 MA 安装路径 |
| NDMF | 1.11.0 | 虚拟控制器与构建集成 |
| UGUI | 1.0.0 | SDK 声明的 UI 源码依赖 |

`core` 锁包含 2 个包、2 个 Editor 模块；`vrc` 包含 17 个包、12 个模块。
VRC 工程额外声明 Editor 自带 AndroidJNI 模块，供 SDK 的 Oculus Editor 代码编译。
ACC/BCG 的 MA 适配要求 SDK ≥3.7.3；该版本首次提供 MA 使用的双参数
`VRCConstraintSource` 构造函数。AnimatorOptimizer 的 NDMF 适配要求 ≥1.11，
使用该版本提供的原对象与 Clip 元数据恢复层身份。各程序集的最低版本以 asmdef 为准。

## 官方登录与机器激活

新云机器先检查网络策略、代理和系统 CA，再安装官方签名 APT 源提供的 Unity Hub。
在 Hub 登录已有账号；Settings → Licenses 显示该机器的授权条目。
已有 Personal 的用户沿用自己的授权。手工安装的旧 Editor 可通过 Installs → Locate 添加，
路径选择上述 Unity 可执行文件，让 Hub 完成相应版本的授权兼容处理。
账号密码和官方条款由用户在 Unity 页面输入和确认。

当前机器的可复用交互组件放在 `/workspace/unitybox-setup`：

```bash
python3 /workspace/unitybox-setup/desktop/desktop_session.py start
python3 /workspace/unitybox-setup/desktop/keyring_session.py
python3 /workspace/unitybox-setup/unityhub/launch_hub.py
```

这些组件包含私有 DBus、Xvfb、Fluxbox、x11vnc、noVNC、Firefox、官方 Hub 3.22.5，
以及当前进程使用的内存 Secret Service。X11 使用 Xauthority；VNC/WebSocket 监听回环。
需要用户远程操作时，用官方 Dev Tunnels CLI 的受保护入口转发 6080 端口：

```bash
python3 /workspace/unitybox-setup/devtunnel/cli.py user login -g -d
python3 /workspace/unitybox-setup/devtunnel/cli.py host -p 6080 --protocol http --expiration 1h
```

入口使用创建者的 GitHub 身份认证；先实际检查匿名访问会跳转到官方登录，再分享 CLI
生成的 HTTPS 地址。完成操作后停止外部转发。不要启用匿名访问或暴露裸 VNC。
Hub、浏览器和 Editor 共用 `unity/interactive/{config,data,cache}`，保持 `HOME` 原值。
账号会话和机器许可证存放在私有环境目录，源码仓库只保存安装与使用说明。

此环境曾出现登录弹窗立即关闭，原因是只读 HOME 的 NSS 数据库缺少平台代理 CA。
已用 Debian 签名的 p11-kit 系统信任模块解决：仅在 Hub/Firefox 启动进程的
`LD_LIBRARY_PATH` 前置 `/workspace/unitybox-setup/tls-trust/lib`，从系统信任库读取 CA。
正向 HTTPS 请求成功，独立自签证书被正常拒绝。保留代理和 TLS 校验。
Xvfb 关闭 MIT-SHM、x11vnc 使用 `-noshm`，解决容器共享内存造成的黑窗。
这些 rootless 组件的安装记录与启动说明分别保存在对应目录的 README 中。

日常测试沿用已激活机器的 `--xdg-root /workspace/unitybox-setup/unity/interactive`。
Unity 2022 启动会写旧版 HOME 快捷键偏好目录，当前只读挂载会产生启动警告；
测试与 Player 构建均正常完成。使用现有 XDG 目录继续执行即可。

## 准备隔离工程

refactor 的七插件和 feature 分支的 AnimatorOptimizer 保持分支独立，测试时组合包源码：

```bash
python3 scripts/unity/test_project.py prepare \
  --repo /workspace/unitybox-refactor-review \
  --source-package /workspace/UnityBox/Assets/UnityBox/AnimatorOptimizer \
  --project /workspace/unitybox-setup/unity/projects/review-core \
  --dependency-cache /workspace/unitybox-setup/unity/dependencies \
  --profile core
```

每个依赖范围使用新的工程路径。纯 SDK 范围改为 `--profile vrc --offline`；
完整 MA/NDMF 范围另加 `--dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar`。
缓存中存在包与启用该集成是两件事，准备命令决定实际启用范围。
单包准备可重复传 `--package`。`--source-package` 组合另外一个 checkout 的包。

工具按仓库打包筛选规则复制源码、测试与原 metadata；根目录的用户场景留在原处。
所有权标记 `.unitybox-test-project.json` 记录实际来源、版本、内容哈希和预期程序集。
已留作证据的工程保持原快照，后续修改使用新目录。工程与证据保存在 `/workspace`，
`/tmp` 用于可重新生成的临时文件。

## 功能、图形、构建与性能

功能测试排除独立的性能及图形类别：

```bash
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/review-core \
  --xdg-root /workspace/unitybox-setup/unity/interactive \
  --exclude-category 'UnityBoxPerformance;UnityBoxGraphics' --timeout 1200
```

runner 显式传本仓库的 `-assemblyNames`，使用真实 EditMode Test Runner。
每次运行单独保存命令、退出码、日志、NUnit XML、结果和 XML 哈希。
通过条件包括实际执行非零测试、预期程序集覆盖、退出码 0 和全部断言成功。
`report --project <路径>` 可重新读取结果；过滤调查用例后，再运行完整所需范围。

图标像素测试使用 `--graphics --test-category UnityBoxGraphics`，并继承私有桌面的
DISPLAY/Xauthority。现有桌面可通过 `desktop_session.environment()` 取得启动环境，
只把该环境传给子进程，不输出认证内容。

```bash
python3 scripts/unity/verify_player.py \
  --project /workspace/unitybox-setup/unity/projects/review-core \
  --xdg-root /workspace/unitybox-setup/unity/interactive --timeout 1200
```

Player 工具复制专用验证脚本到隔离工程，构建实际 Linux Mono Player 并启动执行。
各包提供持久化 Animator 场景，逐帧检查动画、参数和 Blendshape 输出；构建检查 authoring
配置仅从构建场景副本剥离，源配置保留。BuildPipelineLogger 记录实际预处理、Linker、
场景处理及后处理事件。结果位于 `.unitybox-player-results/<run-id>/`。
Editor 安装中需包含对应 LinuxStandaloneSupport Mono 模块。

性能测试与其它 Editor 串行执行：

```bash
python3 scripts/unity/test_project.py run \
  --project /workspace/unitybox-setup/unity/projects/review-core \
  --xdg-root /workspace/unitybox-setup/unity/interactive --benchmark --timeout 1200
```

各场景先校验功能，再预热、多次采样，输出 `UNITYBOX_BENCHMARK` JSON。
报告记录输入大小、样本数、median/p95/min/max；原生 Animator 的优化前后输入规模一致。
Player 另测固定输入下的稳态 `Animator.Update`，与 Editor 生成耗时分别记录。
单次功能运行中的时间仅用于排查；汇总性能使用独立串行运行。

## 工具与发布检查

```bash
python3 -m unittest discover -s scripts/unity -p 'test_*.py' -v
python3 .github/scripts/test_package_vpm.py
```

Python 测试验证准备、依赖、范围筛选、进程退出与证据处理；Unity 测试验证插件行为。
[compile_assemblies.py](../scripts/unity/compile_assemblies.py) 可用真实 Editor Roslyn 与依赖
进行快速类型检查，日常完成标准仍是上述实际 Editor 和 Player 执行。
打包检查按真实 VPM workflow 核对 ZIP 内容、CRC、metadata、manifest、listing SHA256。

最终证据工具 [capture_evidence.py](../scripts/unity/capture_evidence.py) 核对实际导入的本仓库
C#/asmdef/asmref 与最终 checkout 字节一致，记录 XML、日志和源码哈希，再生成文档数据。
依赖范围有重叠，各范围测试数分别列出。
完成后提交并推送 `refactor` 和 `feat/animator-optimizer`，再核对远端提交 SHA。
当前环境安装脚本和启动说明通过环境配置草稿保存，发布入口显示的配置版本独立管理。
