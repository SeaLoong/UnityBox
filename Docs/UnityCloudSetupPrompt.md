# 可复制的 Unity 云环境配置提示词

复制下面的任务说明，在其它云环境中执行。安装、激活和测试都以该机器的实际结果为准。

---

请把当前 Linux 云环境配置成能实际运行 UnityBox 测试和 Player 的开发环境，并把安装命令、启动脚本及结果保存到持久化工作目录。

1. **先取得工具和源码。** 检查当前 checkout、用户改动、磁盘、Linux x86-64 架构、Python 和网络策略。读取可用的云环境 runtime/setup skill，以及仓库的 AGENTS.md。通过已配置的 Git 身份从 `https://github.com/SeaLoong/UnityBox.git` 执行 `git fetch origin refactor feat/animator-optimizer` 获取两个远端分支；复用已有 checkout，或分别建立独立 checkout。先从这两个分支取得 `scripts/unity/`、依赖锁与测试 skill。保留用户场景、原 metadata 和现有改动。当前机器可能没有之前环境的任何文件、桌面、登录会话或许可证，请实际检查并安装。

2. **安装真实工具链。** 使用 Python 3.12+、Unity **2022.3.22f1**；首次解包 Editor 至少准备 **13 GiB** 可用空间，并为多个隔离工程和 Player 另留空间。保留平台的代理与 CA。优先使用仓库安装器：它校验固定摘要的 GameCI OCI manifest/Editor 层，再提取 Editor；已有同版本安装可直接复用。确认 LinuxStandaloneSupport 的 Mono playback engine 完整，缺少时从对应版本的官方模块补齐并校验下载内容。运行 Editor 的 `-version` 核对版本。

   自行选择可写的持久化任务目录。执行命令前设置下列 `TASK_*` 变量为本机实际路径，安装后设置 `TASK_UNITY_EDITOR`。不要改写 `HOME`；给 Hub、浏览器和 Editor 共用独立、可写的 XDG config/data/cache/runtime 目录。不要依赖脚本的旧机器默认路径：

   ```bash
   python3 scripts/unity/install_editor.py \
     --cache "$TASK_UNITY_ROOT/cache" --destination "$TASK_UNITY_ROOT/editor"
   python3 scripts/unity/fetch_dependencies.py --profile core \
     --editor "$TASK_UNITY_EDITOR" --dependency-cache "$TASK_UNITY_ROOT/dependencies"
   python3 scripts/unity/fetch_dependencies.py --profile vrc \
     --editor "$TASK_UNITY_EDITOR" --dependency-cache "$TASK_UNITY_ROOT/dependencies"
   ```

   将 `TASK_UNITY_EDITOR` 设置为解包后的 `editor/opt/unity/Editor/Unity`。依赖按仓库锁核对归档、树哈希、身份和版本，使用真实 UTF **1.1.33**、NUnit **1.0.6**。Avatar 组合使用真实 SDK Base/Avatars **3.8.2**、MA **1.18.0**、NDMF **1.11.0**，包含 UGUI 和声明的传递依赖；禁止伪造 SDK 类型或用空测试代替集成。

3. **让用户通过官方 Hub 完成这台机器的激活。** 安装官方签名 APT 源提供的 Unity Hub。已有 Personal 的用户通常只需在 Hub 登录自己的账号，在 Settings → Licenses 确认这台机器的授权；手工安装的 Editor 用 Installs → Locate 添加。安装 Editor 与机器激活分别检查，不要求用户重复购买许可证。用户在官方页面输入账号并确认条款；不在聊天中索取密码、序列号或许可证内容，也不复制其它机器的认证数据。

   没有桌面时，在任务目录安装 Xvfb、Fluxbox、浏览器、私有 DBus、标准 Secret Service、x11vnc 和 noVNC/websockify。无 root 时，从已验证签名的 APT InRelease/Packages 索引取得并核对软件包 SHA256，再 rootless 解包；Hub 同样验证官方源签名。X11 启用 Xauthority、关闭 TCP 监听；VNC/WebSocket 只监听回环。用官方 Dev Tunnels 或平台提供的身份认证 HTTPS 转发开放操作入口，实际检查匿名访问会进入官方身份认证后，再把有效链接交给用户。登录完成后关闭外部转发。

   若 Hub 登录弹窗一闪即退，检查脱敏后的 TLS/DBus/协议回调错误。只读 HOME 的 NSS 数据库不能取得平台代理 CA 时，使用签名发行版提供的 p11-kit 系统信任模块，仅为 Hub/浏览器进程配置相应库搜索路径，让 NSS 读取系统信任库。验证正常 HTTPS 成功、自签证书被拒绝；保持 TLS 校验，不使用忽略证书开关。容器共享内存导致黑窗时，为自有 Xvfb 关闭 MIT-SHM，并让 x11vnc 使用 `-noshm`。将这些启动步骤保存成脚本，认证内容留在私有目录。

4. **分开准备真实依赖范围。** 测试工程放在源码 checkout 外，使用新目录和脚本所有权标记；测试场景由 fixture 创建，用户场景保持原样。先执行普通 Unity 核心，再执行 SDK 回退、完整 MA/NDMF 集成。八插件组合从 refactor 取七包，从 feature 取优化器：

   ```bash
   python3 scripts/unity/test_project.py prepare \
     --repo "$TASK_REFACTOR_REPO" \
     --source-package "$TASK_OPTIMIZER_REPO/Assets/UnityBox/AnimatorOptimizer" \
     --project "$TASK_UNITY_ROOT/projects/core" --profile core --offline \
     --dependency-cache "$TASK_UNITY_ROOT/dependencies"
   ```

   SDK 工程使用另一个目录和 `--profile vrc --offline`；完整集成再加入 `--dependency nadena.dev.ndmf --dependency nadena.dev.modular-avatar`。只在缓存中有包不会自动启用该范围。记录实际源码指纹、包版本和预期程序集。

5. **真正跑通测试、图形和构建。** 下面路径均替换为该机器已准备的工程、Editor 和共享 XDG 根目录：

   ```bash
   python3 scripts/unity/test_project.py run --project "$TASK_TEST_PROJECT" \
     --unity "$TASK_UNITY_EDITOR" --xdg-root "$TASK_UNITY_XDG" \
     --exclude-category 'UnityBoxPerformance;UnityBoxGraphics' --timeout 1200
   python3 scripts/unity/test_project.py report --project "$TASK_TEST_PROJECT"
   python3 scripts/unity/verify_player.py --project "$TASK_CORE_PROJECT" \
     --unity "$TASK_UNITY_EDITOR" --xdg-root "$TASK_UNITY_XDG" --timeout 1200
   ```

   功能运行使用真实 EditMode Test Runner，显式选择本仓库程序集；检查退出码 **0**、新生成的 NUnit XML、非零执行数、预期程序集和全部断言。修复实际失败后重跑对应完整范围。Player 必须实际构建 Linux Mono Release 并启动产物，检查持久化 Controller 的逐帧输出、提供者覆盖、构建场景配置移除和源场景配置保留。

   图标测试继承自有桌面的 DISPLAY/Xauthority，执行 `run --graphics --test-category UnityBoxGraphics` 并读取实际渲染像素。关闭其它测试 Editor 后串行执行 `run --benchmark`，保存预热、规模、样本、中位数、p95 和正确性结果。Player 的固定输入稳态 Animator.Update 与 Editor 生成成本分别记录。实际输出、构建、测量各自使用独立结果目录。

6. **交付可复用结果。** 运行共享 Python 单元测试和相关打包检查。保存真实命令、退出码、XML、日志与源码哈希，使用仓库证据工具生成验证和性能文档。最后说明安装位置、各范围实际通过数量、Player 与图形结果、性能数据，以及日常启动/重跑命令；把通用流程沉淀为文档和测试 skill。配置草稿与已发布环境版本分别记录，凭据、浏览器会话和许可证内容保留在该机器私有存储。
