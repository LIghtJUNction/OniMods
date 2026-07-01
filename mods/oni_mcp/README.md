# ONI MCP Server

ONI MCP Server 是一个 Oxygen Not Included Mod。它会在游戏内启动本地 MCP Streamable HTTP 服务，让支持 MCP 的 AI 客户端读取殖民地状态、查询游戏数据、分析地图，并在玩家确认后执行明确的游戏操作。

默认端点:

```text
http://localhost:8788/mcp/
```

源代码: https://github.com/LIghtJUNction/OniMods/

## 能做什么

* 检查氧气、食物、电力、温度、复制人、房间和警报。
* 读取 `oni://...` 资源，获得比截图更可靠的游戏状态。
* 执行暂停、调速、截图、日程、门禁、储存过滤、优先级、复制人命名等小任务。
* 读取文本地图，定义区域，辅助规划布局。
* 作为 ONI + MCP + Agent 的实验平台。

## 不适合做什么

* 不保证 AI 能长期自主玩好缺氧。
* 不建议让 AI 在无确认的情况下执行大范围挖掘、拆除、沙盒、保存或加载。
* 这是 MCP 游戏控制面，不是完整自治玩家。

## 安装

订阅本 Mod 后，在游戏 **Mods** 菜单启用 **ONI MCP Server**，然后重启游戏。

加载或创建殖民地后，Mod 会自动启动 MCP 服务。服务会在主菜单阶段尽早启动，但依赖存档状态的工具需要进入殖民地后才有有效数据。

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
      "url": "http://localhost:8788/mcp/"
    }
  }
}
```

首次使用建议先让 AI 只读检查:

```text
先不要修改存档，检查我的殖民地状态，列出最紧急的 3 个风险。
```

如果启用了 token 认证，客户端需要发送:

```text
Authorization: Bearer <token>
```

或:

```text
X-Oni-Mcp-Token: <token>
```

## 配置

可通过 Mod 选项界面配置，也可以编辑 `OniMcpConfig.json`。

常用配置:

* `Host`: 默认 `localhost`。局域网访问可改为 `0.0.0.0`。
* `Port`: 默认 `8788`。
* `AuthEnabled`: 是否启用 token 认证。
* `AuthToken`: token 内容。
* `ScreenshotCleanupEnabled`: 是否自动清理临时截图。
* `ScreenshotRetentionMinutes`: 临时截图保留时间。
* `ScreenshotMaxFiles`: 最多保留临时截图数量。

修改 Host、Port 或认证配置后，建议重启游戏，或在 Mod 选项中保存以触发服务重启。

## 安全建议

默认保持 `Host=localhost`，只允许本机 AI 客户端连接。

如果改为 `0.0.0.0` 供局域网访问，建议开启 token 认证，并设置强 token。

建议让 AI 先只读分析，再请求执行。大范围挖掘、拆除、沙盒、保存、加载等高风险操作应额外确认。长时间自动游玩前请先手动保存。

## MCP 能力

当前默认公开 8 个核心聚合工具:

* `server_control`: 健康检查、工具清单、工具搜索、调用指南。
* `read_control`: 地图、殖民地状态、资源、建筑、机制知识。
* `game_control`: 暂停、恢复、调速、存档、沙盒、UI 标记。
* `navigation_control`: 相机、覆盖层、截图、指针、鼠标格、点击和拖拽。
* `building_control`: 建造规划、材料检查、蓝图预览、储存、过滤、生产队列。
* `orders_control`: 挖掘、清扫、拖地、拆除、优先级、区域订单、管线/线路剪断。
* `dupes_control`: 复制人状态、详情、优先级、命令、改名、技能、帽子。
* `colony_control`: 快照、报告、诊断、通知、日程、饮食、研究、医疗、农牧管理。

旧版细粒度工具保留为隐藏兼容入口。实际工具清单以运行时 `server_control domain=catalog action=manifest` 或 `oni://tools/manifest` 为准。

## 常用资源

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

`oni_mcp` 在 `1.0.0` 前仍处于快速迭代阶段，工具名称、参数、资源路径和返回字段都可能发生不兼容变更。

二创、插件、脚本或第三方客户端请锁定具体版本，并以运行时 `server_control domain=catalog action=manifest` / `oni://tools/manifest` 为准适配。

## Credits

由 **gpt5.5**、**Kimi k2.6** 和玩家 **LIghtJUNction** 联合开发与测试。项目完全开源，欢迎查看、修改和贡献。
