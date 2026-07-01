# ONI MCP API 开发者指南

本文档面向开发者和高级用户，说明如何通过 HTTP JSON-RPC 2.0 与 Oxygen Not Included MCP 服务器交互。

**API 稳定性警告:** 在 `oni_mcp` 发布 `1.0.0` 之前，HTTP 行为、工具名称、参数结构、资源路径和返回字段都可能发生不兼容变更。第三方客户端应锁定目标版本，运行时读取 `server_control domain=catalog action=manifest` 或 `oni://tools/manifest`，并为字段缺失、重命名和语义调整预留兼容逻辑。

## 快速开始

### 1. 启动服务器

1. 将 `oni_mcp` Mod 安装到缺氧游戏的 `mods/` 目录。
2. 启动游戏并启用 **ONI MCP Server**。
3. 加载存档。
4. MCP 服务器默认在 `http://localhost:8788/mcp/` 启动。

如需局域网访问，创建或编辑 `OniMcpConfig.json`:

```json
{
  "Host": "0.0.0.0",
  "Port": 8788,
  "AuthEnabled": true,
  "AuthToken": "replace-with-a-strong-token"
}
```

启用认证时，客户端应发送:

```text
Authorization: Bearer <token>
```

也兼容:

```text
X-Oni-Mcp-Token: <token>
```

### 2. 配置 MCP 客户端

Claude Desktop / Cursor 的示例配置:

```json
{
  "mcpServers": {
    "oni": {
      "url": "http://localhost:8788/mcp/"
    }
  }
}
```

### 3. 协议握手

第一个请求必须是 `initialize`:

```bash
curl -sS -X POST http://localhost:8788/mcp/ \
  -H 'Content-Type: application/json' \
  -H 'Mcp-Protocol-Version: 2025-11-25' \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
      "protocolVersion": "2025-11-25",
      "capabilities": {},
      "clientInfo": { "name": "cli", "version": "1.0" }
    }
  }'
```

服务端响应头会包含 `Mcp-Session-Id`。后续请求必须携带:

```text
Mcp-Session-Id: <session id>
Mcp-Protocol-Version: 2025-11-25
```

## 推荐工具面

默认公开 8 个核心聚合工具:

| 工具 | 用途 |
|------|------|
| `server_control` | 服务、目录、工具搜索、批量、agent program |
| `read_control` | 世界、区域、资源、建筑、机制知识、基础设施摘要 |
| `game_control` | 暂停、调速、存档、沙盒、UI |
| `navigation_control` | 相机、覆盖层、截图、可视指针、点击和拖拽 |
| `building_control` | 建造规划、材料、配置、储存、过滤、生产、侧屏、火箭 |
| `orders_control` | 区域订单、优先级、指定/取消、剪断 |
| `dupes_control` | 复制人状态、命令、优先级、改名、技能、分配 |
| `colony_control` | 快照、报告、诊断、通知、管理、农牧 |

旧版细粒度工具仍隐藏注册为 compatibility。新集成应优先使用 8 个聚合入口。

## 参数设计约定

新工具面采用搜索/动作优先:

- 优先传 `query`、`target`、`search`、`name`、`id`、`areaId`。
- `x/y`、`x1/y1/x2/y2` 仍支持，但作为精确坐标后备。
- 写入和执行动作应支持 `dryRun` 或 `confirm`。
- 面向任务的结果应返回 `reachable`、`executable`、失败原因、缺失条件和建议下一步。
- 建造相关结果应返回材料可行性，至少说明需要材料、可用材料和缺口。

## 调用示例

### 列出工具

```bash
curl -sS -X POST http://localhost:8788/mcp/ \
  -H 'Content-Type: application/json' \
  -H "Mcp-Session-Id: $SID" \
  -H 'Mcp-Protocol-Version: 2025-11-25' \
  -d '{
    "jsonrpc": "2.0",
    "id": 2,
    "method": "tools/list",
    "params": {}
  }'
```

### 搜索工具

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "tools/call",
  "params": {
    "name": "server_control",
    "arguments": {
      "domain": "catalog",
      "action": "search",
      "query": "wire build material",
      "detail": "brief"
    }
  }
}
```

### 读取殖民地状态

```json
{
  "jsonrpc": "2.0",
  "id": 4,
  "method": "resources/read",
  "params": {
    "uri": "oni://colony/status"
  }
}
```

### 定义区域

```json
{
  "jsonrpc": "2.0",
  "id": 5,
  "method": "tools/call",
  "params": {
    "name": "read_control",
    "arguments": {
      "domain": "area",
      "action": "define",
      "x1": 10,
      "y1": 20,
      "x2": 20,
      "y2": 30,
      "label": "starter-dig"
    }
  }
}
```

### 预览蓝图和材料

```json
{
  "jsonrpc": "2.0",
  "id": 6,
  "method": "tools/call",
  "params": {
    "name": "building_control",
    "arguments": {
      "domain": "planning",
      "action": "preview",
      "prefabId": "ManualGenerator",
      "material": "CopperOre",
      "query": "near printing pod"
    }
  }
}
```

预期结果包含类似字段:

```json
{
  "reachable": true,
  "executable": true,
  "materials": {
    "requirementKnown": true,
    "requiredKg": 400.0,
    "selectedAvailableKg": 910.0,
    "satisfied": true,
    "shortageKg": 0.0,
    "availableMaterials": [
      { "tag": "CopperOre", "availableKg": 910.0 }
    ]
  }
}
```

如果 `materials.satisfied=false`，客户端应向用户展示需求、可用材料和缺口，不应继续执行建造。

### 放置自动连接电线

```json
{
  "jsonrpc": "2.0",
  "id": 7,
  "method": "tools/call",
  "params": {
    "name": "building_control",
    "arguments": {
      "domain": "planning",
      "action": "build_area",
      "prefabId": "Wire",
      "material": "CopperOre",
      "x": 10,
      "y": 20,
      "x2": 18,
      "y2": 20,
      "confirm": true
    }
  }
}
```

`Wire`、`LogicWire`、`GasConduit`、`LiquidConduit`、`SolidConduit` 在传入 `points`、`anchors` 或端点坐标时会自动按路径连接，不需要再调用单独连接步骤。

### 剪断线路

```json
{
  "jsonrpc": "2.0",
  "id": 8,
  "method": "tools/call",
  "params": {
    "name": "orders_control",
    "arguments": {
      "domain": "conduit",
      "action": "cut_conduits",
      "areaId": "starter-wire",
      "type": "auto",
      "confirm": true
    }
  }
}
```

`type=auto` 默认包含气管、液管、固体轨道、电线和逻辑线。只剪电线可传 `type=wire`，只剪逻辑线可传 `type=logic`。

## 资源读取

常用资源:

| URI | 说明 |
|-----|------|
| `oni://colony/status` | 周期、复制人数、速度、暂停状态 |
| `oni://colony/diagnostics` | 缺氧、断粮、过热等诊断 |
| `oni://colony/summary` | 面向行动规划的殖民地摘要 |
| `oni://resources/inventory` | 资源库存 |
| `oni://resources/food` | 食物库存和保质信息 |
| `oni://dupes/status-check` | 复制人位置、差事、需求和疑似不可达 |
| `oni://power/summary` | 电网摘要和电池状态 |
| `oni://power/ports` | 电力接口格、锚点、接线点和端口是否已有电线 |
| `oni://world/text-map` | 文本地图 |
| `oni://buildings/defs` | 可建造建筑定义 |
| `oni://tools/manifest` | 工具清单 |
| `oni://tools/guide` | 按目标推荐工具链 |

## Agent Program

`server_control domain=program action=execute` 可执行小型工具脚本。建议只用于结构明确的短流程，并先用 `dryRun=true` 验证结构和工具名。

核心语义:

- `saveAs` 保存工具返回 JSON。
- `$name.path` 读取变量路径。
- 表达式支持 `eq/ne/lt/lte/gt/gte/and/or/not/add/sub/mul/div/mod/contains/exists`。
- `maxSteps` 限制执行步数。

## 开发目录

```text
mods/oni_mcp/Tools/
├── Core/      # 工具和资源注册
├── New/       # 默认公开的 8 个聚合工具入口和英文描述
├── Shared/    # 搜索、定位、JSON、工具辅助逻辑
└── Legacy/    # 隐藏兼容注册和旧版细粒度实现
```

开发建议:

1. 新公开能力优先扩展 `Tools/New/` 的聚合入口。
2. 旧版兼容逻辑放在 `Tools/Legacy/Impl/`。
3. 共享搜索、材料、可达性、坐标后备逻辑放在 `Tools/Shared/`。
4. 默认公开工具的说明维护在 `CoreToolEnglishDescriptions`，保持英文。
5. 使用 `server_control domain=catalog action=static_audit` 和 `manifest` 验证注册结果。

## 客户端兼容建议

- 不要硬编码旧版细粒度工具列表。
- 不要假设坐标是唯一定位方式。
- 先读取 manifest，再按 `domain/action` 组织调用。
- 对缺失字段、未知 action 和 `executable=false` 做兼容处理。
- 对危险动作始终要求用户确认，并在执行后读取状态验证。
