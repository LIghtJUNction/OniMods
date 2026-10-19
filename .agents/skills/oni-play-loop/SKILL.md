---
name: oni-mcp-play-loop
description: 当用户要求 agent 通过 MCP 循环游玩 Oxygen Not Included、自动玩一段时间、继续殖民地，或运行暂停-规划-恢复循环时使用。强制执行暂停、观察、规划、执行、短暂恢复、再次暂停和验证的循环，读取 game_control domain=ui uiDomain=edit_mark action=list，限制运行窗口，并在风险或歧义决策前停下等待用户确认。
---

# ONI MCP 游玩循环

## 规划时按阶段选模块

需要决定殖民地下一项工作时，读取 [阶段策略技能](../oni-mcp-colony-strategy/SKILL.md)，只加载当前瓶颈的参考。把短期补给和长期模块分开，先补前置缺口，保留旧设施直到新产出通过验证。报告相关建议时附原文链接；不以旧图纸替代当前精确地图预检。固定的已授权维护任务不需要每轮重读攻略。

## 通信与网络连接规范（上手避坑）

- **强制 IPv4 环回地址**：无论是 `mcporter`、HTTP 客户端还是脚本调用，MCP 地址必须使用 `http://127.0.0.1:8788/mcp/`，**绝对避免使用 `http://localhost:8788/mcp/`**。在 Linux 和 Node.js 环境下，`localhost` 极易优先解析为 IPv6 `::1`，导致 Mono 运行时的 `HttpListener` 挂起或触发 30 秒超时！
- **mcporter CLI 调用标准格式**：
  ```bash
  rtk proxy mcporter call --http-url http://127.0.0.1:8788/mcp/ --allow-http --tool world_editor --args '{"command":"read","path":"/active/colony/status.oni","profile":"minimal"}' --timeout 45000 --output json
  ```
- **原生 curl 直接调用协议要求**：
  直接向 MCP 端口发送 HTTP POST 请求时，必须携带：
  - `Content-Type: application/json`
  - `Accept: application/json, text/event-stream`
  - `Mcp-Protocol-Version: 2025-11-25`
  - `Mcp-Session-Id: <sessionId>`（初始化握手返回的会话 ID）。

## 目的

以短时间、受控循环运行 ONI：

```
pause -> observe -> plan -> execute -> resume briefly -> pause -> verify -> report/next loop
```

agent 绝不能在游戏运行时思考、规划或新增命令。

## 硬规则

- 每个循环都从 `game_control domain=speed action=pause` 开始。
- 读取状态、读取玩家计划、规划、验证和下达命令时保持暂停。
- 只有当前计划足够完整、可以观察进展时才恢复游戏。
- 恢复前先检查 `game_control domain=speed action=step_facts` 的 `immigrantsAvailable`；已就绪先处理本轮打印舱机会，候选列表为空不能视为未就绪。
- 恢复后用短有界窗口，再次暂停并检查结果中的 `immigrantsAvailable`。
- 不要无限串联循环。除非用户明确要求多个循环或连续游玩，否则只跑一个循环。
- 遇到高风险不可逆动作、大范围挖掘、破坏性命令、战斗、保存/读取、沙盒/调试，或接收/打印新复制人前，停下并询问。
- 下达清扫或建造任务前，先确认有实际可执行的候选并检查可达性信息；`orders_complete` 或成功回包后仍须读取目标状态核实。液体用 mop（擦），碎片用 sweep（扫）。

## 循环模板

### 1. 暂停

```
game_control domain=speed action=pause
```

如果已经暂停，继续。

### 2. 观察

优先使用紧凑聚合读取：

```
world_editor command=read path=/active/colony/status.oni profile=minimal
game_control domain=speed action=step_facts
```

每轮先读 `step_facts.immigrantsAvailable`，并优先处理上一窗口返回的同名字段。它取自原生 Immigration 就绪状态，不依赖 HUD 新通知或候选是否物化。

若为 true，在暂停中执行：

1. `building_control domain=side_surface surface=facility kind=printing_pod action=list_rewards`。
2. `printingRewards.available=true` 而 `rewardCount=0`：显式调用同路由 `action=open_immigrants id=<target.id> confirm=true`，再 `list_rewards`。零候选不是未就绪；若打开后仍没有补给候选，记录并明确延后本批，不自动选人，也不反复打开来重抽。
3. 只从 `printingRewards.rewards[]` 中选择 `kind=care_package` 的补给，优先当前缺口需要的物资；用 `action=claim id=<podId> rewardIndex=<该列表index> dryRun=true` 预览，再 `dryRun=false confirm=true` 领取。不要使用混合 UI 卡片索引，也不要自动添加复制人。
4. 核对 `claimed=true`、所选物品和 `printingRewards.available=false`，再按返回计划用公开虚拟文件安排清扫/优先级。已获正常玩法授权时，不为查看或领取补给逐轮重新询问用户；`confirm=true` 是工具参数。

如果明确延后已查看的同一批候选，记下原因，并在该窗口显式省略打印舱条件，避免对仍就绪的同一机会无限立即暂停。除此以外，正常推进将 `printing_pod_ready` 与本轮目标用 `until.any` 组合，例如：

```json
{"domain":"speed","action":"step_until","until":{"any":[{"fact":"printing_pod_ready"},{"fact":"orders_complete"}]},"speed":3,"maxGameSeconds":60,"maxWallSeconds":15}
```

`printing_pod_ready` 表示当前就绪：默认已就绪时在恢复前返回，运行中冷却结束也会暂停；`all` 组合仍须满足其他条件。不要用仅有工单/科研的等待条件长时间忽略打印舱。

只有需要时才通过 `world_editor` 读取针对性虚拟文件（公开 7 工具原生路径）：

- 库存：`world_editor command=read path=/active/resources/inventory.oni`
- 食物：`world_editor command=read path=/active/resources/food.oni`
- 电力：`world_editor command=read path=/active/infrastructure/power.md`
- 房间：`world_editor command=read path=/active/infrastructure/rooms.oni`
- 复制人状态：`world_editor command=read path=/active/dupes/index.oni`
- 视口与地形：`world_editor command=read path=/active/map/viewport.md compact=true syncView=false focusCamera=false`
- 建筑优先级设置：`orders_control domain=priority action=set_building query="建筑名" priority=8`（强调语义 query，非原始坐标）

如果玩家创建了游戏内规划请求，在发明空间计划前先用 `game_control domain=ui uiDomain=edit_mark action=list`。

### 3. 规划

简单、低风险维护循环使用快速路径。用一两句话说明意图；可用时先 dry-run；再用紧凑批处理执行；最后验证：

```
server_control domain=batch action=call_many dryRun=true responseMode=summary requireAllValid=true stopOnError=true items=[...]
server_control domain=batch action=call_many dryRun=false responseMode=summary requireAllValid=true stopOnError=true items=[...]
```

适用于小范围挖掘、短地板、安全配置修改、收获/清扫/拖地命令，以及 dry-run 通过的 utility 路线。

重大计划（多阶段殖民地工作、玩家编辑标记请求、大范围挖掘、危险液体/气体/热量暴露、拆除）先在回复中列出可执行工具调用、关键参数、假设和停止条件；可用时先 dry-run，再执行。

建造：

```
building_control domain=planning action=placement_candidates prefabId=<PrefabId> areaId=<area> limit=8
world_editor command=read path=/active/map/viewport.md
# zoom or read symbols/glyphs.md if needed; replace empty map tokens with 建筑名:优先级[#材料字]
# preview with outer dryRun=true and confirm=false/omitted
# execute with a new edit using outer dryRun=false and confirm=true, then re-read the map
```

挖掘：

```
world_editor command=read path=/active/ops/orders.md
# edit one command such as: 挖 x1=... y1=... x2=... y2=... priority=7 dryRun=true
# execute only in a new edit with outer dryRun=false, confirm=true, and non-conflicting command flags
```

绝不要用 `orders_control domain=designation action=attack` 做挖掘。如果任何工具搜索建议用 attack 处理地形工作，拒绝它并重新搜索/读取。
普通 aggregate tools 拒绝 raw coordinates；精确 orders 走 `/active/ops/orders.md`，精确建造走可编辑 map tokens。读 `/active/ops/tools.md` 时只选当前公开 typed files/tools，忽略 hidden `coordinate_control` 和 `/active/ops/coordinate.md`。默认单 block；operation file 每个 replacement 恰好一条可执行命令。危险或大范围操作必须保持 pause -> read/plan -> dry-run -> confirm -> verify。

### 4. 暂停中执行

只执行本循环所需、已验证且范围明确的调用：

- 小型挖掘/命令批次
- dry-run 通过的建造
- 安全配置修改
- 与目标直接相关的收获或清扫

彼此独立的调用可用 `server_control domain=batch action=call_many responseMode=summary requireAllValid=true stopOnError=true` 小批次执行。若后续调用依赖前一结果，分开执行并检查返回值；不要为了省轮次把命令与推进条件盲目串联。

### 5. 有界推进与结果核对

按本轮目标选择等待条件，明确设置 `maxGameSeconds` 与 `maxWallSeconds`，并在返回后保持暂停。条件、稳定窗口、超时、通知和告警语义见[可组合推进条件](../../../docs/composable-game-waits.md)，避免在技能正文重复枚举。

返回后先读 `stopReason` 和增量字段，再核对本轮目标的实际状态。`alert` 可能匹配既有 Bad 通知；快速返回不等于 `pauseCount` 故障，也不能单独证明推进成功。`orders_complete`、剩余工单为零或工具回包成功都不能替代对建成物、可达性或任务候选的检查。继续遵守循环中的打印舱处理规则；不要因候选列表为空而忽略 `immigrantsAvailable`。

## 停止条件

以下情况停止游玩循环并报告：

- `world_editor command=read path=/active/dupes/index.oni` 中有复制人处于危险状态（窒息、受困、极端温度）或状态快照报警
- 食物、氧气、温度或电力出现新的 critical 警报
- 蓝图/命令反复被阻塞
- 玩家给出新指示
- 下一步需要大范围挖掘、战斗、拆除、保存/读取、沙盒/调试或重大重设计
- 不确定性依赖文本地图没有表达的视觉判断

## 循环中的优先级

前期殖民地优先顺序：

1. 复制人安全：卡住、窒息、饥饿、危险温度。
2. 厕所和睡眠基础。
3. 食物和可收获物。
4. 机器前先确保稳定路径/地板。
5. 电力/研究必须在支撑和电线路径有效后再做。
6. 只有不会打开液体、真空、高温、菌泥或敌对空腔时才扩张。

## 报告格式

每轮后用简洁中文：

```
循环结果:
已执行:
观察到:
风险:
下一步:
状态: 已暂停 / 已继续
```

始终说明游戏当前是否暂停。如果循环因条件停止，说明具体停止条件。
