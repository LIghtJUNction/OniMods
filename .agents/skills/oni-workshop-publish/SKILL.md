---
name: oni-workshop-publish
description: 发布或更新缺氧 (Oxygen Not Included) Mod 到 Steam 创意工坊，以及在不打开浏览器、不重传 ZIP 的情况下管理已发布条目的页面。涵盖 onim publish、onim workshop page（中英文标题与说明、整组标签、可见性、预览图、变更说明）、首次受控阶段性发布、双 App ID 上下文，以及单文件 Legacy ZIP 与禁止 SteamCMD 目录模式。
---

# 缺氧 Mod 创意工坊发布与更新指南

本技能指导 Agent 在 Oxygen Not Included 项目中安全地发布、更新创意工坊条目，并管理已发布页面。页面管理用 `onim workshop page`，不打开浏览器。

---

## 核心红线（不可妥协）

1. **绝对禁止使用 SteamCMD 进行工坊上传**：
   - 严禁运行 `steamcmd +workshop_build_item`，严禁使用带 `contentfolder` 目录模式上传。
   - 严禁调用现代 `SteamUGC.SetItemContent`（目录模式）。
   - **根本原因**：ONI 游戏客户端读取工坊 Mod 时，硬编码将 `ISteamUGC.GetItemInstallInfo` 返回路径当成单文件 `.zip` 解压读取。一旦使用目录上传，条目会被 Steam 单向转为 Directory UGC 模式，玩家订阅后报“下载失败”，且 **Steam API 永久无法回退**。
2. **禁止对已损坏的旧目录条目重试更新**：
   - 已被目录模式损坏的历史条目无法恢复为 Legacy，只能按照迁移计划新建条目。
3. **必须保持单文件 Legacy ZIP 发布路径**：
   - 唯一合法的路径是使用基于 `ISteamRemoteStorage` 的 Legacy 单文件工坊发布器。

---

## 前置环境准备

1. **Steam 客户端保持运行且已登录**：
   - 发布依赖本机登录的 Steam 账号会话。
   - 必须先确认 Steam 处于登录状态（可在后台静默运行）。
2. **.NET SDK**：
   - 发布器在初次运行时会自动编译并缓存 C# 驱动。
3. **配置文件与元数据**：
   - `onim.toml` 包含目标 Mod 的配置（`path`, `publishedfileid`, `workshop_title`）。
   - Mod 输出目录包含有效资源：`mod.yaml`（含 `staticID`）、`mod_info.yaml`、`preview.png`（或 `preview.jpg`）、以及中英文文档 `docs/steam-description-*.md`。

---

## 场景一：已有 Mod 的常规更新（纯命令行推荐）

对于已经在 `onim.toml` 中配置了有效 `publishedfileid` 的 Mod，直接使用 `onim` CLI 执行全自动发布。

### 1. 发布前验证（Dry-run）
**必须先进行 dry-run**，校验构建产物、描述长度（<= 8000 字符）、VDF 配置与单文件 ZIP 规范：
```bash
onim publish -m <ModKey> --dry-run
```

### 2. 交互式命令行发布
在终端中确认或修改变更日志后自动上传：
```bash
onim publish -m <ModKey>
```

### 3. 全自动 / 无人值守发布（CI / 脚本集成）
自动从 CHANGELOG 提取最新说明，跳过所有输入提示：
```bash
onim publish -m <ModKey> --auto-note -y
```

### 4. 备用专用脚本入口
若需要更详尽的底层日志或特定参数，可使用仓库内置的脚本：
```bash
# 通用更新入口
scripts/publish_legacy_steam.sh <ModKey> --update-existing

# 核心模组专用入口
scripts/publish_cycletrim_steam.sh
scripts/publish_onimcp_steam.sh
```

---

## 场景二：新 Mod 首次创建发布（受控阶段性流程）

为防止网络抖动或误操作在工坊产生重复、无法删除的空白废条目，**未分配 `publishedfileid` 的新 Mod 必须执行严格的分阶段流程**，禁止随意一键生成新条目。

流程遵循：**计划准备 -> 离线复核 -> 单次创建 Private 条目 -> 实机下载验证 -> 正式公开**。

### 阶段 1：准备不可变计划（离线）
```bash
scripts/publish_legacy_steam.sh <ModKey> --prepare-first-item --plan dist/<ModKey>.first-publication-plan.json
```
- 该阶段只生成 Legacy ZIP 与计划 JSON，**不调用 Steam API**。
- 输出计划包含标题、双语描述、ZIP 大小与 SHA256 哈希。

### 阶段 2：复核 SHA256 并创建私有条目（仅限一次）
计算计划文件的 SHA256，核对无误后发起创建：
```bash
PLAN_HASH=$(sha256sum dist/<ModKey>.first-publication-plan.json | awk '{print $1}')

scripts/publish_legacy_steam.sh <ModKey> --create-first-item \
  --plan dist/<ModKey>.first-publication-plan.json \
  --expected-plan-sha256 "$PLAN_HASH"
```
- 条目初始状态为 **Private**。
- 创建日志记录在安全 Journal 中，防止重复重试。

### 阶段 3：切换 Consumer 上下文实机安装验证
```bash
scripts/publish_legacy_steam.sh <ModKey> --verify-first-item \
  --plan dist/<ModKey>.first-publication-plan.json \
  --expected-plan-sha256 "$PLAN_HASH"
```
- 发布器在游戏 App ID (`457140`) 上下文模拟玩家订阅，触发 Steam 下载。
- 校验下载文件是否为单文件 Legacy ZIP，且字节与上传包 100% 吻合。

### 阶段 4：正式公开与双语页面提交
验证成功后，设为 Public 并提交多语言页面：
```bash
scripts/publish_legacy_steam.sh <ModKey> --publish-first-item \
  --plan dist/<ModKey>.first-publication-plan.json \
  --expected-plan-sha256 "$PLAN_HASH"
```

### 阶段 5：登记 ID 到 `onim.toml`
将终端返回的 `Workshop ID` 写入 `onim.toml` 的对应配置块中：
```toml
[mods.<ModKey>]
path = "mods/<ModKey>"
publishedfileid = "<新返回的ID>"
workshop_title = "<标题>"
```
后续该 Mod 的所有更新均可切换到 **场景一** 的一键命令。

---

## 场景三：已发布条目的页面管理（不打开浏览器）

`onim publish` 更新的是 Legacy ZIP。工坊页面的文字、标签、可见性和预览图用 `onim workshop page`，它不替换 ZIP，也不要求先关掉游戏。Steam 客户端需已登录，且当前账号必须是该条目的所有者。若设置了 `ONIM_PUBLISH_EXPECTED_OWNER`，登录账号还须与它一致。

先读当前页面：

```bash
onim workshop page show <WorkshopID>
```

再按需修改。说明从 UTF-8 文件读取，长度 1–8000；标题 1–128。`--tags` 会替换全部标签，不能只追加一项。可见性为 `public`、`friends`、`private` 或 `unlisted`。预览图为 png、jpg 或 jpeg，1 字节到 10 MiB。

```bash
onim workshop page set <WorkshopID> --title "标题" --tags "tweaks,Spaced Out!" --visibility public
onim workshop page set <WorkshopID> --description-file docs/steam-description-en.md --chinese-description-file docs/steam-description-zh.md
onim workshop page set <WorkshopID> --chinese-title "中文标题" --preview preview.png --change-note "更新说明"
```

约束：

- 文本、标签和可见性走 Consumer App ID `457140` 的 `StartItemUpdate`，只调用 `SetItemTitle`、`SetItemDescription`、`SetItemTags`、`SetItemVisibility`。禁止 `SetItemContent`，也禁止 `--content`、`--contentfolder`、`--steamcmd`、`--file`。更新 ZIP 仍走场景一。
- 预览图走 Creator App ID `636750`，只调用 Legacy `UpdatePublishedFilePreviewFile`，不调用 `UpdatePublishedFileFile`。
- 只改英文标题、说明、标签或可见性时，不把当前英文正文重新提交，避免被 Steam 截断后写短。只改中文标题或说明的一侧时，另一侧从当前简体页面补齐。
- 提交后回读最多 60 秒：Legacy 文件句柄仍在，ZIP 字节数不变，所改字段与提交值一致。文件大小变化或句柄消失是失败，不能重试成目录模式。
- 讨论区、截图画廊、YouTube、依赖关系和删除条目仍只能在 Steam 客户端处理。

## 场景四：GUI 兼容回退（OniUploader）

如果因特殊原因需要使用官方图形界面：
```bash
onim publish -m <ModKey> --gui
```
- 此时 `onim` 会启动本地 Steam 安装的 `OxygenNotIncludedUploader` GUI，并弹出文件管理器定位到包含预览图与产物的目录。

---

## 常见问题与排查

1. **`Steam client is not running` / `SteamAPI.Init failed`**：
   - 确保 Steam 客户端已在当前桌面会话中启动并且账号处于登录状态。
2. **`Steam initialized with App ID xxx, expected 636750`**：
   - 检查环境变量是否被污染。发布时 Creator AppID 必须为 `636750`（Oxygen Not Included Uploader），Consumer 验证时必须为 `457140`。
3. **`Steam 描述超过 8000 字符`**：
   - Steam 工坊单语言描述上限为 8000 字符。请精简 `docs/steam-description-*.md`。
4. **`Workshop ID 0` 拒绝发布**：
   - 现有条目更新要求 `publishedfileid` 必须为有效正整数。新建 Mod 请走场景二的阶段性流程。
5. **`does not own this Workshop item`**：
   - 页面读写都要求当前登录账号是条目所有者。换到拥有该条目的 Steam 账号后再执行 `onim workshop page`。
6. **`Workshop file size changed` 或 `no Legacy file handle`**：
   - 页面编辑不应改变 ZIP。立即停止，不要改用 SteamCMD 或 `SetItemContent` 补救。先用 `onim workshop page show <id>` 确认 `legacyFile=true` 和原来的 `fileBytes`。
