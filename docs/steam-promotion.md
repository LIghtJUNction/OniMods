# OniMcp 创意工坊公开迁移

此流程仅适用于原公开 ID `3731864673` 和已通过 ONI 实机加载的私有 Legacy ID `3806839864`。新条目的 ZIP 必须与创建 journal 记录的计划一致；原条目始终保留，不上传或删除其内容。三个在线写入阶段各自有独立的持久意图记录，回调或回读不确定时停止，不自动重试。

## 审查计划

1. 从已验收的 OniMcp 0.2.5 发布包保留 `dist/OniMcp.workshop.vdf`、`dist/OniMcp.workshop-legacy.zip` 和预览图。不要重新构建后沿用旧计划 SHA。
2. 使用上传器 App `636750` 的 Steam 客户端上下文运行 `--capture-promotion-snapshot --expected-new-id 3806839864 --output <snapshot.json>`。这是**只读 Steam 查询**，保存新旧条目的英/简中标题、完整说明、tags、可见性、文件大小与更新时间。新项此时必须是 Private。
3. 运行离线 `--prepare-promotion --vdf <0.2.5 VDF> --zip <已验证 ZIP> --snapshot <snapshot.json> --expected-new-id 3806839864 --output <plan.json>`。该命令读取 `~/.local/state/onim/workshop-create/onimcp-legacy-candidate-from-3731864673.jsonl`，要求唯一成功创建回调与消费端验证记录，重新计算创建 plan SHA 和 ZIP SHA，再输出结构化计划、完整英/简中 before/after 审阅文件、计划 SHA 与阶段 journal 路径。它**不初始化 Steam API**。

拟定的新标题是 `ONI MCP Server (Early Access)`，双语正式说明逐字取自 0.2.5 发布包，tags 沿用旧页的 `Base Game`、`Spaced Out!`、`The Bionic Booster Pack`、`The Frosty Planet Pack`、`New Features`。旧标题拟为 `ONI MCP Server (Moved to Workshop 3806839864)`；旧页英/简中说明顶部添加新 URL、原条目的 Steam 内容格式无法被 ONI 安装、需手动取消旧订阅并订阅新项的提示，原正文接在其后逐字保留。审阅文件会将**拟上线 after 放在前面**；私有候选目前没有简中说明时，before 会明确标成 Steam 的英文回退。

## 分阶段上线门禁

以下命令模式只在计划和文案获准后使用，每次都必须携带 `--plan <plan.json> --expected-plan-sha256 <审过的计划 SHA> --confirm-promotion-stage`。父进程为每个写阶段启动独立的 **Consumer `457140`** 进程，进程启动前设置 App 环境并使用 `consumer/steam_appid.txt`；UGC 元数据写入沿用已验证的游戏 App 上下文。Creator `636750` 仅用于只读基线查询。每阶段调用前核对 creator `636750`、consumer `457140`、owner `76561199137573787`、新旧 ID、页面现状和计划 ZIP SHA；阶段意图先写入 `~/.local/state/onim/workshop-create/promotion/onimcp-3731864673-to-3806839864.jsonl` 并落盘。

1. `--stage-private-metadata`：只更新**新私有项**的正式英文标题/说明、五个 tags，再更新简中标题/说明。各次提交等待有界回调和语言回读；确认仍为 Private、ZIP 大小不变，再在 ONI `457140` 独立进程验证 `LegacyItem`、安装文件路径与 ZIP SHA。旧项的标题、说明和内容保持原样。
2. `--publish-promoted-item`：只在上一步 journal 验证完成后，把新项可见性改为 Public。先回读 Steam 元数据和 ONI 安装 ZIP，再等待无密钥的公开 [GetPublishedFileDetails Web API](https://partner.steamgames.com/doc/webapi/ISteamRemoteStorage) 返回 result `1`、正确 ID/owner/双 App/正式英文标题/说明/tags。公开 API 对 Private 项返回不可见，不能用创建回调代替公开回读。
3. `--link-old-item`：只在新项公开 Web API 回读通过后，给**旧公开项**设置迁移标题与英/简中说明顶部提示。每次提交等待回调；回读完整正文、原 tags、Public 可见性和未变的旧内容大小，再用公开 Web API 核对旧页的新链接。该阶段不调用 `SetItemContent`，不删除旧项，也不自动改订用户。

任一阶段失败或超时后，阶段 journal 仍保留意图并阻止同一阶段自动重试。先核查 Steam 账号页、公开 API、旧/新安装状态与 journal，再由维护者决定人工恢复。计划 SHA、ZIP 或页面基线变化时必须重新生成计划并复审。上述工具不会自动执行下一阶段。
