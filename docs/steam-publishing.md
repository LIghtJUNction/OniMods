# Steam Workshop 发布

仓库提供两个固定目标的发布入口：

| Mod | ONI App ID | Workshop ID | 命令 |
| --- | ---: | ---: | --- |
| CycleTrim | `457140` | `3766318556` | `scripts/publish_cycletrim_steam.sh` |
| OniMcp | `457140` | `3731864673` | `scripts/publish_onimcp_steam.sh` |

两个入口都调用 `tools/OniMods.SteamPublisher/`，使用已登录 Steam 客户端的旧版 `ISteamRemoteStorage` 工坊文件 API 更新**原 Workshop ID**。发布器从 `dist/<Mod>/` 创建单文件 ZIP；它不会启动 ONI 或 OniUploader。

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
- Steam 发布器对 VDF 的独立解析，以及根目录包含 `mod.yaml`、`mod_info.yaml`、模组 DLL 的单文件 ZIP

VDF 写入 `dist/<Mod>.workshop.vdf`，仅用作本地元数据输入。发布包写入 `dist/<Mod>.workshop-legacy.zip`，不会包含 VDF。`--steamcmd` 会被直接拒绝。
实际发布前才会向 Steam 查询并核对 App ID、所有者和标题；离线 dry-run 不能证明这些在线状态。

## 自定义 Steam 安装路径

CLI 会读取平台默认 Steam 路径和 `steamapps/libraryfolders.vdf` 中的库目录。若 Steam 安装在未被自动发现的位置，可显式指定根目录：

```bash
ONIM_STEAM_ROOT=/path/to/Steam onim setup
```

该变量也适用于 CLI 查找 OniUploader；发布脚本本身仍使用 `STEAMCMD` 或系统中的 SteamCMD 路径。

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

脚本在需要时只启动 Steam 客户端，等待账号登录并读取目标条目。发布器分别更新 English 和 schinese 说明；预览图默认保持不变。它保留现有条目的可见性，也核对所有者、游戏 App ID 和标题。

旧版文件更新提交后，发布器要求 Steam 客户端下载该条目，并核对 `LegacyItem` 状态、`GetItemInstallInfo` 的文件路径、ZIP 内容与刚上传的字节一致。任何一步失败都会以非零状态退出，不能宣称下载问题已修复。旧 API 能否把曾被目录上传覆盖的**现有**条目恢复为 Legacy，只能通过这项在线检查确定；Steam 文档没有保证转换能力。

## 安全限制

发布在以下情况停止：

- `onim.toml` 的 Workshop ID 与固定目标不一致
- 条目不属于 ONI App `457140`
- 当前 Steam ID 不是 Workshop 所有者
- 条目标题与目标 Mod 不匹配
- 工作区有未提交改动且没有传 `--allow-dirty`
- 构建、测试、契约或 Steam API 返回错误
- 发布后 Steam 客户端无法取得与本次 ZIP 字节一致的 Legacy 文件

`--skip-tests` 只用于刚完成完整 dry-run 后的同一份发布包。

Steam API 依据：[ISteamUGC.GetItemInstallInfo 与 LegacyItem](https://partner.steamgames.com/doc/api/ISteamUGC)、[ISteamRemoteStorage 旧版文件更新接口](https://partner.steamgames.com/doc/api/ISteamRemoteStorage)。
