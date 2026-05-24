---
name: oni-mcp-dupe-status-check
description: 当用户要求检查 ONI 复制人状态、怀疑复制人卡住/被困/不可达、报告复制人在错误地点发呆或睡觉、要求救援分诊，或需要快速复制人健康/导航审计时使用。指导 agent 先用 dupes_status_check，只检查被标记区域，除非用户明确授权，否则不要下达救援动作。
---

# ONI MCP 复制人状态检查

## 目的

快速识别可能被困、不可达、疲劳、窒息、饥饿，或无法正常执行工作的复制人。这个技能默认以只读为主。

## 快速路径

先使用聚合状态工具：

```
dupes_status_check radius=8 includeReachableSamples=true
```

检查单个复制人：

```
dupes_status_check name=<dupeName> radius=10
```

检查复制人是否能到达目标/基地格子：

```
dupes_status_check targetX=<x> targetY=<y> targetWorldId=<worldId> radius=10
```

除非 `dupes_status_check` 缺少必要细节，否则不要从 `dupes_detail`、`dupes_needs`、截图或大范围地图扫描开始。

## 后续读取

如果 `flagged > 0`，只检查被标记的复制人：

```
world_area_snapshot x1=<scanRect[0]> y1=<scanRect[1]> x2=<scanRect[2]> y2=<scanRect[3]> worldId=<worldId> preset=construction encoding=plain includeScreenshot=false
```

只有需要时才补充细节：

- `dupes_detail id=<id>`：查看属性/日程上下文。
- `minion_todos_list id=<id> includeBlocked=true`：查看工作前置条件失败。
- `camera_focus_dupe id=<id>`：仅在用户需要视觉聚焦时使用。
- 截图只用于视觉确认，不用于坐标推理。

## 风险阅读

这些情况按紧急处理：

- `risk=critical`
- 原因包含 `no_reachable_nearby_cells`、`low_breath`、`invalid_current_cell`
- 复制人在液体中、极端温度中，或热量/体力很低

这些情况按规划警告处理：

- `target_unreachable`
- `very_few_reachable_nearby_cells`
- 当前工作异常、空闲/无工作，或远离基地睡觉

使用工具响应里的 `scanRect` 和 `nextRead`，不要自己发明大范围地图查询。

## 救援策略

默认只读。除非用户明确要求修复/救援/执行，否则不要调用：

- `orders_dig_area`
- `buildings_plan*`
- `dupes_move_to`
- `dupes_move_batch_to`
- `game_resume`

救援动作前：

1. 如果用户要求思考或规划，先暂停。
2. 检查被标记的 `scanRect`。
3. 优先通过挖掘/建造支撑打开可达路径，不要盲目移动复制人。
4. 用 `buildings_plan_many dryRun=true` 做建造预演。
5. 动作完成后用 `dupes_status_check` 验证。

除非 `dupes_status_check targetX/targetY` 或 `dupes_move_to` 的可达性校验确认目标可达，否则不要把复制人移动到该格子。

## 输出格式

用简洁中文返回：

```
复制人状态:
风险:
疑似被困:
环境/路径:
建议:
```

说明复制人姓名、坐标、风险和一个下一步安全读取/动作。如果无人被标记，直接说明并避免额外工具调用。
