[h1]ONI MCP Server[/h1]

[b]为缺氧提供安全 MCP 访问的服务端 Mod[/b]

[h2]它是什么[/h2]
[list]
[*] 在本地启动 HTTP + MCP 服务，面向 AI/客户端读取殖民地状态与执行受控操作。
[*] 提供 `oni://` 资源路径与聚合工具入口（`world_editor`、`game_control`、`building_control` 等）。
[*] 设计目标：受控修改、可审计、可确认的自动化协作。
[/list]

[h2]适用场景[/h2]
[list]
[*] 小范围、可追溯的殖民地巡检与数据读取。
[*] AI 助理协作执行有限操作（暂停/调速/截图/小规模建筑与任务操作）。
[*] 与 MCP 客户端集成联动。
[/list]

[h2]安全与边界[/h2]
[list]
[*] 不适合用于完全无人化长期自治。
[*] 高风险操作应明确确认，默认偏向安全约束和手动审批。
[*] 建议按实际需求配置鉴权，控制网络暴露范围。
[/list]

[h2]兼容性[/h2]
[list]
[*] `1.0.0` 前 API 和参数仍可能有变动。
[*] 与 ONI 主体与 DLC 内容协同运行。
[/list]

[h2]文档[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/oni_mcp/README.md]查看中文说明[/url]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/oni_mcp/README_EN.md]View English documentation[/url]

[h2]基准测试技能[/h2]
[list]
[*] 可在 Agent 中调用 `benchmark` 技能做固定流程性能验证。
[*] 输出格式固定为标准化 JSON（`status`,`ok`,`suite`,`suiteStartedAt`,`suiteEndedAt`,`durationMs`,`summary`,`results`）。
[*] 每条结果包含 `name`,`status`,`iterations`,`durationMs` 等字段，不通过则会返回 `error`。
[/list]
