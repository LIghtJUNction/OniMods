# ONI MCP Server

缺氧 (Oxygen Not Included) 的 Model Context Protocol (MCP) 服务器 Mod。将游戏操作封装为标准化的 MCP 工具，AI 助手可以通过 Streamable HTTP 协议与游戏交互。

## 功能

- **Streamable HTTP 传输层**：基于 `System.Net.HttpListener`，默认监听 `http://localhost:8787/mcp/`
- **Mod 配置**：通过 `OniMcpConfig.json` 配置 MCP 服务器 Host 和 Port
- **MCP 协议兼容**：实现 JSON-RPC 2.0 请求/响应，支持 `initialize`、`tools/list`、`tools/call`、`prompts/list`、`prompts/get`、`resources/list`、`resources/read`、`resources/templates/list` 和 `tasks/*`
- **严格 HTTP 会话校验**：非 `initialize` 请求必须带有效 `Mcp-Session-Id` 和协商后的 `Mcp-Protocol-Version`；缺失或不匹配返回 `400`，已终止或未知 session 返回 `404`
- **Prompt / Resource / Task**：提供可复用殖民地诊断 prompt、`oni://...` 实时资源 URI，以及 `tools/call` 的 task 执行模式
- **代理模式工具面**：
  - `tools/list` 默认只暴露核心路由/发现/批量/规划工具，避免一次塞入完整工具面；完整工具仍可通过 `tools_search detail=full`、`tools_manifest` 或直接 `tools/call` 按名调用
  - `tools_manifest` / `tools_search` / `tools_guide` - 按分组、读写模式、风险等级或玩家目标检索完整注册工具；`tools_search detail=brief` 和 `oni://tools/guide?goal=...` 用于低 token 工具路由
  - `tools_player_action_coverage` - 按玩家操作面审计工具/资源覆盖；`query=...&detail=brief` 用于低 token 查找“玩家能做的操作”
  - `tools_call_many` - 万能批量工具，按顺序一次调用多个工具并返回结果；支持 `dryRun` 预检和 `requireAllValid` 全量有效才执行
  - `plan_harness_create` / `plan_harness_parse` / `plan_harness_record` / `plan_harness_validate` / `plan_harness_execute` - 规划文本解析、反馈、验证、门禁实施和约束记录
  - `database_query` - 查询游戏内置 Database/百科（Codex）条目
  - `server_status` - MCP 服务状态
  - `mcp_client_capabilities` / `mcp_sampling_request_create` / `mcp_elicitation_request_create` - 查看客户端 capabilities，并生成 sampling/elicitation 客户端请求对象
  - `game_time` / `game_pause` / `game_resume` / `game_set_speed` / `game_red_alert_status` / `game_red_alert_set` / `game_screenshot`
  - `game_notification_create` - 创建游戏原生通知，可选点击聚焦地图格子
  - `camera_get_view` / `camera_set_view` / `camera_move` / `camera_switch_view` / `camera_focus_cell` / `camera_focus_dupe`
  - `map_popup_text` / `map_marker_create` / `map_marker_list` / `map_marker_clear`
  - `edit_mark_request_create` / `edit_mark_request_list` / `edit_mark_request_clear` - 框选区域后创建 agent 编辑请求，要求客户端先计划再行动
  - `colony_status` / `colony_diagnostics` / `colony_alerts`
  - `world_list` / `world_cell_info` / `world_element_summary` / `world_text_map`
  - `thermal_overheat_risk_scan` / `overheat_risk_scan` / `thermal_risk_scan` - 扫描建筑过热风险
  - `dupes_list` / `dupes_detail` / `dupes_attributes` / `dupes_needs` / `dupes_rename` / `dupes_auto_rename`
  - `schedule_list` / `schedule_set_block` / `schedule_assign_dupe`
  - `resources_discovered` / `resources_inventory` / `resources_food`
  - `resources_storage_list` / `resources_storage_detail` / `resources_storage_set_filter`
  - `rockets_list` / `rockets_status` / `rockets_detail` / `space_destinations_list`
  - `rockets_set_destination` / `rockets_request_launch` / `rockets_cancel_launch`
  - `buildings_list` / `buildings_summary` / `buildings_set_priority` / `buildings_deconstruct`
  - `buildings_search_defs` / `buildings_materials` / `buildings_plan` / `buildings_plan_rect` / `buildings_plan_many`
    - 建造工具的 `material` 默认等同 `auto`，会按当前世界合法材料库存自动选择；显式材料无效或库存为 0 时会返回 `availableMaterials`/`candidateMaterials` 和重试建议。
  - `power_summary` / `power_circuits_summary` / `power_status` - 汇总电力系统
  - `rooms_list` / `room_list` / `rooms_overview` - 列出房间系统状态
  - `priorities_list` / `priorities_set_area`
  - `orders_sweep_area` / `orders_dig_area` / `orders_attack` / `conduits_cut`

旧版工具名（如 `get_colony_info`、`get_inventory`、`pause_game`）仍作为调用别名兼容。`tools/list` 是懒暴露层，默认只列核心入口；完整注册表请用 `tools_search detail=full` 或 `tools_manifest` 查询，隐藏工具仍可直接 `tools/call`。

## MCP Prompts / Resources / Tasks

Prompts:

- `colony_triage` - 殖民地体检，优先发现缺氧、断粮、停电和复制人风险
- `next_cycle_plan` - 下一周期行动计划
- `inspect_area` - 指定区域地图分析，优先使用文本地图
- `dupe_care_review` - 复制人需求、日程和技能检查
- `power_audit` - 电力审计
- `rooms_overview` - 房间概览
- `thermal_audit` - 热管理审计

Resources:

- `oni://colony/status`、`oni://colony/diagnostics`、`oni://colony/alerts`、`oni://colony/report`、`oni://colony/summary`
- `oni://world/list`、`oni://world/elements`、`oni://thermal/overheat-risk`
- `oni://resources/inventory`、`oni://resources/food`、`oni://storage/list`、`oni://power/summary`
- `oni://rockets/status`、`oni://rooms/list`
- `oni://research/status`、`oni://schedules`、`oni://dupes`、`oni://plans`、`oni://mcp/sessions`、`oni://tools/manifest`
- Tool discovery templates: `oni://tools/manifest{?query,group,mode,risk,detail,limit}`、`oni://tools/search{?query,group,mode,risk,detail,limit}`、`oni://tools/player-action-coverage{?query,group,status,detail,includeResources,includeHotkeys,limit}`、`oni://tools/guide{?goal,detail}`、`oni://tools/static-audit{?includeWarnings}`
- Templates: `oni://world/cell/{x}/{y}`、`oni://world/text-map{?x1,y1,x2,y2,worldId,visibleOnly,view,sparse,includeBuildings,includeItems,includeDupes,includeElements,includeSummary,detail,encoding,profile,format,elementLimit,objectLimit,maxCells}`。`profile=scan&encoding=rle&format=json` 适合低 token 初扫和规划校验，按需再开启对象、元素统计或 full 明细。`view=power/gas_conduits/liquid_conduits/solid_conveyor/logic` 会文本化对应 overlay，并默认 `sparse=true`，只列非空电线、管道、轨道、自动化线和电力设备格子；需要逐格矩阵时传 `sparse=false`。
- `oni://power/summary{?worldId,includeDetails,limit}`、`oni://rooms/list{?worldId,type,includeBuildings,includeCriteria,limit}`、`oni://thermal/overheat-risk{?worldId,marginC,includeNonOverheatable,minTempC,limit}`

Tasks:

- `tools/list` 默认列出的核心工具都会声明 `execution.taskSupport=optional`
- `tools/call` 参数带 `task` 时会立即返回 task 信息，并在 Unity 主线程异步执行工具
- 可通过 `tasks/list`、`tasks/get`、`tasks/result`、`tasks/cancel` 查询或取消任务

Sampling / elicitation 是客户端能力：服务器会在 `initialize` 时记录客户端声明的 `sampling`、`elicitation` 和 `tasks` capabilities，并通过 `mcp_client_capabilities` / `oni://mcp/sessions` 暴露。`mcp_sampling_request_create` 和 `mcp_elicitation_request_create` 可生成标准客户端请求对象；客户端用 `GET /mcp/` 且 `Accept: text/event-stream` 建立 SSE 后，服务端可通过该通道发送 server-initiated JSON-RPC 消息。编辑标记创建时会优先向声明了 `sampling` 的会话推送 `sampling/createMessage`，否则退化为 `notifications/message` 提醒客户端读取 `edit_mark_request_list`。

## 工具风险等级

工具描述中会带 `[group/mode/risk]` 前缀：

- `read`：只读查询，不修改存档
- `write`：修改设置、复制人、日程、优先级或过滤器
- `execute`：对游戏下达动作命令
- `dangerous`：可能大范围改变地图或基地，例如挖掘、拆除；这类工具要求传入 `confirm: true`

`tools_call_many` 的参数格式为 `{"calls":[{"name":"tool_name","arguments":{...}}]}`，也支持低 token 形态 `{"items":[{"t":"tool_name","a":{...}}],"defaults":{...}}`；`defaults/defaultArguments` 会合并到每个子调用，子调用参数优先。最多 20 个子调用。建议先传 `dryRun:true` 做工具存在性、必填参数和危险工具 `confirm` 预检；默认 `requireAllValid:true`，任一子调用预检失败就不会执行任何子调用。传入 `stopOnError:true` 后会在首个执行错误处停止。它不会绕过子工具自己的确认参数和安全校验，也不能递归调用自身。

领域批量工具也尽量使用同一套低 token 约定：`user_menu_actions_batch_press`、`maintenance_actions_batch_execute`、`buildings_config_batch_set`、`automation_controls_batch_set`、`automatable_controls_batch_set`、`critter_sensors_batch_set`、`production_queue_batch_set`、`activation_ranges_batch_set`、`receptacles_batch_control` 和 `storage_tile_selections_batch_set` 支持 `defaults/defaultArguments`，会把默认参数合并到每个 item，item 自身字段优先。对象菜单/维护批量支持 `a/w/e/slot` 等短字段；制作队列批量支持 `r/m/c`；激活阈值支持 `a/d/w`；实体请求支持 `a/tag/w`；StorageTile 目标物品支持 `i/c/w`。

规划 harness 的 `plan` 阶段 payload 支持 `plannedCalls`，也支持与批量工具一致的低 token 形态 `calls/items:[{t,a}]` 和 `defaults/defaultArguments`。需要从文本规划进入可执行流程时，先调用 `plan_harness_parse`，或在 `plan_harness_record stage=plan` 里传 `planText`；解析器支持 JSON/Markdown JSON 代码块/每行 `tool_name {json}`。如果文本无法解析成工具调用，会返回 `parseErrors`、`expectedFormats` 和示例，agent 应据此修正规划文本而不是直接执行。`plan_harness_validate` 会按合并后的参数检查工具存在性、必填参数和危险工具 `confirm`；`plan_harness_execute` 通过反馈/验证门禁后按同一合并规则执行并自动记录 implementation。

截图保存到系统临时目录的 `oni-mcp/screenshots/`（Linux 通常是 `/tmp/oni-mcp/screenshots/`），返回 PNG 路径、周期、屏幕尺寸和本次自动清理结果，便于 agent 读取图片继续分析局势。MCP 会在服务器启动、停止和每次截图前清理旧截图；默认最多保留 40 张、最长保留 120 分钟，可在 Mod Options 里调整。`camera_move` 支持 `mode=pan` 按 `dx/dy` 相对平移，也支持 `mode=jump` 跳转到 `x/y` 世界坐标；可选 `zoom`、`duration` 和 `snap`。`camera_switch_view` 可切换 `none`、`oxygen`、`power`、`gas_conduits`、`liquid_conduits`、`solid_conveyor`、`logic`、`temperature`、`heat_flow`、`thermal_conductivity`、`materials`、`light`、`decor`、`rooms`、`priorities`、`disease`、`radiation`、`sound`、`suit`、`crop`、`harvest` 等覆盖层，并默认保存切换后的截图。

提示和地图标记工具复用游戏原生 UI：`game_notification_create` 使用 `NotificationManager` 创建左侧通知；`map_popup_text` 使用 `PopFXManager` 在目标格子显示浮动文字；`map_marker_create` 使用游戏选择标记 prefab 在目标格子显示临时地图标记，并可通过 `map_marker_list` / `map_marker_clear` 管理。

编辑标记工具会在工具栏添加 `MCP` 入口：选择 `编辑标记` 后拖拽框选地图区域，输入修改提示词并创建请求。请求会保存为 `edit_mark_request_list` 可读取的待处理项，并附带 `areaId`、矩形坐标、内联 `textMap` 文本地图和一个 `sampling/createMessage` 客户端请求对象；如果客户端保持 SSE 连接且声明 sampling，服务端会主动推送这个 sampling 请求唤醒客户端 agent。`contextPriority` 明确要求客户端优先使用 `textMap` / `world_text_map`，截图路径默认不生成，只在调用方显式 `includeScreenshot=true` 时作为视觉补充。客户端 agent 应先用文本地图理解区域，创建/记录可解析规划，经过反馈/验证后再调用 MCP 工具执行修改。

## 技术栈

- **Newtonsoft.Json** - 游戏自带，用于 JSON 序列化
- **System.Net.HttpListener** - .NET Framework 内置 HTTP 服务器
- **Harmony** - 游戏自带，用于代码注入

## 构建

```bash
onim build -m oni_mcp
```

发布包只包含 `OniMcp.dll`、元数据、预览图和资源文件；不需要额外运行库。

## 使用

1. 安装 Mod 到缺氧游戏
2. 启动游戏并加载存档
3. MCP Server 自动在 `http://localhost:8787/mcp/` 启动
4. 使用任意 MCP 客户端连接并调用工具

### Mod 配置

创建或编辑 `OniMcpConfig.json`。Mod 会优先读取 Mod 目录下的同名文件，其次读取游戏持久化目录下的同名文件：

- `Host`：默认 `localhost`。如需允许局域网连接，可设置为 `0.0.0.0`
- `Port`：默认 `8787`
- `Auto-clean screenshots`：自动清理 MCP 截图临时文件，默认启用
- `Screenshot retention minutes`：截图最长保留分钟数，默认 `120`
- `Maximum screenshots`：截图最多保留数量，默认 `40`
示例：

```json
{
  "Host": "localhost",
  "Port": 8787,
  "ScreenshotCleanupEnabled": true,
  "ScreenshotRetentionMinutes": 120,
  "ScreenshotMaxFiles": 40
}
```

修改配置后重启游戏或重新加载 Mod；游戏日志会输出实际 MCP 地址。

### MCP 客户端配置示例

```json
{
  "mcpServers": {
    "oni": {
      "url": "http://localhost:8787/mcp/"
    }
  }
}
```

## 开发

项目结构：

```
mods/oni_mcp/
├── Core/           - MCP 协议类型定义 (JSON-RPC 2.0)
├── Server/         - Streamable HTTP 服务器 + 主线程桥接
├── Tools/          - 游戏操作工具实现
├── ModInfo.cs      - Mod 入口
└── OniMcp.csproj   - 项目配置
```

添加新工具：在 `Tools/` 下创建新的 `McpTool` 并注册到 `OniToolRegistry.Initialize()`。

## 注意事项

- 所有游戏 API 调用都在 Unity 主线程执行（通过 `MainThreadBridge`）
- 普通客户端可只使用 POST 请求/响应模式
- `GET /mcp/` 会按 Streamable HTTP 会话规则校验 header/session；带 `Accept: text/event-stream` 时保持 SSE 连接，用于服务端主动发送 `sampling/createMessage`、`notifications/message` 等 JSON-RPC 消息
