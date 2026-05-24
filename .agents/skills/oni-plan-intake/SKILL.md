---
name: oni-mcp-plan-intake
description: 当用户给出自然语言 ONI 计划、使用模组内编辑标记/规划工具、创建 edit_mark_request、给出建造命令、坐标方案、分阶段殖民地计划，或要求 agent 在行动前读取/理解/记录计划时使用。指导 agent 先为玩家创建的规划请求调用 edit_mark_request_list，然后收集上下文、解析为 plan_harness 记录、校验工具调用，并且除非明确授权，否则不要执行。
---

# ONI MCP 计划录入

## 目的

当用户给出计划，或要求把文本转成可执行/可验证的 ONI MCP 计划时使用本技能。

模组有专门的玩家计划录入工具：`edit_mark_request_list`。当玩家在游戏内使用 MCP 编辑标记框选区域并输入请求时，请求会以 `prompt`、`areaId`、`rect`、`worldId`、`textMap` 和工作流提示的形式保存在那里。agent 必须先读取它，不能自己凭空制定计划。

任务是保留玩家意图，收集刚好足够的当前游戏/工具上下文，把计划记录到 `plan_harness`，并验证它。除非用户明确要求执行，否则不要控制游戏。

## 玩家规划工具

主工具：

```
edit_mark_request_list limit=5
```

以下情况先用它：

- 用户说自己在游戏内做了/标记了/选择了计划
- 用户说“读取规划”、“看我框选的规划”、“按我游戏里标的来”
- 存在编辑标记通知
- 提示词提到“这个区域”、“框选区域”、“玩家规划”或“标记”

每个请求包含：

- `id`：请求 id
- `prompt`：玩家写下的计划/请求
- `areaId`、`rect`、`worldId`：空间锚点
- `textMap`：精确格子上下文；先用它，再考虑截图
- `screenshotPath`：可选视觉证据，仅作辅助
- `workflow`：服务端提供的规则，要求 agent 先规划

消费请求后，在计划记录完成且用户不再需要前不要清除它。只有成功录入或用户明确要求时，才使用 `edit_mark_request_clear id=<id>`。

## 默认流程

```
1. 先读取玩家规划请求:
   edit_mark_request_list limit=5
   如果存在相关请求，用它的 prompt/areaId/rect/textMap 作为主要输入。

2. 读取当前状态:
   colony_state_snapshot profile=brief

3. 读取已有计划:
   plan_harness_list limit=5
   如果要延续已有计划，plan_harness_get id=<relevant plan>

4. 读取计划领域的工具上下文:
   tools_guide goal=<user plan summary>
   tools_search detail=brief query=<key action/building/order>

5. 读取空间上下文:
   如果 edit_mark 请求有 textMap，先用它。
   如果需要更多细节，调用 world_area_snapshot areaId=<request.areaId> preset=construction|utilities encoding=plain。

6. 创建或更新 harness:
   plan_harness_create objective=<user intent> constraints=[...] riskTolerance=low requireVerification=true
   plan_harness_record stage=plan summary=<short summary> payload={ editMarkRequest?, userPlan, assumptions, plannedCalls? }

7. 如果已有调用，解析并验证:
   plan_harness_parse planText=<structured plan text> validate=true
   plan_harness_validate id=<planId>

8. 用简洁状态收尾:
   plan id、已理解内容、缺失信息、验证问题、下一步安全动作。
```

## 录入规则

- 把用户/玩家文本当作权威意图，但不要当作坐标/工具有效性的证明。
- 如果 `edit_mark_request_list` 返回相关请求，把 `request.prompt` 当作主要计划文本，把 `request.textMap` 当作主要空间上下文。
- 在 `payload.userPlan` 中保留用户原文。
- 在 `payload.editMarkRequest` 中保留原始编辑标记请求，或至少保留 `{id, prompt, areaId, rect, worldId}`。
- 在 `payload.assumptions` 中明确记录假设。
- 优先用 `colony_state_snapshot`，不要拆成很多状态工具。
- 地图/坐标规划优先用 `world_area_snapshot`，不要依赖截图。
- 选择不熟悉的工具名前，先用 `tools_guide` 和 `tools_search`。
- `plan_harness_parse` 只用于结构化 JSON 或行式工具调用，不是自然语言规划器。
- 从 `world_text_map`/编辑标记读取的计划，先把坐标转成世界绝对坐标；检查 `plan_harness_parse` 返回的 `resolvedCalls`，它是已合并默认参数的可执行视图。
- 如果用户只给自然语言，先写结构化计划记录；只有读取上下文后再生成 `plannedCalls`。

## 执行门

除非用户明确说执行/应用/照做，否则不要调用：

- `plan_harness_execute`
- `orders_*`
- `buildings_plan*`
- `game_resume`、`game_set_speed`
- 任何会改变游戏状态的写入/执行工具

明确执行前允许：

- 读取工具
- `edit_mark_request_list`
- 成功录入或明确指示后才用 `edit_mark_request_clear`
- `plan_harness_create`
- `plan_harness_record`
- `plan_harness_parse`
- `plan_harness_validate`
- 只在验证候选蓝图时使用 `buildings_plan* dryRun=true`

## 规划启发

对于编辑标记请求：

- 先用 `request.textMap.Text`。它由 `world_text_map` 生成。
- 后续 `world_area_snapshot` 和验证使用 `request.areaId`。
- 在 plan harness payload 中包含 `request.id`，方便追踪请求。
- 如果有多个请求，选最新且相关的；如有歧义，摘要候选项并询问处理哪个。

对于建造计划：

- 先用 `preset=construction` 快照。
- 电力、管道、自动化或运输轨道使用 `preset=utilities`。
- 在规划机器/家具前检查支撑/地板需求。
- 候选调用用 `buildings_plan_many dryRun=true`；支撑砖要放在落地建筑前。

对于区域命令：

- 挖掘用 `orders_dig_area`，不要用 `orders_attack`。
- 清扫仅对固体碎片/可拾取物使用 `orders_sweep_area`。
- 水、污染水、洒落液体、“地上的水”和液体格子使用 `orders_mop_area`，不要用 `orders_sweep_area`。
- 取消用 `orders_cancel_area`。
- 收获用 `orders_harvest_area`。
- 如果执行结果显示 `marked=0`、`liquidCellsInRect > 0` 或返回 `mopHint`，不要重复同一调用；改用正确工具或重新读取区域。

工具调用文本优先使用 JSON：

```json
{
  "defaults": { "areaId": "a3", "relative": true, "confirm": true, "dryRun": true },
  "plannedCalls": [
    { "name": "buildings_plan_many", "arguments": { "items": [{ "p": "Tile", "l": [0, 2, 8, 2] }] } }
  ]
}
```

紧凑行式也有效：

```text
world_area_snapshot {"areaId":"a3","preset":"construction"}
buildings_plan_many {"dryRun":true,"confirm":true,"items":[{"p":"Tile","l":[70,132,78,132]}]}
```

## 最终响应格式

最终回复保持简短：

- `editMarkRequest`：如果读取了请求，给出 id
- `planId`：如果创建/更新了计划，给出 id
- `understood`：一句话说明理解
- `validated`：通过/失败/部分通过
- `issues`：只列阻塞问题
- `next`：下一步安全工具或需要的确认
