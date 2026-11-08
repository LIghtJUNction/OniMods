---
name: oni-workshop-publish
description: 更新《缺氧》Steam 创意工坊条目的页面或通过项目批准的 Legacy ZIP 流程发布模组。
---

# 创意工坊发布

Steam 客户端须登录条目所有者账号。核对 `onim.toml`、当前条目 ID、目标字段、授权和构建产物；不要泄露凭据。

**禁止** SteamCMD 的 `workshop_build_item`、任何目录模式上传和 `SteamUGC.SetItemContent`。这些操作会不可逆地破坏 ONI 的单文件安装路径。已转换的旧条目不能恢复，遵循 `docs/steam-promotion.md`，不要重试其文件更新。

## 页面文字和元数据

先 `onim workshop page show <ID>`，确认所有权、`legacyFile=true` 和文件大小。仅按需使用 `onim workshop page set` 更新：

```bash
onim workshop page set <ID> --description-file <en-file> --chinese-description-file <zh-file> --change-note <note>
```

中英文描述为 UTF-8、每种 1–8000 字符；标题 1–128 字符。`--tags` 替换整组标签，visibility 可为 public/friends/private/unlisted。预览图须为 1 字节至 10 MiB 的 PNG/JPEG。只改中文一侧时保留当前中文另一字段；只改英文标题、标签或可见性时不要重交被截断的英文正文。页面更新只改元数据，不上传内容；预览图走 Legacy 预览 API。

提交后最多等待 60 秒并回读：字段一致、Legacy 文件句柄仍在且 ZIP 字节数不变，否则报告失败，绝不改用目录上传。截图画廊、讨论区、依赖关系和删除不由此命令处理。

## Legacy ZIP 更新

仓库允许的无人值守发布路径是 `scripts/publish_cycletrim_steam.sh` 和 `scripts/publish_onimcp_steam.sh` 等单文件 Legacy 发布器；遵守项目当前脚本和 `docs/steam-promotion.md`。CLI 的 `onim publish` 生成 VDF 并交由 OniUploader GUI，不得假定它是无人值守发布器。先构建并执行 `onim publish -m <ModKey> --dry-run`，核对 ZIP、元数据和描述，再通过批准脚本提交；完成后区分构建、上传、订阅下载和实机验证。

## 新条目

没有 ID 时，先用 `scripts/publish_legacy_steam.sh <ModKey> --prepare-first-item --plan <path>` 生成离线计划，核对计划和 SHA256；用 `--create-first-item --plan <path> --expected-plan-sha256 <hash>` 仅创建一次私有条目。然后依次使用 `--verify-first-item` 和 `--publish-first-item`，各自传入相同计划及 SHA256；验证 Consumer App 457140 下载的 Legacy 单文件与计划一致后再公开，最后登记 ID。失败时检查 journal，避免重复创建。
