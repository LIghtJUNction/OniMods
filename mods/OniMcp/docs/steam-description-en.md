[b]Required mod: Jlib.[/b] Subscribe to and enable Jlib in the game mod menu, then restart ONI. This mod uses the shared prerequisite instead of embedding its own copy.

[h1]ONI MCP Server[/h1]

[b]Model Context Protocol (MCP) Server for Oxygen Not Included[/b]

Connect frontier LLMs (Claude, Gemini, Cursor) to Oxygen Not Included for real-time colony telemetry, engineering diagnostics, and safe automation.

[h2]Required Prerequisite: Jlib[/h2]
This mod requires [b]Jlib[/b] (Steam Workshop ID: [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3835083839]3835083839[/url]).
Please subscribe to Jlib and place it above ONI MCP Server in your in-game Mod Manager.

[h2]Core Features[/h2]
[list]
[*] [b]Standard MCP Compliance[/b]: Native support for the Model Context Protocol, working seamlessly with Claude Desktop, Cursor, Gemini Antigravity, and Codex.
[*] [b]Colony Telemetry[/b]: Exposes spatial grids, thermal fields, piping, electrical circuits, and duplicant vitals via virtual resource endpoints (oni://).
[*] [b]Intelligent Diagnostics[/b]: Leverages LLM reasoning to diagnose pipe blockages, thermal runaway, resource shortages, and automation logic bugs.
[*] [b]Strict Safety Sandbox[/b]: Read-first design with bounded command execution and audit logging to ensure fair, safe gameplay.
[*] [b]Non-Blocking Architecture[/b]: Asynchronous transport handling guarantees that heavy AI telemetry never stalls the game's simulation thread.
[/list]

[h2]Quick Start[/h2]
[olist]
[*] [b]Enable Mods[/b]: Subscribe to Jlib and ONI MCP Server, then enable both in the game's Mod Menu.
[*] [b]Load Colony[/b]: Start or load a save. The local HTTP/MCP server starts automatically (default port: 8788).
[*] [b]Connect Client[/b]: Configure your AI client's MCP settings to point to the server endpoint.
[/olist]

[h2]AI Disclosure[/h2]
Protocol integration, virtual filesystem abstractions, and tool schemas were developed with assistance from frontier AI models including GPT-6, Astra, and Opus 5.5. Documentation refined by Gemini.

[h2]Source Code[/h2]
[url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniMcp]GitHub Repository[/url]
