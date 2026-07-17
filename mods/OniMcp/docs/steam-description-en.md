[h1]ONI MCP Server[/h1]

[b]A local MCP bridge mod for Oxygen Not Included[/b]

[h2]What this mod does[/h2]
[list]
[*] Starts a local MCP-compatible service for colony state access and safe operations.
[*] Exposes `oni://` resources and grouped tool entrypoints (`world_editor`, `game_control`, `building_control`, etc.).
[*] Designed for auditable and confirmation-oriented automation workflows with AI clients.
[/list]

[h2]Typical use cases[/h2]
[list]
[*] Colony snapshots and environment inspection.
[*] Small-scale safe automation (pause/speed/screenshots/small build or maintenance actions).
[*] MCP client integration for structured gameplay workflows.
[/list]

[h2]Safety / Scope[/h2]
[list]
[*] Not intended for full autonomous long-run gameplay.
[*] High-risk actions should require explicit confirmation.
[*] Recommended to use network/auth boundaries when exposed beyond local loopback.
[/list]

[h2]Compatibility[/h2]
[list]
[*] Tool names and payloads may still change before `1.0.0`.
[*] Built for Oxygen base game + DLC environment usage.
[/list]

[h2]Documentation[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README_EN.md]Read English docs[/url]
[url=https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md]查看中文文档[/url]

[h2]Benchmark skill[/h2]
[list]
[*] Use `benchmark` tool in agents for fixed-path perf validation.
[*] Output format is a standardized JSON object (`status`,`ok`,`suite`,`suiteStartedAt`,`suiteEndedAt`,`durationMs`,`summary`,`results`).
[*] Example keys in each result: `name`,`status`,`iterations`,`durationMs`, plus item-specific metrics and optional `error`.
[/list]
