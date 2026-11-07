---
name: oni-mcp-cli
description: "通过 Rust oni-mcp CLI 连接正在运行的 OniMcp 游戏服务器，配置 endpoint/secret，发现和调用 MCP tools、读取 resources。"
---

# OniMcp CLI

Use oni-mcp to access an already-running OniMcp server over MCP Streamable HTTP. The CLI does not start Oxygen Not Included or bypass server authorization and game-context checks.

## Install

Download the archive matching the host OS/architecture from:
https://github.com/LIghtJUNction/OniMods/releases

Extract oni-mcp (or oni-mcp.exe) and put it on PATH. curl must also be installed and available on PATH.

## Configure

Default endpoint: http://localhost:8788/mcp/.

```sh
oni-mcp set-url http://localhost:8788/mcp/
oni-mcp set-secret "$ONIM_MCP_SECRET"
oni-mcp config
oni-mcp status
```

set-secret stores the secret in ~/.config/onim/mcp.toml (Unix permissions 0600). Prefer ONIM_MCP_SECRET for CI or ephemeral use; it overrides the stored secret. Never put real secrets in shell history, issue text, logs, screenshots, or repository files. config only reports whether a secret exists.

For remote servers, use HTTPS or a trusted VPN/tunnel. Bearer authentication over plain HTTP does not encrypt the secret. The CLI does not weaken OniMcp network binding rules.

## MCP operations

```sh
oni-mcp tools
oni-mcp call game_control '{"domain":"speed","action":"time"}'
oni-mcp read oni://tools/manifest
```

tools fetches the live tools/list manifest. Use the exact tool name and JSON arguments returned by the running server; schemas can change between mod versions. call accepts one JSON object. read accepts an MCP resource URI.

Each command creates a short-lived MCP session using protocol 2025-11-25, sends notifications/initialized, performs the operation, and exits. Session IDs are not persisted. Server authorization, admission limits, game readiness, and tool safety policies remain authoritative.

## Troubleshooting

- curl not found: install curl and ensure it is on PATH.
- HTTP 401: check ONIM_MCP_SECRET and the stored secret; confirm server authentication is enabled and its token matches.
- Connection refused: start ONI, enable OniMcp, verify the configured listen address and port.
- Unsupported protocol: update the mod and CLI together; supported protocol is 2025-11-25.
- Tool errors: run oni-mcp tools and use the exact live schema. Do not guess hidden or retired tools.

Do not use this skill to perform destructive game operations without explicit user authorization.
