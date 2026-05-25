**源代码:** https://github.com/LIghtJUNction/OniMods/

# ONI MCP Server

ONI MCP Server 是一个 Oxygen Not Included Mod。它在游戏内启动一个本地 MCP Streamable HTTP 服务，让支持 MCP 的 AI 客户端读取殖民地状态、检索游戏数据库、分析地图，并在玩家授权后执行游戏操作。

默认端点:

```text
http://localhost:8787/mcp/
```

> **API 稳定性警告**：`oni_mcp` 在 `1.0.0` 之前仍处于快速迭代阶段，工具名称、参数、资源路径和返回字段都可能发生不兼容变更。二创、插件、脚本或第三方客户端请锁定具体版本，并以运行时 `tools_manifest` / `oni://tools/manifest` 为准做兼容适配。

## 适合做什么

- 作为殖民顾问: 快速检查氧气、食物、电力、温度、复制人状态、房间和警报。
- 作为数据面板: 通过 `oni://...` 资源实时读取游戏状态，减少只靠截图猜测。
- 作为操作助手: 执行暂停、调速、截图、日程调整、门禁、储存过滤、优先级、复制人命名等明确的小任务。
- 作为规划工具: 读取文本地图、定义区域、生成候选布局，并通过规划门禁让玩家确认后再执行。
- 作为 agent 实验平台: 提供工具搜索、工具清单、资源模板和风险分级，便于给 AI 编写专用技能。

## 不适合做什么

- 不保证 AI 能长期自主玩好缺氧。缺氧的世界规划、优先级系统和连锁故障对当前 AI 仍然很难。
- 不建议让 AI 在没有确认的情况下执行大规模挖掘、拆除、沙盒或存档修改操作。
- 这个项目提供 MCP 服务器和游戏操作面，不包含完整自治 agent。

## 安装

### 方式一: Steam 创意工坊

订阅并启用 **ONI MCP Server**，然后重启游戏。

### 方式二: 本地安装

将发布包 `OniMcp.zip` 解压到缺氧用户数据目录下的本地 Mod 目录，例如:

```text
mods/Local/OniMcp/
```

开发测试时也可以安装到:

```text
mods/Dev/OniMcp/
```

使用本仓库的 `onim` 工具时:

```bash
onim dev -m oni_mcp
```

## 启动

1. 启动 Oxygen Not Included。
2. 在主菜单 **Mods** 中启用 **ONI MCP Server**。
3. 加载或创建殖民地。
4. Mod 会自动启动 MCP 服务，默认监听 `http://localhost:8787/mcp/`。

服务器会在主菜单阶段尽早启动；部分需要存档状态的工具只有进入殖民地后才有数据。

## 连接 AI 客户端

### Claude Desktop 示例

编辑 Claude Desktop 配置文件:

- macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
- Windows: `%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "oni": {
      "url": "http://localhost:8787/mcp/"
    }
  }
}
```

重启客户端后，可以先让 AI 执行只读检查:

```text
先不要修改存档，检查我的殖民地状态，列出最紧急的 3 个风险。
```

### 其他 MCP 客户端

任何支持 MCP Streamable HTTP 传输的客户端都可以连接。将 URL 设置为:

```text
http://localhost:8787/mcp/
```

如果启用了 token 认证，客户端需要发送 `Authorization: Bearer <token>` 或 `X-Oni-Mcp-Token: <token>`。

## 配置

可以通过 Mod 选项界面配置，也可以创建或编辑 `OniMcpConfig.json`。配置文件会优先从 Mod 目录读取；如果不存在，则使用游戏持久化目录。

```json
{
  "Host": "localhost",
  "Port": 8787,
  "AuthEnabled": false,
  "AuthToken": "",
  "GlobalAutoDisinfectDisabled": false,
  "ScreenshotCleanupEnabled": true,
  "ScreenshotRetentionMinutes": 120,
  "ScreenshotMaxFiles": 40
}
```

| 字段 | 默认值 | 说明 |
|------|--------|------|
| `Host` | `localhost` | HTTP 监听地址。局域网访问可改为 `0.0.0.0` |
| `Port` | `8787` | MCP 端口，范围 `1024` 到 `65535` |
| `AuthEnabled` | `false` | 是否要求 token |
| `AuthToken` | 空 | token 内容。为空时会自动关闭认证 |
| `GlobalAutoDisinfectDisabled` | `false` | 是否保持全局自动消毒关闭 |
| `ScreenshotCleanupEnabled` | `true` | 是否自动清理临时截图 |
| `ScreenshotRetentionMinutes` | `120` | 临时截图保留分钟数 |
| `ScreenshotMaxFiles` | `40` | 最多保留临时截图数量 |

修改 `Host`、`Port` 或认证配置后，建议重启游戏或在 Mod 选项里保存触发服务重启。

## 安全建议

- 默认保持 `Host=localhost`，只允许本机 AI 客户端连接。
- 如果改为 `0.0.0.0` 给局域网访问，建议开启 `AuthEnabled` 并设置强 token。
- 让 AI 先做只读分析，再请求执行；大范围挖掘、拆除、沙盒、保存和加载相关操作需要额外确认。
- 长时间自动游玩前先手动保存，并限制 AI 每轮只运行短时间窗口。

## MCP 能力

当前实现包含 330+ 个工具、120+ 个固定资源和 100+ 个资源模板。工具和资源会随代码继续变化，实际清单以运行时 `tools_manifest` 和 `oni://tools/manifest` 为准。

### 核心工具

| 范围 | 代表工具 | 用途 |
|------|----------|------|
| 服务与目录 | `server_status`, `tools_manifest`, `tools_search`, `tools_guide` | 检查服务、搜索工具、按目标生成工具指南 |
| 游戏控制 | `game_pause`, `game_resume`, `game_set_speed`, `game_save` | 暂停、恢复、调速、存档 |
| 相机与截图 | `camera_move`, `camera_switch_view`, `game_screenshot` | 移动视角、切换覆盖层、截图 |
| 世界读取 | `world_text_map`, `world_area_snapshot`, `world_cell_info` | 文本地图、区域快照、格子详情 |
| 区域管理 | `area_define`, `area_get`, `area_blocks`, `area_merge` | 定义和复用地图区域 |
| 指针操作 | `agent_pointer_aim_cell`, `agent_pointer_user_mouse_get`, `agent_pointer_say`, `agent_pointer_left_click` | 用可视 agent 指针执行点击类操作、读取玩家鼠标格和显示气泡 |
| 建筑与订单 | `buildings_search_defs`, `buildings_materials`, `build_preview`, `orders_dig_area`, `orders_sweep_area` | 搜建筑、选材料、预检蓝图、下达挖掘和清扫订单 |
| 复制人 | `dupes_status_check`, `dupes_detail`, `dupes_needs`, `dupes_priority_set`, `dupes_rename` | 状态检查、需求、优先级、命名 |
| 管理界面 | `schedule_list`, `schedule_set_block`, `diet_status`, `resources_storage_set_filter` | 日程、饮食、储存过滤和管理屏设置 |
| 设施侧屏 | `filters_list`, `state_controls_list`, `automation_controls_list`, `lights_color_set` | 常见建筑侧屏配置 |
| 火箭与太空 | `rockets_status`, `rocket_modules_list`, `rocket_crew_requests_list` | 火箭状态、模块、乘员和太空设施 |
| 审计与覆盖 | `tools_player_action_coverage`, `tools_static_audit`, `side_screen_surfaces_audit` | 检查工具覆盖面和缺口 |
| 批量与规划 | `tools_call_many`, `agent_program_execute`, `edit_mark_request_list` | 批量调用、条件/循环流程脚本、读取玩家标记 |

`agent_pointer_*` 的 `agentId` 是当前 MCP session 内的逻辑指针名；不传时使用本 session 的默认 `agent` 指针。不同客户端 session 的同名 `agentId` 不共享状态，默认标签会带客户端名和 session 短前缀；可用 `mcp_client_capabilities` 查看当前 session 和客户端信息。不再需要某个指针时，用 `agent_pointer_clear` 删除它及其跳转点。

### 常用资源

| URI | 说明 |
|-----|------|
| `oni://colony/status` | 周期、复制人数、速度、暂停状态 |
| `oni://colony/diagnostics` | 缺氧、断粮、过热等诊断 |
| `oni://colony/alerts` | 当前警报和通知 |
| `oni://colony/summary` | 面向行动规划的殖民地摘要 |
| `oni://resources/inventory` | 资源库存 |
| `oni://resources/food` | 食物库存和保质信息 |
| `oni://dupes` | 复制人列表 |
| `oni://dupes/status-check` | 复制人位置、差事、需求和疑似被困风险 |
| `oni://power/summary` | 电网摘要和电池状态 |
| `oni://rooms/list` | 房间系统状态 |
| `oni://thermal/overheat-risk` | 建筑过热风险 |
| `oni://world/text-map` | 文本地图 |
| `oni://buildings/defs` | 可建造建筑定义 |
| `oni://tools/manifest` | 工具清单 |
| `oni://tools/guide` | 按目标推荐工具链 |

### 内置 Prompts

- `colony_triage`: 殖民地快速诊断
- `next_cycle_plan`: 下一周期计划
- `inspect_area`: 区域检查
- `dupe_care_review`: 复制人照护检查
- `power_audit`: 电力审计
- `rooms_overview`: 房间概览
- `thermal_audit`: 热管理审计

## 推荐使用方式

1. 先读状态: `oni://colony/status`、`oni://colony/diagnostics`、`oni://resources/food`、`oni://dupes/status-check`。
2. 再找工具: 用 `tools_search` 或 `oni://tools/guide` 根据目标检索工具。
3. 小范围操作: 对区域先 `area_define`，再使用 `*_area` 工具，避免坐标误伤。
4. 批量前先计划: 对多步建造、挖掘、拆除，优先使用规划门禁，让玩家确认。
5. 修改后验证: 执行动作后重新读取对应资源，确认游戏状态符合预期。

## 风险等级

| 风险 | 含义 |
|------|------|
| `read` | 只读查询，不修改存档 |
| `write` | 修改设置、过滤器、优先级等配置 |
| `execute` | 下达游戏动作或触发 UI 行为 |
| `dangerous` | 挖掘、拆除、沙盒、不可逆或大范围改变，通常需要 `confirm: true` |

## 故障排查

- AI 连接不上: 确认 Mod 已启用、游戏正在运行，并检查端口是否为 `8787`。
- 工具有返回但数据为空: 确认已经进入殖民地；主菜单阶段很多游戏状态还不存在。
- 改成 `0.0.0.0` 后无法访问: 检查系统防火墙和路由器网络隔离。
- 启用认证后 401: 确认客户端发送了 `Authorization: Bearer <token>` 或 `X-Oni-Mcp-Token`。
- 端口冲突: 修改 `Port`，保存配置后重启服务或重启游戏。

## 开发

构建:

```bash
onim build -m oni_mcp
```

开发安装:

```bash
onim dev -m oni_mcp
```

项目结构:

```text
mods/oni_mcp/
├── Core/                 # MCP 协议类型
├── Server/               # HTTP 服务和 Unity 主线程桥接
├── Tools/                # MCP 工具、资源、Prompt 注册
├── Config/               # Mod 配置
├── Support/              # 路径、日志、反射和 JSON 辅助
├── Localization/         # 本地化字符串
├── assets/               # 工具图标和资源
├── ModInfo.cs            # Mod 入口
└── OniMcp.csproj         # 项目配置和打包逻辑
```

添加新工具的一般流程:

1. 在 `Tools/` 下实现返回 `McpTool` 的方法。
2. 在 `OniToolRegistry.Initialize()` 中注册。
3. 如需只读入口，在 `OniResourceRegistry` 中添加 `oni://` 资源或资源模板。
4. 为危险工具添加确认参数和合适的风险等级。
5. 用 `tools_static_audit` 或 `tools_manifest` 检查运行时注册结果。

发布包包含 `OniMcp.dll`、Mod 元数据、预览图、README 和必要资源文件。

## 制作与测试

本 Mod 由 **gpt5.5**、**Kimi k2.6** 和玩家 **LIghtJUNction** 联合开发与测试。项目完全开源，欢迎查看、修改和贡献。
