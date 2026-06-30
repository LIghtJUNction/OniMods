---
name: oni-mcp-plan-intake
description: 当用户给出自然语言 ONI 计划、使用模组内编辑标记/规划工具、创建 edit_mark_request、给出建造命令、坐标方案、分阶段殖民地计划，或要求 agent 在行动前读取/理解计划时使用。指导 agent 先为玩家创建的规划请求调用 game_control domain=ui uiDomain=edit_mark action=list，然后收集上下文、输出简洁可执行计划，并且除非明确授权，否则不要执行。
---

# ONI MCP 计划录入

## 目的

当用户给出计划，或要求把文本转成可执行/可验证的 ONI MCP 计划时使用本技能。

模组有专门的玩家计划录入入口：`game_control domain=ui uiDomain=edit_mark action=list`。当玩家在游戏内使用 MCP 编辑标记框选区域并输入请求时，请求会以 `prompt`、`areaId`、`rect`、`worldId`、`textMap` 和工作流提示的形式保存在那里。agent 必须先读取它，不能自己凭空制定计划。

任务是保留玩家意图，收集刚好足够的当前游戏/工具上下文，输出简洁可执行计划，并用 dry-run/只读验证降低风险。除非用户明确要求执行，否则不要控制游戏。

## 玩家规划工具

主工具：

```
game_control domain=ui uiDomain=edit_mark action=list limit=5
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

消费请求后，在计划记录完成且用户不再需要前不要清除它。只有成功录入或用户明确要求时，才使用 `game_control domain=ui uiDomain=edit_mark action=clear id=<id>`。

## 默认流程

```
1. 先读取玩家规划请求:
   game_control domain=ui uiDomain=edit_mark action=list limit=5
   如果存在相关请求，用它的 prompt/areaId/rect/textMap 作为主要输入。

2. 读取当前状态:
   colony_control domain=snapshot action=get profile=brief

3. 读取计划领域的工具上下文:
   server_control domain=catalog action=guide goal=<user plan summary>
   server_control domain=catalog action=search detail=brief query=<key action/building/order>

4. 读取空间上下文:
   如果 edit_mark 请求有 textMap，先用它。
   如果需要更多细节，调用 read_control domain=world action=area_snapshot areaId=<request.areaId> preset=construction|utilities encoding=plain。

5. 输出可执行计划:
   列出目标、约束、关键坐标/区域、拟调用工具、dryRun/验证步骤和风险。

6. 用简洁状态收尾:
   已理解内容、缺失信息、验证问题、下一步安全动作。
```

## 录入规则

- 把用户/玩家文本当作权威意图，但不要当作坐标/工具有效性的证明。
- 如果 `game_control domain=ui uiDomain=edit_mark action=list` 返回相关请求，把 `request.prompt` 当作主要计划文本，把 `request.textMap` 当作主要空间上下文。
- 在回复中保留用户原文摘要。
- 在回复中保留原始编辑标记请求关键字段 `{id, prompt, areaId, rect, worldId}`。
- 明确列出假设。
- 优先用 `colony_control domain=snapshot action=get`，不要拆成很多状态工具。
- 地图/坐标规划优先用 `read_control domain=world action=area_snapshot`，不要依赖截图。
- 选择不熟悉的工具名前，先用 `server_control domain=catalog action=guide` 和 `server_control domain=catalog action=search`。
- 从 `read_control domain=world action=text_map`/编辑标记读取的计划，先把坐标转成世界绝对坐标；写出可执行调用前检查工具 schema。
- 如果用户只给自然语言，先写结构化计划记录；只有读取上下文后再生成 `plannedCalls`。

## 执行门

除非用户明确说执行/应用/照做，否则不要调用：

- `orders_*`
- `navigation_control action=left_click`
- `navigation_control action=hold_left`
- `game_control domain=speed action=resume`、`game_control domain=speed action=set_speed`
- 任何会改变游戏状态的写入/执行工具

明确执行前允许：

- 读取工具
- `game_control domain=ui uiDomain=edit_mark action=list`
- 成功录入或明确指示后才用 `game_control domain=ui uiDomain=edit_mark action=clear`
- `building_control domain=planning action=search_defs/materials` 用于验证候选 prefab 和材料

## 规划启发

对于编辑标记请求：

- 先用 `request.textMap.Text`。它由 `read_control domain=world action=text_map` 生成。
- 后续 `read_control domain=world action=area_snapshot` 和验证使用 `request.areaId`。
- 在回复计划中包含 `request.id`，方便追踪请求。
- 如果有多个请求，选最新且相关的；如有歧义，摘要候选项并询问处理哪个。

对于建造计划：

- 先用 `preset=construction` 快照。
- 电力、管道、自动化或运输轨道使用 `preset=utilities`。
- 在规划机器/家具前检查 `building_control domain=planning action=search_defs` 的 placement、支撑/地板需求和 footprint。指针格是 `lowerLeftCell` 锚点，不是建筑视觉中心。
- 候选动作必须表达为指针跳转、选工具、点击/拖拽；支撑砖要先于落地建筑放置。
- 只对 1x1 footprint 使用 `navigation_control action=hold_left`。床、厕所、机器等多格建筑逐个 anchor 用 `navigation_control action=left_click`，必要时先 `dryRun=true` 预检，再执行并用地图验证 `placementCheck`。

对于区域命令：

- 挖掘用 `orders_control domain=area action=dig`，不要用 `orders_control domain=designation action=attack`。
- 清扫仅对固体碎片/可拾取物使用 `orders_control domain=area action=sweep`。
- 水、污染水、洒落液体、“地上的水”和液体格子使用 `orders_control domain=area action=mop`，不要用 `orders_control domain=area action=sweep`。
- 取消用 `orders_control domain=area action=cancel`。
- 收获用 `orders_control domain=area action=harvest`。
- 如果执行结果显示 `marked=0`、`liquidCellsInRect > 0` 或返回 `mopHint`，不要重复同一调用；改用正确工具或重新读取区域。

工具调用文本优先使用 JSON：

```json
{
  "defaults": { "confirm": true },
  "plannedCalls": [
    { "name": "navigation_control", "arguments": { "action": "jump", "x": 70, "y": 132 } },
    { "name": "navigation_control", "arguments": { "action": "select_tool", "tool": "build", "prefabId": "Tile", "material": "auto" } },
    { "name": "navigation_control", "arguments": { "action": "hold_left", "direction": "right", "length": 9 } }
  ]
}
```

紧凑行式也有效：

```text
read_control domain=world action=area_snapshot {"areaId":"a3","preset":"construction"}
navigation_control {"action":"jump","x":70,"y":132}
navigation_control {"action":"select_tool","tool":"build","prefabId":"Tile","material":"auto"}
navigation_control {"action":"hold_left","direction":"right","length":9,"confirm":true}
```

## 最终响应格式

最终回复保持简短：

- `editMarkRequest`：如果读取了请求，给出 id
- `planId`：如果创建/更新了计划，给出 id
- `understood`：一句话说明理解
- `validated`：通过/失败/部分通过
- `issues`：只列阻塞问题
- `next`：下一步安全工具或需要的确认
