# ONI MCP Server

[![ONI MCP Server 预览图](preview.png)](README_EN.md)

ONI MCP Server 是《缺氧》Mod：启动本地 MCP 服务（`http://localhost:8788/mcp/`），提供 `oni://` 资源读取与受控写入入口，面向 AI 客户端做安全联动。

## 快速索引

- [用途与边界](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md#用途与边界)
- [安装与启动](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md#安装与启动)
- [运行端点与客户端](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md#运行端点与客户端)
- [配置与鉴权](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md#配置与鉴权)
- [主要工具组](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md#主要工具组)
- [常见链接与资源](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md#常见链接与资源)
- [兼容性与稳定性](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md#兼容性与稳定性)
- [更新与验证](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md#更新与验证)
- [参考与鸣谢](https://github.com/LIghtJUNction/OniMods/blob/main/mods/OniMcp/README.md#参考与鸣谢)

## 用途与边界

### 能做什么

- 读取殖民地状态、告警、资源、地图等数据。
- 通过聚合工具执行暂停、调速、截图、任务、建筑和设施类小范围操作。
- 供 MCP 客户端做可追溯、可确认的游戏状态交互。

### 不适合做什么

- 不适合作为完整自动化玩家使用。
- 大规模挖掘、拆除、重开/加载存档这类高风险操作建议由客户端进行显式确认后执行。

## 安装与启动

1. 订阅本 Mod 并在游戏 **Mods** 菜单启用 **ONI MCP Server**。
2. 重启游戏后进入主菜单或殖民地，Mod 自动提供 MCP 服务。
3. `oni://` 资源类工具依赖已加载殖民地；主菜单下仅部分只读接口可用。

## 运行端点与客户端

- 浏览器反馈页: `http://localhost:8788/`
- MCP 端点: `http://localhost:8788/mcp/`
- 示例配置参考: [docs/api-developer-guide.md](../../docs/api-developer-guide.md)
- 资源列表: [docs/mcp-tools-reference.md](../../docs/mcp-tools-reference.md)

## 配置与鉴权

- 配置文件: `OniMcpConfig.json`
- 常见字段、默认值与优先路径见: [mods/OniMcp/ModInfo.cs](ModInfo.cs)
- `localhost`、`127.0.0.1`、`::1` 保持默认无 token 的本机开发体验；默认 `AuthEnabled=false`。
- 非 loopback 地址（包括 `0.0.0.0`、局域网 IP 和远程主机名）必须启用 `AuthEnabled=true`，否则配置保存/监听会 fail closed，不会启动网络监听。
- Bearer token 只提供认证，不提供传输加密。直接远程 `http://` 会明文传输 token；远程访问应优先使用可信 VPN/隧道，或由 TLS 反向代理终止 HTTPS，并让 OniMcp 上游保持 loopback/local。
- 安全迁移只记录迁移版本，不会静默把旧配置改成更宽的网络监听；已开启认证的旧配置仍要求客户端携带 token。
- 修改配置后点击 **Restart MCP server** 或重启游戏生效。

## 主要工具组

- 工具清单入口: `oni://tools/manifest` / `server_control domain=catalog action=manifest`
- 常用公开工具:
  - `world_editor`：虚拟文件化世界读写（`cd`、`ls`、`read`、`search`、`edit`）
  - `game_control`：游戏控制与状态管理
  - `navigation_control`：视图、覆盖层和截图
  - `building_control`：建筑与产线相关操作
  - `orders_control`：挖掘、清扫、拖地、拆除等任务
  - `server_control`：服务状态、日志与截图任务

## 常见链接与资源

- `oni://tools/manifest`
- `oni://tools/guide`
- `oni://colony/status`
- `oni://dupes/status-check`
- `oni://world/text-map`
- `oni://buildings/defs`
- `oni://resources/inventory`
- [scripts/verify_onimcp_tool_surface.py](../../scripts/verify_onimcp_tool_surface.py)
- [CHANGELOG.md](CHANGELOG.md)

## 兼容性与稳定性

- 在 `1.0.0` 之前，工具名、参数和响应结构可能发生不兼容改动。
- 外部客户端请固定版本，并优先以运行时 manifest 为准。
- `2025-11-25` / `2025-06-18` 客户端继续使用 `initialize` + `Mcp-Session-Id` 的完整工具路径。
- `2026-07-28` 提供无会话兼容：`server/discover`、`resources/list`、`resources/templates/list`、`resources/read`，以及一个刻意收窄的工具切片：`tools/list` 只广告只读 `benchmark`，`tools/call` 也只允许调用该工具。该路径要求每个请求携带现代 `_meta`、`Mcp-Protocol-Version`、`Mcp-Method`；资源读取和工具调用还要求匹配的 `Mcp-Name`。它不会创建或返回 `Mcp-Session-Id`。
- 现代路径不会暴露 2025 core Tasks 字段，也暂不广告 Tasks、MRTR、订阅或会改变游戏状态的工具。其他工具继续走 2025 会话路径，直到完成 request-scoped 状态和对应的 2026 header/schema 验证。

## 更新与验证

- 版本历史与兼容说明: [CHANGELOG.md](CHANGELOG.md)
- 需要快速核对契约时，先跑: [scripts/verify_onimcp_tool_surface.py](../../scripts/verify_onimcp_tool_surface.py)

## 参考与鸣谢

- 源码仓库: [LIghtJUNction/OniMods](https://github.com/LIghtJUNction/OniMods)
- MCP 生态: [modelcontextprotocol](https://github.com/modelcontextprotocol)
- 相关框架与参考: [FastTrack](https://github.com/peterhaneve/FastTrack)、[Harmony](https://github.com/pardeike/Harmony)
- 共同开发/测试: gpt5.5、gpt5.6、glm5.2、Kimi k2.6、Kimi k3、Gemini 3.5 Flash、grok4.5、LIghtJUNction
