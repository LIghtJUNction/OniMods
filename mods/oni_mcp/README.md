**源代码:** https://github.com/LIghtJUNction/OniMods/

# ONI MCP Server

让 AI 助手直接连接你的缺氧殖民地。安装此 Mod 后，任何支持 MCP（Model Context Protocol）的 AI（如 Claude、Cursor、或其他 MCP 客户端）都可以通过本地 HTTP 接口读取游戏状态、帮你分析局势、甚至执行建造和调度命令。

> 不需要编程知识。只要安装 Mod、启动游戏、配置 AI 客户端，你的 AI 助手就能「看见」并「操作」殖民地。

## 这个 Mod 能做什么

### 🤖 AI 殖民顾问
- **殖民地体检**：AI 自动扫描氧气、食物、电力、温度，发现风险并给出建议
- **电力审计**：检测供电缺口、电池状态、导线负载，预防停电
- **热管理**：扫描过热风险建筑，提前预警设备损坏
- **房间规划**：检查士气房间是否成型，发现缺失的房间类型

### 🎮 语音/文字指挥
- 对 AI 说"给我暂停游戏并截图" → AI 调用 `game_pause` + `take_screenshot`
- 说"把复制人张三的挖掘优先级调到最高" → AI 调用 `dupes_priority_set`
- 说"在 (100, 200) 位置建两张床" → AI 调用 `buildings_plan`
- 说"检查火箭状态" → AI 调用 `rockets_status`

### 📊 实时数据面板
通过 `oni://...` 资源地址，AI 可以实时读取：
- 殖民地状态、诊断、警报
- 资源库存、食物储备
- 复制人列表和需求
- 电力系统摘要
- 房间系统状态
- 建筑过热风险

### 🛡️ 安全控制
- 所有修改操作都有风险等级标注
- 危险操作（挖掘、拆除）需要显式确认
- 支持规划-验证-执行门禁（Plan Harness），AI 先计划、你确认、再执行

## 安装

### 前提
- 缺氧游戏（Steam 版）
- 安装 Mod：将 `OniMcp.zip` 解压到游戏 `mods/` 目录，或通过 Steam 创意工坊订阅

### 启动
1. 启动缺氧游戏
2. 在主菜单 **Mod** → 勾选 **ONI MCP Server**
3. 加载或创建殖民地
4. MCP 服务器自动在 `http://localhost:8787/mcp/` 启动

### 连接 AI

#### Claude Desktop 配置
编辑 `~/Library/Application Support/Claude/claude_desktop_config.json`（macOS）或 `%APPDATA%\Claude\claude_desktop_config.json`（Windows）：

```json
{
  "mcpServers": {
    "oni": {
      "url": "http://localhost:8787/mcp/"
    }
  }
}
```

重启 Claude Desktop，在对话中输入"检查我的殖民地状态"即可开始使用。

#### 其他 MCP 客户端
任何支持 MCP Streamable HTTP 传输的客户端均可连接。配置 `url: http://localhost:8787/mcp/` 即可。

## Mod 配置

创建或编辑 `OniMcpConfig.json`（Mod 目录或游戏持久化目录均可）：

```json
{
  "Host": "localhost",
  "Port": 8787,
  "ScreenshotCleanupEnabled": true,
  "ScreenshotRetentionMinutes": 120,
  "ScreenshotMaxFiles": 40
}
```

- `Host`: 默认 `localhost`。如需局域网连接改为 `0.0.0.0`
- `Port`: 默认 `8787`
- `ScreenshotCleanupEnabled`: 自动清理 AI 截图
- `ScreenshotRetentionMinutes`: 截图保留时间
- `ScreenshotMaxFiles`: 最多保留截图数

修改后重启游戏生效。

## 功能详情

### 工具（Tools）

约 **320 个工具** 覆盖游戏全系统，按分组包括：

| 系统 | 代表工具 | 能力 |
|------|----------|------|
| 殖民地 | `colony_status`, `colony_diagnostics`, `colony_alerts` | 状态、诊断、警报 |
| 复制人 | `dupes_list`, `dupes_detail`, `dupes_needs`, `dupes_attributes` | 列表、详情、需求、属性 |
| 电力 | `power_summary` | 电力系统摘要 |
| 房间 | `rooms_list` | 房间系统状态 |
| 温度 | `thermal_overheat_risk_scan` | 过热风险扫描 |
| 建筑 | `buildings_search_defs`, `buildings_plan`, `buildings_deconstruct` | 搜索、规划、拆除 |
| 订单 | `orders_dig`, `orders_sweep`, `orders_harvest` | 挖掘、清扫、收割 |
| 火箭 | `rockets_list`, `rockets_status`, `rockets_request_launch` | 列表、状态、发射 |
| 农业 | `farming_planting`, `farming_harvestables` | 种植、收获 |
| 研究 | `research_status`, `set_research` | 状态、设置 |
| 日程 | `schedule_list`, `schedule_set_block` | 列表、设置 |
| 资源 | `resources_inventory`, `resources_food` | 库存、食物 |
| 相机 | `camera_move`, `camera_switch_view`, `take_screenshot` | 移动、切换覆盖层、截图 |
| 世界 | `world_cell_info`, `world_text_map` | 格子信息、文本地图 |
| 游戏控制 | `game_pause`, `game_set_speed`, `save_game` | 暂停、速度、存档 |
| 元工具 | `tools_call_many`, `plan_harness_create` | 批量调用、规划门禁 |

### 提示词（Prompts）

7 个内置场景化 Prompt，一键启动标准工作流：

- `colony_triage` — 殖民地体检
- `power_audit` — 电力审计
- `rooms_overview` — 房间概览
- `thermal_audit` — 热管理审计
- `next_cycle_plan` — 下一周期计划
- `inspect_area` — 区域地图分析
- `dupe_care_review` — 复制人照护检查

### 资源（Resources）

通过 `oni://` URI 实时读取游戏状态：

- `oni://colony/status` — 殖民地状态
- `oni://power/summary` — 电力摘要
- `oni://rooms/list` — 房间列表
- `oni://thermal/overheat-risk` — 过热风险
- `oni://resources/inventory` — 资源库存
- `oni://dupes` — 复制人列表
- `oni://rockets/status` — 火箭状态
- `oni://world/text-map` — 文本地图
- 以及 100+ 其他资源...

### 风险等级

- **read**（只读）：查询状态，不修改存档
- **write**（写入）：修改设置、优先级、过滤器
- **execute**（执行）：下达动作命令
- **dangerous**（危险）：挖掘、拆除等大面积改变，需要 `confirm: true`

## 技术细节

- **传输协议**：Streamable HTTP（JSON-RPC 2.0），默认端口 8787
- **序列化**：Newtonsoft.Json（游戏自带）
- **HTTP 服务器**：System.Net.HttpListener（.NET Framework 内置）
- **代码注入**：Harmony（游戏自带）
- **所有游戏 API 调用**在 Unity 主线程执行，保证稳定性

## 构建

```bash
onim build -m oni_mcp
```

发布包只包含 `OniMcp.dll`、元数据、预览图和资源文件，无需额外运行库。

## 开发

项目结构：

```
mods/oni_mcp/
├── Core/           - MCP 协议类型定义
├── Server/         - HTTP 服务器 + 主线程桥接
├── Tools/          - 游戏操作工具实现
├── ModInfo.cs      - Mod 入口
└── OniMcp.csproj   - 项目配置
```

添加新工具：在 `Tools/` 下创建新的 `McpTool` 并注册到 `OniToolRegistry.Initialize()`。

## 制作与测试

本 Mod 由 **gpt5.5**（消耗约 5 亿 token）、**Kimi k2.6**（杂活） 和玩家 **LIghtJUNction**（资金和施加约束） 联合开发与测试。源代码已完全开源，欢迎查看和贡献。
