# ONI MCP 工具参考

本文档描述 `oni_mcp` 当前推荐的工具面。运行时清单永远以 `server_control domain=catalog action=manifest` 和 `oni://tools/manifest` 为准。

## 快速开始

- MCP 地址: `http://localhost:8788/mcp/`
- 协议版本: `2025-11-25`
- 默认公开工具: 8 个核心聚合入口
- 旧版细粒度工具: 隐藏注册为 compatibility，可按精确名称继续调用
- 工具描述: 默认公开工具的描述和参数描述使用英文

非 `initialize` 请求必须携带会话协商后的 `Mcp-Session-Id` 和 `Mcp-Protocol-Version`。

## 定位与执行原则

新工具面按搜索/动作优先设计:

- 优先使用 `query`、`target`、`search`、`name`、`id`、`areaId`。
- `x/y` 和 `x1/y1/x2/y2` 仍可用，但只作为无法语义定位时的精确后备。
- 面向任务的返回应尽量包含 `reachable`、`executable`、失败原因、缺失条件和建议下一步。
- 区域动作优先先用 `read_control domain=area action=define` 生成 `areaId`，再传给支持区域的工具。
- 写入、执行和危险动作应支持 `dryRun` 或 `confirm`，并在执行后重新读取状态验证。

## 核心工具

| 工具 | 主要 domain/action | 风险 | 用途 |
|------|--------------------|------|------|
| `server_control` | `catalog`, `batch`, `program` | read/execute | 健康检查、工具清单、工具搜索、目标指南、批量调用、agent program |
| `read_control` | `world`, `area`, `resources`, `buildings`, `knowledge`, `infrastructure` | read | 世界地图、区域、资源、建筑、机制知识、电力和房间摘要 |
| `game_control` | `speed`, `state`, `save`, `sandbox`, `ui` | read/execute/dangerous | 暂停、恢复、调速、存档、沙盒、UI 编辑标记 |
| `navigation_control` | `camera`, `pointer` 或按 `action` 推断 | execute | 相机、覆盖层、截图、可视 agent 指针、鼠标格、点击、拖拽 |
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

支持输入:

- `points`: 路径点数组
- `anchors`: 锚点数组
- `x/y -> x2/y2`
- `x1/y1 -> x2/y2`

示例:

```json
{
  "domain": "planning",
  "action": "build_area",
  "prefabId": "Wire",
  "material": "Copper",
  "x": 10,
  "y": 20,
  "x2": 18,
  "y2": 20,
  "confirm": true
}
```

这会直接生成连续线路，不再需要后续单独调用连接步骤。

## 订单与剪断

`orders_control` 支持语义目标、区域句柄和精确坐标。推荐形式:

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
├── New/       # 默认公开的 8 个聚合工具入口和英文描述
├── Shared/    # 搜索、定位、JSON、工具辅助逻辑
└── Legacy/    # 隐藏兼容注册和旧版细粒度实现
```

新增公开能力优先扩展 `Tools/New/` 的聚合入口。旧版兼容逻辑放入 `Tools/Legacy/Impl/`，通过 `Tools/Legacy/LegacyToolRegistry.cs` 注册。共享搜索、可达性、材料检查和坐标后备逻辑放在 `Tools/Shared/`。

## 兼容性说明

旧版工具仍可用于兼容历史客户端，但不再推荐新集成直接依赖。新客户端应:

1. 调用 `tools/list` 获取 8 个公开入口。
2. 调用 `server_control domain=catalog action=search` 或读取 `oni://tools/guide` 查找目标流程。
3. 优先传语义定位参数。
4. 对危险动作传 `confirm: true`。
5. 执行后读取资源或区域快照验证状态。
