# ONI MCP Server

[![ONI MCP Server 预览图](preview.png)](README.md)

ONI MCP Server 是一个 Oxygen Not Included Mod。它会在游戏内启动本地 MCP Streamable HTTP 服务，让支持 MCP 的 AI 客户端以虚拟文件系统的方式读取殖民地状态、查询游戏数据、分析地图，并在玩家确认后执行明确的游戏操作。

浏览器反馈页:

```text
http://localhost:8788/
```

MCP 端点:

```text
http://localhost:8788/mcp/
```

源代码: https://github.com/LIghtJUNction/OniMods/

> **Steam 创意工坊简短描述 (Steam Workshop Short Description):**
> 一个在《缺氧》游戏内启动本地 Model Context Protocol (MCP) 服务器的 Mod。它允许 AI 助手（如 Claude、Gemini 等）连接到游戏，读取殖民地状态（氧气、食物、复制人、电力），并使用虚拟文件系统模式（通过 SEARCH/REPLACE 块）或经玩家确认的安全命令（暂停、调速、日程、门禁等）辅助规划和管理殖民地。

## 能做什么

* 检查氧气、食物、电力、温度、复制人、房间和警报。
* 读取 `oni://...` 资源，获得比截图更可靠的游戏状态。
* 使用 `world_editor` 提供的虚拟存档目录和文件（通过 SEARCH/REPLACE 块）来分析和修改游戏世界。
* 执行暂停、调速、截图、日程、门禁、储存过滤、优先级、复制人命名等小任务。
* 作为 ONI + MCP + Agent 的实验平台。

## 不适合做什么

* 不保证 AI 能长期自主玩好缺氧。
* 不建议让 AI 在无确认的情况下执行大范围挖掘、拆除、沙盒、保存或加载。
* 这是 MCP 游戏控制面，不是完整自治玩家。

## 安装

1. 订阅本 Mod。
2. 在游戏 **Mods** 菜单启用 **ONI MCP Server**。
3. 重启游戏。
4. 进入主菜单或加载/创建殖民地后，Mod 会自动启动本地 MCP 服务。

依赖存档状态的工具需要进入殖民地后才有有效数据；只读的协议和配置检查可以在主菜单阶段工作。

## 配置按钮

启用 Mod 后，游戏 **Mods** 菜单里的 **ONI MCP Server** 条目应出现 **Configure / 配置** 按钮。按钮来自 PLib 配置系统，可修改端口、token、截图清理等选项，也可以查看当前配置文件路径。

如果看不到配置按钮:

1. 确认已经启用 Mod 并重启游戏。
2. 确认当前安装包包含 `PLib.dll` 合并后的 `OniMcp.dll`。
3. 如果仍缺失，请使用 v0.2.0 或更新版本；该版本显式初始化 PLib Options 的 ModsScreen 补丁。
4. 直接编辑下方的 `OniMcpConfig.json`。Mod 启动时会自动生成该文件。

## 配置文件

配置文件名固定为:

```text
OniMcpConfig.json
```

优先位置是缺氧的用户数据目录，不是 Steam 游戏安装目录:

* Windows: `Documents\Klei\OxygenNotIncluded\OniMcpConfig.json`
* Windows 备用: `Documents\Klei\Oxygen Not Included\OniMcpConfig.json`
* Linux: `~/.config/unity3d/Klei/Oxygen Not Included/OniMcpConfig.json`
* macOS: `~/Library/Application Support/unity.Klei.Oxygen Not Included/OniMcpConfig.json`

为了兼容旧版本，Mod 也会读取 Mod 安装目录下已有的 `OniMcpConfig.json`。如果两个位置都不存在，新版本会在首次加载时写入用户数据目录。

常用字段:

```json
{
  "Host": "localhost",
  "Port": 8788,
  "AuthEnabled": false,
  "AuthToken": "自动生成的随机token",
  "GlobalAutoDisinfectDisabled": false,
  "ScreenshotCleanupEnabled": true,
  "ScreenshotRetentionMinutes": 120,
  "ScreenshotMaxFiles": 40
}
```

修改配置后，在配置界面点击 **Restart MCP server**，或重启游戏。

## Token 认证

Mod 默认不启用 token 校验:

```json
"AuthEnabled": false
```

首次启动时会自动生成随机 `AuthToken` 并写入 `OniMcpConfig.json`。客户端连接时需要发送下面任意一个 HTTP 请求头:

```text
Authorization: Bearer <AuthToken>
```

或:

```text
X-Oni-Mcp-Token: <AuthToken>
```

只在本机使用且确认没有暴露端口时，可以关闭校验:

```json
"AuthEnabled": false
```

如果把 `Host` 改成 `0.0.0.0` 供局域网访问，建议保持 token 校验开启，并使用强随机 token。

## 连接客户端

任何支持 MCP Streamable HTTP 的客户端都可以连接:

```text
http://localhost:8788/mcp/
```

Claude Desktop 示例:

```json
{
  "mcpServers": {
    "oni": {
      "url": "http://localhost:8788/mcp/",
      "headers": {
        "Authorization": "Bearer <AuthToken>"
      }
    }
  }
}
```

如果已关闭 token 校验，可以去掉 `headers`。

首次使用请先用只读提示词开始:

```text
先不要修改存档，检查我的殖民地状态，列出最紧急的 3 个风险。
```

## 主要工具

公开工具面已精简为一组聚合入口，运行时清单是事实来源:

```text
server_control domain=catalog action=manifest
```

或读取:

```text
oni://tools/manifest
```

公开的聚合工具:

* `world_editor`: 代码文件风格的世界编辑器。将存档视为虚拟目录，通过 `cd`, `ls`, `read`, `search` 来观察游戏内文件，通过 `edit` 配合 SEARCH/REPLACE 块来下达修改意图（挖掘、建造、调整优先级、连接管道等）。
* `game_control`: 游戏状态、速度、存档、UI 与游戏级操作。
* `navigation_control`: 相机、视图、覆盖层和截图操作。
* `building_control`: 建筑规划、放置、配置、生产与管线连接。
* `orders_control`: 挖掘、清扫、拖地、拆除等任务标记。
* `server_control`: MCP 服务状态、会话清单、截图任务、后台任务以及清单/开发指南获取。

`colony_control`、`dupes_control`、`read_control`、`search_control` 等其他聚合入口仍保持注册，供虚拟文件内部路由和兼容客户端调用，但不会出现在默认 `tools/list` 中。

`coordinate_control` 不属于当前公开运行时，普通聚合工具也会拒绝 raw coordinates。精确订单先读 `/active/ops/tools.md`，再编辑 `/active/ops/orders.md`；精确建造则读取并编辑 `/active/map/viewport.md` 中的地图 token。只选择当前公开的 typed files/tools，忽略 hidden `coordinate_control` 和 `/active/ops/coordinate.md` 兼容入口。

## 常见资源

* `oni://colony/status`
* `oni://colony/diagnostics`
* `oni://colony/alerts`
* `oni://colony/summary`
* `oni://resources/inventory`
* `oni://resources/food`
* `oni://dupes`
* `oni://dupes/status-check`
* `oni://power/summary`
* `oni://rooms/list`
* `oni://world/text-map`
* `oni://buildings/defs`
* `oni://tools/manifest`
* `oni://tools/guide`

## API 稳定性

在 `oni_mcp` 到达 `1.0.0` 之前，工具名、参数、资源路径、响应字段都可能发生不兼容变化。衍生 Mod、插件、脚本、第三方客户端应固定版本，并优先读取运行时 manifest 作为兼容依据。

## 致谢

由 **gpt5.5**、**gpt5.6**、**glm5.2**、**Kimi k2.6**、**Kimi k3**、**Gemini 3.5 Flash**、**grok4.5** 和玩家 **LIghtJUNction** 共同开发测试。本项目开源，可检查、修改和贡献。
