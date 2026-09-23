[h1]ONI MCP Server[/h1]

[b]A local MCP bridge mod for Oxygen Not Included[/b]

[h2]v0.2.4 protocol and reliability update[/h2]
[list]
[*] Support modern MCP discovery and protocol negotiation while retaining legacy client compatibility.
[*] Reject malformed HTTP and MCP requests before they reach game actions; bound session and task lifetimes.
[*] Report the installed mod version consistently to both modern and legacy clients.
[/list]

[h2]v0.2.3 stability update[/h2]
[list]
[*] Fix resource dispatch and JSON-RPC null responses, and keep tool metadata isolated from callers.
[*] Cancel timed-out queued game-thread calls and protect session-owned tasks.
[*] Save settings atomically and preserve the last valid settings if a reload fails.
[/list]

[h2]v0.2.2 maintenance update[/h2]
[list]
[*] Remove 680 unused `using` directives and clear code-inspection noise.
[*] Pass the Release build with warnings treated as errors.
[*] MCP tools, protocol behavior, and save behavior are unchanged.
[/list]

[h2]What this mod does[/h2]
[list]
[*] Starts a local MCP-compatible service for colony state access and safe operations.
[*] Exposes `oni://` resources and grouped tool entrypoints (`world_editor`, `game_control`, `building_control`, etc.).
[*] Returns bounded, auditable results and requires confirmation for risky changes.
[/list]

[h2]Typical use cases[/h2]
[list]
[*] Colony snapshots and environment inspection.
[*] Small-scale safe automation (pause/speed/screenshots/small build or maintenance actions).
[*] MCP client integration for structured gameplay workflows.
[/list]

[h2]Safety / Scope[/h2]
[list]
[*] Long autonomous sessions should use bounded run windows, pause-and-verify loops, and risk confirmation.
[*] High-risk actions require explicit confirmation.
[*] Recommended to use network/auth boundaries when exposed beyond local loopback.
[/list]

[h2]Compatibility[/h2]
[list]
[*] Tool names and payloads may still change before `1.0.0`.
[*] Built for Oxygen base game + DLC environment usage.
[/list]

[h2]Documentation[/h2]
[url=https://github.com/LIghtJUNction/OniMods/blob/v0.2.4/mods/OniMcp/README_EN.md]Read English docs[/url]
[url=https://github.com/LIghtJUNction/OniMods/blob/v0.2.4/mods/OniMcp/README.md]查看中文文档[/url]

[h2]Optional project support[/h2]
[url=https://api.lmm.best]api.lmm.best[/url] offers optional AI service tokens that support OniMods development. The mod does not require this site or a purchase; you can use a compatible MCP client and model of your choice.

[h2]Benchmark skill[/h2]
[list]
[*] Use `benchmark` tool in agents for fixed-path perf validation.
[*] Output format is a standardized JSON object (`status`,`ok`,`suite`,`suiteStartedAt`,`suiteEndedAt`,`durationMs`,`summary`,`results`).
[*] Example keys in each result: `name`,`status`,`iterations`,`durationMs`, plus item-specific metrics and optional `error`.
[/list]
