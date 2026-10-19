# ONI MCP 工具参考

本文档描述 `OniMcp` 当前推荐的工具面。运行时清单永远以 `server_control domain=catalog action=manifest` 和 `oni://tools/manifest` 为准。

## 快速开始

- MCP 地址: `http://localhost:8788/mcp/`
- 协议兼容: `2026-07-28` 无会话兼容路径 + `2025-11-25` / `2025-06-18` legacy initialize/session 路径
- Legacy/default public tools: 7 entrypoints: `benchmark`, `world_editor`, `game_control`, `navigation_control`, `building_control`, `orders_control`, `server_control`
- Modern `2026-07-28` `tools/list`: 当前只广告只读 `benchmark`
- 旧聚合入口: 仅作为虚拟文件工作流的内部操作，不再注册为 MCP 工具
- `coordinate_control` 不属于当前公开运行时；普通聚合工具拒绝 raw coordinates
- Tool descriptions: default-public tool descriptions and parameter descriptions are in English

现代 `2026-07-28` 请求不使用 `initialize`，也不要求或返回 `Mcp-Session-Id`。每个请求携带 `_meta`、`MCP-Protocol-Version` 和 `Mcp-Method`；`resources/read` / `tools/call` 等有具体资源或工具名的请求还需匹配的 `Mcp-Name`。推荐先调用 `server/discover` 并以实际 capability 为准。

Legacy `2025-11-25` / `2025-06-18` 客户端继续先调用 `initialize`，随后请求携带协商得到的 `Mcp-Session-Id` 和 `Mcp-Protocol-Version`。

## 定位与执行原则

Authoritative model:

- Saves are directories. `latest/` is the fixed alias for the current/latest save.
- `cd latest` enters a save; `cd` or `cd ~` exits back to `/`, representing the main menu/root.
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
public surface also keeps `benchmark`, `game_control`, `navigation_control`,
`building_control`, `orders_control`, and `server_control` available for direct
focused calls. Other former aggregate entrypoints are internal-only virtual-file
operations and are not callable as MCP tools.

新工具面按搜索/动作优先设计:

- 优先使用 `query`、`target`、`search`、`name`、`id`、`areaId`。
- For dedicated search, use `world_editor command=search` or the typed `/active/ops/search.md` workflow. The underlying `search_control` operation is internal and is not a direct MCP tool.
- Public aggregate tools do not accept raw `x/y`, `x1/y1/x2/y2`, `dx/dy`, `points`, or `anchors`. For exact orders, read `/active/ops/tools.md` and edit `/active/ops/orders.md`; use only currently public typed files/tools and ignore hidden `coordinate_control` and `/active/ops/coordinate.md` compatibility entries.
- 面向任务的返回应尽量包含 `reachable`、`executable`、失败原因、缺失条件和建议下一步。
- 需要定义区域并取得 `areaId` 时，通过 `world_editor` 的 typed `/active/ops/read.md` 工作流完成；底层 `read_control` 是 internal operation，不应作为 MCP 工具直接调用。
- 写入、执行和危险动作应支持 `dryRun` 或 `confirm`，并在执行后重新读取状态验证。
- 危险或大范围精确操作必须保持 pause -> read/plan -> dry-run -> confirm -> verify。

## 核心工具

| 工具 | 主要 domain/action | 风险 | 用途 |
|------|--------------------|------|------|
| `benchmark` | `cases`, `iterations`, `tool`, `includeDetails` | read | 固定工具链路基准与诊断，不修改游戏状态 |
| `world_editor` | `cd`, `ls`, `read`, `search`, `edit`, `plan`, `connect` | read/write/execute | 虚拟文件化世界访问、搜索、规划与受控编辑 |
| `server_control` | `catalog`, `batch`, `program` | read/execute | 健康检查、工具清单、工具搜索、目标指南、批量调用、agent program |
| `game_control` | `speed`, `state`, `save`, `sandbox`, `ui` | read/execute/dangerous | 暂停、恢复、调速、存档、沙盒、UI 编辑标记 |
| `navigation_control` | `camera` 或按已知相机 `action` 推断 | execute | 相机移动、世界切换、覆盖层、聚焦和截图 |
| `building_control` | `planning`, `config`, `storage`, `filter`, `production`, `side_surface`, `rocket` | read/write/execute | 建造规划、材料检查、蓝图、建筑侧屏配置、储存过滤、生产队列、火箭 |
| `orders_control` | `area`, `priority`, `designation`, `conduit` | execute/dangerous | 挖掘、清扫、拖地、拆除、优先级、区域订单、线路/管线剪断 |

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

Prefer one SEARCH/REPLACE block. Multiple blocks require outer `allowPartial=true` and cannot be transactionally rolled back. Each operation-file replacement must contain exactly one executable command. Preview with outer `world_editor edit` `dryRun=true` and `confirm=false` (or omitted); execute with a new edit using outer `dryRun=false`, `confirm=true`, and non-conflicting command flags, then re-read the map or state.

This directly creates a continuous line, with no separate follow-up connection step required.

## 文件夹式蓝图管理系统 (Folder-based Blueprints)

`world_editor command=blueprint` 提供了完备的蓝图全生命周期管理，深度集成 Blueprints Expanded v2 格式与原生自动建造回退机制：

| action | 说明 | 示例参数 |
|--------|------|---------|
| `list` / `ls` | 列出分类文件夹与蓝图文件（支持树状和 JSON 格式） | `category: "power"`, `format: "json"` |
| `preview` | 预览蓝图（尺寸、建筑清单、材料用量、网格字符图） | `name: "power/spom"` |
| `create` / `capture` | 从纯文本创建或直接**框选地图矩形捕获现存建筑与管路** | `name: "spom"`, `category: "power"`, `x1: 10, y1: 20, x2: 25, y2: 30` |
| `import` | 从 JSON、Markdown 或 Base64 分享码导入蓝图 | `name: "spom"`, `content: "<json/base64>"` |
| `export` | 导出蓝图为 JSON、Markdown 或 Base64 字符串 | `name: "power/spom"`, `format: "json"` |
| `share` | 将单个蓝图或整个分类打包为分享代码 | `name: "power/spom"` 或 `category: "power"` |
| `rename` | 重命名或跨目录移动蓝图 | `from: "test"`, `to: "power/spom"` |
| `categorize` | 将蓝图移动到分类文件夹 | `name: "spom"`, `category: "power"` |
| `replace_materials` | 批量替换蓝图内的建筑材料 | `name: "power/spom"`, `from: "Sandstone", to: "Granite"` |
| `place` / `use` | 放置蓝图（支持 dryRun 预览、材料动态重定向与原生回退） | `name: "power/spom"`, `x: 15, y: 30, dryRun: false, confirm: true` |
| `cancel` | 取消指定区域或指定蓝图范围内的待建工单 | `name: "power/spom", x: 15, y: 30` 或 `x1: 15, y1: 30, x2: 25, y2: 40` |
| `delete` | 删除指定蓝图文件或空目录 | `name: "power/spom"` |

- **视觉反馈**：Agent 进行预览、手持蓝图准备放置、成功放置、捕获、材料替换时，会在玩家鼠标光标附近实时弹出 `ToolCallSpeechOverlay` 对话气泡（例如 `📐 手持蓝图 [ SPOM ] 准备放置于 (15,30) [预览]`）。

---

## 存档隔离的文件夹式游戏笔记工具 (Save-Isolated Notes & AGENTS.md)

虚拟文件系统 `/active/notes/` 与 `/active/ops/colony.md` (notes 域) 提供按当前活跃游戏存档物理隔离的笔记管理系统：

- **自动生成 `AGENTS.md`**：每个存档的 `notes/AGENTS.md` 自动初始化为该存档专属的核心背景文档，包含殖民地战略目标、防爆温/防爆管心法、禁忌规则（默认禁用自动消毒、严禁盲目开挖液体蓄水池等）及阶段里程碑。
- **子文件夹管理**：支持组织 `plans/`（工程规划）、`milestones/`（阶段日志）、`issues/`（疑难杂症）等目录。
- **操作接口**：可通过 `/active/ops/colony.md` 调度 notes 域动作：
  - `list` [folder="plans"]
  - `read` path="AGENTS.md"
  - `write` path="plans/spom.md" content="..."
  - `append` path="milestones/cycle-50.md" content="..."
  - `rename` from="..." to="..."
  - `delete` path="..."
  - `status`
- **虚拟文件系统支持**：可通过 `world_editor` 读取 `/active/notes/AGENTS.md` 并通过 `SEARCH/REPLACE` 直接编辑。
- **视觉反馈**：读写、更新 AGENTS.md 或追加里程碑日志时，同样在鼠标光标旁弹出实时气泡提示。

---

## 动态资源体系与异步长耗时通知 (Dynamic Resources & Async Notifications)

除了传统的电网与建筑工具，ONI 具备丰富的动态游戏生态状态。OniMcp 将核心状态作为 MCP 原生 **Dynamic Resources (`oni://...`)** 公开，替代繁重的工具轮询：

### 核心殖民地动态资源

| URI | 说明 | 内容格式 |
|-----|------|---------|
| `oni://atmosphere/summary` | 全图大气质量、氧气占比、主要气体分布与气温统计 | JSON |
| `oni://atmosphere/breathability` | 复制人可呼吸格子数、氧气储量、CO2 及窒息风险评估 | JSON |
| `oni://fluid/liquid/summary` | 净水、污水、原油、盐水等液体存量、储液罐与温度分布 | JSON |
| `oni://fluid/gas/summary` | 世界主要气体储量、储气罐与管道通畅度 | JSON |
| `oni://plumbing/summary` | 液体/气体管网网格段数、畅通状态及布局统计 | JSON |
| `oni://thermal/summary` | 世界最高/最低/平均温度场，极端高温与极寒格统计 | JSON |
| `oni://food/summary` | 全殖民地食物卡路里储备、人均可用天数及断粮预警 | JSON |
| `oni://farming/crops` | 种植箱与农砖作物数量、成熟可收获数量及生长分布 | JSON |
| `oni://ranching/summary` | 动物存活总数、按物种分布统计与圈舍拥挤状态 | JSON |
| `oni://dupes/vitals` | 复制人生理指标：生命、压力、饱食、呼吸、膀胱等 | JSON |
| `oni://dupes/morale` | 复制人士气值、士气期望值、净士气差额及高压预警 | JSON |
| `oni://geysers/summary` | 已探明喷孔、类型、喷发元素、温度及坐标 | JSON |
| `oni://research/progress` | 当前研发科技、已解锁科技总数及各级研究点数 | JSON |
| `oni://resources/stockpile` | 按分类汇总的矿物、金属、原材料与工业存量 | JSON |
| `oni://notes/{path}` | 读取当前存档隔离的游戏笔记（如 `AGENTS.md`） | Markdown |
| `oni://blueprints/{name}` | 读取已保存蓝图的完整结构定义 | JSON |

### 异步执行与主动通知 (Method 2: Async + Dynamic Resources Notification)

对于长耗时计算（如大范围扫描、生存长跑分诊、高负载蓝图分析）：
1. **任务提交**：调用操作时附带 `async: true`，服务器立即返回：
   ```json
   {
     "status": "submitted",
     "taskId": "task_20261019_...",
     "resourceUri": "oni://reports/task_20261019_...",
     "message": "任务已提交，完成后结果将写入 oni://reports/... 并主动发送 notifications/resources/list_changed 通知。"
   }
   ```
2. **异步执行**：MCP 服务器在 Unity 主线程调度队列或后台任务池中异步执行，不阻塞客户端调用。
3. **主动通知**：任务结束时，服务器写入该动态报告资源，并通过 SSE 主动广播 `notifications/resources/list_changed` 与 `notifications/resources/updated`。
4. **按需读取**：Agent 接收到通知后，调用 `resources/read` 读取 `oni://reports/task_...` 获取完整结果，实现真正的事件驱动与动态资源分离架构。

---

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

## 尚未就绪的读数

电力端口的 `networkPending=true` 表示电网正在重建、组件电路 ID 与网络不一致，或网络尚不可读。此时 `connected` 和 `circuitId` 为 `null`，不能推断为断电；网络稳定后，`circuitId` 仍使用字符串，`"-1"` 表示没有连接。

建筑配置中的温度传感器需要填满原生八次采样缓冲区并发布平均值。预热期间修改阈值可能提前发布不完整平均值；采样计数达到八次、尚未执行下一次原生发布时也保守返回等待状态。就绪前 `samplePending=true`，`currentValue` 和 `formattedCurrentValue` 为 `null`；不要把初始零值当成零开尔文。其他阈值组件的有限零值仍有效。读取后等待短暂模拟，再重新查询；这两个标记不会自动恢复游戏。

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
| `oni://plumbing/liquid` | 液体管道回路、流动方向向量、流速与堵塞瓶颈 |
| `oni://plumbing/gas` | 气体管道回路、流动方向向量与气体元素分布 |
| `oni://circuits/logic` | 自动化逻辑信号网络（0/1 电平与 4-bit 线束通道） |
| `oni://circuits/power` | 电网摘要、回路负载、发电量与过载状态 |
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

新客户端建议按双协议处理：

1. 优先以 `2026-07-28` 调用 `server/discover`，使用返回的 capability；现代请求不发送 `Mcp-Session-Id`。
2. 若目标服务端只支持旧协议，再走 `initialize` + `Mcp-Session-Id` 的 `2025-11-25` / `2025-06-18` 路径。
3. 不要硬编码旧版细粒度工具列表；按当前协议调用 `tools/list` 或读取运行时 manifest。
4. 调用 `server_control domain=catalog action=search` 或读取 `oni://tools/guide` 查找目标流程。
5. 优先传语义定位参数。
6. 对危险动作传 `confirm: true`。
7. 执行后读取资源或区域快照验证状态。
