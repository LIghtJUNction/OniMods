# ONI MCP API 开发者指南

本文档面向开发者和高级用户，描述如何通过 HTTP JSON-RPC 2.0 与缺氧（Oxygen Not Included）MCP 服务器进行协议级交互。

---

## 快速开始

### 1. 启动 MCP 服务器

- 将 `oni_mcp` Mod 安装到缺氧游戏的 `mods/` 目录
- 启动游戏并加载存档
- MCP 服务器自动在 `http://localhost:8787/mcp/` 启动

如需允许局域网访问，创建 `OniMcpConfig.json`：

```json
{ "Host": "0.0.0.0", "Port": 8787 }
```

### 2. 配置 MCP 客户端

Claude Desktop / Cursor 的 `mcpServers` 配置：

```json
{
  "mcpServers": {
    "oni": { "url": "http://localhost:8787/mcp/" }
  }
}
```

### 3. 发送第一个请求（initialize）

首次连接不需要 `Mcp-Session-Id`，服务器会分配一个新的。响应头中会携带 `Mcp-Session-Id` 和 `Mcp-Protocol-Version`，后续请求必须带上这两个 header。

```bash
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" \
  -H "Mcp-Protocol-Version: 2025-11-25" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "initialize",
    "params": {
      "protocolVersion": "2025-11-25",
      "capabilities": { "sampling": {}, "tasks": {} },
      "clientInfo": { "name": "my-client", "version": "1.0.0" }
    }
  }'
```

响应示例：

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "protocolVersion": "2025-11-25",
    "capabilities": {
      "tools": { "listChanged": false },
      "prompts": { "listChanged": false },
      "resources": { "subscribe": false, "listChanged": false },
      "tasks": { "list": {}, "cancel": {}, "requests": { "tools": { "call": {} } } }
    },
    "serverInfo": { "name": "OniMcp", "version": "0.1.4" }
  }
}
```

保存响应头中的 `Mcp-Session-Id`（例如 `a1b2c3d4...`）和 `Mcp-Protocol-Version`，后续所有请求都要带上。

---

## 协议详解

### HTTP 传输层

| 方法 | 行为 |
|------|------|
| `POST /mcp/` | 发送 JSON-RPC 请求，返回响应 |
| `GET /mcp/` | 校验 header/session 后返回 `405`（当前不支持 SSE） |
| `DELETE /mcp/` | 终止当前会话，返回 `204` |
| `OPTIONS /mcp/` | CORS 预检，返回 `204` |

### Header 规范

| Header | 方向 | 说明 |
|--------|------|------|
| `Mcp-Session-Id` | 请求/响应 | 会话标识。initialize 可省略，响应中分配 |
| `Mcp-Protocol-Version` | 请求/响应 | 支持 `2025-11-25` |
| `Content-Type` | 请求 | 必须设为 `application/json` |

非 `initialize` 请求缺失 header 或 session 不存在/已终止，返回 `400` 或 `404`。

### 会话管理

- **创建**：首次 `initialize` 不带 `Mcp-Session-Id` 时，服务器创建新 session 并在响应头返回
- **复用**：后续请求必须携带相同的 `Mcp-Session-Id` 和协商后的 `Mcp-Protocol-Version`
- **终止**：发送 `DELETE /mcp/` 或服务器关闭时清除
- **版本协商**：客户端声明版本，服务器返回协商后的版本存入 session

```bash
# 终止会话
curl -X DELETE http://localhost:8787/mcp/ \
  -H "Mcp-Session-Id: YOUR_SESSION_ID" \
  -H "Mcp-Protocol-Version: 2025-11-25"
```

### JSON-RPC 2.0 消息格式

**请求**（通知可省略 `id`）：

```json
{ "jsonrpc": "2.0", "id": 1, "method": "tools/list", "params": {} }
```

**成功响应**：

```json
{ "jsonrpc": "2.0", "id": 1, "result": { ... } }
```

**错误响应**：

```json
{ "jsonrpc": "2.0", "id": 1, "error": { "code": -32601, "message": "Method not found" } }
```

**通知**（无 `id`）：服务器返回 HTTP `202 Accepted`，在后台处理。

---

## 工具调用

以下示例均假设已设置 `SID` 和 `VER` 环境变量：

```bash
export SID="YOUR_SESSION_ID"
export VER="2025-11-25"
```

### 同步调用

```bash
# tools/list
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'

# tools/call
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"colony_status","arguments":{}}}'
```

工具参数通过 `arguments` 传递，类型为 `JObject`（键值对）。

### 异步任务调用

在 `tools/call` 的 `params` 中增加 `task` 字段，立即返回任务信息，工具在 Unity 主线程异步执行：

```bash
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{
    "jsonrpc": "2.0", "id": 4,
    "method": "tools/call",
    "params": {
      "name": "colony_diagnostics",
      "arguments": {},
      "task": { "title": "Colony diagnostics scan", "ttl": 600000 }
    }
  }'
```

返回任务 ID：

```json
{
  "jsonrpc": "2.0", "id": 4,
  "result": {
    "task": {
      "taskId": "abc123", "status": "working",
      "statusMessage": "Calling tool colony_diagnostics",
      "createdAt": "2026-05-24T05:00:00Z",
      "lastUpdatedAt": "2026-05-24T05:00:00Z",
      "ttl": 600000, "pollInterval": 1000
    },
    "_meta": { "related-task": { "taskId": "abc123" } }
  }
}
```

查询任务状态和结果：

```bash
# 列出所有任务
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":5,"method":"tasks/list","params":{}}'

# 获取单个任务
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":6,"method":"tasks/get","params":{"taskId":"abc123"}}'

# 获取任务结果
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":7,"method":"tasks/result","params":{"taskId":"abc123"}}'

# 取消任务
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":8,"method":"tasks/cancel","params":{"taskId":"abc123"}}'
```

### 批量调用

`tools_call_many` 一次顺序调用最多 20 个工具，支持 `dryRun` 预检和 `defaults` 合并。默认返回 `responseMode: "summary"`，避免批量读把完整子工具内容重复嵌套到响应里：

```bash
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{
    "jsonrpc": "2.0", "id": 9,
    "method": "tools/call",
    "params": {
      "name": "tools_call_many",
      "arguments": {
        "dryRun": true, "requireAllValid": true,
        "responseMode": "summary",
        "defaults": { "confirm": true },
        "calls": [
          { "name": "game_pause", "arguments": {} },
          { "name": "colony_status", "arguments": {} },
          { "name": "power_summary", "arguments": { "detail": "brief" } }
        ]
      }
    }
  }'
```

低 Token 批量形态（字段缩写）：

```json
{
  "items": [
    { "t": "game_pause", "a": {} },
    { "t": "colony_status", "a": {} }
  ],
  "defaults": { "detail": "brief" }
}
```

预检通过后去掉 `dryRun` 再次请求即可执行。`stopOnError: true` 会在首个执行错误处停止。需要完整子工具返回时传 `responseMode: "full"`；只关心失败项时传 `responseMode: "errors"`。

建造蓝图不再暴露直接坐标规划工具。先用 `buildings_search_defs` / `buildings_materials` 选择建筑和材料，再用可视 agent 指针执行：`agent_pointer_jump` 或 `agent_pointer_aim_cell` → `agent_pointer_select_tool tool=build` → `agent_pointer_left_click` 或 `agent_pointer_hold_left`。

`buildings_search_defs` 会返回 `placement`，其中 `anchor=lowerLeftCell` 表示指针格是建筑 footprint 的左下锚点，不是视觉中心。`agent_pointer_left_click dryRun=true` 会返回预期 footprint；实际执行后返回 `placementCheck`，后续必须用 `world_area_snapshot` / `world_text_map` 复核。

对 `BuildLocationRule=OnFloor` 的建筑，必须先通过指针放置或确认下方已有实体地板/支撑。电线、管道、梯子和砖块的水平/垂直路线使用多段 `agent_pointer_hold_left`。床、厕所、机器等宽/高大于 1 的建筑默认拒绝拖拽建造，必须逐个 anchor 用 `agent_pointer_left_click` 放置；只有明确需要重复 footprint 时才传 `allowFootprintDrag=true`。

快速直线示例：先把指针跳到起点，选择 `Wire`，再 `agent_pointer_hold_left direction=right length=9 confirm=true`。折线拆成多段水平/垂直拖拽。

---

## 资源和 Prompt

### 读取资源

```bash
# 列出固定资源
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":10,"method":"resources/list","params":{}}'

# 读取固定 URI
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":11,"method":"resources/read","params":{"uri":"oni://colony/status"}}'

# 列出模板资源
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":12,"method":"resources/templates/list","params":{}}'

# 读取模板资源（plain 文本地图）
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{
    "jsonrpc": "2.0", "id": 13,
    "method": "resources/read",
    "params": { "uri": "oni://world/text-map?x1=100&y1=50&x2=120&y2=70&encoding=plain&profile=standard" }
  }'
```

### 获取 Prompt

```bash
# 列出 prompt
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":14,"method":"prompts/list","params":{}}'

# 获取带参数的 prompt
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{
    "jsonrpc": "2.0", "id": 15,
    "method": "prompts/get",
    "params": {
      "name": "inspect_area",
      "arguments": { "x1": "100", "y1": "50", "x2": "120", "y2": "70" }
    }
  }'
```

---

## 客户端能力

### initialize 时声明

在 `initialize` 的 `params.capabilities` 中声明：

```json
{ "sampling": {}, "elicitation": {}, "tasks": {} }
```

### 服务端暴露

```bash
# 查看当前会话的客户端能力
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":16,"method":"tools/call","params":{"name":"mcp_client_capabilities","arguments":{}}}'

# 生成 sampling 请求对象
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{
    "jsonrpc": "2.0", "id": 17,
    "method": "tools/call",
    "params": {
      "name": "mcp_sampling_request_create",
      "arguments": {
        "messages": [{ "role": "user", "content": { "type": "text", "text": "Describe this" } }]
      }
    }
  }'
```

---

## 错误处理

### HTTP 状态码

| 状态码 | 含义 |
|--------|------|
| `200` | 正常响应（即使 JSON-RPC 内部错误也在 body 中） |
| `202` | 通知已接受 |
| `204` | 会话终止成功 |
| `400` | 缺少或无效的 header |
| `404` | 会话不存在或已终止 |
| `405` | 不支持的方法（GET 的 SSE） |

### JSON-RPC 错误码

| 错误码 | 名称 | 触发场景 |
|--------|------|----------|
| `-32700` | Parse Error | 请求体不是合法 JSON |
| `-32600` | Invalid Request | `jsonrpc` 不是 `2.0`，或缺少必要 header |
| `-32601` | Method Not Found | 调用了未实现的方法 |
| `-32602` | Invalid Params | 参数缺失、类型错误或工具名不存在 |
| `-32603` | Internal Error | 工具执行抛出异常 |

---

## 工作流示例

### 示例 1：殖民地体检

```bash
# Step 1: initialize（提取 Session ID）
RESP=$(curl -s -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Protocol-Version: 2025-11-25" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{"tasks":{}},"clientInfo":{"name":"cli","version":"1.0"}}}')
SID=$(echo "$RESP" | grep -oP 'Mcp-Session-Id: \K[^\r]+' || echo "")

# Step 2: tools/list → colony_status → colony_diagnostics → resources/read
for P in '{"method":"tools/list","params":{}}' \
         '{"method":"tools/call","params":{"name":"colony_status","arguments":{}}}' \
         '{"method":"tools/call","params":{"name":"colony_diagnostics","arguments":{}}}' \
         '{"method":"resources/read","params":{"uri":"oni://colony/diagnostics"}}'; do
  curl -X POST http://localhost:8787/mcp/ \
    -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: 2025-11-25" \
    -d "{\"jsonrpc\":\"2.0\",\"id\":$((++ID)),$P"
done
```

### 示例 2：电力审计（异步任务）

```bash
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{
    "jsonrpc": "2.0", "id": 20,
    "method": "tools/call",
    "params": {
      "name": "power_summary",
      "arguments": { "detail": "full" },
      "task": { "title": "Power audit" }
    }
  }'
```

### 示例 3：批量执行建造计划

```bash
# 1. 创建计划
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{
    "jsonrpc": "2.0", "id": 30,
    "method": "tools/call",
    "params": {
      "name": "plan_harness_create",
      "arguments": {
        "goal": "Expand oxygen production",
        "plannedCalls": [
          { "name": "agent_pointer_jump", "arguments": { "x": 120, "y": 60 } },
          { "name": "agent_pointer_select_tool", "arguments": { "tool": "build", "prefabId": "Electrolyzer", "material": "auto" } },
          { "name": "agent_pointer_left_click", "arguments": { "confirm": true } }
        ]
      }
    }
  }'

# 2. 验证（替换 PLAN_ID）
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":31,"method":"tools/call","params":{"name":"plan_harness_validate","arguments":{"planId":"PLAN_ID"}}}'

# 3. 执行
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":32,"method":"tools/call","params":{"name":"plan_harness_execute","arguments":{"planId":"PLAN_ID","confirm":true}}}'
```

---

## 进阶主题

### 低 Token 模式

大量工具支持 `detail` 参数：`brief`（极简）、`compact`（紧凑）、`detail`（完整）。部分批量工具支持字段缩写：`t` → `name`，`a` → `arguments`。RLE 编码的文本地图适合大范围初扫。

```bash
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":40,"method":"tools/call","params":{"name":"tools_search","arguments":{"query":"power","detail":"brief"}}}'
```

### 风险工具调用

工具描述中带 `[group/mode/risk]` 前缀，`dangerous` 等级（如挖掘、拆除）要求传入 `confirm: true`：

```bash
curl -X POST http://localhost:8787/mcp/ \
  -H "Content-Type: application/json" -H "Mcp-Session-Id: $SID" -H "Mcp-Protocol-Version: $VER" \
  -d '{"jsonrpc":"2.0","id":50,"method":"tools/call","params":{"name":"buildings_deconstruct","arguments":{"buildingId":"B123","confirm":true}}}'
```

建议通过 `plan_harness_create` → `plan_harness_validate` → `plan_harness_execute` 对危险操作进行计划、验证和门禁执行。

### 性能优化

1. **缓存工具元信息**：`tools/list` 是懒暴露层，首次调用后缓存结果，完整列表用 `tools_manifest`
2. **批量调用减少往返**：用 `tools_call_many` 代替多次单独 `tools/call`
3. **异步任务**：长耗时工具（大范围地图扫描）用 `task` 模式避免阻塞

---

## 参考

### 完整工具列表

详见 [mcp-tools-reference.md](mcp-tools-reference.md)。

### 常见问题

**Q: 连接不上？**  
A: 确认游戏已加载存档且 Mod 已启用。检查 `OniMcpConfig.json` 的 `Host`/`Port`，以及防火墙设置。服务器地址会输出到游戏日志。

**Q: 请求返回 400？**  
A: 非 `initialize` 请求必须同时携带 `Mcp-Session-Id` 和 `Mcp-Protocol-Version`，且版本必须与 session 协商值一致。

**Q: 请求超时？**  
A: 游戏主线程可能在加载或暂停。长耗时操作使用 `task` 异步模式。任务默认 TTL 为 10 分钟。

**Q: 如何调试？**  
A: 使用 `curl -v` 查看完整 HTTP 交互。检查响应头和 JSON-RPC 错误码。游戏日志（`Player.log`）会输出 MCP 服务器异常信息。
