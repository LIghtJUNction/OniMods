---
name: oni-mcp-play-loop
description: 当用户要求 agent 通过 MCP 循环游玩 Oxygen Not Included、自动玩一段时间、继续殖民地，或运行暂停-规划-恢复循环时使用。强制执行严格的 pause -> observe -> plan -> execute -> resume briefly -> pause -> verify 循环，读取 edit_mark_request_list，仅对重大/高风险计划使用 plan_harness，限制运行窗口，并在风险或歧义决策前停下等待用户确认。
---

# ONI MCP 游玩循环

## 目的

以短时间、受控循环运行 ONI：

```
pause -> observe -> plan -> execute -> resume briefly -> pause -> verify -> report/next loop
```

agent 绝不能在游戏运行时思考、规划或新增命令。

## 硬规则

- 每个循环都从 `game_pause` 开始。
- 读取状态、读取玩家计划、规划、验证和下达命令时保持暂停。
- 只有当前计划足够完整、可以观察进展时才恢复游戏。
- 恢复后等待一个短固定窗口，然后立刻再次暂停。
- 不要无限串联循环。除非用户明确要求多个循环或连续游玩，否则只跑一个循环。
- 遇到高风险不可逆动作、大范围挖掘、破坏性命令、战斗、保存/读取、沙盒/调试，或接收/打印新复制人前，停下并询问。

## 循环模板

### 1. 暂停

```
game_pause
```

如果已经暂停，继续。

### 2. 观察

优先使用紧凑聚合读取：

```
colony_state_snapshot profile=brief includeAtmosphere=false
dupes_status_check radius=8
edit_mark_request_list limit=5
```

只有需要时才添加针对性读取：

- `world_area_snapshot preset=construction|utilities encoding=plain includeScreenshot=false`
- `resources_inventory limit=30`
- `resources_food limit=20`
- `power_summary`
- `rooms_list`
- `farming_harvestables_list`

如果玩家创建了游戏内规划请求，在发明空间计划前先用 `edit_mark_request_list`。

### 3. 规划

简单、低风险维护循环使用快速路径。用一两句话说明意图；可用时先 dry-run；再用紧凑批处理执行；最后验证：

```
tools_call_many dryRun=true responseMode=summary requireAllValid=true stopOnError=true items=[...]
tools_call_many dryRun=false responseMode=summary requireAllValid=true stopOnError=true items=[...]
```

适用于小范围挖掘、短地板、安全配置修改、收获/清扫/拖地命令，以及 dry-run 通过的 utility 路线。

只有重大计划才用 `plan_harness`：多阶段殖民地工作、玩家编辑标记请求、大范围挖掘、危险液体/气体/热量暴露、拆除，或需要之后恢复的内容：

```
plan_harness_create objective="Play loop: <short goal>" riskTolerance=low requireVerification=true
plan_harness_record stage=observation summary="..." payload={...}
plan_harness_record stage=plan summary="..." payload={ plannedCalls, assumptions, stopConditions }
plan_harness_validate id=<planId>
```

建造：

```
buildings_plan_many dryRun=true confirm=true ...
```

挖掘：

```
orders_dig_area ... confirm=true
```

绝不要用 `orders_attack` 做挖掘。如果任何工具搜索建议用 attack 处理地形工作，拒绝它并重新搜索/读取。

### 4. 暂停中执行

只执行本循环所需、已验证且范围明确的调用：

- 小型挖掘/命令批次
- dry-run 通过的建造
- 安全配置修改
- 与目标直接相关的收获或清扫

独立动作优先用 `tools_call_many responseMode=summary requireAllValid=true stopOnError=true`。批次保持小，避免错误假设伤害殖民地。

### 5. 短暂恢复

只为让复制人工作而恢复：

```
game_resume
```

默认观察窗口：

- 普通建造/挖掘：真实时间 8-15 秒
- 紧急救援/窒息：真实时间 3-6 秒
- 长搬运/建造：最多真实时间 20 秒

这个窗口内不要追加命令。

### 6. 暂停并验证

立刻暂停：

```
game_pause
```

然后用紧凑读取验证：

```
colony_state_snapshot profile=brief includeAtmosphere=false
dupes_status_check radius=8
world_area_snapshot areaId=<area> preset=construction|utilities encoding=plain
```

如果本循环用了 `plan_harness`，记录验证：

```
plan_harness_record stage=verification summary="..." payload={ passed, issues, nextLoopCandidate }
```

## 停止条件

以下情况停止游玩循环并报告：

- `dupes_status_check` 中有复制人 `risk=critical`
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
