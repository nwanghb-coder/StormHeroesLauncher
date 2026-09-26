# HOSLauncher — Stage 2: Feature Complete

Current development version: **0.3.0-dev.6**, on `stage2-feature-complete`.
Dev.6 reuses game-matched active UU acceleration when optional zone/server fields are missing,
while rejecting explicit conflicts. The visible product is now HOSLauncher. See
[decision rules, rename boundaries and manual acceptance](docs/DEV-6.md).
Developer Observer v1 is implemented behind an explicit compile-time build property; normal builds
exclude its implementation. See [Observer build and daily-use instructions](docs/OBSERVER.md).
Compact borderless progress, stable game-window readiness and bounded preparation-dialog hiding are
enabled in both builds; see [state mapping and manual acceptance](docs/PROGRESS.md). The developer
Observer tail runs separately and permits warm relaunch. See [prep diagnostics](docs/HEROES-PREP.md).
Final Stage 3 visual design remains deferred.

See [PROJECT-STATE.md](PROJECT-STATE.md) for current behavior and safety boundaries,
[Stage 2 roadmap](docs/STAGE-2.md) for work order, and
[Git workflow](docs/GIT-WORKFLOW.md) for local history and release handling.

Stage 1 is complete and frozen at tag `friend-0.2` (version `0.2.0-friend-test`).
The [release record](docs/releases/friend-0.2.txt) preserves the original package hashes.
The frozen ZIP remains under `artifacts/Friend-0.2-Release/`; binaries are outside Git.
Do not overwrite that package or move the tag.

The current portable layout is `HOSLauncher.exe` plus `app/HOSLauncher.WindowHelper.exe`,
both self-contained x64. Settings, logs and CLI cache use `%LOCALAPPDATA%\StormHeroesLauncher`.

Offline validation with .NET 10 SDK and previously restored dependencies:

```powershell
dotnet build StormHeroesLauncher.slnx --configuration Release -p:Platform=x64 --no-restore
dotnet run --project tests/StormHeroesLauncher.OfflineTests/StormHeroesLauncher.OfflineTests.csproj --configuration Release --no-restore
```

## Historical development notes

The Alpha/MVP notes below describe earlier stages, not current configuration or launch behavior.
Use PROJECT-STATE.md for the accepted launch architecture.

# Alpha 0.1

Current headless local Alpha: see [ALPHA-0.1.md](ALPHA-0.1.md) for workflow, limitations and manual acceptance instructions. Earlier MVP notes below are historical.

# StormHeroesLauncher

C# / .NET 10 / WPF，Windows 10/11 x64。MVP 1 检测与启动 UU；MVP 2 通过用户已验证的外部 UU CLI 管理风暴英雄国际服加速。尚无 Battle.net 或游戏启动功能。

## 构建与离线验证

需要 .NET 10 SDK x64。运行需要 .NET 10 Windows Desktop Runtime x64。

```powershell
dotnet build src/StormHeroesLauncher/StormHeroesLauncher.csproj --configuration Release -p:Platform=x64 --output artifacts/mvp2-release-x64
dotnet run --project tests/StormHeroesLauncher.OfflineTests/StormHeroesLauncher.OfflineTests.csproj --configuration Release
```

离线测试使用内存响应及测试程序自身的模拟子进程，不运行真实 UU CLI、不连接网络、不启动 UU。无需第三方测试包。

## 配置

外部 CLI 不复制进本项目。发布目录的 `uu-cli.settings.json` 配置 CLI 路径、游戏/区服 ID、请求/等待超时，修改后重启应用生效。源码中的配置文件用于构建默认值；重新构建时可能覆盖输出目录的手工修改。

默认 CLI：`E:\CodexProjects\UU-CLI-Investigation\netease-uu-booster\bin\uu-cli.exe`。

用户已人工验证的默认值：

| 项目 | ID |
|---|---|
| 风暴英雄国际服 | 569cbb26a26c753e42982639 |
| 亚洲区 | 5e84381b04c2150cc09e485e |
| 韩国（默认） | 57a3ec73a26c752383c56217 |
| 台湾（可选） | 57a3ec69a26c752383c56213 |
| 新加坡（可选） | 5a7bc676e3a8b266d8825dcf |

## 行为与架构

- UuService 保留 MVP 1：检测 uu.exe；必要时启动 `D:\UU\Netease\UU\uu_launcher.exe`，ShellExecute + runas，UAC 由用户手动处理。应用自身仍为 asInvoker。
- CliProcessRunner：ArgumentList、UTF-8、并发捕获 stdout/stderr/退出码，支持超时与取消。
- UuCliService：仅使用已验证的 start/status/stop 命令；JSON success 是判断依据，退出码 0 不保证成功。
- HeroesBoostWorkflow：确保 UU 运行 → 用 status 等待 CLI 就绪（15 秒）→ 发送一次 start → 轮询目标游戏 status（30 秒），必须 isBoosting=true 且 status=boosting 才报告就绪。
- 停止只针对配置的游戏 ID，随后查询确认 not_boosting；不发送 stop --all。
- 每条 CLI 命令默认最长 20 秒；工作流期限会进一步限制剩余等待。UAC 手动确认时间不算入上述 CLI 等待期限。
- 启动应用仅检测 UU 进程，不自动调用 CLI 或加速。所有 CLI 操作均来自用户按钮。
- 操作期间禁用冲突按钮，可取消等待。取消/超时仅终止本工具创建的 CLI 请求进程，不终止进程树、UU 或游戏，不自动补发 stop。请求可能已生效，应刷新状态确认；取消按钮不操作或关闭 UAC。
- 日志保存于 `%LOCALAPPDATA%\StormHeroesLauncher\Logs\launcher-yyyy-MM-dd.log`。记录参数、退出码、结果分类及节点指标；原始 stdout/stderr 只在内存捕获，避免持久化任意令牌/凭据。
- 状态兼容文档的 data.boosters 数组和用户示例可能使用的 data 扁平结构；未知字段忽略，关键字段缺失/矛盾或游戏 ID 不匹配时报错。

## VPN 关闭后的人工验收（本轮未执行）

1. 结束当前 Codex 工作后，由用户自行关闭 VPN；本程序不会检查、操作 Astrill 或修改网络。关闭其他旧版 StormHeroesLauncher。
2. 检查输出目录 uu-cli.settings.json，确认 CLI 文件存在，默认韩国区服 ID 正确。
3. 运行 artifacts/mvp2-release-x64/StormHeroesLauncher.exe。应仅显示 UU 进程状态，不能自行加速。
4. UU 已运行时点击“检测 / 启动 UU”，应直接显示已运行，无 UAC。
5. 如需验证冷启动，由用户手动退出 UU，然后点击该按钮。拒绝 UAC 应显示取消；再次点击并手动接受后，应确认 UU 主窗口就绪。
6. 点击“刷新加速状态”，核对未加速/既有状态。再点击“启动风暴英雄加速”；观察就绪查询、一次 start、后续 status 日志。必须确认 boosting 后显示节点、延迟、丢包率。
7. 点击“刷新加速状态”，与 UU 界面人工对照。不要点击或启动游戏。
8. 点击“停止风暴英雄加速”，应只停止本游戏并经 status 确认未加速。
9. 可在启动/等待期间点“取消等待”，界面应恢复可操作；随后刷新，不能假设取消等于停止加速。
10. 关闭应用后暂将配置 CliPath 改为不存在的绝对路径，再打开并刷新，确认错误清楚且不崩溃；随后恢复配置。不要改动 UU 安装文件。
11. 将人工返回 JSON 与离线样本对照，若实际状态结构不同，保留去除敏感字段的样本供下一轮适配。

## 范围

不直接连接 UU localhost 端口/管道，不做 UI Automation、图像识别、UAC 自动确认。
不修改 UU、游戏、注册表、服务、防火墙、VPN、代理、Winsock、LSP、路由或适配器。
不读取游戏内存、不注入、不抓取游戏流量。本轮只构建及离线测试，VPN 保持原状。
