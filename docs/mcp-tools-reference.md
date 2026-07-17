# ONI MCP 工具参考

本文档描述 `OniMcp` 当前推荐的工具面。运行时清单永远以 `server_control domain=catalog action=manifest` 和 `oni://tools/manifest` 为准。

## 快速开始

- MCP 地址: `http://localhost:8788/mcp/`
- 协议版本: `2025-11-25`
- Default public tools: 6 aggregate entrypoints: `world_editor`, `game_control`, `navigation_control`, `building_control`, `orders_control`, `server_control`
- 其他聚合入口: 仍保持注册，供虚拟文件内部路由和兼容客户端按精确名称调用，但默认 `tools/list` 不返回
- `coordinate_control` 不属于当前公开运行时；普通聚合工具拒绝 raw coordinates
- Tool descriptions: default-public tool descriptions and parameter descriptions are in English

非 `initialize` 请求必须携带会话协商后的 `Mcp-Session-Id` 和 `Mcp-Protocol-Version`。

## 定位与执行原则

Authoritative model:

- Saves are directories. `latest/` is the fixed alias for the current/latest save.
- `cd latest` enters the save; `cd` or `cd ~` exits back to `/`, representing the main menu/root.
- Save contents are structured world files such as `map/terrain.oni`, `buildings/plans.oni`, `infrastructure/power.oni`, and `views/power.png`.
- There are no action patch files. World changes use `world_editor command=edit`; prefer one SEARCH/REPLACE block. Multiple blocks require outer `allowPartial=true` and cannot be transactionally rolled back.
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
actions, and coordinate fallback are routed through `world_editor`. The default
public surface also keeps `game_control`, `navigation_control`, `building_control`,
`orders_control`, and `server_control` available for direct focused calls. Other
aggregate entrypoints remain registered for internal virtual-file routing and
compatibility clients.

新工具面按搜索/动作优先设计:

- 优先使用 `query`、`target`、`search`、`name`、`id`、`areaId`。
- Use `search_control` for dedicated search. It returns `searchResult`, `nextActions`, and `searchActionPatch`, so selecting a result is structurally tied to the next action call like a search/replace edit.
- Public aggregate tools do not accept raw `x/y`, `x1/y1/x2/y2`, `dx/dy`, `points`, or `anchors`. For exact orders, read `/active/ops/tools.md` and edit `/active/ops/orders.md`; use only currently public typed files/tools and ignore hidden `coordinate_control` and `/active/ops/coordinate.md` compatibility entries.
- 面向任务的返回应尽量包含 `reachable`、`executable`、失败原因、缺失条件和建议下一步。
- 区域动作优先先用 `read_control domain=area action=define` 生成 `areaId`，再传给支持区域的工具。
- 写入、执行和危险动作应支持 `dryRun` 或 `confirm`，并在执行后重新读取状态验证。
- 危险或大范围精确操作必须保持 pause -> read/plan -> dry-run -> confirm -> verify。

## 核心工具

| 工具 | 主要 domain/action | 风险 | 用途 |
|------|--------------------|------|------|
| `server_control` | `catalog`, `batch`, `program` | read/execute | 健康检查、工具清单、工具搜索、目标指南、批量调用、agent program |
| `read_control` | `world`, `area`, `resources`, `buildings`, `knowledge`, `infrastructure` | read | 世界地图、区域、资源、建筑、机制知识、电力和房间摘要 |
| `search_control` | `tools`, `world`, `resources`, `buildings`, `dupes`, `knowledge` | read | Dedicated search with action-ready `nextActions` |
| `game_control` | `speed`, `state`, `save`, `sandbox`, `ui` | read/execute/dangerous | 暂停、恢复、调速、存档、沙盒、UI 编辑标记 |
| `navigation_control` | `camera` 或按已知相机 `action` 推断 | execute | 相机移动、世界切换、覆盖层、聚焦和截图 |
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

For semantic building, prefer `plan`, `blueprint`, `areaId`, search results, or semantic calls in `/active/ops/build.md`. For exact placement, read `/active/map/viewport.md` (zoom or read `symbols/glyphs.md` when needed), then edit the map markdown by replacing target empty-cell tokens with `建筑名:优先级` and optional `#材料字`. The map route translates these tokens to underlying `building_control build_area` anchors; `/active/ops/build.md` does not accept raw coordinates.

示例:

Prefer one SEARCH/REPLACE block. Multiple blocks require outer `allowPartial=true` and cannot be transactionally rolled back. Each operation-file replacement must contain exactly one executable command. Preview with outer `world_editor edit` `dryRun=true` and `confirm=false` (or omitted); execute with a new edit using outer `dryRun=false` and `confirm=true`, with non-conflicting command flags, then re-read the map or state.

This directly creates a continuous line, with no separate follow-up connection step required.

## 订单与剪断

`orders_control` supports semantic targets and area handles. For an exact rectangle, read `/active/ops/tools.md`, ignore hidden coordinate compatibility entries, then edit `/active/ops/orders.md`, for example `挖 x1=10 y1=20 x2=18 y2=20 priority=7 dryRun=true`. Preview with outer `dryRun=true` and no confirmation; execute only with a new edit using outer `dryRun=false`, `confirm=true`, and non-conflicting command flags.

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

## 相机与视图

`navigation_control` 仅用于相机、覆盖层和截图。支持的动作包括：

- `get_view`：读取当前相机位置、缩放和激活世界。
- `set_active_world`：切换激活世界并移动相机。
- `set_view` / `move`：设置或平移相机。
- `switch_view`：切换氧气、电力、管线、温度等覆盖层，可选截图。
- `focus_cell` / `focus_dupe`：聚焦格子或复制人。
- `screenshot` / `coordinate_screenshot`：保存普通截图或带坐标网格的区域截图。

建造和任务操作应直接使用 `building_control` 与 `orders_control`。每次工具调用必填的 `task` 文本会自动显示在玩家鼠标附近，无需额外定位流程。

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
mods/OniMcp/
├── ModInfo.cs           # KMod 入口
├── Config/              # 选项
├── Core/                # MCP 协议类型
├── Localization/        # STRINGS
├── Patches/             # Harmony 补丁与游戏策略
├── UI/                  # 运行时 Overlay（坐标网格、对话气泡）
├── Server/              # HTTP/MCP 服务
├── Support/             # 日志、路径、反射
└── Tools/
    ├── Core/            # 工具与资源注册
    ├── Entry/           # 聚合入口（*Control* / Read / 英文描述）
    ├── WorldEditor/     # 虚拟世界文件系统
    ├── Shared/          # 共享辅助
    └── Impl/            # 各域实现（Build/Dupes/World/...）
```

Prefer extending aggregate entrypoints in `Tools/Entry/` for new public capabilities. Domain implementations live under `Tools/Impl/<Domain>/`. Shared search, reachability, and material checks belong in `Tools/Shared/`; exact spatial operations are routed through typed files under `/active/ops/`.

## 兼容性说明

旧版工具仍可用于兼容历史客户端，但不再推荐新集成直接依赖。新客户端应:

1. Call `tools/list` to get the 6 default-public entrypoints.
2. 调用 `server_control domain=catalog action=search` 或读取 `oni://tools/guide` 查找目标流程。
3. 优先传语义定位参数。
4. 对危险动作传 `confirm: true`。
5. 执行后读取资源或区域快照验证状态。
