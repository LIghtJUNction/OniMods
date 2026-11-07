# oni-mcp CLI

oni-mcp is a standalone Rust client for the Streamable HTTP endpoint exposed by mods/OniMcp. It complements the existing onim mod-development workflow and does not require a running Rust daemon.

## Install

Download the platform archive from [GitHub Releases](https://github.com/LIghtJUNction/OniMods/releases), extract the binary, and add it to PATH. The CLI uses the system curl executable for HTTP(S), so curl must be installed.

## Configure

```sh
oni-mcp set-url http://localhost:8788/mcp/
oni-mcp set-secret "$ONIM_MCP_SECRET"
oni-mcp status
```

The default endpoint is http://localhost:8788/mcp/. Configuration is stored at ~/.config/onim/mcp.toml (Unix mode 0600). ONIM_MCP_SECRET overrides the stored secret. The secret is never printed by oni-mcp config.

For remote access, use HTTPS or a trusted VPN/tunnel. Bearer tokens sent over plain HTTP are not encrypted. The server remains responsible for authentication and authorization.

## Commands

| Command | Purpose |
| --- | --- |
| oni-mcp config | Show endpoint and whether a secret is configured |
| oni-mcp set-url <url> | Set MCP endpoint |
| oni-mcp set-secret <secret> | Store MCP secret |
| oni-mcp clear-secret | Remove stored secret |
| oni-mcp status | Initialize and ping the server |
| oni-mcp tools | List live MCP tools |
| oni-mcp call <name> '<json>' | Call a tool |
| oni-mcp read <uri> | Read an MCP resource |

Example:

```sh
oni-mcp tools
oni-mcp call game_control '{"domain":"speed","action":"time"}'
oni-mcp read oni://tools/manifest
```

Each invocation creates an MCP session using protocol 2025-11-25, performs one operation, and exits. Session IDs are not persisted. Use the live tool manifest because tool schemas evolve with the mod.

## Releases

Tags named oni-mcp-v* trigger multi-platform builds for Linux x86_64/aarch64, Windows x86_64, and macOS x86_64/aarch64. Each archive includes the executable and SHA-256 checksums are published with the release.
