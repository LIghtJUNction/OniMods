# Steam Workshop 发布

仓库提供两个固定目标的发布入口：

| Mod | ONI App ID | Workshop ID | 命令 |
| --- | ---: | ---: | --- |
| CycleTrim | `457140` | `3766318556` | `scripts/publish_cycletrim_steam.sh` |
| OniMcp | `457140` | `3731864673` | `scripts/publish_onimcp_steam.sh` |

两个入口都调用 `tools/OniMods.SteamPublisher/`。默认传输使用已登录 Steam 客户端的 UGC API，不启动 ONI，也不打开 OniUploader。

## 发布前检查

```bash
scripts/publish_cycletrim_steam.sh --dry-run --allow-dirty
scripts/publish_onimcp_steam.sh --dry-run --allow-dirty
```

Dry-run 检查：

- Release 构建与警告即错误检查
- 静态契约和回归脚本
- Rust 测试
- Workshop App、条目、所有者和标题
- 双语说明、更新记录和 VDF
- Steam 发布器对 VDF 的独立解析

VDF 写入 `dist/<Mod>.workshop.vdf`，不会进入 Mod 内容目录。

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

脚本在需要时只启动 Steam 客户端，等待账号登录并读取目标条目。发布器分别更新 English 和 schinese 说明；预览图默认保持不变。

## SteamCMD 备用路径

```bash
scripts/publish_cycletrim_steam.sh --steamcmd
scripts/publish_onimcp_steam.sh --steamcmd
```

SteamCMD 首次使用需要一次终端登录：

```bash
scripts/publish_cycletrim_steam.sh --login
```

SteamCMD 在终端读取密码和 Steam Guard 验证码并缓存令牌。脚本不读取、不保存密码。

## 安全限制

发布在以下情况停止：

- `onim.toml` 的 Workshop ID 与固定目标不一致
- 条目不属于 ONI App `457140`
- 当前 Steam ID 不是 Workshop 所有者
- 条目标题与目标 Mod 不匹配
- 工作区有未提交改动且没有传 `--allow-dirty`
- 构建、测试、契约或 Steam API 返回错误

`--skip-tests` 只用于刚完成完整 dry-run 后的同一份发布包。
