[h1]ONI MCP Server[/h1]

[b]为缺氧提供安全 MCP 访问的服务端 Mod[/b]

[h2]v0.2.4 协议与稳定性更新[/h2]
[list]
[*] 支持现代 MCP 发现与协议协商，同时保留旧版客户端兼容路径。
[*] 在请求进入游戏操作前拒绝格式错误的 HTTP/MCP 内容，并限制会话与任务的生命周期。
[*] 现代与旧版客户端都能读到当前安装的 Mod 版本。
[*] 缩短设置窗口状态文字、编辑时遮挡 Token，并加入可选的项目支持按钮。
[/list]
[b]验证说明：[/b] 自动检查与 PLib 设置界面的调用链已通过；尚未完成游戏内设置窗口的逐项点击验收。

[h2]v0.2.3 稳定性更新[/h2]
[list]
[*] 修复资源分发与 JSON-RPC 空响应，并保护工具元数据不受调用方修改。
[*] 取消超时后仍排队的游戏主线程调用，限制任务只能由所属会话访问。
[*] 原子保存设置；重新加载失败时保留上一次有效配置。
[/list]

[h2]v0.2.2 维护更新[/h2]
[list]
[*] 移除 680 条未使用 `using`，清除代码检查噪声。
[*] Release 构建以警告即错误模式通过。
[*] MCP 工具、协议与存档行为不变。
[/list]

[h2]它是什么[/h2]
[list]
[*] 在本地启动 HTTP + MCP 服务，面向 AI/客户端读取殖民地状态与执行受控操作。
[*] 提供 `oni://` 资源路径与聚合工具入口（`world_editor`、`game_control`、`building_control` 等）。
[*] 修改操作带确认、范围限制和可审计结果。
[/list]

[h2]适用场景[/h2]
[list]
[*] 小范围、可追溯的殖民地巡检与数据读取。
[*] AI 助理协作执行有限操作（暂停/调速/截图/小规模建筑与任务操作）。
[*] 与 MCP 客户端集成联动。
[/list]

[h2]安全与边界[/h2]
[list]
[*] 长时间自治应使用受限运行窗口、暂停验证和风险确认。
[*] 高风险操作要求明确确认。
[*] 建议按实际需求配置鉴权，控制网络暴露范围。
[/list]

[h2]兼容性[/h2]
[list]
[*] `1.0.0` 前 API 和参数仍可能有变动。
[*] 与 ONI 主体与 DLC 内容协同运行。
[/list]

[h2]文档[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/v0.2.4/mods/OniMcp/README.md]查看中文说明[/url]
[url=https://github.com/LIghtJUNction/OniMods/blob/v0.2.4/mods/OniMcp/README_EN.md]View English documentation[/url]

[h2]可选支持项目[/h2]
[url=https://api.lmm.best]api.lmm.best[/url] 提供可选的 AI 服务 Token，可用于支持 OniMods 后续开发。使用本 Mod 不需要访问该网站或购买服务；你可以自行选择兼容的 MCP 客户端与模型。

[h2]基准测试技能[/h2]
[list]
[*] 可在 Agent 中调用 `benchmark` 技能做固定流程性能验证。
[*] 输出格式固定为标准化 JSON（`status`,`ok`,`suite`,`suiteStartedAt`,`suiteEndedAt`,`durationMs`,`summary`,`results`）。
[*] 每条结果包含 `name`,`status`,`iterations`,`durationMs` 等字段，不通过则会返回 `error`。
[/list]
