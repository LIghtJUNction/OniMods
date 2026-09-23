# Steam Workshop 发布

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
