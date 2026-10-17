# Steam Workshop 发布

## 禁止事项（不可协商）

SteamCMD 的 `contentfolder` 目录上传与 `SteamUGC.SetItemContent` 都会把条目**单向转换**
为 UGC 目录模式。ONI 把 `ISteamUGC.GetItemInstallInfo` 的返回值当 ZIP 文件打开，
目录路径会让玩家报“下载失败”。转换**没有回退 API**：一旦条目被 ISteamUGC 更新，
`ISteamRemoteStorage` 对该条目永久失效。唯一出路是新建条目，见
[steam-promotion.md](steam-promotion.md)。

因此：

- 不得运行 `steamcmd +workshop_build_item`
- 不得把 VDF 交给 SteamCMD 上传
- 不得调用 `SteamUGC.SetItemContent`（Directory 模式）
- 不得对已转换的旧条目重试旧文件更新

`contentfolder` **字段本身不是禁用标志**：C# 发布器
(`tools/OniMods.SteamPublisher/`)**要求**该字段，它据此读取目录并打包成单文件
Legacy ZIP。决定是否转换的是消费该字段的 API，不是字段存在与否。

## 唯一合法发布路径

单文件 Legacy ZIP 发布器，即下面的两个脚本。`onim publish` CLI 只生成 VDF 并交给
OniUploader GUI，不自行上传；它的 `--steamcmd` / `--steam-user` 参数已移除。

仓库提供两个固定目标的发布入口：

| Mod | 上传器 App ID | ONI App ID | Workshop ID | 命令 |
| --- | ---: | ---: | ---: | --- |
| CycleTrim | `636750` | `457140` | `3766318556` | `scripts/publish_cycletrim_steam.sh` |
| OniMcp | `636750` | `457140` | `3731864673` | `scripts/publish_onimcp_steam.sh` |

两个入口都调用 `tools/OniMods.SteamPublisher/`，使用已登录 Steam 客户端的旧版 `ISteamRemoteStorage` 工坊文件 API 更新**原 Workshop ID**。原条目的 creator app 是 Oxygen Not Included Uploader (`636750`)，consumer app 才是 ONI (`457140`)。发布器从 `dist/<Mod>/` 创建单文件 ZIP，在 `636750` 上下文写入 Steam Cloud 并提交更新；它不会启动 ONI 或 OniUploader。

ONI 当前的 Steam 模组读取路径把 `ISteamUGC.GetItemInstallInfo` 返回值作为 ZIP 文件打开。普通 `SteamUGC.SetItemContent` 和 SteamCMD 的 `contentfolder` 上传会产生目录路径，ONI 因而报“下载失败”。只有 Steam 的 `LegacyItem` 状态返回文件路径。不能把“Steam 接收上传”视为兼容性验证。

## 发布前检查

```bash
scripts/publish_cycletrim_steam.sh --dry-run --allow-dirty
scripts/publish_onimcp_steam.sh --dry-run --allow-dirty
```

Dry-run 检查：

- Release 构建与警告即错误检查
- 静态契约和回归脚本
- Rust 测试
- `onim.toml` 中的固定 Workshop ID
- 双语说明、更新记录和 VDF
- Steam 发布器对 VDF 的独立解析、两个 App ID 提示文件，以及根目录包含 `mod.yaml`、`mod_info.yaml`、模组 DLL 的单文件 ZIP

VDF 写入 `dist/<Mod>.workshop.vdf`，仅用作本地元数据输入。发布包写入 `dist/<Mod>.workshop-legacy.zip`，不会包含 VDF。`--steamcmd` 会被直接拒绝。
实际发布前会分别在 `636750` 和 `457140` 两个只读进程里查询同一条目，核对 `SteamUtils.GetAppID` 的真实上下文值、条目的两个 App ID、所有者和英文标题；离线 dry-run 不能证明这些在线状态。
若只想运行这项在线身份检查，先启动 Steam 客户端，再运行 `scripts/publish_cycletrim_steam.sh --preflight-only` 或 `scripts/publish_onimcp_steam.sh --preflight-only`；该模式不构建、不上传，也不启动游戏。

## 自定义 Steam 安装路径

CLI 会读取平台默认 Steam 路径和 `steamapps/libraryfolders.vdf` 中的库目录。若 Steam 安装在未被自动发现的位置，可显式指定根目录：

```bash
ONIM_STEAM_ROOT=/path/to/Steam onim setup
```

该变量也适用于 CLI 查找 OniUploader；发布脚本使用本机 Steam 客户端。

## 无界面发布

干净工作区：

```bash
scripts/publish_cycletrim_steam.sh
scripts/publish_onimcp_steam.sh
```

明确发布未提交改动：

```bash
scripts/publish_cycletrim_steam.sh --allow-dirty
scripts/publish_onimcp_steam.sh --allow-dirty
```

脚本在需要时只启动 Steam 客户端，等待账号登录并读取目标条目。发布器分别更新 English 和 schinese 说明；预览图默认保持不变。它保留现有条目的可见性，也核对所有者、creator/consumer App ID 和标题。

旧版文件更新提交后，上传器的 Steam API 会先退出；独立子进程从含 `457140` 专用 `steam_appid.txt` 的目录启动，且在进程启动前设置对应 App 环境，以 ONI 上下文请求下载该条目，并核对 `LegacyItem` 状态、`GetItemInstallInfo` 的文件路径、ZIP 内容与刚上传的字节一致。任何一步失败都会以非零状态退出，不能宣称下载问题已修复。旧 API 能否把曾被目录上传覆盖的**现有**条目恢复为 Legacy，只能通过这项在线检查确定；Steam 文档没有保证转换能力。

## 安全限制

发布在以下情况停止：

- `onim.toml` 的 Workshop ID 与固定目标不一致
- 条目不属于 ONI App `457140`
- 条目的 creator app 不是 Oxygen Not Included Uploader `636750`
- 当前 Steam ID 不是 Workshop 所有者
- 条目标题与目标 Mod 不匹配
- 工作区有未提交改动且没有传 `--allow-dirty`
- 构建、测试、契约或 Steam API 返回错误
- 发布后 Steam 客户端无法取得与本次 ZIP 字节一致的 Legacy 文件

`--skip-tests` 只用于刚完成完整 dry-run 后的同一份发布包。

Steam API 依据：[独立上传工具与 App Publish Permissions](https://partner.steamgames.com/doc/features/workshop/implementation)、[SteamAPI App ID 初始化](https://partner.steamgames.com/doc/sdk/api)、[ISteamUGC.GetItemInstallInfo 与 LegacyItem](https://partner.steamgames.com/doc/api/ISteamUGC)、[ISteamRemoteStorage 旧版文件更新接口](https://partner.steamgames.com/doc/api/ISteamRemoteStorage)。

## OniMcp 新建私有 Legacy 候选（备用方案）

现有 OniMcp 条目 `3731864673` 已是目录模式：旧文件详情没有有效 `m_hFile` 或文件名；一次受控的旧接口更新在 `CommitPublishedFileUpdate` 返回失败。不要对该 ID 重试旧文件更新。备用流程只为 OniMcp 创建**新条目**，不会修改旧条目，也不会自行公开或替换原链接。

先在干净的待发布提交上执行完整 dry-run，生成 `dist/OniMcp.workshop.vdf` 和内容目录。然后用固定目标环境运行**离线**准备命令：

```bash
ONIM_PUBLISH_CREATOR_APP_ID=636750 \
ONIM_PUBLISH_CONSUMER_APP_ID=457140 \
ONIM_PUBLISH_WORKSHOP_ID=3731864673 \
ONIM_PUBLISH_EXPECTED_OWNER=76561199137573787 \
ONIM_PUBLISH_NAME=OniMcp \
ONIM_PUBLISH_TITLE_CONTAINS='ONI MCP Server' \
dotnet tools/OniMods.SteamPublisher/bin/Release/net10.0/OniMods.SteamPublisher.dll \
  --prepare-private-candidate --vdf "$PWD/dist/OniMcp.workshop.vdf"
```

这一步不初始化 Steam API。检查输出的固定标题 `ONI MCP Server [Private Legacy Test Candidate for 3731864673]`、Private 可见性、creator `636750`、consumer `457140`、owner、ZIP/预览 SHA256、`planSha256` 和 one-shot journal 路径。Linux 的 journal 固定为 `~/.local/state/onim/workshop-create/onimcp-legacy-candidate-from-3731864673.jsonl`（Windows 位于 LocalAppData 的 `OniMods/workshop-create`）。它跨工作区保留，不随 `dist/` 清理。

只有在复核输出并另行决定创建时，才可把同一命令改成 `--create-private-candidate --expected-plan-sha256 <刚复核的哈希> --confirm-private-create-once`，且让进程**启动前**带 `SteamAppId=636750 SteamGameId=636750`。程序会先确认原条目 ID、双 App、owner、标题；把单 ZIP 与预览分别写入并共享到上传器 Cloud，复核旧条目未变，**再在调用 `PublishWorkshopFile` 前以 CreateNew + fsync 写入 journal**。调用参数固定为 Private 和 Community。成功回调的新 ID 会进入 journal；随后程序复核新条目仍为 Private、旧条目未变，并在独立的 ONI `457140` 进程中等待下载回调、核对 `LegacyItem`、文件路径和 ZIP SHA256。

如果创建回调超时、I/O 失败、进程崩溃，或后续验证失败，**不得直接再运行创建命令**。journal 会阻止再次创建；先查看其中记录和 Steam 账号已发布的私有条目，用固定候选标题搜索是否已有新 ID，核对 owner/双 App/Private，再决定是否单独验证该 ID。只有确认没有创建且完成记录审计后，才可由维护者人工处理 journal 与新尝试。验证通过也不会自动公开候选或弃用原 ID。

## CycleTrim 0.3.4 新建私有 Legacy 候选

CycleTrim 使用另一套固定目标：原条目 `3766318556`、标题 `CycleTrim [Private Legacy Test Candidate for 3766318556]`、creator `636750`、consumer `457140`、owner `76561199137573787`，可见性固定 Private。它有独立的 one-shot journal：Linux 上是 `~/.local/state/onim/workshop-create/cycletrim-legacy-candidate-from-3766318556.jsonl`；不会读取、覆盖或解除 OniMcp 的 `onimcp-legacy-candidate-from-3731864673.jsonl`。两份计划的 ZIP、预览图及 plan SHA 分别计算。

CycleTrim 候选包必须来自经审查的 **0.3.4 安全发布工作区**，而非自动改用 main 的 CycleTrim 游戏逻辑。在该工作区先运行 `scripts/publish_cycletrim_steam.sh --dry-run` 生成 Release 内容及 `dist/CycleTrim.workshop.vdf`，再用包含此候选流程的 Publisher 构建产物，对该 VDF 执行离线 `--prepare-private-candidate`。环境需固定 `ONIM_PUBLISH_NAME=CycleTrim`、`ONIM_PUBLISH_WORKSHOP_ID=3766318556`、`ONIM_PUBLISH_TITLE_CONTAINS=CycleTrim`，以及上面的两个 App ID 和 owner。审查输出的实际 ZIP/预览 SHA、plan SHA、Private 标记和 journal 路径；**在 OniMcp 真实 ONI 安装测试通过并获得单独授权前，不运行 `--create-private-candidate`**。

## 原公开条目到新公开条目的链接迁移

OniMcp 与 CycleTrim 的分阶段工具与审阅步骤见 [steam-promotion.md](steam-promotion.md)。

新 ID 先保持 Private，完成 Steam `LegacyItem`/ZIP 哈希验证及 ONI 内真实加载测试。决定正式迁移后，先审查新条目的最终标题、双语说明、预览和版本，去掉“Test Candidate”字样，再由所有者将**新 ID**公开并回读其页面和订阅安装结果。随后在**旧公开条目**的标题、说明和公告显著位置写明新页面链接、迁移原因与版本，提醒原订阅用户自行改订新 ID；保留旧页作为入口和历史记录，观察反馈后再单独决定后续处理。当前候选代码不会自动公开、修改旧页、改订用户或删除任何条目。
