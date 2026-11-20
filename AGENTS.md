# TileStart 本地工作指令

## Windows 11 固定项导入

### Key Files

| 文件 | 用途 |
| --- | --- |
| `src/TileStart.Host/Applications/NativeStartPins.cs` | 导出和解析当前用户的原生固定项，为新增磁贴生成分组。 |
| `tests/TileStart.Host.Tests/NativeStartPinsTests.cs` | 验证快捷方式、应用 ID、导出顺序和重复导入。 |

### Dependencies

- 使用 Windows 11 自带的 Windows PowerShell 和 `Export-StartLayout`，通过应用目录解析应用 ID。

### Common Patterns

- 导入由设置页显式触发。解析和图标加载完成后，先将新增分组写入布局文件，再刷新当前磁贴区。重复导入按启动目标去重。

## 开始菜单尺寸

- `WindowSizeStore` 以 DIP 保存菜单宽高与应用列表宽度，并兼容旧版磁贴列数配置。普通模式按显示器工作区定位，全屏使用该显示器的完整矩形。
- 应用列表宽度由主窗口分隔线控制，磁贴视口跟随右侧边界布局。视口预留滚动条宽度，打开菜单时无需为滚动条改写窗口大小。
- 普通模式在当前显示器可见的置顶任务栏之后放置菜单窗口，并在获取前台焦点后恢复这个层级，避免 DWM 外阴影叠到任务栏上。全屏模式仍置于任务栏之上。

## Windows 11 窗口材质

| Key Files | 用途 |
| --- | --- |
| `src/TileStart.Host/Windowing/SystemWindowBackdrop.cs` | 让主菜单和对话框共享 Windows 11 DWM Desktop Acrylic 调用。 |

- Windows 11 默认使用浅色。主菜单与拖拽临时底色使用当前主题的中性色。透明效果关闭、高对比度或系统调用失败时使用实体底色。

## Win 键接管

- `WinKeyHook` 放行组合键的原始按下和释放事件，检测 Win 按下前已按住的键，并在本次 Win 按住期间出现其他按键时取消单按判定。
- 仅在单按 Win 释放时抑制原生开始菜单，先让后续钩子收到原事件，再用带内部标记的输入补齐系统侧的释放。掩码注入失败时放行原释放。

## 持续工作与提交边界

- Win10/Win11 视觉复刻、平台适配等由多个连续批次组成的长期任务中，用户对某一批回复“ok”“可以”“继续”等，只表示该批效果可继续推进，不代表整个工作轮已经完成。
- 长期任务中，每项可独立回退的代码、资源或文档改动完成并通过相应自动验证后，立即创建本地提交作为检查点；无需等待整个复刻阶段结束，也不把多个实验长期堆在工作区。
- 检查点提交默认不推送、不合并、不发布；只有用户明确要求时才执行对应远端或发布操作。
- 被用户实测否定的实验应先撤销再提交后续方案；若保留实验仅为便于对照，提交信息必须明确标记其未解决的问题。

## Windows 界面验证

- 简单改动若可由源码检查、构建和自动测试充分覆盖，默认不使用 Computer Use，覆盖安装并启动开发版后由用户手动验收。
- 只有视觉布局、真实桌面交互、Shell 接管等无法由自动测试覆盖的行为，或用户明确要求自动验证时，才使用 Computer Use。
- 需要桌面自动化时先按对应 skill 初始化 Computer Use；确认可用后直接用于 Win10/Win11 的黑盒与视觉验证，不重复说明可用性、恢复过程或降级方案。
- 只有 Computer Use 实际失败、尝试恢复后仍阻塞当前验证时，才向用户说明具体限制和未覆盖项。
- 用户提供录屏或超大截图时，主代理禁止直接打开原尺寸画面，避免大图解码和上下文处理造成卡顿；先读取文件元数据，再将原始分辨率关键帧或原图的视觉观察直接交给单个子代理，主代理只整合结构化观察结论。除非用户明确要求，不降低子代理查看材料的清晰度，也不允许该子代理再次委派视觉分析。
- 测试完成后关闭独立验收窗口和临时进程。本机覆盖安装后启动的交付版本留在当前桌面会话中供用户使用。

## 本机开发版交付

- TileStart 功能、修复和界面改动通过相应验证后，默认构建当前提交的本机开发安装包并覆盖安装。交付版本在 `InformationalVersion` 中记录 `+local.<commit>`。
- 覆盖安装前备份用户的布局和偏好，使用已安装 Host 的 `--shutdown` 正常退出并卸载 Hook，再运行安装包。
- 安装后直接从安装目录启动新版 Host，核对安装版本、应用内容就绪和 Hook 加载结果，并保留新版运行。

## Win10 虚拟机远程验证

- Win10 22H2 虚拟机固定通过 SSH 访问：主机 `192.168.3.128`，用户 `Narylr`，密钥 `%USERPROFILE%\.ssh\tilestart_win10_vm_ed25519`；当前已验证系统 build 为 `19045.6466`。
- SSH 服务进程不在用户当前桌面 Session。发送开发版、读取日志和执行普通命令可以直接使用 SSH；启动或关闭 TileStart GUI 必须投递到当前交互桌面 Session，不能直接从 SSH 后台运行 `TileStart.Host.exe`。
- VM 已配置交互式计划任务 `\TileStart-Dev-Shutdown` 和 `\TileStart-Dev`：前者关闭当前桌面 Session 中的 TileStart Host，后者在该桌面 Session 启动已发送的开发版。Win10 兼容改动完成构建后，默认通过 SCP 发送开发版并依次运行这两个任务，不再要求用户手动安装开发安装包。
- VM 的 TileStart 日志位于 `C:\Users\Narylr\AppData\Local\TileStart\TileStart.log`，崩溃转储位于 `C:\Users\Narylr\AppData\Local\CrashDumps`。出现 Win10 专属异常时先通过 SSH 读取这些证据，再判断是否改代码。

## 逆向研究工具

- 本机 Ghidra 逆向工具目录为 `D:\Narylr\tools\Ghidra`；本项目需要 Ghidra、符号或现有逆向辅助工具时先检查该目录。
- TileStart 的本机逆向研究目录为 `D:\Narylr\TileStart-Research`：`startui-ghidra` 保存 StartUI Ghidra 工程，`exports` 保存函数与布局导出，`scripts` 保存研究脚本，仓库原 `tools\reverse` 固定迁移到 `D:\Narylr\TileStart-Research\tools\reverse`；开始新的逆向工作前先检查并复用这里的材料。
- Rider 对应的 dotTrace CLI 固定保存在 `D:\Narylr\TileStart-Research\tools\dottrace`；诊断 Host 首次交互、卡顿和 CPU 热点时优先复用该工具，将 `.dtp` 快照等分析产物留在研究目录，禁止提交到公共仓库。
- `D:\Narylr\TileStart-Research\docs` 保存原版截图、个人布局、完整逆向规格和研究过程；这些材料可能包含个人应用、用户名、绝对路径和桌面内容，禁止重新提交到公共仓库。
- 研究目录位于主仓库外；微软二进制、PDB、Ghidra 数据库和大体积导出不提交到 TileStart 仓库。
- 缺少本项目必要的逆向工具时直接下载；一次性工具、克隆和分析产物放在 Windows 临时目录。

## IDE 格式化

- JetBrains IDE 格式化产生的无害文本结构、换行或行尾变化，只要不影响语义、有效 diff、构建和行为，就不要反复修整或单独纠结。

## 发布自动化

| Key Files | 用途 |
| --- | --- |
| `scripts/Build-ReleaseUpdate.ps1` | 验证上一正式版的便携包，生成更新清单、可选热补丁和发布校验文件。 |
| `src/TileStart.Host/Updates/UpdateReleaseManifest.cs` | 解析经校验的清单，检查基线版本和安装文件 SHA-256。 |

### Dependencies

- Release 构建使用现有 GitHub CLI 获取基线附件，使用现有 Inno Setup 编译完整安装器和热补丁。不含运行时版需要系统安装 .NET 8 Desktop Runtime（x64）。运行时交接使用系统自带的 Windows PowerShell。

### Common Patterns

- `GitHubUpdateService` 只接受 fork 同版本 Release 附件，并通过对应的 `SHA256SUMS.txt` 校验。基线版本及文件匹配时优先下载热补丁，缺少补丁或验证失败时使用完整包。
- Release 同时提供自包含和不含运行时的完整安装器。后者从实际 `runtimeconfig.json` 辨认安装类型，自更新保持该类型，从自包含版切换时先暂存已知随包运行库，安装失败则恢复。运行时、外部依赖或文件集合变化时跳过热补丁，补丁写入前复查自包含版基线。
- `UpdateInstallerLauncher` 等待 Host 退出，将包覆盖到当前目录并从原会话重新启动。便携自更新采用 Inno Setup 当前用户模式，跳过卸载器、快捷方式、注册表和安装标记。早期 Release 仍支持手动解压便携包。`Local Hotfix` 继续用于显式开发部署。

- 用户要求发布明确版本（例如“发布 0.1.1”）时，默认在 `main` 上触发 GitHub Actions 的 `Release` 工作流并传入版本号，监控到完成后报告 Release 地址和产物；不再手工构建、计算校验值或逐个上传附件。
- 每个 GitHub Release 必须填写面向用户的中文更新说明，至少概括本版新增、修复和重要兼容性变化；不能留空、只放 Full Changelog 链接或只依赖附件。工作流生成 Release 后检查正文，不完整时使用 `gh release edit` 补齐。
- Release 成功后只报告版本、地址和产物；只有用户明确要求“更新本地”或“安装该版本”时，才下载并校验正式安装器，先通过 Host 的 `--shutdown` 正常卸载 Hook，再执行升级安装、恢复 Host，并核对安装版本与基本启动行为。
- 自动发布失败时优先读取 GitHub Actions 日志并修复工作流或打包脚本；仅在调试发布流水线时使用本地 `scripts\Build-Package.ps1`。
- 普通 Bug、UI 调整和小功能默认组成一批，在形成完整用户价值、主线稳定且自动测试与打包验证通过后发布补丁版本，不采用“一项小改动一个 Release”。
- 当前用户在日常使用中实际遇到、修复后已验收但不足以单独创建正式 Release 的普通 Bug，远端交付默认提交、推送并合并 `main`。本机开发版按「本机开发版交付」部署。用户明确要求 CI 热更时，触发 `Local Hotfix` 工作流，并通过 `scripts\Install-Local-Hotfix.ps1` 将经过 CI 测试和 SHA-256 校验的 Release 安装器部署到本机。Local Hotfix 不创建 tag 或 GitHub Release，稳定版本号保持不变，`InformationalVersion` 记录 `+hotfix.<commit>`，后续正式 Release 必须覆盖该热更。
- 软件无法启动、托盘/Win 键/开始按钮完全失效、安装或更新失败、配置/布局/备份可能丢失、fail-open 失效或出现安全问题时，修复验证通过后立即发布补丁热修，不等待累计。
- 进入 Windows 大版本迁移、Shell 适配或其他明显开发阶段前，先发布一个稳定补丁版本作为上一环境的可回退基线。
- `0.x` 阶段中，兼容性修复、普通功能和小批量改进递增补丁号；形成新的平台适配阶段或较大功能集合时递增次版本号。用户未指定版本但明确要求“发布”时，先核对最新 Release，默认选择下一个合理的补丁版本。

## 可审计的工作说明

- 不把工具输出或“已修复、测试通过”当作完整说明；每轮按“现状、证据、判断、准备怎么改、实际改动、验证结果、剩余不确定性”向用户解释。
- 开始修改前，先说明本轮准备改哪些文件或代码路径，以及每项改动预期解决什么问题；发现原假设错误时及时说明并调整。
- 明确区分实测事实、源码证据、合理推断和临时近似实现；没有证据时不得使用“已经还原”“完全一致”等确定表述。
- 工具和命令形式不限，PowerShell 复杂脚本也可正常使用；关键是调用前说明本次操作要确认什么、为什么需要确认、结果会决定哪项改动，调用后把关键证据翻译成项目结论，不让用户从工具日志中猜测正在做什么。
- 完成后解释关键代码如何工作、数据或事件如何流动、为什么该修复能覆盖用户报告的场景，而不只列出修改文件和测试数量。
- 对 UI、动画和逆向重建，说明已获得的原版证据、仍缺失的参数，以及本次实现属于精确复刻还是待实机校准的近似方案。
