---
name: oni-mcp-play-loop
description: 当用户要求 agent 通过 MCP 循环游玩 Oxygen Not Included、自动玩一段时间、继续殖民地，或运行暂停-规划-恢复循环时使用。强制执行暂停、观察、规划、执行、短暂恢复、再次暂停和验证的循环，读取 game_control domain=ui uiDomain=edit_mark action=list，限制运行窗口，并在风险或歧义决策前停下等待用户确认。
---

# ONI MCP 游玩循环

## 规划时按阶段选模块

需要决定殖民地下一项工作时，读取 [阶段策略技能](../oni-mcp-colony-strategy/SKILL.md)，只加载当前瓶颈的参考。把短期补给和长期模块分开，先补前置缺口，保留旧设施直到新产出通过验证。报告相关建议时附原文链接；不以旧图纸替代当前精确地图预检。固定的已授权维护任务不需要每轮重读攻略。


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
- 恢复后等待一个短固定窗口，然后立刻再次暂停。
- 不要无限串联循环。除非用户明确要求多个循环或连续游玩，否则只跑一个循环。
- 遇到高风险不可逆动作、大范围挖掘、破坏性命令、战斗、保存/读取、沙盒/调试，或接收/打印新复制人前，停下并询问。

## 循环模板

### 1. 暂停

```
game_control domain=speed action=pause
```

如果已经暂停，继续。

### 2. 观察

优先使用紧凑聚合读取：

```
colony_control domain=snapshot action=get profile=brief includeAtmosphere=false
dupes_control domain=info action=status_check radius=8
game_control domain=ui uiDomain=edit_mark action=list limit=5
```

只有需要时才添加针对性读取：

- `read_control domain=world action=area_snapshot preset=construction|utilities encoding=plain includeScreenshot=false`
- `read_control domain=resources action=inventory limit=30`
- `read_control domain=resources action=food limit=20`
- `read_control domain=infrastructure action=power_summary`
- `read_control domain=infrastructure action=rooms`
- `colony_control domain=bio bioDomain=farming action=list_harvestables`

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

### 4. 暂停中执行（或使用 CodeMode 一站式执行）

只执行本循环所需、已验证且范围明确的调用：

- 小型挖掘/命令批次
- dry-run 通过的建造
- 安全配置修改
- 与目标直接相关的收获或清扫

**最高效路径（CodeMode）**：使用 `server_control domain=code action=execute code=[...]` 将命令下达与 `step_until` 串联执行，单回合完成规划、快进与返回：
```json
{
  "domain": "code",
  "action": "execute",
  "code": [
    { "tool": "orders_control", "args": { "domain": "dig", "action": "line", "x1": 20, "y1": 20, "x2": 25, "y2": 20 } },
    { "tool": "game_control", "args": { "domain": "speed", "action": "step_until", "condition": "orders_complete", "speed": 3, "maxGameSeconds": 60 }, "saveAs": "stepResult" },
    { "return": "$stepResult" }
  ]
}
```

独立调用优先用 `server_control domain=batch action=call_many responseMode=summary requireAllValid=true stopOnError=true`。批次保持小，避免错误假设伤害殖民地。

### 5. 目标驱动快进推进（对标 Playwright waitForCondition 与游戏自动化报警）

不要使用无目标的盲目恢复休眠！使用 `step_until` 以 3x 速自动推进至目标完成、自动化警报触发或异常捕获：

```
game_control domain=speed action=step_until condition=orders_complete speed=3 maxGameSeconds=60
```

可选停止条件 `condition`：
- `orders_complete`（默认）：当前重点工单全部完成。
- `dupe_idle`：复制人变为空闲（说明手头活干完了或无路可走）。
- `cycle_end`：周期结束/入夜。
- `alarm` 或 `automation_alarm`：**游戏内自动化通知器（LogicAlarm）被绿灯信号触发**。
- `alarm:<name>` 或 `<name>`（如 `alarm:WaterFull` 或 `WaterFull`）：匹配指定名称的自动化通知器。
- `alert` 或 `threat`：出现窒息、受困、红警或严重低生命值等紧急危险（任何运行模式下出现致命危险均会自动紧急刹车）。
- `notification:<keyword>`：匹配 HUD 上出现的指定关键字通知。
- `any`：上述任一条件触发即自动 Pause 并返回。

**缺氧原生硬件事件机制（终极省 Token 范式）**：
在缺氧中，你可以利用温度/液位/气压/储量等逻辑传感器连接**自动化通知器（LogicAlarm）**，并命名为 `WaterFull` 或 `SafeTemp`。接着下达 `step_until condition="WaterFull"`。游戏以 3 倍速飞快模拟，一旦传感器触发绿信号，主线程每帧监听到报警后**毫秒级自动暂停并返回**，实现真正意义上的零轮询、零 Token 浪费！

### 6. 增量差异核对（零额外 Token 验证）

`step_until` 返回时已自动处于暂停状态，并携带极精简的增量结果：
```json
{
  "stopReason": "orders_complete",
  "elapsedGameSeconds": 38.5,
  "elapsedWallSeconds": 12.8,
  "remainingTargetOrders": 0,
  "idleDuplicants": ["Harold"],
  "alerts": "none",
  "isPaused": true
}
```

- 若 `stopReason == "orders_complete"` 且无警报：**工单已全部达成**，无需重读大范围地图，可直接推进下一阶段！
- 若因 `alert`、`dupe_idle` 或 `timeout` 停止：按需使用 `dupes_control domain=info action=status_check` 或局部 `read_control domain=world action=area_snapshot` 进行针对性排查。

记录验证结果：说明目标是否推进、发现的问题、下一轮是否需要继续。

## 停止条件

以下情况停止游玩循环并报告：

- `dupes_control domain=info action=status_check` 中有复制人 `risk=critical`
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
