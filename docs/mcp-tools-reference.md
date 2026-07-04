# ONI MCP 工具参考

本文档描述 `oni_mcp` 当前推荐的工具面。运行时清单永远以 `server_control domain=catalog action=manifest` 和 `oni://tools/manifest` 为准。

## 快速开始

- MCP 地址: `http://localhost:8788/mcp/`
- 协议版本: `2025-11-25`
- Default public tools: 3 core aggregate entrypoints: `world_editor`, `colony_control`, `server_control`
- 旧版细粒度工具: 隐藏注册为 compatibility，可按精确名称继续调用
- Tool descriptions: default-public tool descriptions and parameter descriptions are in English

非 `initialize` 请求必须携带会话协商后的 `Mcp-Session-Id` 和 `Mcp-Protocol-Version`。

## 定位与执行原则

Authoritative model:

- Saves are directories. `latest/` is the fixed alias for the current/latest save.
- `cd latest` enters the save; `cd` or `cd ~` exits back to `/`, representing the main menu/root.
- Save contents are structured world files such as `map/terrain.oni`, `buildings/plans.oni`, `infrastructure/power.oni`, and `views/power.png`.
- There are no action patch files. The only world-changing operation is `world_editor command=edit` with one SEARCH/REPLACE block.
- Reading the same file again is the observation step after an edit.

Example edit:

```text
`<<<<<<< SEARCH`
# observed or empty planning text
`=======`
用铜矿连接电池到制氧机
`>>>>>>> REPLACE`
```

`world_editor` is the default world interaction tool. It treats the loaded save as
a virtual folder and exposes map views as files:

- `world_editor command=ls path=/`
- `world_editor command=read path=/world/map/text.txt`
- `world_editor command=read path=/world/views/power.png`
- `world_editor command=search domain=buildings query="wire"`
- `world_editor command=plan plan="用铜矿连接电池到制氧机"`
- `world_editor command=connect plan="connect battery to oxygen diffuser"`

Search, planning, actions, building, orders, navigation, game actions, dupe
actions, and coordinate fallback are routed through `world_editor`. `colony_control`
and `server_control` remain separate public tools for colony-wide state and MCP
server operations.

新工具面按搜索/动作优先设计:

- 优先使用 `query`、`target`、`search`、`name`、`id`、`areaId`。
- Use `search_control` for dedicated search. It returns `searchResult`, `nextActions`, and `searchActionPatch`, so selecting a result is structurally tied to the next action call like a search/replace edit.
- Except for `coordinate_control`, public tools do not accept `x/y`, `x1/y1/x2/y2`, `dx/dy`, `points`, or `anchors`; coordinates are an auxiliary gateway capability only.
- 面向任务的返回应尽量包含 `reachable`、`executable`、失败原因、缺失条件和建议下一步。
- 区域动作优先先用 `read_control domain=area action=define` 生成 `areaId`，再传给支持区域的工具。
- 写入、执行和危险动作应支持 `dryRun` 或 `confirm`，并在执行后重新读取状态验证。

## 核心工具

| 工具 | 主要 domain/action | 风险 | 用途 |
|------|--------------------|------|------|
| `server_control` | `catalog`, `batch`, `program` | read/execute | 健康检查、工具清单、工具搜索、目标指南、批量调用、agent program |
| `read_control` | `world`, `area`, `resources`, `buildings`, `knowledge`, `infrastructure` | read | 世界地图、区域、资源、建筑、机制知识、电力和房间摘要 |
| `search_control` | `tools`, `world`, `resources`, `buildings`, `dupes`, `knowledge` | read | Dedicated search with action-ready `nextActions` |
| `game_control` | `speed`, `state`, `save`, `sandbox`, `ui` | read/execute/dangerous | 暂停、恢复、调速、存档、沙盒、UI 编辑标记 |
| `navigation_control` | `camera`, `pointer` 或按 `action` 推断 | execute | 相机、覆盖层、截图、可视 agent 指针、鼠标格、点击、拖拽 |
| `coordinate_control` | `targetTool` gateway | execute/dangerous | The only coordinate auxiliary entrypoint; explicitly forwards x/y, rectangles, path points, or anchors to an underlying tool |
| `building_control` | `planning`, `config`, `storage`, `filter`, `production`, `side_surface`, `rocket` | read/write/execute | 建造规划、材料检查、蓝图、建筑侧屏配置、储存过滤、生产队列、火箭 |
| `orders_control` | `area`, `priority`, `designation`, `conduit` | execute/dangerous | 挖掘、清扫、拖地、拆除、优先级、区域订单、线路/管线剪断 |
| `dupes_control` | `info`, `priority`, `command`, `skill`, `hat`, `assignable` | read/write/execute | 复制人状态、命令、优先级、改名、技能、帽子、可分配物 |
| `colony_control` | `snapshot`, `read`, `report`, `diagnostic`, `notification`, `management`, `bio` | read/write | 殖民地快照、报告、诊断、通知、日程、饮食、研究、医疗、农牧 |

## 建造规划

`building_control domain=planning` 是新的建造入口。

| action | 用途 |
|--------|------|
| `search_defs` | 搜索可建建筑定义 |
| `materials` | 查询建筑可用材料和库存 |
| `preview` | 预检一个蓝图锚点，返回可执行性和材料需求 |
| `placement_candidates` | 在区域或目标附近搜索可放置位置 |
| `build_area` | 放置蓝图，线性 utility 支持自动连接 |
| `auto_connect` | 兼容旧流程的显式连接入口，新流程通常不需要单独调用 |

### 材料返回

蓝图预检和放置结果应返回材料可行性。旧字段 `materialSelection` 仍保留兼容，新字段 `materials` 用于任务级判断:

```json
{
  "materials": {
    "requirementKnown": true,
    "requiredKg": 200.0,
    "selectedAvailableKg": 1240.0,
    "satisfied": true,
    "shortageKg": 0.0,
    "availableMaterials": [
      { "tag": "SandStone", "availableKg": 1240.0 }
    ]
  }
}
```

调用方应优先检查 `materials.satisfied`。如果为 `false`，向用户返回需要材料、可用材料和缺口，而不是继续下达蓝图。

### 线路和管路

`build_area` 对线性设施支持自动连接:

- `Wire`
- `LogicWire`
- `GasConduit`
- `LiquidConduit`
- `SolidConduit`

For semantic building, prefer `plan`, `blueprint`, `areaId`, or search results. When endpoint coordinates, path point arrays, or anchor arrays are required, forward to `building_control` through `coordinate_control`.

示例:

```json
{
  "targetTool": "building_control",
  "domain": "planning",
  "action": "build_area",
  "payload": {
    "prefabId": "Wire",
    "material": "Copper"
  },
  "x": 10,
  "y": 20,
  "x2": 18,
  "y2": 20,
  "confirm": true
}
```

This directly creates a continuous line, with no separate follow-up connection step required.

## 订单与剪断

`orders_control` supports semantic targets and area handles. Exact coordinate orders must be forwarded through `coordinate_control`.

```json
{
  "domain": "area",
  "action": "dig",
  "areaId": "a1",
  "confirm": true
}
```

剪断管线和线路时:

- `cut_conduits type=auto` 默认包含气管、液管、固体轨道、电线和逻辑线。
- `type=wire` 只剪电线。
- `type=logic` 只剪逻辑线。
- `type=all` 适合明确需要更大范围时使用。

## 导航与指针

`navigation_control` 用于可视定位和 UI 操作。`agentId` 是逻辑指针名；省略时使用默认 `agent`。同一个 `agentId` 会跨 MCP session 复用，避免重连后留下多个指针。

常用动作:

- `get`: 获取指针状态。
- `user_mouse`: 读取玩家当前鼠标格。
- `jump`: 跳到指定格、保存点或 `code=mouse`。
- `aim_cell`: 指向精确格。
- `select_tool`: 选择游戏工具。
- `left_click` / `hold_left`: 点击或拖拽。
- `say`: 在指针旁显示短消息。
- `clear`: 清理指针和跳转点。

需要给玩家解释意图时，传 `displayText` 或 `message`。

## 常用资源

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
| `oni://power/ports` | 电力接口格、锚点、接线点和端口是否已有电线 |
| `oni://rooms/list` | 房间系统状态 |
| `oni://thermal/overheat-risk` | 建筑过热风险 |
| `oni://world/text-map` | 文本地图 |
| `oni://buildings/defs` | 可建造建筑定义 |
| `oni://tools/manifest` | 工具清单 |
| `oni://tools/guide` | 按目标推荐工具链 |
| `oni://guide/mechanics` | 机制、公式、边界条件速查 |

## 代码目录

```text
mods/oni_mcp/Tools/
├── Core/      # 工具和资源注册
├── New/       # Default-public 10 aggregate tool entrypoints and English descriptions
├── Shared/    # 搜索、定位、JSON、工具辅助逻辑
└── Legacy/    # 内部兼容和旧版细粒度实现
```

Prefer extending aggregate entrypoints in `Tools/New/` for new public capabilities. Put legacy compatibility logic in `Tools/Legacy/Impl/`, registered through `Tools/Legacy/LegacyToolRegistry.cs`. Shared search, reachability, and material checks belong in `Tools/Shared/`; coordinate entry is centralized in `coordinate_control`.

## 兼容性说明

旧版工具仍可用于兼容历史客户端，但不再推荐新集成直接依赖。新客户端应:

1. Call `tools/list` to get the 10 public entrypoints.
2. 调用 `server_control domain=catalog action=search` 或读取 `oni://tools/guide` 查找目标流程。
3. 优先传语义定位参数。
4. 对危险动作传 `confirm: true`。
5. 执行后读取资源或区域快照验证状态。
